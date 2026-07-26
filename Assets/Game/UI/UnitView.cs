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

        public const float UnitWidth = 260f;
        public const float UnitHeight = 200f;

        private BattleScreen _screen;
        private Image _bg;

        /// <summary>敌人立绘。没配图时为 null，此处的一切表现要按无立绘的老路走。</summary>
        private Image _art;

        /// <summary>
        /// 提亮层：受击白闪 / 护甲蓝闪 / 可选中高亮都叠在它上面。
        /// ★ 不能再靠改 <see cref="_bg"/> 的颜色——立绘会把 Body 底色整个盖住。
        /// </summary>
        private RectTransform _tint;
        private Image _tintImage;

        /// <summary>
        /// 血条的两条。★ 存的是 <see cref="RectTransform"/> 而不是 <see cref="Image"/>：
        /// 长度靠锚点驱动，不靠 <c>Image.fillAmount</c>——理由见 <see cref="MakeBar"/>。
        /// </summary>
        private RectTransform _hpFill;

        /// <summary>延迟血条（残影）。掉血时它停一拍再追下来，那段空隙就是「刚刚掉了多少」。</summary>
        private RectTransform _hpGhost;

        /// <summary>Body 上的整体透明度，死亡淡出用。</summary>
        private CanvasGroup _group;
        private TMP_Text _nameText;
        private TMP_Text _hpText;
        private TMP_Text _blockText;
        private TMP_Text _intentText;

        /// <summary>状态列表的容器。状态改成一条一个小牌子，才可能逐条悬停出解释。</summary>
        private RectTransform _statusArea;

        private float _flashTimer;
        private Color _baseColor;

        /// <summary>倒下后的面板底色（无立绘）。</summary>
        private static readonly Color DeadPanelColor = new Color(0.12f, 0.12f, 0.12f);

        /// <summary>倒下后给立绘的染色。★ 不能用 0.12 那个值——乘到图上会黑成一团看不出是什么。</summary>
        private static readonly Color DeadArtTint = new Color(0.38f, 0.36f, 0.40f);

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
            UIFactory.SetSize(rt, UnitWidth, UnitHeight);

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

            // 死亡时整块淡出。★ 逐个 Graphic 改 alpha 要遍历十几个节点、还要记住每个的原色，
            //   CanvasGroup 一个数字搞定，而且不会破坏 Refresh 每帧重写的 _bg.color。
            v._group = body.gameObject.AddComponent<CanvasGroup>();

            // ---- 立绘。★ 必须建在这里：兄弟顺序 = 遮挡顺序，
            //      它要垫在意图 / 名字 / 血条 / 状态**全部之下**，只盖住 Body 的底色。
            var art = UIFactory.CreateArtWindow(body, "Art", unit.EnemyDef != null ? unit.EnemyDef.Art : null,
                UnitWidth, UnitHeight, anchorY: 1f);
            if (art != null)
            {
                UIFactory.Stretch(art);
                v._art = art.GetComponentInChildren<Image>();

                // 有立绘时底色改成白：Image.color 是**乘算**，留着深红会把整张图染红
                v._baseColor = Color.white;
            }

            // ---- 提亮层。受击白闪、护甲蓝闪、可选中高亮全部改由它承载。
            //
            // ★★ 这一层是立绘接进来之后**必须**加的，不是锦上添花：
            //   原来「闪白」写的是 _bg.color（Color.Lerp(c, white, t)）。
            //   立绘一铺上去就把 Body 底色整个盖住，于是所有有立绘的敌人
            //   受击闪白、护甲蓝闪会全部静默消失——而画面上一切正常、不报任何错。
            //
            // ★ 无立绘时的观感一点没变：不透明底色 c 上叠一层 alpha 为 t 的白，
            //   结果正是 c*(1-t) + white*t，与原来的 Lerp 逐像素相同。
            v._tint = UIFactory.CreatePanel(body, "Tint", new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(v._tint);
            v._tintImage = v._tint.GetComponent<Image>();
            v._tintImage.raycastTarget = false;

            // 面板刚建出来时，表现值就是当前的逻辑值——此刻没有任何待播事件
            v._shownHp = unit.Hp;
            v._shownBlock = unit.Block;
            v._ghostPct = unit.MaxHp > 0 ? Mathf.Clamp01((float)unit.Hp / unit.MaxHp) : 0f;

            v._intentText = UIFactory.CreateText(body, "Intent", "", 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f));
            UIFactory.SetAnchored(v._intentText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -34), new Vector2(-4, -2));

            v._nameText = UIFactory.CreateText(body, "Name", unit.Name, 22, TextAnchor.MiddleCenter);
            UIFactory.SetAnchored(v._nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(4, -70), new Vector2(-4, -36));

            var hpBg = UIFactory.CreatePanel(body, "HpBg", new Color(0.1f, 0.1f, 0.1f));
            UIFactory.SetAnchored(hpBg, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10, -104), new Vector2(-10, -74));

            // ---- 延迟血条（残影条）
            //
            // ★ 建在 HpFill **之前**：兄弟顺序 = 遮挡顺序，残影在底下，
            //   露出来的就只有「主条到残影之间」那一段，正好是刚刚掉掉的血。
            //   顺序反了的话残影会把主条整个盖住，血条看起来永远是满的。
            //
            // ★ 这是打击感最便宜的一笔：血条本来就是一个 fillAmount，
            //   加一条滞后的同款就有了「掉了多少」的量感，而不只是「现在剩多少」。
            v._hpGhost = MakeBar(hpBg, "HpGhost", new Color(1f, 0.85f, 0.45f));
            v._hpFill = MakeBar(hpBg, "HpFill", new Color(0.75f, 0.22f, 0.22f));

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

        // ============================================================ 血条

        /// <summary>
        /// 建一条血条。左对齐、长度由锚点驱动。
        ///
        /// ★★ 这里**不能**用 <c>Image.type = Filled</c> + <c>fillAmount</c>，那条路是坏的：
        ///   <see cref="UIFactory.CreatePanel"/> 建出来的 <see cref="Image"/> 没有 sprite，
        ///   而 Unity 的 <c>Image.OnPopulateMesh</c> 在 <c>sprite == null</c> 时**直接画一个整块矩形并 return**，
        ///   根本不看 <c>type</c>，于是 <c>fillAmount</c> 写什么都没用。
        ///
        ///   后果是血条从第一天起就恒满，只有旁边的「71 / 80」在变——
        ///   而这个故障看起来完全像是「忘了刷新」，查错的方向一开始就是错的。
        ///   这不是打击反馈引入的问题，它比这三层都老。
        ///
        /// ★ 改用锚点而不是「给 Image 塞一张 1x1 白图」：
        ///   锚点是 uGUI 的原生布局语义，不依赖任何资产，也不需要在运行时造并持有一个 Sprite。
        ///   本工程零 prefab 零资产，这条路更贴。
        /// </summary>
        private static RectTransform MakeBar(RectTransform parent, string name, Color color)
        {
            var rt = UIFactory.CreatePanel(parent, name, color);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        /// <summary>把一条血条设成父容器宽度的 <paramref name="pct"/>。</summary>
        private static void SetBar(RectTransform rt, float pct)
        {
            if (rt == null) return;

            pct = Mathf.Clamp01(pct);

            // 只动右锚点：左边永远贴着容器左缘，右边按比例走。
            // offsetMin/Max 保持 0，所以宽度严格等于 pct × 容器宽度，随分辨率自动缩放。
            var max = rt.anchorMax;
            if (Mathf.Approximately(max.x, pct)) return;   // 没变就别碰，免得每帧标脏布局

            max.x = pct;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void Refresh(BattleContext ctx, bool targetable, bool highlighted)
        {
            if (Unit == null) return;

            AlignWhenIdle(ctx);

            float pct = Unit.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)_shownHp / Unit.MaxHp);
            SetBar(_hpFill, pct);
            _hpText.text = $"{_shownHp} / {Unit.MaxHp}";

            UpdateGhostBar(pct);
            UpdateBlockText();
            RefreshStatusChips();

            if (!Unit.IsPlayer) UpdateIntent();

            // ---- 底色（乘算的那一半）：死亡变暗、被指向时染黄、残血脉动。
            //      有立绘时 _baseColor 是白，这些就正好变成对立绘的染色。
            Color c = _baseColor;
            if (!DisplayAlive) c = _art != null ? DeadArtTint : DeadPanelColor;
            else if (highlighted) c = Color.Lerp(_baseColor, new Color(1f, 0.9f, 0.4f), 0.5f);

            if (DisplayAlive) c = ApplyLowHpPulse(c, pct);

            if (_art != null) _art.color = c; else _bg.color = c;

            // ---- 提亮层（叠加的那一半）：可选中、护甲蓝闪、受击白闪。
            //
            // ★ 这三样原本都是 Color.Lerp(c, 某个亮色, t) 写进 _bg.color 的。
            //   立绘一铺上去 Body 底色就再也看不见，只能改成一层叠加。
            //   数学上等价：不透明底色 c 上叠 alpha 为 t 的亮色 = Lerp(c, 亮色, t)，
            //   所以没有立绘的单位观感一点没变。
            Color overlay = Color.clear;

            if (DisplayAlive && targetable && !highlighted)
                overlay = new Color(1f, 1f, 1f, 0.15f);

            // 护甲蓝闪在白闪之前：两者同时发生时（挡下一部分又掉了血），白闪该压在上面
            if (_blockFlashTimer > 0f)
            {
                _blockFlashTimer -= Time.deltaTime;
                overlay = new Color(0.45f, 0.72f, 1f, Mathf.Clamp01(_blockFlashTimer * 3f));
            }

            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                overlay = new Color(1f, 1f, 1f, Mathf.Clamp01(_flashTimer * 4f));
            }

            if (_tintImage != null) _tintImage.color = overlay;

            _nameText.text = DisplayAlive
                ? Unit.DisplayName
                : Loc.T("ui.unit.dead", "{0}（已倒下）", Unit.DisplayName);
        }

        // ============================================================ 延迟血条

        /// <summary>挨打后残影条先停住多久再开始追。停顿本身就是「刚刚掉了这么多」的读数时间。</summary>
        private const float GhostHold = 0.28f;

        /// <summary>残影条追赶速度（每秒百分比）。</summary>
        private const float GhostSpeed = 0.55f;

        private float _ghostPct;
        private float _ghostHold;

        private void UpdateGhostBar(float pct)
        {
            if (_ghostPct <= pct)
            {
                // 治疗 / 对齐：残影没有理由落在主条后面，直接跟上
                _ghostPct = pct;
                _ghostHold = 0f;
            }
            else if (_ghostHold > 0f)
            {
                _ghostHold -= Time.deltaTime;
            }
            else
            {
                _ghostPct = Mathf.MoveTowards(_ghostPct, pct, GhostSpeed * Time.deltaTime);
            }

            SetBar(_hpGhost, _ghostPct);
        }

        // ============================================================ 低血量

        private const float LowHpThreshold = 0.3f;
        private const float LowHpPulseSpeed = 3.4f;

        /// <summary>
        /// 血量见底时面板脉动。★ 只给玩家做：敌人残血是好消息，不需要报警。
        /// 让「我快死了」变成余光里就能感觉到的东西，而不是一个要低头去读的数字。
        /// </summary>
        private Color ApplyLowHpPulse(Color c, float pct)
        {
            if (!Unit.IsPlayer || pct > LowHpThreshold || !FeedbackSettings.FlashEnabled) return c;

            // 越接近 0 越强
            float urgency = 1f - pct / LowHpThreshold;
            float wave = (Mathf.Sin(Time.unscaledTime * LowHpPulseSpeed) + 1f) * 0.5f;

            return Color.Lerp(c, new Color(0.62f, 0.10f, 0.12f), wave * urgency * 0.75f);
        }

        // ============================================================ 护甲 / 意图的变化提示

        private int _lastShownBlock = -1;
        private string _lastIntentText;

        private void UpdateBlockText()
        {
            _blockText.text = _shownBlock > 0 ? Loc.T("ui.unit.block", "[ 护甲 {0} ]", _shownBlock) : "";

            if (_lastShownBlock == _shownBlock) return;
            bool first = _lastShownBlock < 0;
            _lastShownBlock = _shownBlock;

            if (first || _shownBlock <= 0) return;
            Punch(_blockText.rectTransform, 0.28f, 0.24f);
        }

        /// <summary>
        /// 意图变了就弹一下。
        /// ★ 玩家给敌人上虚弱之后意图数字会变小，这是他刚刚那张牌**唯一**的可见回报，
        ///   而原来它是静默替换的，很容易完全没被注意到。
        /// </summary>
        private void UpdateIntent()
        {
            string text = DisplayAlive ? FormatIntent(Unit.CurrentIntent) : "";
            _intentText.text = text;

            if (_lastIntentText == text) return;
            bool first = _lastIntentText == null;
            _lastIntentText = text;

            if (first || string.IsNullOrEmpty(text)) return;
            Punch(_intentText.rectTransform, 0.35f, 0.28f);
        }

        /// <summary>弹一下就回来。★ 每次都先 Kill：连着变两次时两条 tween 会抢同一个 localScale。</summary>
        private static void Punch(RectTransform rt, float strength, float duration)
        {
            if (rt == null || FeedbackSettings.HitMotionScale <= 0.001f) return;

            DOTween.Kill(rt);
            rt.localScale = Vector3.one;
            rt.DOPunchScale(Vector3.one * (strength * FeedbackSettings.HitMotionScale), duration, 6, 0.6f);
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
        public void OnDamaged(int amount)
        {
            _shownHp = Mathf.Max(0, _shownHp - amount);
            _ghostHold = GhostHold;
        }

        public void OnHealed(int amount)
        {
            int max = Unit != null ? Unit.MaxHp : _shownHp + amount;
            _shownHp = Mathf.Min(max, _shownHp + amount);
        }

        public void OnBlockGained(int amount) => _shownBlock += amount;

        public void OnBlockConsumed(int amount) => _shownBlock = Mathf.Max(0, _shownBlock - amount);

        public void OnBlockCleared() => _shownBlock = 0;

        /// <summary>表现护甲已经归零。<see cref="BattlePresenter"/> 用它判断这一下是不是把盾打穿了。</summary>
        public bool ShownBlockEmpty => _shownBlock <= 0;

        /// <summary>
        /// 挨了一下但被护甲吃掉：蓝色一闪，护甲数字弹一下；打穿了则更重。
        /// ★ 不走 <see cref="PlayHit"/>：那是「打到肉上」的表现，被挡下不该有击退，
        ///   否则玩家分不清自己到底掉没掉血。
        /// </summary>
        public void PlayBlockHit(bool broke)
        {
            if (FeedbackSettings.FlashEnabled)
                _blockFlashTimer = broke ? 0.45f : 0.22f;

            Punch(_blockText.rectTransform, broke ? 0.55f : 0.3f, broke ? 0.35f : 0.24f);
        }

        /// <summary>护甲被削时的蓝闪计时。与受击白闪分开——颜色不同，含义也不同。</summary>
        private float _blockFlashTimer;

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

            // 已经在倒下了就不要再被推来推去——死亡动画是这块面板最后的位姿主人
            if (_deathPlayed) return;

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
        /// 攻击前冲：朝目标猛探一下再弹回。
        ///
        /// ★ 这是「打出去了」的那半句话。本工程的单位就是几块色块，
        ///   前冲比任何粒子都更像「我打了它」，而且和受击反应共用同一套位姿通道。
        ///
        /// ★ 前冲若正撞上自己挨打（荆棘反弹），后来的那个会 Kill 掉前一个——
        ///   同一个 Body 只能有一个位姿主人，这是刻意的，不是竞态。
        /// </summary>
        public void PlayLunge(Vector2 direction)
        {
            if (_body == null || _deathPlayed) return;
            if (direction.sqrMagnitude < 0.0001f) return;

            float scale = FeedbackSettings.HitMotionScale;
            if (scale <= 0.001f) return;

            DOTween.Kill(_body);
            _body.localScale = Vector3.one;

            var seq = DOTween.Sequence().SetTarget(_body);
            seq.Append(DOTween.To(() => _body.anchoredPosition, v => _body.anchoredPosition = v,
                                  direction * (LungeDistance * scale), LungeOutTime).SetEase(Ease.OutQuad));
            seq.Append(DOTween.To(() => _body.anchoredPosition, v => _body.anchoredPosition = v,
                                  Vector2.zero, LungeBackTime).SetEase(Ease.OutBack));
        }

        private const float LungeDistance = 40f;
        private const float LungeOutTime = 0.08f;
        private const float LungeBackTime = 0.20f;

        // ============================================================ 死亡

        private const float DeathTime = 0.55f;
        private const float DeathSink = -46f;
        private const float DeathTilt = 12f;
        private const float DeathAlpha = 0.28f;

        private bool _deathPlayed;

        /// <summary>
        /// 倒下：白闪一下 → 下沉 + 倾倒 + 淡出。
        ///
        /// ★ 只播一次（<see cref="_deathPlayed"/>）。死亡事件本身只来一条，
        ///   但状态衰减、亡语这些会让面板在之后继续被刷新，没有这道闸就会反复重播。
        ///
        /// ★ 面板**不销毁**：铁律 7——死亡单位不从 AllUnits 移除，只是 Hp 为 0。
        ///   界面上留一具变暗的躯壳，和逻辑层是同一个语义。
        /// </summary>
        public void PlayDeath()
        {
            if (_body == null || _deathPlayed) return;
            _deathPlayed = true;

            Flash(FlashLethal);
            DOTween.Kill(_body);

            var seq = DOTween.Sequence().SetTarget(_body);

            // 先被打得一缩，再软下去——直接开始沉会像是自己走下去的
            seq.Append(_body.DOScale(new Vector3(1.14f, 0.86f, 1f), 0.09f).SetEase(Ease.OutQuad));
            seq.Append(_body.DOScale(new Vector3(0.92f, 0.88f, 1f), DeathTime).SetEase(Ease.InQuad));

            seq.Join(DOTween.To(() => _body.anchoredPosition, v => _body.anchoredPosition = v,
                                new Vector2(0f, DeathSink), DeathTime).SetEase(Ease.InQuad));

            seq.Join(_body.DOLocalRotate(new Vector3(0f, 0f, DeathTilt), DeathTime).SetEase(Ease.InQuad));

            if (_group != null)
            {
                seq.Join(DOTween.To(() => _group.alpha, a => _group.alpha = a, DeathAlpha, DeathTime)
                                .SetTarget(_body));
            }
        }

        /// <summary>
        /// ★ 铁律 45：对象要没了就必须把它身上的 tween 收掉。
        ///   战斗结束、切界面、敌人面板重建都会销毁本组件，而 tween 活在 DOTween 的全局队列里，
        ///   没人收的话它会继续去写一个已经销毁的 RectTransform。
        /// </summary>
        private void OnDisable()
        {
            if (_intentText != null) DOTween.Kill(_intentText.rectTransform);
            if (_blockText != null) DOTween.Kill(_blockText.rectTransform);

            for (int i = 0; i < _chipRoots.Count; i++)
                if (_chipRoots[i] != null) DOTween.Kill(_chipRoots[i]);

            if (_body == null) return;

            DOTween.Kill(_body);
            _body.anchoredPosition = Vector2.zero;
            _body.localScale = Vector3.one;
            _body.localRotation = Quaternion.identity;
            if (_group != null) _group.alpha = 1f;
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

        /// <summary>状态图标边长。行高 21，留一点上下余量。</summary>
        private const float StatusIconSize = 17f;

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

        /// <summary>
        /// 重建前的那一批状态 Id。
        /// ★ 只给**新出现**的状态播弹入动画：整个牌子列表是一起重建的，
        ///   不区分的话，新上一层易伤会让屏幕上原有的四个牌子跟着一起蹦，
        ///   玩家反而看不出到底哪个是新的。
        /// </summary>
        private readonly HashSet<string> _previousChipIds = new HashSet<string>();

        private void RebuildStatusChips(List<StatusInstance> list)
        {
            _previousChipIds.Clear();
            for (int i = 0; i < _chipIds.Count; i++) _previousChipIds.Add(_chipIds[i]);

            for (int i = 0; i < _chipRoots.Count; i++)
            {
                if (_chipRoots[i] == null) continue;
                DOTween.Kill(_chipRoots[i]);
                Destroy(_chipRoots[i].gameObject);
            }

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

                // 有图标就在牌子左端放一个，文字（名字 + 层数）跟着右移。
                // ★ 图标不替代文字：层数是每回合都在变的关键读数，
                //   只画一个图标的话玩家就得靠悬停才能知道「易伤还剩几层」。
                var icon = UIFactory.CreateArtWindow(chip, "Icon",
                    s.Def.Icon, StatusIconSize, StatusIconSize, anchorY: 0.5f);

                float labelLeft = 0f;
                if (icon != null)
                {
                    icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                    icon.pivot = new Vector2(0f, 0.5f);
                    icon.anchoredPosition = new Vector2(3f, 0f);
                    labelLeft = StatusIconSize + 6f;
                }

                var label = UIFactory.CreateText(chip, "Label", "", 15,
                    icon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, accent);
                UIFactory.SetAnchored(label.rectTransform, Vector2.zero, Vector2.one,
                    new Vector2(labelLeft, 0f), Vector2.zero);

                TooltipTarget.Attach(chip.gameObject, new StatusTooltipSource(Unit, s.Id));

                if (!_previousChipIds.Contains(s.Id)) PlayChipEnter(chip);

                _chipRoots.Add(chip);
                _chipLabels.Add(label);
                _chipIds.Add(s.Id);
            }
        }

        /// <summary>
        /// 新状态弹入。★ 给敌人上易伤 / 虚弱原本是完全无声无息的——
        /// 玩家打出那张牌之后，唯一的变化是面板角落多了一行小字。
        /// </summary>
        private static void PlayChipEnter(RectTransform chip)
        {
            if (FeedbackSettings.HitMotionScale <= 0.001f) return;

            chip.localScale = new Vector3(0.5f, 0.5f, 1f);
            chip.DOScale(Vector3.one, 0.28f).SetEase(Ease.OutBack);
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
