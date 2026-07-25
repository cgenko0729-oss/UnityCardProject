using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
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
        private Text _costText;
        private Text _nameText;
        private Text _descText;
        private Text _typeText;
        private RectTransform _rt;

        private static readonly Color ColAttack = new Color(0.55f, 0.20f, 0.20f);
        private static readonly Color ColSkill = new Color(0.20f, 0.35f, 0.55f);
        private static readonly Color ColPower = new Color(0.42f, 0.28f, 0.55f);
        private static readonly Color ColCurse = new Color(0.25f, 0.25f, 0.25f);
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

        public static CardView Create(Transform parent, BattleScreen screen, CardInstance card)
        {
            var rt = UIFactory.CreatePanel(parent, "Card_" + card.DisplayName, ColSkill);
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

            var costBg = UIFactory.CreatePanel(rt, "Cost", new Color(0.1f, 0.1f, 0.1f, 0.9f));
            UIFactory.SetAnchored(costBg, new Vector2(0, 1), new Vector2(0, 1), new Vector2(4, -44), new Vector2(44, -4));
            view._costText = UIFactory.CreateText(costBg, "CostText", "1", 24);
            UIFactory.Stretch(view._costText.rectTransform);

            view._nameText = UIFactory.CreateText(rt, "Name", card.DisplayName, 20, TextAnchor.UpperCenter);
            UIFactory.SetAnchored(view._nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(48, -42), new Vector2(-4, -6));

            view._descText = UIFactory.CreateText(rt, "Desc", "", 16, TextAnchor.UpperLeft);
            UIFactory.SetAnchored(view._descText.rectTransform, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(8, 26), new Vector2(-8, -50));

            view._typeText = UIFactory.CreateText(rt, "Type", "", 14, TextAnchor.LowerCenter,
                new Color(1f, 1f, 1f, 0.6f));
            UIFactory.SetAnchored(view._typeText.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(4, 4), new Vector2(-4, 24));

            // 悬停解释这张牌涉及的关键字与状态。挂在卡根上，HoverPad 的射线会冒泡上来。
            TooltipTarget.Attach(rt.gameObject, view);

            return view;
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

            _costText.text = Card.GetCostText(ctx);
            _nameText.text = Card.DisplayName + (Card.UpgradeLevel > 0 ? "+" : "");
            _descText.text = Card.GetDescription(ctx, ctx?.Player, null);

            string kw = "";
            if (Card.HasKeyword(CardKeyword.Exhaust)) kw += " 消耗";
            if (Card.HasKeyword(CardKeyword.Retain)) kw += " 保留";
            if (Card.HasKeyword(CardKeyword.Innate)) kw += " 固有";
            if (Card.HasKeyword(CardKeyword.Ethereal)) kw += " 虚无";
            _typeText.text = TypeLabel(Card.Type) + kw;

            Color baseColor = playable ? TypeColor(Card.Type) : ColDisabled;
            if (selected) baseColor = Color.Lerp(baseColor, Color.white, 0.35f);
            _bg.color = baseColor;

            // ★ 缩放不在这里设：它属于位姿，由 SetLayoutTarget / Update 统一管。
            //   两处都写 localScale 会互相打架（一处每帧设 1，一处每帧插值到 1.1）。
        }

        // ============================================================ 位姿

        private Vector2 _targetPos;
        private float _targetRot;
        private float _targetScale = 1f;

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
            if (SnapPosition) _rt.anchoredPosition = position;
        }

        /// <summary>把当前位姿直接钉在某处（建牌时当飞入动画的起点用）。</summary>
        public void SnapTo(Vector2 position, float rotation, float scale)
        {
            _rt.anchoredPosition = position;
            _rt.localRotation = Quaternion.Euler(0f, 0f, rotation);
            _rt.localScale = Vector3.one * scale;
            SetLayoutTarget(position, rotation, scale);
        }

        private void Update()
        {
            float k = 1f - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime);

            if (!SnapPosition)
                _rt.anchoredPosition = Vector2.Lerp(_rt.anchoredPosition, _targetPos, k);

            _rt.localRotation = Quaternion.Slerp(_rt.localRotation,
                Quaternion.Euler(0f, 0f, _targetRot), k);
            _rt.localScale = Vector3.Lerp(_rt.localScale, Vector3.one * _targetScale, k);
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

        // ============================================================ 文案 / 配色

        private static string TypeLabel(CardType t) => t switch
        {
            CardType.Attack => "攻击",
            CardType.Skill => "技能",
            CardType.Power => "能力",
            CardType.Status => "状态",
            CardType.Curse => "诅咒",
            _ => ""
        };

        private static Color TypeColor(CardType t) => t switch
        {
            CardType.Attack => ColAttack,
            CardType.Skill => ColSkill,
            CardType.Power => ColPower,
            CardType.Curse => ColCurse,
            CardType.Status => ColCurse,
            _ => ColSkill
        };
    }
}
