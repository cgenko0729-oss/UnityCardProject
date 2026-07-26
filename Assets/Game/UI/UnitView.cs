using System.Collections.Generic;
using DG.Tweening;
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

        /// <summary>
        /// 打击反应（击退 / 挤压）作用的节点。★ 所有可见内容都在它下面，面板根节点是空的。
        ///
        /// ★ 为什么要多这一层：面板根的位置是**布局**说了算的——
        ///   玩家面板拉伸填满 <c>_playerSlot</c>，敌人面板按下标算锚点。
        ///   打击反应如果直接动根节点，就变成「布局」和「特效」两个来源同时写一个位姿，
        ///   正是铁律 23 在手牌那边已经踩过的坑（一处每帧设 1，一处每帧插值到 1.1，永远打架）。
        ///   拆开之后，根节点只归布局管、Body 只归打击反应管，两边永远不会碰面。
        ///
        /// ★ 附带的好处：飘字位置取的是**根节点**的中心（<c>BattleScreen.AnchoredPosOf</c>），
        ///   于是伤害数字不会跟着被击退的身体一起乱跑，多段攻击时读数依然稳定。
        /// </summary>
        private RectTransform _body;

        public static UnitView Create(Transform parent, BattleScreen screen, BattleUnit unit, bool isPlayer)
        {
            var baseColor = isPlayer ? new Color(0.16f, 0.28f, 0.20f) : new Color(0.30f, 0.16f, 0.16f);

            // 根节点只负责「占位 + 吃点击」。alpha 不能真的是 0——完全透明时调试起来看不见范围，
            // 但只要 raycastTarget 为真它照样收事件（同 CardView 的 HoverPad 的做法）。
            var rt = UIFactory.CreatePanel(parent, "Unit_" + unit.Name, new Color(0f, 0f, 0f, 0.004f));
            UIFactory.SetSize(rt, 260, 200);

            var v = rt.gameObject.AddComponent<UnitView>();
            v._screen = screen;
            v.Unit = unit;
            v._baseColor = baseColor;

            var body = UIFactory.CreatePanel(rt, "Body", baseColor);
            UIFactory.Stretch(body);
            v._body = body;
            v._bg = body.GetComponent<Image>();

            // ★ Body 不吃射线：它会被击退，判定区跟着跑的话「点哪儿算点中这个敌人」会随特效漂移。
            //   点击一律由不动的根节点接。
            v._bg.raycastTarget = false;

            // 面板刚建出来时，表现值就是当前的逻辑值——此刻没有任何待播事件
            v._shownHp = unit.Hp;
            v._shownBlock = unit.Block;

            v._intentText = UIFactory.CreateText(body, "Intent", "", 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f));
            UIFactory.SetAnchored(v._intentText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -34), new Vector2(-4, -2));

            v._nameText = UIFactory.CreateText(body, "Name", unit.Name, 22, TextAnchor.MiddleCenter);
            UIFactory.SetAnchored(v._nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -70), new Vector2(-4, -36));

            var hpBg = UIFactory.CreatePanel(body, "HpBg", new Color(0.1f, 0.1f, 0.1f));
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

            v._blockText = UIFactory.CreateText(body, "Block", "", 20, TextAnchor.MiddleCenter,
                new Color(0.6f, 0.8f, 1f));
            UIFactory.SetAnchored(v._blockText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -134), new Vector2(-4, -106));

            v._statusArea = UIFactory.CreateEmpty(body, "Statuses");
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

            AlignWhenIdle(ctx);

            float pct = Unit.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)_shownHp / Unit.MaxHp);
            _hpFill.fillAmount = pct;
            _hpText.text = $"{_shownHp} / {Unit.MaxHp}";
            _blockText.text = _shownBlock > 0 ? Loc.T("ui.unit.block", "[ 护甲 {0} ]", _shownBlock) : "";

            RefreshStatusChips();

            if (!Unit.IsPlayer)
            {
                _intentText.text = DisplayAlive ? FormatIntent(Unit.CurrentIntent) : "";
            }

            Color c = _baseColor;
            if (!DisplayAlive) c = new Color(0.12f, 0.12f, 0.12f);
            else if (highlighted) c = Color.Lerp(_baseColor, new Color(1f, 0.9f, 0.4f), 0.5f);
            else if (targetable) c = Color.Lerp(_baseColor, Color.white, 0.15f);

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(_flashTimer * 4f));
            }
            _bg.color = c;

            _nameText.text = DisplayAlive
                ? Unit.DisplayName
                : Loc.T("ui.unit.dead", "{0}（已倒下）", Unit.DisplayName);
        }

        // ============================================================ 表现血量

        /// <summary>
        /// 表现血量 / 表现护甲。★ 这两个不是 <c>Unit.Hp</c> / <c>Unit.Block</c>。
        ///
        /// 战斗逻辑是**同步**的：玩家点「结束回合」的那一帧，三个敌人的攻击已经全部结算完，
        /// 而 <see cref="BattlePresenter"/> 还要花一秒多把飘字一条条播出来。
        /// 血条若直读 <c>Unit.Hp</c>，血就在第一个飘字出现之前掉光了——
        /// 现在只有飘字还勉强看不出来，一旦加上闪白 / 震屏 / 慢放，
        /// 这个脱节就会变成「屏幕在为一件早就结束的事情抖动」。
        ///
        /// 所以血条只认 <see cref="BattlePresenter"/> 播到的那一条事件（<see cref="OnDamaged"/> 等），
        /// 逻辑值只在**队列播完时**用来对齐（见 <see cref="AlignWhenIdle"/>）。
        /// </summary>
        private int _shownHp;
        private int _shownBlock;

        /// <summary>「画面上它还活着吗」。★ 与 <c>Unit.IsAlive</c> 不同，这个会晚到死亡事件播出来那一刻。</summary>
        private bool DisplayAlive => _shownHp > 0;

        /// <summary>
        /// 事件播完了就与逻辑值对齐。
        ///
        /// ★ 这一步是**兜底，不是可选项**：哪天有个路径改了 Hp 却忘了 Post 事件
        ///   （或者新加的效果走了别的分支），有它最多晚半秒自动纠正，
        ///   没它就是永久错位，而且画面上一切正常、不报任何错、没人会发现。
        /// </summary>
        private void AlignWhenIdle(BattleContext ctx)
        {
            if (ctx == null || ctx.Events.Count > 0) return;
            _shownHp = Unit.Hp;
            _shownBlock = Unit.Block;

            // 队列空了 = 这一波打完了，下一波飘字重新从正中那个槽位开始
            _floatSlot = 0;
        }

        // 下面这几个只由 BattlePresenter 在播到对应事件时调用。
        // ★ 不要在别处调——「谁能改表现值」必须和「谁在播事件」是同一个人，
        //   否则就退回成了两个来源互相覆盖的老问题（同铁律 23 的道理）。

        /// <summary>
        /// 只改表现血量，不播任何特效。
        /// ★ 特效由 <see cref="PlayHit"/> 单独播，两件事分开：
        ///   「血掉了多少」是信息，「怎么表现」受 <see cref="FeedbackSettings"/> 控制，
        ///   关掉闪白的玩家仍然必须看到血条正确地掉。
        /// </summary>
        public void OnDamaged(int amount) => _shownHp = Mathf.Max(0, _shownHp - amount);

        public void OnHealed(int amount)
        {
            int max = Unit != null ? Unit.MaxHp : _shownHp + amount;
            _shownHp = Mathf.Min(max, _shownHp + amount);
        }

        public void OnBlockGained(int amount) => _shownBlock += amount;

        public void OnBlockConsumed(int amount) => _shownBlock = Mathf.Max(0, _shownBlock - amount);

        public void OnBlockCleared() => _shownBlock = 0;

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

        /// <summary>受击闪白。★ 受 <see cref="FeedbackSettings.FlashEnabled"/> 控制（光敏感 / 减少动态效果）。</summary>
        public void Flash(float duration = FlashNormal)
        {
            if (!FeedbackSettings.FlashEnabled) return;
            _flashTimer = Mathf.Max(_flashTimer, duration);
        }

        // ============================================================ 打击反应

        // ★ 手感参数集中在这里（同 HandFanLayout 的写法）。要调就调这几个数。

        private const float FlashNormal = 0.25f;
        private const float FlashLethal = 0.55f;

        /// <summary>击退距离：轻伤 → 重伤。</summary>
        private const float KnockMin = 10f;
        private const float KnockMax = 46f;

        /// <summary>挤压量（横向拉伸 = 纵向压缩的比例）。</summary>
        private const float SquashMin = 0.04f;
        private const float SquashMax = 0.17f;

        /// <summary>致命一击额外放大多少倍。</summary>
        private const float LethalBoost = 1.5f;

        /// <summary>回位耗时。用 OutElastic 会带一点回弹，看起来像被打了而不是被推了。</summary>
        private const float RecoverTime = 0.34f;

        /// <summary>
        /// 播一次受击反应。
        /// </summary>
        /// <param name="severity">这一下占最大生命的比例（0~1），决定所有幅度。</param>
        /// <param name="direction">击退方向（已归一化）。<see cref="Vector2.zero"/> 表示没有攻击者
        ///   ——中毒、灼烧这类掉血不该有击退，只挤一下。</param>
        /// <param name="lethal">是不是致命的那一下。</param>
        public void PlayHit(float severity, Vector2 direction, bool lethal)
        {
            Flash(lethal ? FlashLethal : FlashNormal);

            if (_body == null) return;

            severity = Mathf.Clamp01(severity);
            float boost = (lethal ? LethalBoost : 1f) * FeedbackSettings.HitMotionScale;
            if (boost <= 0.001f)
            {
                // 「减少动态效果」：只闪不动。血条与飘字照常——那是信息不是特效。
                DOTween.Kill(_body);
                _body.anchoredPosition = Vector2.zero;
                _body.localScale = Vector3.one;
                return;
            }

            // ★ 上一次的击退还没回位就又挨一下（多段攻击）：直接接管，
            //   不 Kill 的话两条 tween 会同时写 anchoredPosition，后写的赢，看起来是「卡住不动」。
            DOTween.Kill(_body);

            float squash = Mathf.Lerp(SquashMin, SquashMax, severity) * boost;
            _body.localScale = new Vector3(1f + squash, 1f - squash, 1f);
            _body.DOScale(Vector3.one, RecoverTime).SetEase(Ease.OutElastic);

            if (direction.sqrMagnitude > 0.0001f)
            {
                float knock = Mathf.Lerp(KnockMin, KnockMax, severity) * boost;
                _body.anchoredPosition = direction * knock;

                // ★ 用 DOTween.To 而不是 DOAnchorPos：后者在 DOTweenModuleUI.cs 里，
                //   那些模块脚本没有 asmdef、编进 Assembly-CSharp，而 Game.UI 是 asmdef
                //   程序集，引用不到它们。核心 DLL 里的 DOTween.To / DOScale 才够得着。
                DOTween.To(() => _body.anchoredPosition, v => _body.anchoredPosition = v,
                           Vector2.zero, RecoverTime)
                       .SetEase(Ease.OutElastic)
                       .SetTarget(_body);
            }
            else
            {
                _body.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>
        /// ★ 铁律 45：对象要没了就必须把它身上的 tween 收掉。
        ///   战斗结束、切界面、敌人面板重建都会销毁本组件，而 tween 活在 DOTween 的全局队列里，
        ///   没人收的话它会继续去写一个已经销毁的 RectTransform。
        /// </summary>
        private void OnDisable()
        {
            if (_body == null) return;

            DOTween.Kill(_body);
            _body.anchoredPosition = Vector2.zero;
            _body.localScale = Vector3.one;
        }

        // ============================================================ 飘字槽位

        /// <summary>
        /// 飘字的落点轮转表（相对面板中心）。
        ///
        /// ★ 必须有：飘字活 0.9 秒，而伤害事件之间只隔 0.18 秒——
        ///   五段攻击时屏幕上同时有五个数字。原来只有 ±18 像素的随机横向抖动，
        ///   五个「-6」会叠成一坨谁也读不出来。轮转固定槽位才能保证它们互相错开。
        /// </summary>
        private static readonly Vector2[] FloatSlots =
        {
            new Vector2(0f, 0f),
            new Vector2(-66f, 20f),
            new Vector2(64f, 12f),
            new Vector2(-36f, 44f),
            new Vector2(40f, 50f),
        };

        private int _floatSlot;

        /// <summary>取下一个飘字落点。由 <see cref="BattlePresenter"/> 调用。</summary>
        public Vector2 NextFloatOffset()
        {
            var o = FloatSlots[_floatSlot % FloatSlots.Length];
            _floatSlot++;
            return o;
        }

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

                string text = Loc.T("tooltip.status_with_stacks", "{0} {1}",
                                    inst.Def.LocalizedName, inst.Stacks);
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
