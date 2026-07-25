using Game.Battle;
using Game.Cards;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 单张手牌的显示。★ 只读 CardInstance，绝不修改战斗数据。
    /// 点击后把「意图」交给 BattleScreen，由它去调 BattleController。
    /// </summary>
    public class CardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
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

        public static CardView Create(Transform parent, BattleScreen screen, CardInstance card)
        {
            var rt = UIFactory.CreatePanel(parent, "Card_" + card.DisplayName, ColSkill);
            UIFactory.SetSize(rt, 170, 240);

            var view = rt.gameObject.AddComponent<CardView>();
            view._rt = rt;
            view._screen = screen;
            view.Card = card;
            view._bg = rt.GetComponent<Image>();

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

            return view;
        }

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

            _rt.localScale = selected ? Vector3.one * 1.08f : Vector3.one;
        }

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

        /// <summary>鼠标是否悬停。由 BattleScreen 在排列手牌时读取，决定抬高多少。</summary>
        public bool Hovered { get; private set; }

        /// <summary>手牌基准位置，由 BattleScreen 计算后写入。</summary>
        public Vector2 BasePosition;

        public void ApplyLayout(bool selected)
        {
            float lift = (Hovered ? 40f : 0f) + (selected ? 30f : 0f);
            _rt.anchoredPosition = BasePosition + new Vector2(0, lift);
        }

        public void OnPointerClick(PointerEventData e) => _screen.OnCardClicked(this);
        public void OnPointerEnter(PointerEventData e) => Hovered = true;
        public void OnPointerExit(PointerEventData e) => Hovered = false;
    }
}
