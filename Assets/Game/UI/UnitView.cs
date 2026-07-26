using System.Collections.Generic;
using Game.Battle;
using Game.Enemies;
using Game.Localization;
using Game.Statuses;
using Game.Units;
using TMPro;
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
        private TMP_Text _nameText;
        private TMP_Text _hpText;
        private TMP_Text _blockText;
        private TMP_Text _intentText;

        /// <summary>状态列表的容器。状态改成一条一个小牌子，才可能逐条悬停出解释。</summary>
        private RectTransform _statusArea;

        private float _flashTimer;
        private Color _baseColor;

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

            v._statusArea = UIFactory.CreateEmpty(rt, "Statuses");
            UIFactory.SetAnchored(v._statusArea, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -138 - StatusRowsShown * StatusRowHeight), new Vector2(-4, -138));

            // 意图也可悬停：图标上只有数字，「这一击还会给你上两层易伤」是玩家看不见又必须知道的。
            // ★ CreateText 默认 raycastTarget = false（文字不该吃点击），这里必须单独打开，
            //   否则挂上去的 TooltipTarget 永远收不到 Enter。
            if (!isPlayer)
            {
                v._intentText.raycastTarget = true;
                TooltipTarget.Attach(v._intentText.gameObject, new IntentTooltipSource(unit));
            }

            return v;
        }

        public void Refresh(BattleContext ctx, bool targetable, bool highlighted)
        {
            if (Unit == null) return;

            float pct = Unit.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)Unit.Hp / Unit.MaxHp);
            _hpFill.fillAmount = pct;
            _hpText.text = $"{Unit.Hp} / {Unit.MaxHp}";
            _blockText.text = Unit.Block > 0 ? Loc.T("ui.unit.block", "[ 护甲 {0} ]", Unit.Block) : "";

            RefreshStatusChips();

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

            _nameText.text = Unit.IsAlive ? Unit.DisplayName : Loc.T("ui.unit.dead", "{0}（已倒下）", Unit.DisplayName);
        }

        private static string FormatIntent(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Attack:
                    return intent.Times > 1 ? $"⚔ {intent.Value} x{intent.Times}" : $"⚔ {intent.Value}";
                case IntentKind.AttackDefend:
                    return Loc.T("intent.attack_defend", "⚔ {0} + 防御", intent.Value);
                case IntentKind.AttackDebuff:
                    return Loc.T("intent.attack_debuff", "⚔ {0} + 减益", intent.Value);
                case IntentKind.Defend: return $"🛡 {intent.Value}";
                case IntentKind.Buff: return Loc.T("intent.buff", "▲ 强化");
                case IntentKind.Debuff: return Loc.T("intent.debuff", "▼ 减益");
                case IntentKind.Sleep: return "z z z";
                case IntentKind.Special: return "？";
                default: return "";
            }
        }

        public void Flash() => _flashTimer = 0.25f;

        public void OnPointerClick(PointerEventData e) => _screen.OnUnitClicked(this);

        // ============================================================ 状态小牌子

        private const float StatusRowHeight = 21f;

        /// <summary>状态区域名义上能放几行。超出的会溢到面板下方——和原来那段文字溢出的行为一致。</summary>
        private const int StatusRowsShown = 3;

        private readonly List<RectTransform> _chipRoots = new List<RectTransform>();
        private readonly List<TMP_Text> _chipLabels = new List<TMP_Text>();

        /// <summary>
        /// 每个牌子对应的状态 Id，与 <see cref="_chipLabels"/> 一一对应。
        /// ★ 不能用「牌子的下标 == Unit.Statuses 的下标」来刷新文字：
        ///   Def 为 null 的状态会被跳过、不建牌子，于是两边的下标从那一条起全部错位，
        ///   「易伤 2」会被写到「虚弱」的牌子上。按 Id 反查才对得住。
        /// </summary>
        private readonly List<string> _chipIds = new List<string>();

        /// <summary>已经建过牌子的状态 Id 序列（含被跳过的）。只有它变了才重建。</summary>
        private readonly List<string> _chipSignature = new List<string>();

        /// <summary>
        /// 让状态小牌子跟上 <see cref="BattleUnit.Statuses"/>。
        /// ★ 与手牌 / 药水栏同一套「签名比对」：层数每回合都在动，
        ///   但只有**状态的种类**变了才需要重建节点，层数只改文字。
        ///   不比对的话每帧销毁重建，悬停会因为节点消失而不断中断。
        /// </summary>
        private void RefreshStatusChips()
        {
            var list = Unit.Statuses;

            bool changed = list.Count != _chipSignature.Count;
            if (!changed)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Id != _chipSignature[i]) { changed = true; break; }
            }
            if (changed) RebuildStatusChips(list);

            for (int i = 0; i < _chipLabels.Count; i++)
            {
                var inst = Unit.FindStatus(_chipIds[i]);
                if (inst == null || inst.Def == null) continue;

                string text = $"{inst.Def.DisplayName} {inst.Stacks}";
                if (_chipLabels[i].text != text) _chipLabels[i].text = text;
            }
        }

        private void RebuildStatusChips(List<StatusInstance> list)
        {
            for (int i = 0; i < _chipRoots.Count; i++)
                if (_chipRoots[i] != null) Destroy(_chipRoots[i].gameObject);

            _chipRoots.Clear();
            _chipLabels.Clear();
            _chipIds.Clear();
            _chipSignature.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                _chipSignature.Add(s.Id);
                if (s.Def == null) continue;

                float y = -i * StatusRowHeight;
                var accent = TooltipContent.AccentOf(s.Def.Polarity);

                var chip = UIFactory.CreatePanel(_statusArea, "Status_" + s.Id,
                    new Color(accent.r, accent.g, accent.b, 0.16f));
                UIFactory.SetAnchored(chip, new Vector2(0, 1), new Vector2(1, 1),
                    new Vector2(0, y - StatusRowHeight + 2f), new Vector2(0, y));

                var label = UIFactory.CreateText(chip, "Label", "", 15, TextAnchor.MiddleCenter, accent);
                UIFactory.Stretch(label.rectTransform);

                TooltipTarget.Attach(chip.gameObject, new StatusTooltipSource(Unit, s.Id));

                _chipRoots.Add(chip);
                _chipLabels.Add(label);
                _chipIds.Add(s.Id);
            }
        }

        // ============================================================ 词条源

        /// <summary>
        /// 一条状态的解释。★ 存 Id 而不是存 <see cref="StatusInstance"/>：
        /// 层数掉到 0 时实例会被换掉/移除，抓着旧实例会显示一条早已不存在的状态。
        /// </summary>
        private sealed class StatusTooltipSource : ITooltipSource
        {
            private readonly BattleUnit _unit;
            private readonly string _statusId;

            public StatusTooltipSource(BattleUnit unit, string statusId)
            {
                _unit = unit;
                _statusId = statusId;
            }

            public bool BuildTooltip(List<TooltipEntry> buffer)
            {
                var inst = _unit != null ? _unit.FindStatus(_statusId) : null;
                if (inst == null || inst.Def == null) return false;

                buffer.Add(TooltipContent.ForStatus(inst.Def, inst.Stacks));
                return true;
            }
        }

        private sealed class IntentTooltipSource : ITooltipSource
        {
            private readonly BattleUnit _unit;

            public IntentTooltipSource(BattleUnit unit) => _unit = unit;

            public bool BuildTooltip(List<TooltipEntry> buffer)
                => TooltipContent.BuildForIntent(_unit, buffer);
        }
    }
}
