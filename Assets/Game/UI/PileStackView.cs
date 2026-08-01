using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 牌堆按钮左边那一小摞牌。**厚度随张数变**，让「还剩多少」不用读数字就看得出来。
    ///
    /// ★★ 为什么值得做：三颗牌堆按钮从前只是三条写着「抽牌堆 12」的横杠。
    ///    而抽牌堆的余量是**每回合都要做的决策依据**（「还够不够抽到那张关键牌 / 这轮会不会洗牌」），
    ///    读数字要眼睛聚焦 + 认字，而厚度是余光就能扫到的。
    ///    数字**保留**——厚度回答「大概还剩多少」，数字回答「精确是多少」，两者不互相替代。
    ///
    /// ★★ 尺寸受一条硬约束：整个左侧 HUD 列宽 <c>HudColWidth = 120</c>，
    ///    而 <c>BattleScreen.HandWidth</c> 是**从这个数推导出来的**
    ///    （见 BattleScreen 里 HandWidth 那段注释，铁律 24 的同一形状）。
    ///    所以这一摞只能画在 120 之内，往右撑宽按钮会把手牌挤歪，
    ///    而那个后果要到「手牌满 10 张时最右一张压住结束回合按钮」时才看得见。
    ///
    /// ★ 层数走 <c>sqrt(张数)</c> 而不是线性：牌堆常见范围是 0~40 张，
    ///   线性映射会让 5 张和 10 张看起来一样（都是「薄薄一片」），
    ///   而那恰恰是玩家最在意的那一段差别。开方把低位的分辨率让出来给高位。
    /// </summary>
    public class PileStackView : MonoBehaviour
    {
        // ============================================================ 尺寸

        /// <summary>整个图标区占按钮左边多宽。★ 与 <see cref="LabelInset"/> 是同一个数，改要一起改。</summary>
        public const float Width = 36f;

        /// <summary>最上面那张牌的尺寸。2:3，与卡背图同比。</summary>
        private const float CardW = 19f, CardH = 28f;

        /// <summary>每多一层往右下挪多少。★ 两个方向都要有：只往右像百叶窗，只往下像楼梯。</summary>
        private const float StepX = 2.2f, StepY = 1.4f;

        /// <summary>
        /// 最多画几层。★ 上限由宽度决定：<c>CardW + MaxLayers*StepX</c> 必须塞进 <see cref="Width"/>。
        /// 19 + 7×2.2 = 34.4 &lt; 36 ✓
        /// </summary>
        private const int MaxLayers = 7;

        /// <summary>最上面那张的颜色（乘在灰度卡背上）。</summary>
        private static readonly Color TopColor = new Color(0.66f, 0.72f, 0.92f);

        /// <summary>最底下那层的颜色。中间各层在这两者之间插值 → 一摞牌的侧面有渐深的阴影。</summary>
        private static readonly Color DeepColor = new Color(0.26f, 0.29f, 0.40f);

        /// <summary>牌堆空了的时候那个虚位的颜色。</summary>
        private static readonly Color EmptyColor = new Color(0.22f, 0.23f, 0.27f);

        /// <summary>置灰时整摞乘上这个。★ 与按钮自己的置灰保持同步，见 SetCount 的注释。</summary>
        private const float DisabledMul = 0.45f;

        // ============================================================ 节点

        /// <summary>
        /// 从底到顶的层。★ **全部预先建好**，之后只改 SetActive——
        /// 张数每帧都可能变（抽一张、弃一张），按需 Instantiate/Destroy 等于每帧建销毁 GameObject。
        /// </summary>
        private Image[] _layers;

        /// <summary>牌堆为 0 时露出来的空位框。</summary>
        private Image _empty;

        /// <summary>上一次画的层数与置灰态。★ 没变就一个节点都不碰，见 <see cref="SetCount"/>。</summary>
        private int _shownLayers = -1;
        private bool _shownEnabled = true;

        /// <summary>当前贴着的卡背。见 <see cref="SetCardBack"/>。</summary>
        private Sprite _back;

        /// <summary>
        /// 建在 <paramref name="button"/> **内部**（子节点画在父的 Image 之上，正好压住按钮底色）。
        /// </summary>
        public static PileStackView Create(RectTransform button, Sprite cardBack)
        {
            var root = UIFactory.CreateEmpty(button, "Stack");
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 0.5f);
            root.offsetMin = new Vector2(6f, 0f);
            root.offsetMax = new Vector2(6f + Width, 0f);

            var view = root.gameObject.AddComponent<PileStackView>();
            var sprite = UIFactory.CardBackOr(cardBack);
            view._back = sprite;

            // ---- 空位框。建在最底下（最先建 = 最先画）
            view._empty = MakeCard(root, "Empty", sprite, EmptyColor, MaxLayers - 1);

            // ---- 从底层往顶层建，于是顶层最后画、压在所有人上面
            view._layers = new Image[MaxLayers];
            for (int i = MaxLayers - 1; i >= 0; i--)
            {
                float t = MaxLayers <= 1 ? 0f : i / (float)(MaxLayers - 1);
                var color = Color.Lerp(TopColor, DeepColor, t);
                view._layers[i] = MakeCard(root, "L" + i, sprite, color, i);
            }

            return view;
        }

        /// <summary>
        /// 换卡背图。
        ///
        /// ★★ 这个方法存在的唯一理由是一个时序问题，不是「为了灵活」：
        ///    这一摞是在 <c>BattleScreen.BuildUI</c> 里建的，而
        ///    <c>BattleScreen.Database</c> 是从 <c>Ctx.Run</c> 上取的——
        ///    <b>Bind 的那一刻 Ctx 完全可能还是 null</b>（战斗尚未开始，
        ///    见 <c>BattleScreen._boundCtx</c> 那段注释记的同一个坑）。
        ///    于是构建时拿到的卡背恒为 null，<c>GameDatabase.CardBack</c> 配了也**永远用不上**，
        ///    而画面上是那张内置卡背——看起来完全正常，只是配置静默失效了。
        ///    所以必须在 Ctx 就绪之后（每帧的 RefreshPileButtons）再补一次。
        ///
        /// ★ 自带「没变就不动」：它每帧被调用，而 sprite 几乎永远不变。
        /// </summary>
        public void SetCardBack(Sprite cardBack)
        {
            var sprite = UIFactory.CardBackOr(cardBack);
            if (_back == sprite) return;
            _back = sprite;

            if (_empty != null) _empty.sprite = sprite;
            for (int i = 0; i < _layers.Length; i++)
                if (_layers[i] != null) _layers[i].sprite = sprite;
        }

        /// <param name="depth">0 = 最上面那张。越大越往右下、越暗。</param>
        private static Image MakeCard(RectTransform parent, string name, Sprite sprite, Color color, int depth)
        {
            var rt = UIFactory.CreatePanel(parent, name, color);
            UIFactory.SetSize(rt, CardW, CardH);

            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(depth * StepX, -depth * StepY);

            var img = rt.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;

            // ★ 整摞都不吃射线：它铺在按钮里面，开着的话点在牌堆图标上反而点不开牌堆浏览面板
            //   ——而那正是这颗按钮唯一的功能。
            img.raycastTarget = false;

            return img;
        }

        /// <summary>
        /// 更新厚度。<paramref name="enabled"/> 跟着按钮自己的置灰走。
        ///
        /// ★★ 「没变就什么都不碰」是必须的，不是优化：
        ///    本方法由 <c>RefreshPileButtons</c> 每帧调用，而 <c>SetActive</c> 与写 <c>Image.color</c>
        ///    都会把 Canvas 标脏并触发一次批次重建。张数在绝大多数帧里是不变的
        ///    （只在抽 / 弃 / 消耗那几帧才动），无条件重画等于给三颗按钮白付每帧 8 次重建。
        ///    这与 <c>CardView.RefreshKeywordDots</c> 用位掩码短路是同一条纪律。
        /// </summary>
        public void SetCount(int count, bool enabled)
        {
            int layers = LayersFor(count);
            if (layers == _shownLayers && enabled == _shownEnabled) return;

            _shownLayers = layers;
            _shownEnabled = enabled;

            if (_empty != null)
            {
                bool showEmpty = layers == 0;
                if (_empty.gameObject.activeSelf != showEmpty) _empty.gameObject.SetActive(showEmpty);
                if (showEmpty) _empty.color = Tint(EmptyColor, enabled);
            }

            for (int i = 0; i < _layers.Length; i++)
            {
                var img = _layers[i];
                if (img == null) continue;

                bool on = i < layers;
                if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
                if (!on) continue;

                // ★ 无条件重染每一个**激活着**的层。
                //   看起来可以「只在置灰态变了时才染」，但那样会漏掉一种情况：
                //   置灰期间张数变了（表现播放时正好在弃牌），新露出来的那一层
                //   从没被染过、会顶着构建时的亮色出现在一摞灰牌里。
                //   本方法整体已经被上面那句「没变就 return」保护住了，
                //   走到这里的帧本来就是稀有的，多染几个 Image 不值得再分一次支。
                float t = _layers.Length <= 1 ? 0f : i / (float)(_layers.Length - 1);
                img.color = Tint(Color.Lerp(TopColor, DeepColor, t), enabled);
            }
        }

        private static Color Tint(Color c, bool enabled)
            => enabled ? c : new Color(c.r * DisabledMul, c.g * DisabledMul, c.b * DisabledMul);

        /// <summary>
        /// 张数 → 层数。
        ///
        /// ★ 开方：1→1、4→2、9→3、16→4、25→5、36→6、49→7。
        ///   牌组通常 15~30 张，正好落在 4~6 层这一段，而 0~10 张（快要洗牌了，
        ///   也就是最需要被察觉的时刻）每两三张就长一层。
        /// </summary>
        private static int LayersFor(int count)
        {
            if (count <= 0) return 0;
            return Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count)), 1, MaxLayers);
        }
    }
}
