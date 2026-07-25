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
        public const float CardWidth = 170f;
        public const float CardHeight = 240f;

        /// <summary>手牌少时的相邻间距。刻意小于牌宽，让牌之间始终有一点叠压——完全不叠不像一把牌。</summary>
        private const float MaxSpacing = 134f;

        /// <summary>每多一张牌，两端各多倾斜多少度。</summary>
        private const float TiltPerCard = 4.2f;

        /// <summary>两端倾角上限。再大牌面上的字就开始难读了。</summary>
        private const float MaxEndTilt = 17f;

        /// <summary>每多一张牌，两端各多下沉多少像素。</summary>
        private const float ArcPerCard = 7.5f;

        /// <summary>两端下沉上限。</summary>
        private const float MaxArcDepth = 54f;

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
