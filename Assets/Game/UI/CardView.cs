using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 单张手牌的显示。★ 只读 CardInstance，绝不修改战斗数据。
    /// 点击 / 拖拽都只是把「意图」交给 BattleScreen，由它去调 BattleController。
    ///
    /// 位姿（位置 / 角度 / 缩放）由 BattleScreen 每帧写进 <see cref="SetLayoutTarget"/>，
    /// 本组件自己朝目标插值。★ 这一层插值是扇形手感的关键：
    /// 打出一张牌之后其余牌要「滑」回新的扇形位置，而不是瞬移。
    /// </summary>
    public class CardView : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        ITooltipSource
    {
        public CardInstance Card { get; private set; }

        private BattleScreen _screen;
        private Image _bg;

        /// <summary>卡框。没配框时为 null。</summary>
        private Image _frame;

        /// <summary>卡框的 RectTransform，缩放要写在它身上。</summary>
        private RectTransform _frameRt;

        private TMP_Text _costText;
        private TMP_Text _nameText;
        private TMP_Text _descText;
        private RectTransform _rt;

        // ★ 卡面底色现在是「这张牌是什么类型」的**唯一**表达——底部那条类型栏
        //   （「攻击」「技能」「能力」）已经删掉，那 32 像素全给了插画。
        //   于是原本共用一个灰的 Status 与 Curse 必须分家，否则「晕眩」和「伤口」长得一模一样。
        private static readonly Color ColAttack = new Color(0.55f, 0.20f, 0.20f);
        private static readonly Color ColSkill = new Color(0.20f, 0.35f, 0.55f);
        private static readonly Color ColPower = new Color(0.42f, 0.28f, 0.55f);
        private static readonly Color ColStatus = new Color(0.30f, 0.31f, 0.34f);
        private static readonly Color ColCurse = new Color(0.24f, 0.16f, 0.30f);
        private static readonly Color ColDisabled = new Color(0.18f, 0.18f, 0.18f);

        /// <summary>位姿追随速度。指数收敛，值越大越「硬」。</summary>
        private const float FollowSpeed = 16f;

        /// <summary>
        /// 悬停判定区往下多伸多少像素。
        ///
        /// ★ 必须有：pivot 在底边，悬停会把牌整体抬高。如果判定区就是卡面本身，
        ///   光标停在卡面下缘时——抬起 → 牌底离开光标 → 判定为移出 → 落回 → 又进入，
        ///   牌会在光标处每帧抖动。往下多伸一块不可见的判定垫，抬起后光标仍在垫子里。
        /// </summary>
        private const float HoverPadBelow = 84f;

        // ---- 卡面的纵向预算
        //
        // ★★ 布局的**方向**是「文字区定死，剩下全给插画」，不是按比例分配：
        //    名字栏、描述区、底部留白都是固定像素（它们装的是字，字号不随卡变大），
        //    <see cref="ArtHeight"/> 是拿卡高减掉它们**推导**出来的。
        //    于是把卡牌调大，多出来的像素 100% 落进插画窗——这正是放大卡牌的目的。
        //
        // ★ 沿革：卡高 240 时插画只有 84（35%）；删掉底部类型栏(32)、
        //   把费用球挪出卡外让名字栏从 48 压到 30，再把卡高加到 330，
        //   插画窗从 158×84 变成 218×172 —— 面积 2.83 倍。
        //
        //   346 = 名字栏(4..34) + 间隙(4) + 插画(38..226) + 间隙(6) + 描述区(88) + 底部留白(26)
        //
        // ★★ 配了卡框之后（<see cref="CardFrameSkin"/>），上面每一块都要**整体缩进到框的开口里**：
        //    框的花饰边压在卡面外圈，不缩进的话名字和色点会被边饰盖掉。
        //    缩进量一框一配，见 CardFrameSkin.InsetSide / InsetTop / InsetBottom。
        //    没配框时三个 inset 全是 0，排版与接框之前**逐像素相同**。

        private const float ArtSideMargin = 6f;
        private const float NameBarTop = 4f;
        private const float NameBarHeight = 30f;
        private const float ArtGap = 6f;

        /// <summary>名字栏底到插画窗顶的间隙。</summary>
        private const float NameToArtGap = 4f;

        /// <summary>
        /// 描述区高度（无框）。★ 固定值：它装的是文字，而字号不跟着卡牌尺寸走。
        /// 88 够放 3 行 19 号中文；再配上自动缩字号，长描述与英译文都兜得住。
        /// </summary>
        private const float DescHeight = 88f;

        // ★ 有框时的描述区高度**不在这里**——它在 CardFrameSkin.DescHeight，因为要能运行中调。
        //   默认 72，比无框的 88 矮，这与「窄了要折更多行、所以该更高」的直觉相反，
        //   理由是纵向预算已经见底：框的上下边饰吃掉 73、名字栏 30、色点与留白 24，
        //   插画只剩 117，被压成 1.97:1 的横条，而卡池里的立绘全是竖构图，裁完只剩一条脸。

        /// <summary>
        /// 卡框边饰吃掉卡面多少（像素）。没配框时全为 0。
        /// ★ 三个字段一起算，因为它们全部来自同一张框图的实测比例。
        /// </summary>
        /// <summary>
        /// 一整套卡面排版尺寸，由卡框皮肤（或「没有框」）算出来。
        ///
        /// ★ 实现 <see cref="System.IEquatable{T}"/> 是为了**运行中调参**：
        ///   <see cref="Refresh"/> 每帧拿新算的一份与已生效的一份比，不等才重排。
        ///   有了这一步，卡框的缩进就能在 Play 模式里拖着调、当场看效果，
        ///   不必「改一个数 → 退出 Play → 再进 → 再看」地来回猜。
        /// </summary>
        private readonly struct FrameInset : System.IEquatable<FrameInset>
        {
            public readonly float Side, Top, Bottom;

            /// <summary>这张卡到底有没有框。所有「有框才这样」的分支都问它，不要去看 Side &gt; 0。</summary>
            public readonly bool Framed;

            /// <summary>名字栏高。有框时来自皮肤（可调），无框时是 <see cref="CardView.NameBarHeight"/>。</summary>
            public readonly float NameBarHeight;

            /// <summary>色点离卡底 / 离右边的距离。有框时收紧，那几像素要留给插画。</summary>
            public readonly float DotMargin;

            public readonly float DescHeight;

            public FrameInset(CardFrameSkin skin)
            {
                Framed = skin != null;
                Side = skin != null ? skin.InsetSide * HandFanLayout.CardWidth : 0f;
                Top = skin != null ? skin.InsetTop * HandFanLayout.CardHeight : 0f;
                Bottom = skin != null ? skin.InsetBottom * HandFanLayout.CardHeight : 0f;

                NameBarHeight = skin != null ? skin.NameBarHeight : CardView.NameBarHeight;
                DescHeight = skin != null ? skin.DescHeight : CardView.DescHeight;
                DotMargin = skin != null ? skin.DotMargin : CardView.DotMargin;
            }

            public bool Equals(FrameInset o)
                => Framed == o.Framed
                   && Side == o.Side && Top == o.Top && Bottom == o.Bottom
                   && NameBarHeight == o.NameBarHeight && DescHeight == o.DescHeight
                   && DotMargin == o.DotMargin;

            public override bool Equals(object o) => o is FrameInset f && Equals(f);

            public override int GetHashCode()
                => Side.GetHashCode() ^ Top.GetHashCode() ^ Bottom.GetHashCode()
                   ^ NameBarHeight.GetHashCode() ^ DescHeight.GetHashCode() ^ DotMargin.GetHashCode();

            /// <summary>名字栏顶边离卡顶多远。有框时内边距收窄——框本身就是视觉上的留白。</summary>
            public float NameTop => Top + (Framed ? 2f : NameBarTop);

            /// <summary>插画窗顶边离卡顶多远。</summary>
            public float ArtTop => NameTop + NameBarHeight + NameToArtGap;

            /// <summary>
            /// 描述区底边离卡底多远。
            /// ★ 由关键字色点**反推**，不是拍一个数：色点摆在 Bottom + DotMargin 处、高 DotSize，
            ///   描述末行必须落在它上面。无框时这条式子恰好等于原来写死的 26。
            /// </summary>
            public float DescBottom => Bottom + DotMargin + DotSize + 3f;

            /// <summary>
            /// 名字区左边缘。
            /// ★ 无框时要给**卡内**的费用球让位；有框时费用球整个探到卡外，不再抢名字的地方，
            ///   于是名字可以在安全区里左右对称——不让的话名字会莫名其妙地偏右 11 像素。
            /// </summary>
            public float NameLeft => Framed
                ? Side + 6f
                : Mathf.Max(CostSize - CostOverhang + 6f, 6f);

            public float NameRight => Side + 6f;

            /// <summary>
            /// 插画窗高度 = 卡高 − 上面那些固定块。**改卡牌尺寸 / 换框时唯一会自动变的那个。**
            /// ★ Mathf.Max 兜底：卡太矮或框太厚时这个式子会变成负数，
            ///   届时插画窗退化成一条 40 高的窄带，而不是把 RectTransform 弄成负尺寸。
            /// </summary>
            public float ArtHeight => Mathf.Max(40f,
                HandFanLayout.CardHeight - ArtTop - ArtGap - DescHeight - DescBottom);
        }

        // ---- 费用球
        //
        // ★ 它是**溢出到卡外**的：直径 62 的圆，左上各探出 16 像素，
        //   于是名字栏不必再给它让出一整行的高度（最早那版 48 里有 44 是被费用球撑的），
        //   名字独占一条 30 的窄带。
        //
        // ★ 探出去的那一圈不会被邻牌盖住：兄弟顺序是左→右递增（右边的牌压左边的），
        //   而费用球探的是自己的**左**边——它落在左邻居身上，而自己在左邻居之上。
        private const float CostSize = 62f;
        private const float CostOverhang = 16f;

        // ---- 关键字色点（右下角）
        //
        // ★ 「消耗 / 保留 / 固有 / 虚无」原本是底部类型栏尾巴上的一串文字，
        //   连同类型栏一起被删掉了。改成四个固定配色的小圆点：
        //   词条的**名字与解释**本来就在悬停提示里（TooltipContent.BuildForCard 会输出它们），
        //   卡面上只需要一个「这张牌有点特别」的可见标记。
        private const float DotSize = 15f;
        private const float DotGap = 6f;
        private const float DotMargin = 8f;

        /// <summary>色点的顺序 = 位顺序，与 <see cref="TooltipContent"/> 里的 AllKeywords 一致。</summary>
        private static readonly CardKeyword[] DotKeywords =
        {
            CardKeyword.Exhaust, CardKeyword.Retain, CardKeyword.Innate, CardKeyword.Ethereal
        };

        private static readonly Color[] DotColors =
        {
            new Color(1.00f, 0.50f, 0.28f),   // 消耗：橙
            new Color(0.42f, 0.86f, 0.88f),   // 保留：青
            new Color(1.00f, 0.84f, 0.38f),   // 固有：金
            new Color(0.76f, 0.58f, 1.00f),   // 虚无：紫
        };

        /// <summary>与 <see cref="DotKeywords"/> 一一对应的圆点节点。不亮的那些 SetActive(false)。</summary>
        private RectTransform[] _dots;

        // ---- 悬停倾斜（跟着光标转的伪 3D）
        //
        // ★★ 先说清楚这套东西**为什么不是纯粹的 X/Y 旋转**，否则下一个人会把补偿项当成多余的删掉：
        //
        //    界面的 Canvas 走的是**正交投影**。
        //    ★ 注意这里说的不再是「因为它是 Overlay」——Canvas 已经改成 ScreenSpaceCamera
        //      并接进了 URP 的后处理（见 <see cref="UIRenderSetup"/>）。
        //      但那台 UI 相机是**正交**的，所以下面这条限制**一个字都没变**。
        //      要拿到真透视，得把 UIRenderSetup 里的相机改成透视相机，
        //      而那会牵动整套 UI 的尺寸推导（CanvasScaler 的参考分辨率、
        //      HandWidth 那一串按屏幕宽推出来的常量），是独立的一件事。
        //    于是绕 Y 轴转 10° 在屏幕上的全部效果就是「水平方向乘了个 cos(10°) = 0.985」，
        //    也就是**宽度缩掉 1.5%**，肉眼基本看不出来，更不会有「卡片朝我倾过来」的感觉。
        //    只写 <c>localRotation</c> 就收工的话，这个功能等于没做。
        //
        //    补上的两项都是在**仿射变换以内**能做到的透视线索：
        //
        //    ① <see cref="TiltShift"/>：卡整体朝光标方向平移几个像素。
        //       透视投影下，一个离观察者 d 远的物体绕轴转 θ，其投影的一阶近似正是
        //       「转动 + 一个正比于 sinθ 的横移」。这一项是三者里贡献最大的——
        //       眼睛对「跟着我走」比对「压扁了 1.5%」敏感得多。
        //    ② <see cref="_glow"/>：跟着光标走的一块高光。真实卡片的立体感有相当大比例
        //       来自光泽随视角移动，而这一项在正交投影下是**100% 有效**的，
        //       不受上面那个限制影响。
        //
        // ★ 等哪天 UI 相机换成透视相机，这里的旋转会自动变好看，
        //   而 ① 和 ② 与真透视并不冲突，可以原样留着（透视本来就同时包含这两项）。

        /// <summary>倾斜的最大角度。★ 再大就会露馅：正交投影下大角度只是被压扁，不像转。</summary>
        private const float MaxTiltDegrees = 10f;

        /// <summary>倾斜到最大时卡整体朝光标方向平移多少像素。见上面 ① 的推导。</summary>
        private const float TiltShift = 9f;

        /// <summary>高光圆斑的直径（相对卡宽）。比卡宽大，让光斑边缘落在卡外、卡面上只见渐变。</summary>
        private const float GlowSizeRatio = 1.35f;

        /// <summary>高光最亮时的 alpha。★ 压得很低：它盖在描述文字上，再亮就开始吃可读性。</summary>
        private const float GlowAlpha = 0.15f;

        /// <summary>倾斜量与高光的收敛速度。比位姿的 <see cref="FollowSpeed"/> 快，跟手才不粘。</summary>
        private const float TiltFollowSpeed = 22f;

        /// <summary>高光图。挂在裁到卡面的 Mask 底下，见 <see cref="Create"/>。</summary>
        private Image _glow;

        private RectTransform _glowRt;

        /// <summary>高光那棵子树的根。不悬停时整棵关掉，省下 Mask 的模板缓冲开销。</summary>
        private GameObject _glowMaskGo;

        /// <summary>本帧是否该跟着光标倾斜。由 <see cref="BattleScreen.LayoutHand"/> 每帧写。</summary>
        private bool _tiltWanted;

        /// <summary>已经生效的倾斜角（X/Y），朝目标插值。</summary>
        private float _tiltX, _tiltY;

        /// <summary>倾斜带来的附加平移。★ 它是**附加项**，不参与位置插值，理由同 <see cref="_scale"/>。</summary>
        private Vector2 _tiltOffset;

        private float _glowAlpha;

        /// <summary>插画窗。这张卡没配图时为 null。</summary>
        private RectTransform _art;

        /// <summary>这张卡用的卡框皮肤。null = 没框。运行中调参时要回头问它当前的数值。</summary>
        private CardFrameSkin _skin;

        /// <summary>当前**已经生效**的那套排版。见 <see cref="ApplyLayout"/>。</summary>
        private FrameInset _layout;

        /// <summary>最右那颗色点的落点（右下角起算，已含卡框安全区的缩进）。由 ApplyLayout 写入。</summary>
        private Vector2 _dotOrigin;

        /// <summary>上一次摆过的关键字位掩码。-1 = 还没摆过。见 <see cref="Refresh"/>。</summary>
        private int _dotMask = -1;

        /// <summary>描述的上色器。★ 每张牌一份——它内部带缓存，见 RichDescription 的类注释。</summary>
        private readonly RichDescription _rich = new RichDescription();

        /// <summary>上一次写进 TMP 的描述文本。用来跳过「没变还硬写一遍」。</summary>
        private string _lastDesc;

        public static CardView Create(Transform parent, BattleScreen screen, CardInstance card)
        {
            // ★ 圆角 + 投影（RoundedStyle.Card）。手牌是**扇形互相压着**的，
            //   在这之前一张牌压在另一张上只能靠底色的深浅差来读，边界很含糊；
            //   给每张牌一圈投影之后，压着的那条边立刻变成一道明确的落差，
            //   「哪张在上面」不用再靠猜。这是本次改动里收益最直接的一处。
            //
            // ★ 配了卡框的牌看不到圆角（框是矩形贴图，会盖住四角），但**投影照样在**——
            //   它画在卡的外面，框盖不到。所以有框无框两种卡的层次感是一致的。
            var rt = UIFactory.CreateRoundedPanel(parent, "Card_" + card.DisplayName,
                ColSkill, RoundedStyle.Card).rectTransform;
            UIFactory.SetSize(rt, HandFanLayout.CardWidth, HandFanLayout.CardHeight);

            // pivot 放底边中点：扇形旋转要绕着「握牌的那一端」转，绕中心转会像风车
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);

            var view = rt.gameObject.AddComponent<CardView>();
            view._rt = rt;
            view._screen = screen;
            view.Card = card;
            view._bg = rt.GetComponent<Image>();

            // 见 HoverPadBelow 的注释。alpha 不能是 0 —— Image 完全透明时看不出来，
            // 但只要 raycastTarget 为真它照样收事件；这里留一点 alpha 便于调试时看见范围。
            var pad = UIFactory.CreatePanel(rt, "HoverPad", new Color(0f, 0f, 0f, 0.004f));
            UIFactory.SetAnchored(pad, Vector2.zero, Vector2.one,
                new Vector2(0f, -HoverPadBelow), Vector2.zero);

            // ---- 卡框皮肤。null = 这类卡没配框，下面所有 inset 归零，排版与接框之前逐像素相同。
            var skin = screen != null && screen.Database != null
                ? screen.Database.GetCardFrame(card.Type)
                : null;
            var inset = new FrameInset(skin);

            // ============================================================
            // ★★ 从这里到色点，**建立的先后顺序 = 遮挡顺序**，四段都不能换位置：
            //    插画 → 卡框 → 名字 / 描述 / 色点 → 费用球。
            //
            //    ① 插画最先：它是唯一允许**探到卡边**的元素（横向铺满整卡），
            //       让框的花饰边压在插画的外圈上，而不是把插画挤进开口里再缩一圈。
            //    ② 卡框压住插画的外圈。
            //    ③ 文字全部排在框的安全区**里面**，所以理论上跟框不重叠；
            //       建在框之后是留一道保险——inset 万一配小了，宁可字压在花饰上（还能读），
            //       也不要字被花饰盖掉（读不到，且看起来像 bug）。
            //    ④ 费用球最后：它探到卡外，正落在框左上角的花饰上，必须压在最上面。
            // ============================================================

            // ---- 插画窗（只在这张卡真的配了图时才建）
            //
            // ★ 没配图的卡插画窗整条不建，描述区一直顶到名字下面——
            //   多出来的高度全给描述，而不是留一个空窗在那里。
            //   卡池里绝大多数卡还没有图，一个空插画窗会让「没配图」这件事变成满屏的破洞。
            //
            // ★ 有框时插画**横向铺满整张卡**（不留 ArtSideMargin）：外圈会被框的花饰边盖住，
            //   若还按无框那样先缩进 6 再被框吃掉 23，可见宽度会白白少一截。见 ArtWidthFor。
            view._art = UIFactory.CreateArtWindow(rt, "Art", card.Def != null ? card.Def.Art : null,
                ArtWidthFor(inset), inset.ArtHeight);

            if (view._art != null)
            {
                // pivot 顶边中点 + 锚在卡顶：往下挂多远由 ApplyLayout 每次设
                view._art.anchorMin = view._art.anchorMax = new Vector2(0.5f, 1f);
                view._art.pivot = new Vector2(0.5f, 1f);
            }

            // ---- 卡框
            //
            // ★ 整张铺满卡面即可、不需要任何偏移量：框图已经裁到它自己的外接盒
            //   （见 Assets/Texture/CardFrames/），而卡的宽高比就是按那个外接盒定的
            //   （HandFanLayout.CardWidth/Height 那段注释）。两边对齐，拉伸不会变形。
            if (skin != null)
            {
                var frame = UIFactory.CreatePanel(rt, "Frame", skin.Tint);
                UIFactory.Stretch(frame);

                var frameImg = frame.GetComponent<Image>();

                // 框不吃射线：它铺满整张卡（放大之后还探到卡外），raycastTarget 为真的话
                // 每次命中都要多算一层，而事件反正会冒泡到卡根上，收在这里没有任何意义。
                // ★ 探到卡外这一点让它更不该吃射线：否则一张牌会抢走邻座牌边缘的点击。
                frameImg.raycastTarget = false;

                view._frame = frameImg;
                view._frameRt = frame;
            }

            // ★ 开自动缩字号：卡名换成英文后会膨胀 1.6–2 倍，而这条带子只有 NameBarHeight 高、
            //   宽度还被费用球（无框时）和框的安全区（有框时）切。见 FrameInset.NameLeft。
            view._nameText = UIFactory.CreateText(rt, "Name", card.DisplayName, 24, TextAnchor.UpperCenter);
            UIFactory.EnableAutoSize(view._nameText, 15f, 24f);

            view._descText = UIFactory.CreateText(rt, "Desc", "", 19, TextAnchor.UpperLeft);

            // ★ 插画窗吃掉了一半描述高度，长描述（「重复 3 次…」「选择弃掉 N 张…」）会溢出。
            //   UIFactory.EnableAutoSize 是第七次会话为本地化写好、但一直没接到任何调用点的那个开关，
            //   这里是它第一个真正的用处：中文长描述与英文（膨胀 1.6–2 倍）同时被它兜住。
            //   ★ 有框时无论有没有插画都要开：安全区把描述区又勒窄了约 30，折行数跟着涨。
            if (view._art != null || inset.Framed) UIFactory.EnableAutoSize(view._descText, 13f, 19f);

            // ---- 关键字色点。四个全建出来，摆位交给 ApplyLayout / RefreshKeywordDots。
            view._dots = new RectTransform[DotKeywords.Length];
            for (int i = 0; i < DotKeywords.Length; i++)
            {
                var dot = UIFactory.CreateCircle(rt, "Kw_" + DotKeywords[i], DotColors[i]);
                dot.anchorMin = dot.anchorMax = new Vector2(1f, 0f);
                dot.pivot = new Vector2(1f, 0f);
                UIFactory.SetSize(dot, DotSize, DotSize);
                dot.gameObject.SetActive(false);
                view._dots[i] = dot;
            }

            // ---- 费用球：圆形，往卡的左上角外面探出去（见 CostSize 那段注释）。
            //      ★ 建在最后 = 压在框之上。它落在框左上角的花饰上，被盖住就读不到费用了。
            var costBg = UIFactory.CreateCircle(rt, "Cost", new Color(0.08f, 0.08f, 0.10f, 0.95f));
            UIFactory.SetAnchored(costBg, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(-CostOverhang, -(CostSize - CostOverhang)), new Vector2(CostSize - CostOverhang, CostOverhang));
            view._costText = UIFactory.CreateText(costBg, "CostText", "1", 34);
            UIFactory.Stretch(view._costText.rectTransform);

            // ---- 悬停高光。★ 建在**最后** = 压在所有东西之上，真实的光泽本来就盖住一切。
            //
            // ★ 用 <see cref="Mask"/> 而不是 RectMask2D 来裁到卡面：
            //   RectMask2D 是**屏幕轴对齐**的矩形裁剪，而这张牌一直是斜的
            //   （扇形角度 ±15°，再叠上倾斜），裁出来的边会跟卡面斜着交叉，非常显眼。
            //   Mask 走模板缓冲，跟着任何旋转都是对的。代价是一个额外的 draw call，
            //   而同一时刻只有一张牌在悬停，这点代价买的是「不出错」。
            var glowMask = UIFactory.CreatePanel(rt, "GlowMask", Color.white);
            UIFactory.Stretch(glowMask);
            var glowMaskImg = glowMask.GetComponent<Image>();
            glowMaskImg.raycastTarget = false;

            var mask = glowMask.gameObject.AddComponent<Mask>();
            view._glowMaskGo = glowMask.gameObject;

            // ★ 默认关掉整棵高光子树，只在真的悬停时才打开。
            //   Mask 走模板缓冲，即使遮罩图形不画、高光 alpha 为 0，它仍然要为每张牌
            //   多出一对「写模板 / 还原模板」的 draw call——手上十几张牌就是几十个，
            //   而其中至多有一张是被悬停的。
            glowMask.gameObject.SetActive(false);

            // ★ 遮罩图形本身不画出来（否则卡面会被一整块白盖住），但它仍然写模板缓冲。
            //   ★ 颜色必须是**不透明**白：Mask 靠这张图的 alpha 决定哪里透得过去，
            //     照抄 CreateScrollView 里那个 alpha=0.001 的写法会把高光整个裁没，
            //     而表现只是「高光功能好像没生效」，不报任何错。
            mask.showMaskGraphic = false;

            view._glowRt = UIFactory.CreatePanel(glowMask, "Glow", new Color(1f, 0.98f, 0.90f, 0f));
            view._glow = view._glowRt.GetComponent<Image>();
            view._glow.sprite = UIFactory.RadialGlowSprite;
            view._glow.raycastTarget = false;

            view._glowRt.anchorMin = view._glowRt.anchorMax = new Vector2(0.5f, 0f);
            view._glowRt.pivot = new Vector2(0.5f, 0.5f);
            UIFactory.SetSize(view._glowRt,
                HandFanLayout.CardWidth * GlowSizeRatio, HandFanLayout.CardWidth * GlowSizeRatio);

            // 悬停解释这张牌涉及的关键字与状态。挂在卡根上，HoverPad 的射线会冒泡上来。
            TooltipTarget.Attach(rt.gameObject, view);

            view._skin = skin;
            view.ApplyLayout(inset);

            // ★ 贴图 / 染色 / 缩放归 RefreshSkinVisuals 管，但它要到本帧稍后的 RefreshCards 才跑。
            //   这里先催一次，否则这张牌在被创建到被刷新之间是一个「没有贴图的纯色方块」。
            //   实际上 BattleScreen 同一个 LateUpdate 里就会刷到它，中间并不会真的渲染出去——
            //   但那依赖于两个方法的调用顺序，而顺序是别人家的事，不该由本类来赌。
            view.RefreshSkinVisuals(true, false);
            return view;
        }

        /// <summary>
        /// 有框时插画横向铺满整张卡，无框时留 <see cref="ArtSideMargin"/> 的内边距。
        /// 理由见 <see cref="Create"/> 里那段注释。
        /// </summary>
        private static float ArtWidthFor(FrameInset inset)
            => inset.Framed ? HandFanLayout.CardWidth : HandFanLayout.CardWidth - ArtSideMargin * 2f;

        /// <summary>
        /// 把一套排版尺寸落到实际的 RectTransform 上。
        ///
        /// ★★ 刻意做成「可以反复调用」而不是在 <see cref="Create"/> 里一次性摆好：
        ///    卡框的缩进要能在 **Play 模式里拖着调**。这个界面没有 prefab、全是代码搭的，
        ///    所以「所见即所得」不能靠 Unity 的场景编辑，只能靠这条重排通路。
        ///    没有它，每试一个数字都要「改 → 退出 Play → 再进 → 再看」，而卡框贴不贴合
        ///    是个纯目测的问题，一轮猜一个数根本收敛不了。
        ///
        /// ★ 只碰位置和尺寸，不碰任何**内容**（文字、颜色、显隐）——那些归 <see cref="Refresh"/>。
        ///   两边各管一摊，才不会出现「重排顺手把某个状态冲掉了」那类隔了很久才发现的问题。
        /// </summary>
        private void ApplyLayout(FrameInset inset)
        {
            _layout = inset;

            UIFactory.SetAnchored(_nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(inset.NameLeft, -(inset.NameTop + inset.NameBarHeight)),
                new Vector2(-inset.NameRight, -inset.NameTop));

            float descTop = -inset.ArtTop;      // 无图：描述直接顶到名字栏下面
            if (_art != null)
            {
                // ★ 必须走 ResizeArtWindow 而不是只改 sizeDelta：cover 裁切依赖窗口比例，
                //   只改窗不重算图，窗变大会露出背景、变小则裁过头。
                UIFactory.ResizeArtWindow(_art, ArtWidthFor(inset), inset.ArtHeight);
                _art.anchoredPosition = new Vector2(0f, -inset.ArtTop);

                descTop = -(inset.ArtTop + inset.ArtHeight + ArtGap);
            }

            UIFactory.SetAnchored(_descText.rectTransform, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(inset.Side + 10f, inset.DescBottom),
                new Vector2(-(inset.Side + 10f), descTop));

            // ★ 有框时描述**居中**而不是靠上靠左。
            //   靠上对齐时，绝大多数卡只有一行描述（「获得 5 点护甲。」），那一行会贴在插画下沿，
            //   它下面到卡底整整 100 像素全是空底色——看起来像排版塌了，而不是留白。
            //   居中之后短描述落在下半区的正中，与框底的花饰对称，空白变成构图的一部分。
            UIFactory.SetAlignment(_descText, inset.Framed ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft);

            // 色点只改落点。★ 把掩码作废，逼 RefreshKeywordDots 下一帧照新落点重摆一次——
            //   否则掩码没变（关键字确实没变），色点会留在旧位置上不动。
            _dotOrigin = new Vector2(-(inset.Side + inset.DotMargin), inset.Bottom + inset.DotMargin);
            _dotMask = -1;
        }

        /// <summary>
        /// 悬停时解释这张牌涉及的关键字（消耗 / 保留 …）与状态（易伤 / 虚弱 …）。
        /// 状态是**扫效果树**得到的，不是从描述文字里猜的——见 <see cref="TooltipContent"/>。
        /// </summary>
        public bool BuildTooltip(List<TooltipEntry> buffer)
            => TooltipContent.BuildForCard(Card, _screen != null ? _screen.Database : null, buffer);

        /// <summary>每帧刷新（费用会被状态/遗物改，描述会随力量变化）。</summary>
        public void Refresh(BattleContext ctx, bool playable, bool selected)
        {
            if (Card == null) return;

            // ★ 运行中调参：在 Inspector 里拖 CardFrameSkin 的滑条，屏幕上的牌当场重排。
            //
            // ★ 刻意**不**包 #if UNITY_EDITOR。出包之后这几个数确实不会再变，包起来能省一点，
            //   但省下的是「一次结构体构造 + 7 次 float 比较 × 至多十几张牌」——四舍五入是零，
            //   而代价是编辑器与出包走两条不同的代码路径。这种分叉出问题时只在出包后才看得见，
            //   为这点开销买它不划算。将来若真有运行时换框的需求，这里也已经天然支持。
            if (_skin != null)
            {
                var wanted = new FrameInset(_skin);
                if (!wanted.Equals(_layout)) ApplyLayout(wanted);
            }

            _costText.text = Card.GetCostText(ctx);
            _nameText.text = Card.DisplayName + (Card.UpgradeLevel > 0 ? "+" : "");

            // ★ 富文本描述：伤害红、护甲蓝、词条名按类型上色。见 RichDescription。
            //   装饰器**每张牌各持一份**（它带着模板染色的缓存），不共享。
            _rich.SetCard(Card, _screen != null ? _screen.Database : null);
            string desc = Card.GetDescription(ctx, ctx?.Player, null, _rich);

            // ★ 只在真的变了才写。TMP 的 text setter 会触发一次文本重解析 + 网格重建，
            //   而富文本让每次重解析都更贵（要扫标签）。绝大多数帧里描述是一个字都没变的
            //   ——它只在力量层数、能量、手牌数变化时才会变。
            if (!string.Equals(_lastDesc, desc))
            {
                _lastDesc = desc;
                _descText.text = desc;
            }

            RefreshKeywordDots();
            RefreshSkinVisuals(playable, selected);

            // ★ 缩放不在这里设：它属于位姿，由 SetLayoutTarget / Update 统一管。
            //   两处都写 localScale 会互相打架（一处每帧设 1，一处每帧插值到 1.1）。
        }

        /// <summary>
        /// 卡面底图 / 卡框的贴图、染色与缩放。
        ///
        /// ★ **每帧无条件重读皮肤**，不缓存。与 <see cref="ApplyLayout"/> 那边「比对了才重排」
        ///   的做法不同，是因为这几项的写入本身就自带幂等短路：
        ///   <c>Image.sprite</c> 与 <c>Image.color</c> 的 setter 在值没变时直接 return，
        ///   缩放这一项则在下面显式比对过。于是「让它能在 Play 里拖着调」这件事是白送的，
        ///   不需要再维护一份「上次用的是什么」的影子状态（那种状态漏更新时极难看出来）。
        ///
        /// ★ 打不出 / 选中这两种反馈**压在最后**，作用于底图和卡框两层。
        ///   只压底色的话，打不出的牌是「暗底 + 全亮的花框」，而框占了整个外圈——
        ///   那一圈亮色会盖过底色想传达的「这张打不出」。
        /// </summary>
        private void RefreshSkinVisuals(bool playable, bool selected)
        {
            // ---- 底图。没配底图时退回按类型写死的纯色：类型栏已经删掉，
            //      底色是「这是攻击牌还是技能牌」的唯一表达（见 ColStatus 那段注释）。
            Sprite bgSprite = _skin != null ? _skin.Background : null;
            Color bgTint = bgSprite != null ? _skin.BackgroundTint : TypeColor(Card.Type);

            SetSprite(_bg, bgSprite);
            _bg.color = Shade(bgTint, playable, selected);

            // ---- 卡框
            if (_frame == null) return;

            SetSprite(_frame, _skin.Frame);
            _frame.color = Shade(_skin.Tint, playable, selected);

            var scale = new Vector3(_skin.FrameScale.x, _skin.FrameScale.y, 1f);
            if (_frameRt.localScale != scale) _frameRt.localScale = scale;
        }

        /// <summary>
        /// 换贴图，并按它有没有九宫格边距自动挑 Sliced / Simple。
        /// ★ 自动挑而不是让人在 Inspector 里配：选错的表现是花饰边被拉糊或四角被压扁，
        ///   而这个判断完全可以从 sprite 自己身上读出来，没有让人做选择题的必要。
        /// </summary>
        private static void SetSprite(Image img, Sprite sprite)
        {
            img.sprite = sprite;      // setter 自带「没变就 return」
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : Image.Type.Simple;
        }

        /// <summary>
        /// 打不出 → 压暗；选中 → 提亮。
        /// ★ 打不出是「把原色压暗」而不是「换成一个统一的灰」：一律 ColDisabled 会让手上
        ///   所有打不出的牌（能量不够的攻击、诅咒、状态）在画面上糊成同一块灰。
        /// </summary>
        private static Color Shade(Color c, bool playable, bool selected)
        {
            if (!playable) c = Color.Lerp(c, ColDisabled, 0.65f);
            if (selected) c = Color.Lerp(c, Color.white, 0.35f);
            return c;
        }

        /// <summary>
        /// 右下角那排关键字色点。
        ///
        /// ★ 只在位掩码**变化时**才动 SetActive / anchoredPosition：
        ///   Refresh 是每帧跑的，而写 RectTransform 会把 Canvas 标脏并触发重建批次。
        ///   关键字整场战斗基本不变（少数效果会临时挂「消耗」），
        ///   无条件每帧摆四个点等于给一把手牌白白付几十次重建。
        ///
        /// ★ 色点没有文字。词条的名字与解释由悬停提示给出——
        ///   <see cref="BuildTooltip"/> → <see cref="TooltipContent.BuildForCard"/> 本来就会输出它们，
        ///   而且用的是 <c>card.Def.Keywords | card.ExtraKeywords</c>，与这里的 HasKeyword 同源。
        /// </summary>
        private void RefreshKeywordDots()
        {
            if (_dots == null) return;

            int mask = 0;
            for (int i = 0; i < DotKeywords.Length; i++)
                if (Card.HasKeyword(DotKeywords[i])) mask |= 1 << i;

            if (mask == _dotMask) return;
            _dotMask = mask;

            // slot 从右往左数：亮着的点必须挨在一起，不能按位序留出空档
            int slot = 0;
            for (int i = 0; i < DotKeywords.Length; i++)
            {
                var dot = _dots[i];
                if (dot == null) continue;

                bool on = (mask & (1 << i)) != 0;
                if (dot.gameObject.activeSelf != on) dot.gameObject.SetActive(on);
                if (!on) continue;

                dot.anchoredPosition = new Vector2(_dotOrigin.x - slot * (DotSize + DotGap), _dotOrigin.y);
                slot++;
            }
        }

        // ============================================================ 位姿

        private Vector2 _targetPos;
        private float _targetRot;
        private float _targetScale = 1f;

        /// <summary>当前缩放的「干净值」——不含落位弹跳的附加系数。见 <see cref="Update"/>。</summary>
        private float _scale = 1f;

        /// <summary>
        /// 当前位置的「干净值」——不含悬停倾斜的附加平移。
        ///
        /// ★ 与 <see cref="_scale"/> 存在的理由**一模一样**：插值的源必须是没被附加项污染过的值。
        ///   直接读 <c>_rt.anchoredPosition</c> 的话，上一帧的倾斜位移会被喂回插值，
        ///   于是光标横向扫过一排牌时，每张牌都会朝光标方向「漂」出去一点点再慢慢爬回来。
        /// </summary>
        private Vector2 _pos;

        /// <summary>
        /// 位置是否当帧直接生效（不插值）。自由拖拽时必须为 true——
        /// 插值会让牌明显落在光标后面，像挂了根橡皮筋。
        /// 「举牌 + 拉箭头」那种模式反而要插值，牌是飞上去的。
        /// </summary>
        public bool SnapPosition { get; set; }

        /// <summary>由 BattleScreen 每帧写入的目标位姿。</summary>
        public void SetLayoutTarget(Vector2 position, float rotation, float scale)
        {
            _targetPos = position;
            _targetRot = rotation;
            _targetScale = scale;

            // Update 在 LateUpdate 之前跑，所以拖拽时得当场写一次，否则永远晚一帧
            if (SnapPosition)
            {
                _pos = position;
                _rt.anchoredPosition = position + _tiltOffset;
            }
        }

        /// <summary>把当前位姿直接钉在某处（建牌时当飞入动画的起点用）。</summary>
        public void SnapTo(Vector2 position, float rotation, float scale)
        {
            _rt.anchoredPosition = position;
            _rt.localRotation = Quaternion.Euler(0f, 0f, rotation);
            _rt.localScale = Vector3.one * scale;

            // ★ _scale 必须跟着钉，否则下一帧 Update 会从上一个 _scale 开始插值，
            //   牌会先「跳」回原来的大小再飞出去。
            _scale = scale;
            _pos = position;
            _punchLeft = 0f;

            // ★ 倾斜也要一并清零：这张牌刚从抽牌堆冒出来，不该带着上一个占用者的姿态。
            //   （视图会按 Uid 复用，_tilt* 是跨牌存活的字段。）
            _tiltX = _tiltY = 0f;
            _tiltOffset = Vector2.zero;
            _glowAlpha = 0f;

            SetLayoutTarget(position, rotation, scale);
        }

        // ---- 落位弹跳
        //
        // ★ 为什么不用 DOTween.DOPunchScale：本组件的 Update 每帧都在
        //   Lerp(localScale, one * _targetScale)，tween 也每帧写 localScale，
        //   两者会逐帧对着打，缩放永远到不了任何一个目标值。这正是铁律 23 记下的坑。
        //   改成「插值结果 × 一个自己衰减的附加系数」，位姿的出口仍然只有下面这个 Update。

        private const float PunchDuration = 0.22f;

        /// <summary>剩余弹跳时间，> 0 表示正在弹。</summary>
        private float _punchLeft;

        /// <summary>本次弹跳的峰值幅度（0.12 = 最大鼓到 112%）。</summary>
        private float _punchAmount;

        /// <summary>弹一下。幅度已在调用方乘过无障碍开关。外部一律走 <see cref="ArmArrivalPunch"/>。</summary>
        private void Punch(float amount)
        {
            if (amount <= 0.001f) return;
            _punchAmount = amount;
            _punchLeft = PunchDuration;
        }

        /// <summary>「飞到位之后再弹」。0 = 不弹。</summary>
        private float _armedPunch;

        /// <summary>牌落进扇形还差多少像素就算到位了。</summary>
        private const float ArriveDistance = 26f;

        /// <summary>
        /// 预约一次落位弹跳：这张牌从抽牌堆飞进扇形、**真的到位那一刻**才弹。
        ///
        /// ★ 为什么由 CardView 自己判定到位，而不是 BattleScreen 建完牌就 <see cref="Punch"/>：
        ///   建牌的那一刻牌还在屏幕左下角的抽牌堆上，在那里弹一下玩家根本注意不到；
        ///   而「飞到了没有」只有插值的持有者知道——飞行时长取决于 <see cref="FollowSpeed"/>
        ///   与出发点到目标的距离，BattleScreen 那边估不准，也不该去估（铁律 23）。
        /// </summary>
        public void ArmArrivalPunch(float amount) => _armedPunch = amount;

        private void Update()
        {
            float k = 1f - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime);

            UpdateTilt();

            if (!SnapPosition)
                _pos = Vector2.Lerp(_pos, _targetPos, k);

            _rt.anchoredPosition = _pos + _tiltOffset;

            // ★ 倾斜的 X/Y 直接进同一个 Slerp 的目标里，而不是另起一条通路去乘 localRotation：
            //   位姿的出口必须只有这一处（铁律 23）。
            _rt.localRotation = Quaternion.Slerp(_rt.localRotation,
                Quaternion.Euler(_tiltX, _tiltY, _targetRot), k);

            // 飞到位了 → 兑现预约的那一次弹跳。★ 比的是干净位置，不是 _rt.anchoredPosition：
            // 悬停时那上面挂着倾斜位移，牌明明已经落位却因为差了几个像素而迟迟不弹。
            if (_armedPunch > 0f
                && Vector2.Distance(_pos, _targetPos) <= ArriveDistance)
            {
                Punch(_armedPunch);
                _armedPunch = 0f;
            }

            // ★ 插值的源必须是**没被弹跳污染过**的 _scale，不能直接读 _rt.localScale：
            //   读回来的值里含着上一帧的弹跳系数，弹跳就会反过来喂进插值，
            //   表现是弹一下之后缩放要拖好几帧才回得去。
            _scale = Mathf.Lerp(_scale, _targetScale, k);

            // 单峰正弦：t 从 0 走到 1，系数 1 → 1+amount → 1，两端严丝合缝地接回插值结果
            float punch = 1f;
            if (_punchLeft > 0f)
            {
                _punchLeft -= Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(1f - _punchLeft / PunchDuration);
                punch = 1f + _punchAmount * Mathf.Sin(t * Mathf.PI);
            }

            _rt.localScale = Vector3.one * (_scale * punch);
        }

        // ============================================================ 悬停倾斜

        /// <summary>
        /// 本帧这张牌该不该跟着光标倾斜。由 <see cref="BattleScreen.LayoutHand"/> 每帧写。
        ///
        /// ★ 由 BattleScreen 来发这个开关、而不是 CardView 自己读 <see cref="Hovered"/>：
        ///   「谁在被悬停」这件事已经收口在 LayoutHand 里了（铁律 28），
        ///   同一帧里两张牌的 Hovered 都为真是完全可能的（进/出事件的顺序不由我们控制），
        ///   自己读的话会出现两张牌一起倾斜。
        ///
        /// ★ 拖拽中一律传 false：那时牌被抓在手上跟着光标走，再叠一层「朝光标倾」
        ///   就变成两个系统抢同一个输入，而且倾斜位移会把抓取点算偏。
        /// </summary>
        public void SetTiltWanted(bool on) => _tiltWanted = on;

        /// <summary>
        /// 这张牌要离场了，把悬停附加的东西收干净。
        ///
        /// ★ 必须由外部显式调，不能指望 <see cref="UpdateTilt"/> 自己收敛回去：
        ///   离场的第一件事就是 <c>view.enabled = false</c>（见 <see cref="CardFlyOut.Detach"/>），
        ///   Update 从那一刻起再也不跑了。不收的话，一张正被悬停时打出去的牌
        ///   会带着那块高光一路飞出去 / 一路烧到底。
        ///
        /// ★ 位置不用管：<see cref="_tiltOffset"/> 那几个像素已经算在当前的世界坐标里，
        ///   而 Detach 是按**世界坐标**搬家的，清掉字段不会让牌跳。
        /// </summary>
        public void ClearHoverFx()
        {
            _tiltWanted = false;
            _tiltX = _tiltY = 0f;
            _tiltOffset = Vector2.zero;
            _glowAlpha = 0f;
            if (_glowMaskGo != null) _glowMaskGo.SetActive(false);
        }

        /// <summary>
        /// 算这一帧的倾斜角、附加平移与高光位置。★ 三样都只写字段，实际落到 Transform 上
        /// 是 <see cref="Update"/> 那一处的事（铁律 23）。
        /// </summary>
        private void UpdateTilt()
        {
            float nx = 0f, ny = 0f;
            bool active = false;

            if (_tiltWanted && _rt != null)
            {
                // ★ 相机走 UIFactory.CanvasCamera：Overlay 下它是 null（那正是 Overlay 要的），
                //   接了管线之后是那台 UI 相机。写死 null 的话，倾斜量会随光标离屏幕中心越远越离谱。
                // ★ 拿 _rt 自己当参照系是有反馈的（倾斜 → 局部坐标变 → 倾斜再变），
                //   但增益远小于 1（10° 的仿射形变对局部坐标的影响是百分之几），
                //   再加上下面那一层指数插值，实测是收敛的，不会自激。
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _rt, InputCompat.PointerPosition, UIFactory.CanvasCamera, out var lp))
                {
                    // _rt 的 pivot 在底边中点，所以 lp 的原点就在卡底中央
                    nx = Mathf.Clamp(lp.x / (HandFanLayout.CardWidth * 0.5f), -1f, 1f);
                    ny = Mathf.Clamp((lp.y - HandFanLayout.CardHeight * 0.5f)
                                     / (HandFanLayout.CardHeight * 0.5f), -1f, 1f);
                    active = true;

                    if (_glowRt != null) _glowRt.anchoredPosition = lp;
                }
            }

            // 光标在右 → 卡的右缘朝里倒（绕 Y 正转）；光标在上 → 上缘朝里倒（绕 X 负转）
            float wantTiltX = active ? -ny * MaxTiltDegrees : 0f;
            float wantTiltY = active ? nx * MaxTiltDegrees : 0f;
            var wantOffset = active ? new Vector2(nx, ny) * TiltShift : Vector2.zero;
            float wantGlow = active ? GlowAlpha : 0f;

            float k = 1f - Mathf.Exp(-TiltFollowSpeed * Time.unscaledDeltaTime);

            _tiltX = Mathf.Lerp(_tiltX, wantTiltX, k);
            _tiltY = Mathf.Lerp(_tiltY, wantTiltY, k);
            _tiltOffset = Vector2.Lerp(_tiltOffset, wantOffset, k);
            _glowAlpha = Mathf.Lerp(_glowAlpha, wantGlow, k);

            // ★ 指数插值永远只是趋近 0，不会到 0。不掐掉这个尾巴的话，
            //   离开悬停之后的每一帧都还在写一个「差别看不见但确实不等」的 alpha，
            //   于是这张牌的 Canvas 批次**再也不会安静下来**。
            if (wantGlow <= 0f && _glowAlpha < 0.002f) _glowAlpha = 0f;

            // 整棵子树的开关。★ 只在进 / 出悬停时各切一次，不是每帧写。
            if (_glowMaskGo != null)
            {
                bool on = _glowAlpha > 0f;
                if (_glowMaskGo.activeSelf != on) _glowMaskGo.SetActive(on);
            }

            if (_glow != null)
            {
                var c = _glow.color;

                // ★ 只在真的变了才写：Image.color 的 setter 会把 Canvas 标脏，
                //   而手上十几张牌里有十几个高光节点，每帧无条件写等于每帧重建一次批次。
                //   收敛之后 _glowAlpha 会稳定在 0，那些没被悬停的牌就一次都不写。
                if (!Mathf.Approximately(c.a, _glowAlpha))
                {
                    c.a = _glowAlpha;
                    _glow.color = c;
                }
            }
        }

        // ============================================================ 输入

        /// <summary>鼠标是否悬停。BattleScreen 排列手牌时读它，决定抬高多少。</summary>
        public bool Hovered { get; private set; }

        public void OnPointerClick(PointerEventData e)
        {
            // 拖拽结束时 EventSystem 本来就不会再发 click（eligibleForClick 已被清掉），
            // 这里再挡一道，免得将来换输入模块时冒出「拖完还额外点了一下」的怪事
            if (e.dragging) return;
            _screen.OnCardClicked(this);
        }

        // ★ 只记在自己身上，不去通知 BattleScreen「现在悬停的是我」。
        //   进/出事件的先后顺序不由我们控制，靠通知维护一个「当前悬停者」字段，
        //   遇到 Enter(B) 先于 Exit(A) 的顺序就会把它清成 null。
        //   BattleScreen 每帧扫一遍 Hovered，与事件顺序无关。
        public void OnPointerEnter(PointerEventData e) => Hovered = true;
        public void OnPointerExit(PointerEventData e) => Hovered = false;

        public void OnBeginDrag(PointerEventData e) => _screen.OnCardBeginDrag(this, e);
        public void OnDrag(PointerEventData e) => _screen.OnCardDrag(this, e);
        public void OnEndDrag(PointerEventData e) => _screen.OnCardEndDrag(this, e);

        // ============================================================ 配色

        private static Color TypeColor(CardType t) => t switch
        {
            CardType.Attack => ColAttack,
            CardType.Skill => ColSkill,
            CardType.Power => ColPower,
            CardType.Curse => ColCurse,
            CardType.Status => ColStatus,
            _ => ColSkill
        };
    }
}
