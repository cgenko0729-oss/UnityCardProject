using System.Text;
using Game.Battle;
using Game.Enemies;
using Game.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>单位面板：名字、血条、护甲、状态、意图。★ 只读 BattleUnit。</summary>
    public class UnitView : MonoBehaviour, IPointerClickHandler
    {
        public BattleUnit Unit { get; private set; }

        private BattleScreen _screen;
        private Image _bg;
        private Image _hpFill;
        private Text _nameText;
        private Text _hpText;
        private Text _blockText;
        private Text _statusText;
        private Text _intentText;

        private float _flashTimer;
        private Color _baseColor;

        private readonly StringBuilder _sb = new StringBuilder(64);

        public static UnitView Create(Transform parent, BattleScreen screen, BattleUnit unit, bool isPlayer)
        {
            var baseColor = isPlayer ? new Color(0.16f, 0.28f, 0.20f) : new Color(0.30f, 0.16f, 0.16f);
            var rt = UIFactory.CreatePanel(parent, "Unit_" + unit.Name, baseColor);
            UIFactory.SetSize(rt, 260, 200);

            var v = rt.gameObject.AddComponent<UnitView>();
            v._screen = screen;
            v.Unit = unit;
            v._bg = rt.GetComponent<Image>();
            v._baseColor = baseColor;

            v._intentText = UIFactory.CreateText(rt, "Intent", "", 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f));
            UIFactory.SetAnchored(v._intentText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -34), new Vector2(-4, -2));

            v._nameText = UIFactory.CreateText(rt, "Name", unit.Name, 22, TextAnchor.MiddleCenter);
            UIFactory.SetAnchored(v._nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -70), new Vector2(-4, -36));

            var hpBg = UIFactory.CreatePanel(rt, "HpBg", new Color(0.1f, 0.1f, 0.1f));
            UIFactory.SetAnchored(hpBg, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -104), new Vector2(-10, -74));

            var fill = UIFactory.CreatePanel(hpBg, "HpFill", new Color(0.75f, 0.22f, 0.22f));
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImg = fill.GetComponent<Image>();
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.raycastTarget = false;
            v._hpFill = fillImg;

            v._hpText = UIFactory.CreateText(hpBg, "HpText", "", 18);
            UIFactory.Stretch(v._hpText.rectTransform);

            v._blockText = UIFactory.CreateText(rt, "Block", "", 20, TextAnchor.MiddleCenter,
                new Color(0.6f, 0.8f, 1f));
            UIFactory.SetAnchored(v._blockText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -134), new Vector2(-4, -106));

            v._statusText = UIFactory.CreateText(rt, "Status", "", 16, TextAnchor.UpperCenter,
                new Color(1f, 1f, 0.75f));
            UIFactory.SetAnchored(v._statusText.rectTransform, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(4, 4), new Vector2(-4, -136));

            return v;
        }

        public void Refresh(BattleContext ctx, bool targetable, bool highlighted)
        {
            if (Unit == null) return;

            float pct = Unit.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)Unit.Hp / Unit.MaxHp);
            _hpFill.fillAmount = pct;
            _hpText.text = $"{Unit.Hp} / {Unit.MaxHp}";
            _blockText.text = Unit.Block > 0 ? $"[ 护甲 {Unit.Block} ]" : "";

            _sb.Clear();
            for (int i = 0; i < Unit.Statuses.Count; i++)
            {
                var s = Unit.Statuses[i];
                if (s.Def == null) continue;
                if (_sb.Length > 0) _sb.Append("  ");
                _sb.Append(s.Def.DisplayName).Append(' ').Append(s.Stacks);
            }
            _statusText.text = _sb.ToString();

            if (!Unit.IsPlayer)
            {
                _intentText.text = Unit.IsAlive ? FormatIntent(Unit.CurrentIntent) : "";
            }

            Color c = _baseColor;
            if (!Unit.IsAlive) c = new Color(0.12f, 0.12f, 0.12f);
            else if (highlighted) c = Color.Lerp(_baseColor, new Color(1f, 0.9f, 0.4f), 0.5f);
            else if (targetable) c = Color.Lerp(_baseColor, Color.white, 0.15f);

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(_flashTimer * 4f));
            }
            _bg.color = c;

            _nameText.text = Unit.IsAlive ? Unit.Name : Unit.Name + "（已倒下）";
        }

        private static string FormatIntent(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Attack:
                    return intent.Times > 1 ? $"⚔ {intent.Value} x{intent.Times}" : $"⚔ {intent.Value}";
                case IntentKind.AttackDefend:
                    return $"⚔ {intent.Value} + 防御";
                case IntentKind.AttackDebuff:
                    return $"⚔ {intent.Value} + 减益";
                case IntentKind.Defend: return $"🛡 {intent.Value}";
                case IntentKind.Buff: return "▲ 强化";
                case IntentKind.Debuff: return "▼ 减益";
                case IntentKind.Sleep: return "z z z";
                case IntentKind.Special: return "？";
                default: return "";
            }
        }

        public void Flash() => _flashTimer = 0.25f;

        public void OnPointerClick(PointerEventData e) => _screen.OnUnitClicked(this);
    }
}
