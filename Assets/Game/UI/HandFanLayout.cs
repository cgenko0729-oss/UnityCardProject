using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 手牌扇形排布的纯计算：给定「第几张 / 一共几张」，算出这张牌该在哪、该转多少度。
    ///
    /// ★ 为什么不用圆周公式（x = R·sinθ, y = R·cosθ - R）：
    ///   纯圆周的横向间距由半径决定，手牌数一变间距就跟着变，很难同时满足
    ///   「2 张牌不要离太远」和「12 张牌不要溢出屏幕」。
    ///   这里改成「横向按间距线性排 + 纵向按抛物线下沉 + 倾角按归一化位置线性插值」，
    ///   三条曲线各自独立可调，看起来和圆弧没有区别，但参数是可控的。
    ///
    /// ★ 这个类刻意不碰任何 MonoBehaviour / RectTransform：
    ///   排布出问题时可以只盯着这几行看，不必怀疑是不是 anchor 或 pivot 配错了。
    /// </summary>
    public static class HandFanLayout
    {
        // ================================================================= 唯一的尺寸源
        //
        // ★★ 全工程「卡牌有多大」只由这两个数决定。改它们的完整步骤见
        //    Assets/Docs/ProjectUseGuide.md 第 22.2 节。
        //
        // ★ 沿革：170×240 → 200×290 → 230×330 → 230×346。前几次放大都不是单纯放大，而是先把
        //   卡面上浪费的排版省出来（删了底部类型栏、费用球挪到卡外），插画窗才吃得到这些像素。
        //
        // ★ 最后这次 330 → 346 是为了**对上卡框素材的宽高比 0.664**：
        //   卡框是一整张不能九宫格拉伸的花饰图（四角有玫瑰、顶部正中挂着宝石），
        //   卡的比例一旦和框对不上，框就会被拉变形。所以现在这两个数**不是自由的**——
        //   换一套框就要重新按框的外接盒比例调（量法见 ProjectUseGuide 22.2）。
        //
        // ★ 本文件里其余与尺寸有关的量**一律按比例推导**，不再写死像素。
        //   写死的话每次改尺寸都要手工重算四五处，而漏算一处的表现是
        //   「牌变大了但扇形手感变了 / 边上的牌被 HUD 吃掉点击」——都不会报错。
        public const float CardWidth = 230f;
        public const float CardHeight = 346f;

        /// <summary>
        /// 相邻间距 ÷ 牌宽。0.86 = 14% 叠压。
        ///
        /// ★ 这里是**比例**不是像素：曾经写死 134，牌从 170 变大后叠压率随之飙到 21%，
        ///   只有 5 张牌就已经把左邻居的插画整片吃掉了（右边的牌压左边的，见 <c>ApplyHandOrder</c>）。
        /// ★ 完全不叠（≥1）不像一把牌；低于 0.75 插画主体就开始被吃。
        /// </summary>
        private const float SpacingRatio = 0.86f;

        private static float MaxSpacing => CardWidth * SpacingRatio;

        /// <summary>每多一张牌，两端各多倾斜多少度。</summary>
        private const float TiltPerCard = 3.5f;

        /// <summary>
        /// 两端倾角上限。★ 角度不随尺寸缩放，但**大牌要更小的角**：
        /// 倾角吃掉的横向空间是 <c>牌高 × sinθ</c>，牌越高同一个角度探得越远
        /// （见 <see cref="OuterReach"/>），而且大卡面上的斜排文字更难读。17° → 14°。
        /// </summary>
        private const float MaxEndTilt = 14f;

        /// <summary>每多一张牌，两端各多下沉牌高的百分之几。</summary>
        private const float ArcPerCardRatio = 0.029f;

        /// <summary>两端下沉上限，占牌高的比例。</summary>
        private const float MaxArcDepthRatio = 0.207f;

        private static float ArcPerCard => CardHeight * ArcPerCardRatio;
        private static float MaxArcDepth => CardHeight * MaxArcDepthRatio;

        /// <summary>
        /// 最外侧那张牌从自己的**底边中点**向外探出的最大水平距离。
        ///
        /// ★ 探得最远的不是卡的侧边，而是倾斜之后那个**上外角**：
        ///     半宽·cosθ + 牌高·sinθ
        ///   牌高在这条式子里是带 sinθ 的一项，所以「把牌加高」比「把牌加宽」
        ///   更快地吃掉两侧的空间——230×330 在 14° 下探出 191，其中 80 是高度贡献的。
        ///
        /// ★ <see cref="BattleScreen"/> 用它反解手牌区宽度：手牌区建得比 HUD 晚，
        ///   牌压到哪个按钮上就会把那颗按钮的点击吃掉（铁律 24），所以这条余量必须算准。
        /// </summary>
        public static float OuterReach
        {
            get
            {
                float t = MaxEndTilt * Mathf.Deg2Rad;
                return CardWidth * 0.5f * Mathf.Cos(t) + CardHeight * Mathf.Sin(t);
            }
        }

        /// <summary>一张牌的目标位姿。Position 是牌的**底边中点**（CardView 的 pivot 就设在那里）。</summary>
        public struct Slot
        {
            public Vector2 Position;

            /// <summary>绕 Z 轴的角度。左侧为正（逆时针），与「扇子往两边张开」一致。</summary>
            public float Rotation;
        }

        /// <param name="index">这是第几张（0 起）。</param>
        /// <param name="count">手牌总数。</param>
        /// <param name="availableWidth">手牌区可用宽度，牌多时会被压缩到这个宽度以内。</param>
        /// <param name="baseY">正中那张牌底边的 y。</param>
        public static Slot Compute(int index, int count, float availableWidth, float baseY)
        {
            var slot = new Slot { Position = new Vector2(0f, baseY), Rotation = 0f };
            if (count <= 1 || index < 0 || index >= count) return slot;

            // 牌多时压缩间距：(count - 1) 个间隔要塞进「可用宽度减掉最后一张牌的宽度」里
            float spacing = Mathf.Min(MaxSpacing, (availableWidth - CardWidth) / (count - 1));

            float center = (count - 1) * 0.5f;
            float offset = index - center;      // -center .. +center
            float t = offset / center;          // -1 .. 1，两端为 ±1

            float endTilt = Mathf.Min(TiltPerCard * center, MaxEndTilt);
            float arcDepth = Mathf.Min(ArcPerCard * center, MaxArcDepth);

            slot.Position = new Vector2(offset * spacing, baseY - arcDepth * t * t);
            slot.Rotation = -endTilt * t;
            return slot;
        }
    }
}
