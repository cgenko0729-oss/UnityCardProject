using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Localization;
using Game.Potions;
using Game.Units;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 战斗界面。整个 UI 在运行时用代码搭建，不需要任何 prefab。
    /// ★ 只读 BattleContext，只通过 BattleController 的 TryPlayCard / EndTurn / CanPlayCard 三个入口写入。
    /// </summary>
    public class BattleScreen : MonoBehaviour
    {
        private BattleController _controller;
        private BattleContext Ctx => _controller != null ? _controller.Ctx : null;

        /// <summary>内容数据库。Tooltip 靠它按关键字位反查 <see cref="KeywordDefinition"/>。</summary>
        public GameDatabase Database => Ctx != null && Ctx.Run != null ? Ctx.Run.Database : null;

        private BattlePresenter _presenter;

        private RectTransform _battlefield;
        private ScreenShake _shake;
        private BattleOverlayFx _overlayFx;
        private RectTransform _energyPanel;
        private RectTransform _enemyRow;
        private RectTransform _playerSlot;
        private RectTransform _handArea;
        private RectTransform _resultPanel;
        public RectTransform PopupLayer { get; private set; }

        private TMP_Text _turnText;
        private TMP_Text _energyText;
        private TMP_Text _logText;
        private TMP_Text _hintText;
        private TMP_Text _resultText;
        private Button _endTurnButton;

        // 三个牌堆的查看按钮。label 每帧带上张数，见 RefreshHud。
        private Button _drawPileButton;
        private Button _discardPileButton;
        private Button _exhaustPileButton;

        private readonly List<UnitView> _unitViews = new List<UnitView>();
        private readonly List<CardView> _cardViews = new List<CardView>();
        private readonly List<int> _handSignature = new List<int>();
        private readonly List<BattleUnit> _enemyBuffer = new List<BattleUnit>();
        private readonly StringBuilder _sb = new StringBuilder(512);

        private CardView _selected;

        // ---------------- 手牌区几何（全部是 _handArea 的本地坐标）

        /// <summary>
        /// 手牌区宽度。
        ///
        /// ★ 1360 不是随手取的：手牌区现在建在「结束回合」按钮之后（为了拖起来的牌不被 HUD 盖住），
        ///   于是遮挡关系反过来了——牌只要压到按钮上就会把点击吃掉。
        ///   最两侧那张牌的中心最远到 (HandWidth − CardWidth) / 2 = 595，加半张牌宽 85 = 680，
        ///   而按钮左边缘在 1920/2 + 700 = 1660 处，正好留 20 的余量。
        ///   要加宽手牌区，必须同时把按钮往外挪或把手牌整体压低。
        /// </summary>
        private const float HandWidth = 1360f;

        private const float HandAreaBottom = 20f;
        private const float HandAreaTop = 280f;

        /// <summary>正中那张牌底边的 y。</summary>
        private const float HandBaseY = 24f;

        /// <summary>悬停抬高量。</summary>
        private const float HoverLift = 46f;

        /// <summary>点击选中后的抬高量（比悬停更明显，玩家要能分清「我只是划过」和「我选了它」）。</summary>
        private const float SelectedLift = 72f;

        /// <summary>
        /// 出牌线的 y。不需要目标的牌只要**牌面中心**越过它，松手就出。
        /// ★ 判定用牌面中心而不是光标：光标离牌底有一段距离（保留了抓取时的相对位置），
        ///   用光标判定会出现「线还在牌上方，但已经算越过了」的怪事。
        /// </summary>
        private const float PlayLineY = 330f;

        // ============================================================ 构建

        /// <summary>
        /// 绑定一场战斗并搭界面。
        /// <paramref name="parent"/> 为 null 时自建一个 Canvas（单独调试战斗场景用）；
        /// 传入时直接挂到宿主界面下（局外流程里由 BattleHostScreen 传）。
        /// </summary>
        public void Bind(BattleController controller, RectTransform parent = null)
        {
            _controller = controller;
            BuildUI(parent);

            _presenter = gameObject.GetComponent<BattlePresenter>();
            if (_presenter == null) _presenter = gameObject.AddComponent<BattlePresenter>();

            AdoptContext();
        }

        /// <summary>
        /// 接管当前的 <see cref="BattleContext"/>：建单位面板、重接表现层。
        ///
        /// ★ 只有这一处做接管，Bind 与 LateUpdate 都调它。
        ///   曾经 Bind 里做一半、LateUpdate 里做另一半，而 Bind 结尾又把 _boundCtx 设成了当前 Ctx，
        ///   于是 LateUpdate 那一半永远不会执行——加在那里的任何新逻辑都会被静默跳过。
        /// </summary>
        private void AdoptContext()
        {
            _boundCtx = Ctx;
            _intentVersion = -1;
            ClearHandViews();
            BuildUnitViews();
            if (_presenter != null) _presenter.Init(this, Ctx);
        }

        /// <summary>
        /// 已经给哪个 BattleContext 建过单位面板。
        /// ★ 单位面板原本只在 Bind 里建一次，一旦 Bind 时 Ctx 恰好还是 null（战斗尚未开始），
        ///   整场战斗就再也不会有敌人和玩家面板，而手牌因为每帧刷新看起来一切正常——
        ///   这种「半个界面」的故障很难一眼看出原因。这里改成按 Ctx 变化补建。
        /// </summary>
        private BattleContext _boundCtx;

        /// <summary>上次重算敌人意图数值时的 <see cref="BattleContext.StateVersion"/>。</summary>
        private int _intentVersion = -1;

        /// <summary>结算面板是否已经弹出来了（= 表现事件已播完）。宿主界面据此显示「继续」。</summary>
        public bool ResultVisible => _resultPanel != null && _resultPanel.gameObject.activeSelf;

        private void BuildUI(RectTransform parent)
        {
            RectTransform root;
            if (parent != null)
            {
                root = UIFactory.CreateEmpty(parent, "BattleRoot");
                UIFactory.Stretch(root);
            }
            else
            {
                var canvas = UIFactory.CreateCanvas("BattleCanvas");
                canvas.transform.SetParent(transform, false);
                canvas.sortingOrder = 0;
                InputCompat.EnsureEventSystem();
                root = (RectTransform)canvas.transform;
            }

            var bg = UIFactory.CreatePanel(root, "Background", new Color(0.08f, 0.09f, 0.11f));
            UIFactory.Stretch(bg);

            // 被局外流程托管时，屏幕顶部那条已经被 GameApp 的状态栏占了，整体下移让开
            float top = parent != null ? -56f : 0f;

            // ---- 顶栏
            var topBar = UIFactory.CreatePanel(root, "TopBar", new Color(0f, 0f, 0f, 0.35f));
            UIFactory.SetAnchored(topBar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, top - 60), new Vector2(0, top));
            _turnText = UIFactory.CreateText(topBar, "Turn", "", 26);
            UIFactory.Stretch(_turnText.rectTransform);

            // ---- 战场层（敌人区 + 玩家区）
            //
            // ★ 单独一层是为了震屏：震它就等于震「被打的东西」，而 HUD、手牌、日志不动。
            //   全屏震会有两个实际问题：手牌区的拖拽判定要把屏幕坐标换算成 _handArea 的
            //   本地坐标（推导见上面 HandWidth 的注释），手牌一动判定就带着抖动的偏移；
            //   而且抖手牌非常晕。见 ScreenShake 的类注释。
            //
            // ★ 建在这个位置（原来 EnemyRow 的位置）是为了保持兄弟顺序 = 遮挡顺序不变。
            _battlefield = UIFactory.CreateEmpty(root, "Battlefield");
            UIFactory.Stretch(_battlefield);
            _shake = _battlefield.gameObject.AddComponent<ScreenShake>();

            // ---- 敌人区
            _enemyRow = UIFactory.CreateEmpty(_battlefield, "EnemyRow");
            UIFactory.SetAnchored(_enemyRow, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-700, top - 330), new Vector2(700, top - 80));

            // ---- 玩家区
            _playerSlot = UIFactory.CreateEmpty(_battlefield, "PlayerSlot");
            UIFactory.SetAnchored(_playerSlot, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(60, -100), new Vector2(320, 100));

            // ---- 能量
            var energyBg = UIFactory.CreatePanel(root, "Energy", new Color(0.85f, 0.7f, 0.2f, 0.9f));
            UIFactory.SetAnchored(energyBg, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 120), new Vector2(150, 230));
            _energyPanel = energyBg;
            _energyText = UIFactory.CreateText(energyBg, "EnergyText", "3/3", 34, TextAnchor.MiddleCenter, Color.black);
            UIFactory.Stretch(_energyText.rectTransform);

            // ---- 药水栏
            var potionHeader = UIFactory.CreateText(root, "PotionHeader", Loc.T("ui.battle.potions", "药　水"), 20,
                TextAnchor.MiddleLeft, new Color(0.62f, 0.86f, 0.78f));
            UIFactory.SetAnchored(potionHeader.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, top - 96), new Vector2(300, top - 68));

            _potionBar = UIFactory.CreateEmpty(root, "PotionBar");
            UIFactory.SetAnchored(_potionBar, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(24, top - 258), new Vector2(320, top - 100));

            // ---- 牌堆信息（三颗可点按钮）
            //
            // ★ 位置沿用原来那行纯文字的区域（左下 40..420 × 20..110），兄弟顺序也没变——
            //   它夹在能量球与 _handArea 之间，而 _handArea 的宽度是按遮挡关系推算出来的（铁律 24）。
            //   把这块往右挪或加宽，就要重算 HandWidth。
            BuildPileButtons(root);

            // ---- 结束回合按钮
            _endTurnButton = UIFactory.CreateButton(root, "EndTurn", Loc.T("ui.battle.end_turn", "结束回合"), 26, new Color(0.55f, 0.25f, 0.25f));
            UIFactory.SetAnchored((RectTransform)_endTurnButton.transform, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-260, 130), new Vector2(-40, 210));
            _endTurnButton.onClick.AddListener(OnEndTurnClicked);

            // ---- 战斗日志
            var logBg = UIFactory.CreatePanel(root, "LogPanel", new Color(0f, 0f, 0f, 0.4f));
            UIFactory.SetAnchored(logBg, new Vector2(1, 0.5f), new Vector2(1, 1), new Vector2(-420, -280), new Vector2(-20, -70));
            _logText = UIFactory.CreateText(logBg, "LogText", "", 18, TextAnchor.LowerLeft);
            UIFactory.Stretch(_logText.rectTransform, 10);

            // ---- 手牌区
            //
            // ★ 刻意建在能量球 / 药水栏 / 日志之后：uGUI 的遮挡顺序就是兄弟顺序，
            //   建在前面的话，拖起来的牌会钻到这些面板底下去。
            //   手牌区自己没有 Image，不会挡住任何点击。
            _handArea = UIFactory.CreateEmpty(root, "HandArea");

            // ★ pivot 必须先设再设 offset：pivot 定在底边中点后，手牌区的本地原点
            //   就和子节点 anchor(0.5, 0) 的参考点重合，
            //   于是「屏幕坐标 → 手牌区本地坐标」的结果可以直接当卡牌的 anchoredPosition 用。
            //   否则默认 pivot 是正中，两套坐标会差半个手牌区高度，出牌线的判定会莫名偏。
            _handArea.pivot = new Vector2(0.5f, 0f);
            UIFactory.SetAnchored(_handArea, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-HandWidth * 0.5f, HandAreaBottom), new Vector2(HandWidth * 0.5f, HandAreaTop));

            // ---- 拖拽层（出牌线 + 指向箭头）。在手牌之后，箭头才压得住举起来的那张牌。
            BuildDragLayer(root);

            // ---- 提示。★ 在手牌与拖拽层之后：抬起 / 举起的牌会侵入这条提示的高度，
            //      建在前面的话玩家正需要看提示的时候恰好被牌盖住。
            _hintText = UIFactory.CreateText(root, "Hint", "", 24, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
            UIFactory.SetAnchored(_hintText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-500, 290), new Vector2(500, 340));

            // ---- 整屏效果（回合横幅 / 受创闪光）。在手牌与提示之后、飘字之前：
            //      要盖住战场和 HUD，但不该盖住伤害数字——那是要读的。
            _overlayFx = BattleOverlayFx.Create(root);

            // ---- 飘字层
            PopupLayer = UIFactory.CreateEmpty(root, "PopupLayer");
            UIFactory.Stretch(PopupLayer);

            // ---- 模态层（选牌面板）。建在飘字层之后，保证它盖在飘字上面。
            _modalLayer = UIFactory.CreateEmpty(root, "ModalLayer");
            UIFactory.Stretch(_modalLayer);

            // ---- 结果面板
            _resultPanel = UIFactory.CreatePanel(root, "ResultPanel", new Color(0f, 0f, 0f, 0.75f));
            UIFactory.Stretch(_resultPanel);
            _resultText = UIFactory.CreateText(_resultPanel, "ResultText", "", 64);
            UIFactory.Stretch(_resultText.rectTransform);
            _resultPanel.gameObject.SetActive(false);
        }

        // ============================================================ 牌堆浏览

        /// <summary>三个牌堆各一颗按钮。数字每帧由 <see cref="RefreshHud"/> 写进 label。</summary>
        private void BuildPileButtons(RectTransform root)
        {
            const float x0 = 40f, y0 = 20f, w = 122f, h = 46f, gap = 6f;

            _drawPileButton = MakePileButton(root, "PileDraw", x0, y0, w, h, CardPile.Draw);
            _discardPileButton = MakePileButton(root, "PileDiscard", x0 + (w + gap), y0, w, h, CardPile.Discard);
            _exhaustPileButton = MakePileButton(root, "PileExhaust", x0 + (w + gap) * 2f, y0, w, h, CardPile.Exhaust);
        }

        private Button MakePileButton(RectTransform root, string name, float x, float y,
                                      float w, float h, CardPile pile)
        {
            var btn = UIFactory.CreateTextButton(root, name, "", 18, PileButtonColor, () => OpenPileView(pile));
            UIFactory.SetAnchored((RectTransform)btn.transform, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(x, y), new Vector2(x + w, y + h));

            // 按钮宽度写死 122，而英文比中文长 1.6–2 倍（「消耗堆 12」→「Exhaust 12」）
            // 且张数可能是两位数 → 让文字自己缩，别指望 122 永远够
            UIFactory.EnableAutoSize(UIFactory.LabelOf(btn), 12f, 18f);
            return btn;
        }

        private static readonly Color PileButtonColor = new Color(0.22f, 0.25f, 0.32f);

        /// <summary>当前开着的牌堆浏览面板。null = 没开。</summary>
        private CardListView _cardList;

        /// <summary>
        /// 打开某个牌堆。
        ///
        /// ★ 面板建在 <see cref="_modalLayer"/>（本界面自己的层），**不是** <c>GameApp.OverlayLayer</c>：
        ///   单场战斗调试场景 <c>Battle.unity</c> 里没有 <see cref="GameApp"/>，
        ///   用那一层的话调试场景一点就 NullReference。
        /// </summary>
        private void OpenPileView(CardPile pile)
        {
            if (Ctx == null || Ctx.BattleEnded || InputLocked) return;
            if (_cardList != null) return;

            var deck = Ctx.Deck;
            List<CardInstance> cards;
            string title;
            CardListOrder order;

            switch (pile)
            {
                case CardPile.Draw:
                    cards = deck.DrawPile;
                    title = Loc.T("ui.cardlist.title.draw", "抽牌堆（{0}）", deck.DrawPile.Count);
                    // ★ 抽牌堆必须排序：按真实顺序显示等于直接告诉玩家下几张抽什么（铁律 53）
                    order = CardListOrder.Sorted;
                    break;

                case CardPile.Discard:
                    cards = deck.DiscardPile;
                    title = Loc.T("ui.cardlist.title.discard", "弃牌堆（{0}）", deck.DiscardPile.Count);
                    order = CardListOrder.AsIs;
                    break;

                default:
                    cards = deck.ExhaustPile;
                    title = Loc.T("ui.cardlist.title.exhaust", "消耗堆（{0}）", deck.ExhaustPile.Count);
                    order = CardListOrder.AsIs;
                    break;
            }

            // ★ cards 直接传引用，由 CardListView 自己复制一份再排序——绝不能在这里就地排（铁律 52）
            _cardList = CardListView.Open(_modalLayer, title, cards, Database, order,
                                          onClosed: () => _cardList = null);
        }

        private void CloseCardList()
        {
            if (_cardList != null) _cardList.Close();
            _cardList = null;
        }

        private void BuildUnitViews()
        {
            for (int i = 0; i < _unitViews.Count; i++)
                if (_unitViews[i] != null) Destroy(_unitViews[i].gameObject);
            _unitViews.Clear();

            if (Ctx == null) return;

            var playerView = UnitView.Create(_playerSlot, this, Ctx.Player, true);
            UIFactory.SetAnchored((RectTransform)playerView.transform, new Vector2(0, 0), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            _unitViews.Add(playerView);

            int enemyCount = 0;
            for (int i = 0; i < Ctx.AllUnits.Count; i++) if (!Ctx.AllUnits[i].IsPlayer) enemyCount++;

            int idx = 0;
            for (int i = 0; i < Ctx.AllUnits.Count; i++)
            {
                var u = Ctx.AllUnits[i];
                if (u.IsPlayer) continue;

                var v = UnitView.Create(_enemyRow, this, u, false);
                var rt = (RectTransform)v.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                float spacing = 300f;
                float x = (idx - (enemyCount - 1) * 0.5f) * spacing;
                rt.anchoredPosition = new Vector2(x, 0);
                _unitViews.Add(v);
                idx++;
            }
        }

        // ============================================================ 输入

        /// <summary>
        /// 表现层还在播事件时禁止输入。
        ///
        /// ★ 战斗逻辑是同步的：玩家点「结束回合」的一瞬间，敌人回合已经跑完、
        ///   下一个自己的回合也已经开始，而 BattlePresenter 还在按 0.12 秒一条地播动画。
        ///   没有这道门禁，玩家可以在敌人的攻击动画还没播出来的时候就出牌，
        ///   画面和逻辑完全脱节（打出去的牌看起来「提前」生效了）。
        ///
        /// 事件队列清空 == 表现追上了逻辑。presenter 不在时不上锁，避免永久卡死。
        /// </summary>
        private bool InputLocked
            => Ctx == null
               || Ctx.IsWaitingForSelection
               || (Ctx.Events.Count > 0 && _presenter != null && _presenter.isActiveAndEnabled);

        /// <summary>
        /// 点击出牌（选中 → 点目标）。★ 与拖拽双轨并存，不是遗留代码：
        /// 触屏、以及习惯了「先看清再确认」的玩家都需要它，药水栏也是同一套交互。
        /// </summary>
        public void OnCardClicked(CardView view)
        {
            if (Ctx == null || Ctx.BattleEnded || InputLocked) return;
            if (_dragMode != DragMode.None) return;

            if (_selected == view) { _selected = null; return; }

            if (!_controller.CanPlayCard(view.Card, FirstAliveEnemy(), out var reason)
                && reason != PlayFailReason.NeedTarget)
            {
                ShowHint(ReasonText(reason));
                return;
            }

            if (_controller.NeedsTargetSelection(view.Card))
            {
                _selected = view;
                ShowHint(Loc.T("ui.battle.pick_target", "选择一个目标"));
            }
            else
            {
                PlaySelected(view.Card, null);
            }
        }

        public void OnUnitClicked(UnitView view)
        {
            if (Ctx == null || Ctx.BattleEnded || InputLocked) return;
            if (view.Unit == null || view.Unit.IsPlayer || !view.Unit.IsAlive) return;

            if (_selectedPotion != null) { UsePotion(_selectedPotion, view.Unit); return; }
            if (_selected == null) return;

            PlaySelected(_selected.Card, view.Unit);
        }

        private void PlaySelected(CardInstance card, BattleUnit target)
        {
            // ★ 必须在 TryPlayCard **之前**算好飞行终点：出牌是同步结算的，
            //   这一行返回时目标可能已经死了、面板可能已经变灰，甚至战斗都结束了。
            PrepareFlyOut(card, target);

            if (!_controller.TryPlayCard(card, target, out var reason))
            {
                _flyOutUid = -1;   // 没打出去，那就没有牌要飞
                ShowHint(ReasonText(reason));
            }
            else
            {
                ShowHint("");
            }

            _selected = null;
        }

        // ============================================================ 出牌飞行

        /// <summary>刚打出去、等着从手牌里消失并飞走的那张牌。-1 表示没有。</summary>
        private int _flyOutUid = -1;

        /// <summary>它该飞向哪（PopupLayer 的本地坐标）。</summary>
        private Vector2 _flyOutTo;

        /// <summary>
        /// 记下「这张牌该往哪飞」。
        /// ★ 无目标的牌飞向玩家自己——防御、能力这类的作用对象本来就是自己，
        ///   一律往敌人飞会让「获得护甲」看起来像是打了对面一下。
        /// </summary>
        private void PrepareFlyOut(CardInstance card, BattleUnit target)
        {
            _flyOutUid = card != null ? card.Uid : -1;
            if (_flyOutUid < 0) return;

            UnitView dest = target != null ? FindUnitView(target.Uid) : null;
            if (dest == null && Ctx != null && Ctx.Player != null) dest = FindUnitView(Ctx.Player.Uid);

            _flyOutTo = dest != null ? AnchoredPosOf(dest) : Vector2.zero;
        }

        /// <summary>取消一切「正在指目标」的状态：选中的牌、选中的药水、正在进行的拖拽。</summary>
        private void CancelTargeting()
        {
            _selected = null;
            _selectedPotion = null;
            EndDrag();
            ShowHint("");
        }

        private void OnEndTurnClicked()
        {
            if (Ctx == null || Ctx.BattleEnded || InputLocked) return;
            _selected = null;
            EndDrag();
            _controller.EndTurn();
        }

        private void Update()
        {
            // ★ 牌堆浏览面板优先吃掉 Esc / 右键，而且必须**在 CancelTargeting 之前**问它。
            //   写成「面板自己在 Update 里轮询」会踩时序：两个 MonoBehaviour 的 Update
            //   先后顺序不确定，本方法有可能同一帧先跑，Esc 被 CancelTargeting 先吃掉，
            //   于是面板「有时候关得掉、有时候关不掉」。见 CardListView.ConsumeCancelInput。
            if (_cardList != null && _cardList.ConsumeCancelInput()) return;

            // 取消选择永远允许——即使在播动画，玩家也该能反悔
            if (InputCompat.RightMouseDown || InputCompat.EscapeDown)
            {
                CancelTargeting();
                return;
            }

            // ★ 面板开着时必须挡住键盘。遮罩只吃**射线**，空格 / E 照样能打到这里，
            //   玩家会在看着弃牌堆的时候把自己的回合结束掉。
            if (_cardList != null) return;

            // ---- 催表现快点播完
            //
            // ★ 优先级写死在这三段的先后顺序里：牌堆面板 > 取消（右键 / Esc）> 快进。
            //   快进必须排最后，否则玩家想按 Esc 取消选中的牌时会先被当成「催一下」。
            //
            // ★ 只在 InputLocked 时响应：没积压的时候点屏幕是在出牌 / 选目标，
            //   那些点击由 EventSystem 派发给具体的 Graphic，与这里无关。
            //   这里之所以要自己读输入而不是挂个按钮，是因为「点屏幕任何地方都算」——
            //   包括点在背景、日志、空白处，那些位置没有任何可点的 Graphic。
            if (InputLocked && _presenter != null
                && (InputCompat.LeftMouseDown || InputCompat.SpaceDown || InputCompat.KeyDown(KeyCode.E)))
            {
                _presenter.RequestFastForward();
                return;
            }

            if (InputCompat.SpaceDown || InputCompat.KeyDown(KeyCode.E))
            {
                // ★ 正在选目标时，空格先取消选择而不是直接结束回合，
                //   否则玩家会莫名其妙地把选好的牌 / 药水丢掉。
                if (_selected != null || _selectedPotion != null || _dragMode != DragMode.None)
                {
                    CancelTargeting();
                    return;
                }
                OnEndTurnClicked();
            }
        }

        // ============================================================ 刷新

        private void LateUpdate()
        {
            if (Ctx == null) return;

            // Bind 时战斗还没开始的话，在这里补建单位面板并重新接上表现层
            if (!ReferenceEquals(_boundCtx, Ctx)) AdoptContext();

            SyncSelectionPicker();

            // 状态一变就重算敌人意图数值。★ 不这样做的话，玩家给敌人上「虚弱」之后
            // 意图上显示的还是回合开始时算的旧数字，玩家会照着错的数字做决策。
            if (_intentVersion != Ctx.StateVersion)
            {
                _intentVersion = Ctx.StateVersion;
                _controller.RefreshIntents();
            }

            // ★ 顺序有讲究，三步都不能换：
            //   ① 先扫队列，得出「哪些牌还没发出来 / 还没走掉」；
            //   ② RefreshHandViews 据此决定建谁、收谁；
            //   ③ UpdateLeavingCards 放飞那些「事件刚被播到」的离场牌。
            //   ③ 必须在 ② 之后：这一帧刚被 ② 塞进 _leaving 的牌，
            //   如果它那条事件同帧就已经播完（0 时长事件连播），③ 会当场把它放走，不必等下一帧。
            ScanPendingCardEvents();
            RefreshHandViews();
            UpdateLeavingCards();

            // ★ 顺序有讲究：先算拖拽状态（它会写 _dragCardSlot / _aimTarget），
            //   LayoutHand 才排得出被拖那张牌的位置。
            UpdateDragVisuals();

            // 正举着一张牌找目标时不该有提示框跳出来碍事。
            //
            // ★ 这是一次**无条件赋值**，所以任何别处压下的 Suppressed 都会被它每帧冲掉。
            //   牌堆浏览面板弹出放大卡面时也要压住 tooltip，因此必须在这里 OR 进来——
            //   否则大卡开着，底下网格的 tooltip 照样从大卡背后冒出来（铁律 31 的另一面：
            //   全局静态开关不止「忘了放开」会坏，「被别人每帧覆盖」也会坏，而且更难看出来）。
            TooltipView.Suppressed = _dragMode != DragMode.None
                                     || (_cardList != null && _cardList.SuppressesTooltip);

            LayoutHand();
            RefreshCards();
            RefreshPotionBar();
            RefreshUnits();
            RefreshHud();
        }

        /// <summary>Uid → 已经建好的手牌视图。增量复用靠它。</summary>
        private readonly Dictionary<int, CardView> _viewByUid = new Dictionary<int, CardView>();
        private readonly List<CardView> _viewBuffer = new List<CardView>();
        private readonly HashSet<int> _liveUids = new HashSet<int>();

        /// <summary>手牌视图的兄弟顺序是否需要重排。</summary>
        private bool _orderDirty;

        /// <summary>当前被提到最前面的那张牌（悬停 / 选中 / 拖拽）。</summary>
        private CardView _frontCard;

        /// <summary>鼠标正悬停的牌。</summary>
        private CardView _hoveredCard;

        private const float SpawnRotation = -28f;
        private const float SpawnScale = 0.55f;

        /// <summary>落位弹跳的幅度（0.10 = 到位那一下鼓到 110%）。</summary>
        private const float DealPunch = 0.10f;

        /// <summary>
        /// 新建手牌视图的起点：抽牌堆按钮的**真实位置**，换算成 `_handArea` 的本地坐标。
        ///
        /// ★ 原本这里是写死的 <c>(-720, -20)</c>，而抽牌堆按钮在左下 x 40..162
        ///   （换算过来约 x = -860）——差了 140 像素，牌是从抽牌堆**旁边**冒出来的。
        ///   牌少的时候没人看得出，一旦改成一张一张发，玩家的视线会跟着每一张牌从头看到尾，
        ///   起点对不对就变得很显眼了。
        ///
        /// ★ 每次现算而不是缓存：`Bind` 那一帧 Canvas 可能还没完成第一次布局，
        ///   此刻算出来的值是错的，而缓存会把这个错误一直留到战斗结束。
        ///   代价只是每张新牌两次矩阵变换。
        /// </summary>
        private Vector2 SpawnSlot => _drawPileButton != null
            ? CenterIn(_handArea, (RectTransform)_drawPileButton.transform)
            : new Vector2(-720f, -20f);

        /// <summary>
        /// 把 <paramref name="source"/> 的矩形中心换算成 <paramref name="target"/> 的本地坐标。
        /// 两个 Canvas 都是 Overlay，所以相机传 null。
        /// </summary>
        private static Vector2 CenterIn(RectTransform target, RectTransform source)
        {
            if (target == null || source == null) return Vector2.zero;
            Vector3 world = source.TransformPoint(source.rect.center);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(target, screen, null, out var local);
            return local;
        }

        // ============================================================ 逐张发牌 / 逐张离场
        //
        // ★ 这一段解决的是全工程唯一一处「表现不跟事件队列走」的地方。
        //
        //   血条、护甲、飘字、震屏全都由 BattlePresenter 从 BattleContext.Events 里
        //   一条一条取出来播（见 UnitView._shownHp）；唯独手牌视图是**直接读逻辑状态**的
        //   ——LateUpdate 拿 Deck.Hand 的 Uid 签名比对，一变就把缺的视图全部补齐。
        //   而战斗逻辑是同步的：BeginTurn 里 Deck.Draw(5) 是个纯 for 循环，返回时 5 张牌
        //   已经全在 Hand 里了。于是 5 张牌在同一帧诞生，5 条一模一样的飞入动画叠在一起，
        //   看起来就是「啪」一下多了一把牌。
        //
        //   补法是给它一个节拍器，而节拍器早就有了：DeckController.DrawOne 每抽一张就
        //   Post 一条 CardDrawn（带 card.Uid），只是 BattlePresenter 从来没接过它、
        //   DurationOf 也给的 0。现在给了时长，这里只要多问一句：
        //   **「这张牌的 CardDrawn 播过了吗？没播过就先别画它。」**

        /// <summary>队列里还没被播到的 CardDrawn 的 Uid：逻辑上已在手，表现上还没发出来。</summary>
        private readonly HashSet<int> _pendingDraw = new HashSet<int>();

        /// <summary>队列里还没被播到的 CardDiscarded / CardExhausted 的 Uid。</summary>
        private readonly HashSet<int> _pendingLeave = new HashSet<int>();

        /// <summary>
        /// 一张正在等着离场的牌，以及它该飞去哪。
        /// ★ 刻意做成一个结构体而不是两个按下标对应的 List：那正是铁律 33 那条错位的形状，
        ///   而这里的增删发生在两个不同的方法里，更容易对不齐。
        /// </summary>
        private struct LeavingCard
        {
            public CardView View;
            public CardPile To;
        }

        /// <summary>已经离手、正等着自己那条事件被播到才起飞的视图。</summary>
        private readonly List<LeavingCard> _leaving = new List<LeavingCard>();

        /// <summary>过滤掉「还没发出来」的那些牌之后的手牌。</summary>
        private readonly List<CardInstance> _visibleHand = new List<CardInstance>(12);

        /// <summary>
        /// 扫一遍表现事件队列，记下哪些牌的「进」「出」还没被播到。
        ///
        /// ★ 用**扫队列**而不是「presenter 播到时通知我一声、我记进一个集合」：
        ///   后者要自己维护跨帧状态，于是必须自己回答三个问题——换战斗时谁清、读档时谁清、
        ///   战斗结束时谁清；而且任何一条没发出来的事件（别的路径进手牌、逻辑层将来改写）
        ///   都会让那张牌**永远不出现，且不报任何错**。
        ///   扫队列没有状态：队列播空 → 集合天然为空 → 手上所有牌必然可见。
        ///   `AddCard(CardPile.Hand)` 发的是 CardAdded、固有牌在 Init 时就进了抽牌堆、
        ///   保留牌跨回合根本没离手——这些从来不进集合，行为与改动前逐帧完全相同。
        ///
        /// ★ 零 GC：Ctx.Events 声明成具体的 Queue&lt;BattleEvent&gt;，
        ///   foreach 用的是它的 struct 枚举器，不装箱。队列常态不到 20 条。
        /// </summary>
        private void ScanPendingCardEvents()
        {
            _pendingDraw.Clear();
            _pendingLeave.Clear();

            // presenter 不在（或被关掉）时事件永远不会被消费，此时一条都不该挡——
            // 否则手牌会整把消失。这与 InputLocked 里「presenter 不在就不上锁」是同一条兜底。
            if (_presenter == null || !_presenter.isActiveAndEnabled) return;

            foreach (var e in Ctx.Events)
            {
                switch (e.Type)
                {
                    case BattleEventType.CardDrawn:
                        _pendingDraw.Add(e.TargetUid);
                        break;

                    case BattleEventType.CardDiscarded:
                    case BattleEventType.CardExhausted:
                        _pendingLeave.Add(e.TargetUid);
                        break;
                }
            }
        }

        /// <summary>
        /// 手牌视图与 <c>Deck.Hand</c> 对齐（准确说是与**可见手牌**对齐，见 <see cref="ScanPendingCardEvents"/>）。
        ///
        /// ★ 增量复用而不是全量重建：原来手牌一变就 Destroy 全部 CardView 再重建，
        ///   于是打出一张牌之后其余牌是「瞬移」到新的扇形位置的——
        ///   扇形排列的手感几乎全部来自这段滑动，全量重建等于把它抹掉。
        ///   顺带这也是抽牌/出牌动画的硬前置（原「已知遗留 #1」）。
        /// </summary>
        private void RefreshHandViews()
        {
            var hand = Ctx.Deck.Hand;

            // ---- 可见手牌 = 手牌 − 还没「发」出来的那些
            _visibleHand.Clear();
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                if (_pendingDraw.Count > 0 && _pendingDraw.Contains(c.Uid)) continue;
                _visibleHand.Add(c);
            }

            // ★ 签名必须按**可见手牌**比对。按 hand 比对的话，presenter 每播掉一条 CardDrawn
            //   手牌本身并没有变化，签名也就不变，整帧被 early-out 跳过——
            //   牌会一直不出现，直到下一次真的有牌进出手牌为止。
            bool changed = _visibleHand.Count != _handSignature.Count;
            if (!changed)
            {
                for (int i = 0; i < _visibleHand.Count; i++)
                    if (_visibleHand[i].Uid != _handSignature[i]) { changed = true; break; }
            }
            if (!changed) return;

            _liveUids.Clear();
            for (int i = 0; i < _visibleHand.Count; i++) _liveUids.Add(_visibleHand[i].Uid);

            // ---- 回收已经离手的牌
            for (int i = 0; i < _cardViews.Count; i++)
            {
                var v = _cardViews[i];
                if (v == null) continue;
                if (v.Card != null && _liveUids.Contains(v.Card.Uid)) continue;

                // ★ 「刚被打出去」优先于「被弃掉」。
                //   打出一张牌的收尾会走 FinishPlay → SendCardToDestination → Deck.Discard，
                //   所以它**同时**发了 CardPlayed 和 CardDiscarded，两条路径都认领得了这张牌。
                //   必须让飞向目标那条赢：那是「谁干的」这句因果的唯一表达（见 CardFlyOut 的类注释），
                //   被弃牌动画抢走的话，敌人闪白、被击退、飘出数字，而画面上没有任何东西指向它。
                bool flies = v.Card != null && v.Card.Uid == _flyOutUid;

                if (v.Card != null) _viewByUid.Remove(v.Card.Uid);
                ForgetCardView(v);

                if (flies)
                {
                    _flyOutUid = -1;
                    CardFlyOut.Play(v, PopupLayer, _flyOutTo);
                }
                else if (v.Card != null && _pendingLeave.Contains(v.Card.Uid))
                {
                    // 它自己那条 CardDiscarded / CardExhausted 还没播到 → 先钉在原地等着
                    BeginLeaving(v);
                }
                else
                {
                    Destroy(v.gameObject);
                }
            }

            // ---- 按手牌顺序重排，缺的现建
            _viewBuffer.Clear();
            for (int i = 0; i < _visibleHand.Count; i++)
            {
                var card = _visibleHand[i];
                if (!_viewByUid.TryGetValue(card.Uid, out var v) || v == null)
                {
                    v = CardView.Create(_handArea, this, card);
                    v.SnapTo(SpawnSlot, SpawnRotation, SpawnScale);

                    // 到位再弹，不是现在弹——现在这张牌还趴在屏幕左下的抽牌堆上
                    v.ArmArrivalPunch(DealPunch * FeedbackSettings.HitMotionScale);
                    FlashPileButton(CardPile.Draw);

                    _viewByUid[card.Uid] = v;
                }
                _viewBuffer.Add(v);
            }

            _cardViews.Clear();
            _cardViews.AddRange(_viewBuffer);

            _handSignature.Clear();
            for (int i = 0; i < _visibleHand.Count; i++) _handSignature.Add(_visibleHand[i].Uid);

            _orderDirty = true;
        }

        /// <summary>
        /// 一张牌离手了，但它的「弃掉 / 消耗」还没轮到播。先把它从手牌逻辑里摘干净、钉在原地。
        /// </summary>
        private void BeginLeaving(CardView v)
        {
            // ★ 必须当场断掉射线：它已经不在 _cardViews 里，LayoutHand / RefreshCards 都不会再管它，
            //   但它仍然是屏幕上一张长得一模一样的牌。玩家点上去会走进 OnCardClicked，
            //   拿着一张已经不在手里的 CardInstance 去问 CanPlayCard——不会崩，但会弹一句
            //   莫名其妙的失败提示；悬停还会给它弹 tooltip。
            var group = v.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = v.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // ★ 归宿从**牌现在真的在哪一堆**读，不从事件类型猜：
            //   CardExhausted 与 CardDiscarded 在队列里长得一样（都只带 Uid），
            //   而一张牌任何时刻只属于一个牌堆，直接问牌堆是唯一不会答错的问法。
            _leaving.Add(new LeavingCard
            {
                View = v,
                To = Ctx.Deck.ExhaustPile.Contains(v.Card) ? CardPile.Exhaust : CardPile.Discard
            });
        }

        /// <summary>
        /// 放飞那些「事件刚被播到」的离场牌。
        ///
        /// ★ 判据是「它的 Uid 从 <see cref="_pendingLeave"/> 里消失了」= presenter 本帧刚播过那一条。
        ///   与发牌那半边是同一个套路，方向相反。
        /// </summary>
        private void UpdateLeavingCards()
        {
            for (int i = _leaving.Count - 1; i >= 0; i--)
            {
                var item = _leaving[i];
                var v = item.View;
                if (v == null) { _leaving.RemoveAt(i); continue; }

                // ★ 兜底：队列播空 / 战斗结束时无条件放飞。
                //   战斗结束会把没播完的事件连同整个 Ctx 一起丢掉，
                //   没有这一句的话这些牌会永远钉在屏幕上，盖在结算面板底下。
                bool due = Ctx.Events.Count == 0
                           || Ctx.BattleEnded
                           || v.Card == null
                           || !_pendingLeave.Contains(v.Card.Uid);

                if (!due) continue;

                _leaving.RemoveAt(i);

                FlashPileButton(item.To);
                CardFlyOut.Play(v, PopupLayer, PileAnchor(item.To));
            }
        }

        /// <summary>某个牌堆按钮的位置，换算成 <see cref="PopupLayer"/> 的本地坐标（飞行终点用）。</summary>
        private Vector2 PileAnchor(CardPile pile)
        {
            var btn = pile == CardPile.Exhaust ? _exhaustPileButton
                    : pile == CardPile.Draw ? _drawPileButton
                    : _discardPileButton;

            return btn != null
                ? CenterIn(PopupLayer, (RectTransform)btn.transform)
                : Vector2.zero;
        }

        /// <summary>某张牌的视图即将消失：把所有指向它的引用清掉。</summary>
        private void ForgetCardView(CardView v)
        {
            if (_selected == v) _selected = null;
            if (_hoveredCard == v) _hoveredCard = null;
            if (_frontCard == v) { _frontCard = null; _orderDirty = true; }
            if (_dragCard == v) EndDrag();
        }

        /// <summary>换战斗时把手牌视图整批丢掉——它们属于上一场的 CardInstance。</summary>
        private void ClearHandViews()
        {
            for (int i = 0; i < _cardViews.Count; i++)
                if (_cardViews[i] != null) Destroy(_cardViews[i].gameObject);

            // ★ 离场队列也要一起清。它装的是「已经离手、正等着自己那条事件被播到」的牌，
            //   换战斗时那些事件属于上一场、永远不会再来，留着就是一把钉在新战斗界面上的旧牌。
            for (int i = 0; i < _leaving.Count; i++)
                if (_leaving[i].View != null) Destroy(_leaving[i].View.gameObject);
            _leaving.Clear();

            _pendingDraw.Clear();
            _pendingLeave.Clear();
            _visibleHand.Clear();

            _cardViews.Clear();
            _viewByUid.Clear();
            _handSignature.Clear();
            _selected = null;
            _hoveredCard = null;
            _frontCard = null;
            EndDrag();
        }

        /// <summary>
        /// 算出每张牌的目标位姿。只写「目标」，实际移动由 <see cref="CardView"/> 自己插值。
        /// </summary>
        private void LayoutHand()
        {
            int n = _cardViews.Count;
            if (n == 0) return;

            _hoveredCard = null;
            for (int i = 0; i < n; i++)
                if (_cardViews[i] != null && _cardViews[i].Hovered) { _hoveredCard = _cardViews[i]; break; }

            for (int i = 0; i < n; i++)
            {
                var v = _cardViews[i];
                if (v == null) continue;

                var slot = HandFanLayout.Compute(i, n, HandWidth, HandBaseY);
                float scale = 1f;

                if (v == _dragCard)
                {
                    if (_dragMode == DragMode.Free)
                    {
                        // 自由拖拽：牌跟着手走，扶正
                        slot.Position = _dragCardSlot;
                        slot.Rotation = 0f;
                        scale = 1.06f;
                    }
                    else
                    {
                        // 举牌拉箭头：牌飞到固定的「举牌位」定住，不跟手——
                        // 跟手的话牌本身会挡住玩家想点的敌人
                        slot.Position = AimSlot;
                        slot.Rotation = 0f;
                        scale = 1.12f;
                    }
                }
                else if (v == _selected)
                {
                    slot.Position.y += SelectedLift;
                    slot.Rotation *= 0.35f;      // 选中的牌扶正一点，字更好读
                    scale = 1.12f;
                }
                else if (v == _hoveredCard && _dragMode == DragMode.None)
                {
                    slot.Position.y += HoverLift;
                    slot.Rotation *= 0.45f;
                    scale = 1.10f;
                }

                v.SetLayoutTarget(slot.Position, slot.Rotation, scale);
            }

            ApplyHandOrder();
        }

        /// <summary>
        /// 兄弟顺序 = 遮挡顺序：左→右递增（右边的牌压住左边的），
        /// 悬停 / 选中 / 拖拽的那张提到最前，否则它会被右边的邻居切掉一半。
        /// ★ 只在「谁在最前」变了或视图列表重建过时才动——SetSiblingIndex 会让 Canvas 变脏。
        /// </summary>
        private void ApplyHandOrder()
        {
            CardView front = _dragCard != null ? _dragCard
                           : _selected != null ? _selected
                           : _hoveredCard;

            if (!_orderDirty && front == _frontCard) return;

            _frontCard = front;
            _orderDirty = false;

            for (int i = 0; i < _cardViews.Count; i++)
                if (_cardViews[i] != null) _cardViews[i].transform.SetSiblingIndex(i);

            if (front != null) front.transform.SetAsLastSibling();
        }

        private void RefreshCards()
        {
            var probe = FirstAliveEnemy();
            bool locked = InputLocked;

            for (int i = 0; i < _cardViews.Count; i++)
            {
                var v = _cardViews[i];
                bool playable = !locked
                                && (_controller.CanPlayCard(v.Card, probe, out var reason)
                                    || reason == PlayFailReason.NeedTarget);
                v.Refresh(Ctx, playable, v == _selected || v == _dragCard);
            }
        }

        private void RefreshUnits()
        {
            bool targeting = _selected != null || _selectedPotion != null || _dragMode == DragMode.Aim;
            for (int i = 0; i < _unitViews.Count; i++)
            {
                var v = _unitViews[i];
                bool targetable = targeting && v.Unit != null && !v.Unit.IsPlayer && v.Unit.IsAlive;
                bool highlighted = _aimTarget != null && v.Unit == _aimTarget;
                v.Refresh(Ctx, targetable, highlighted);
            }
        }

        /// <summary>上一帧看到的能量。-1 表示还没看过（首帧不该弹）。</summary>
        private int _lastEnergy = -1;

        /// <summary>
        /// 花掉能量时能量球脉冲一下。
        /// ★ 只在**减少**时弹：回合开始能量从 0 回满也是一次变化，但那是补给不是消耗，
        ///   两者都弹的话每回合开头必弹一次，很快就没人看了。
        /// </summary>
        private void PulseEnergyOnSpend(int energy)
        {
            int previous = _lastEnergy;
            _lastEnergy = energy;

            if (previous < 0 || energy >= previous) return;
            if (_energyPanel == null || FeedbackSettings.HitMotionScale <= 0.001f) return;

            DOTween.Kill(_energyPanel);
            _energyPanel.localScale = Vector3.one;
            _energyPanel.DOPunchScale(Vector3.one * (0.3f * FeedbackSettings.HitMotionScale), 0.3f, 6, 0.6f);
        }

        private void RefreshHud()
        {
            _turnText.text = Loc.T("ui.battle.turn_header", "第 {0} 回合    —    {1}", Ctx.TurnNumber, PhaseText(Ctx.Phase));

            _energyText.text = $"{Ctx.Energy}/{Ctx.EnergyPerTurn}";
            PulseEnergyOnSpend(Ctx.Energy);
            RefreshPileButtons();

            if (_presenter != null)
            {
                _sb.Clear();
                var log = _presenter.Log;
                int start = Mathf.Max(0, log.Count - 12);
                for (int i = start; i < log.Count; i++) _sb.AppendLine(log[i]);
                _logText.text = _sb.ToString();
            }

            _endTurnButton.interactable = Ctx.Phase == BattlePhase.PlayerTurn && !InputLocked;

            if (Ctx.BattleEnded && !_resultPanel.gameObject.activeSelf && Ctx.Events.Count == 0)
            {
                _resultPanel.gameObject.SetActive(true);
                _resultText.text = Ctx.Victory ? Loc.T("ui.battle.victory", "战 斗 胜 利") : Loc.T("ui.battle.defeat", "战 斗 失 败");
                _resultText.color = Ctx.Victory ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.4f, 0.4f);
            }
        }

        /// <summary>
        /// 三颗牌堆按钮的文字与可用性。
        ///
        /// ★ 表现事件还在播时置灰（<see cref="InputLocked"/>）：战斗逻辑是同步的，
        ///   玩家点「结束回合」那一瞬间敌人回合已经跑完、下一个自己的回合也开始了，
        ///   而画面还在按 0.12 秒一条地播。此刻打开弃牌堆看到的是**逻辑上的现在**，
        ///   与玩家眼前的画面对不上。与手牌变灰 / 结束回合置灰是同一条纪律。
        /// </summary>
        private void RefreshPileButtons()
        {
            var deck = Ctx.Deck;
            bool on = !InputLocked && !Ctx.BattleEnded;

            SetPileButton(_drawPileButton, Loc.T("ui.battle.pile.draw", "抽牌堆 {0}", deck.DrawPile.Count), on);
            SetPileButton(_discardPileButton, Loc.T("ui.battle.pile.discard", "弃牌堆 {0}", deck.DiscardPile.Count), on);
            SetPileButton(_exhaustPileButton, Loc.T("ui.battle.pile.exhaust", "消耗堆 {0}", deck.ExhaustPile.Count), on);

            // ★ 兜底：战斗结束时把还开着的面板收掉，否则它会浮在结算面板上。
            //   正常流程走不到这里（面板开着时玩家点不了任何能推进战斗的东西），
            //   但第三次会话的界面泄漏就是「以为走不到」的那一类，留一行不亏。
            if (Ctx.BattleEnded && _cardList != null) CloseCardList();
        }

        private void SetPileButton(Button btn, string label, bool on)
        {
            if (btn == null) return;
            var text = UIFactory.LabelOf(btn);
            if (text != null) text.text = label;
            UIFactory.SetInteractable(btn, on, PileButtonColor);
        }

        // ============================================================ 牌堆反馈

        /// <summary>牌堆按钮被「碰到」时鼓一下的幅度与时长。</summary>
        private const float PileFlashPunch = 0.22f;
        private const float PileFlashTime = 0.22f;

        /// <summary>
        /// 某个牌堆刚吞进 / 吐出一张牌，让它的按钮弹一下。
        ///
        /// ★ 这是三颗按钮并排挤在左下角的补偿。抽牌堆 x 40..162、弃牌堆 168..290、
        ///   消耗堆 296..418，彼此只隔 128 像素——牌飞过去的终点在画面上几乎是同一个角落，
        ///   光看轨迹分不出这张牌到底进了哪一堆。让接收方自己动一下，指向才算说清楚。
        ///
        /// ★ 只动 scale 不动颜色：按钮底色被 <see cref="SetPileButton"/> 每帧无条件重写
        ///   （表现播放期间要置灰），在这里 tween 颜色活不过一帧——同铁律 54 那条，
        ///   两边的代码单独看都是对的。
        /// </summary>
        private void FlashPileButton(CardPile pile)
        {
            if (FeedbackSettings.HitMotionScale <= 0.001f) return;

            var btn = pile == CardPile.Exhaust ? _exhaustPileButton
                    : pile == CardPile.Discard ? _discardPileButton
                    : _drawPileButton;
            if (btn == null) return;

            var rt = (RectTransform)btn.transform;
            DOTween.Kill(rt);
            rt.localScale = Vector3.one;
            rt.DOPunchScale(Vector3.one * (PileFlashPunch * FeedbackSettings.HitMotionScale),
                            PileFlashTime, 5, 0.7f).SetTarget(rt);
        }

        /// <summary>
        /// 洗牌：一叠牌从弃牌堆飞回抽牌堆。由 <see cref="BattlePresenter"/> 播到
        /// <see cref="BattleEventType.DeckShuffled"/> 时调。
        /// </summary>
        public void PlayShuffleFx()
        {
            if (PopupLayer == null) return;

            PileFlyFx.Play(PopupLayer, PileAnchor(CardPile.Discard), PileAnchor(CardPile.Draw));
            FlashPileButton(CardPile.Draw);
        }

        // ============================================================ 拖拽出牌

        /// <summary>
        /// 拖拽的两种形态。分岔点是 <see cref="BattleController.NeedsTargetSelection"/>：
        /// 要指定单个敌人的牌走 <see cref="Aim"/>，其余走 <see cref="Free"/>。
        /// </summary>
        private enum DragMode
        {
            None,

            /// <summary>举牌 + 拉箭头指目标（SingleEnemy）。</summary>
            Aim,

            /// <summary>牌跟着手走，越过出牌线松手即出（None / Self / AllEnemies / RandomEnemy）。</summary>
            Free
        }

        private CardView _dragCard;
        private DragMode _dragMode;

        /// <summary>光标屏幕坐标。★ 存下来而不是每次向输入系统要：
        /// OnDrag 只在光标移动时才来，而箭头起点会因为手牌重排而移动，
        /// 所以每帧都要用「最后一次已知的光标位置」重画。</summary>
        private Vector2 _dragPointer;

        /// <summary>抓取瞬间「牌的位置 − 光标位置」。保留它，拖起来时牌才不会跳一下。</summary>
        private Vector2 _dragGrabOffset;

        /// <summary>Free 模式下这张牌本帧该在哪（_handArea 本地坐标）。</summary>
        private Vector2 _dragCardSlot;

        /// <summary>Aim 模式下光标锁住的敌人。null 表示没指到任何合法目标。</summary>
        private BattleUnit _aimTarget;
        private BattleUnit _lastAimTarget;

        /// <summary>Free 模式下当前松手能不能出牌。</summary>
        private bool _freeDropReady;

        private RectTransform _dragLayer;
        private TargetArrowView _arrow;
        private RectTransform _playLine;
        private Image _playLineBar;
        private TMP_Text _playLineLabel;

        /// <summary>举牌位（_handArea 本地坐标）。选在敌人区与手牌区之间的空档上。</summary>
        private static readonly Vector2 AimSlot = new Vector2(0f, 400f);

        private static readonly Color ArrowFree = new Color(0.95f, 0.85f, 0.45f, 0.80f);
        private static readonly Color ArrowLocked = new Color(1.00f, 0.42f, 0.35f, 0.95f);
        private static readonly Color PlayLineIdle = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color PlayLineReady = new Color(0.55f, 0.95f, 0.60f, 0.90f);

        private void BuildDragLayer(RectTransform root)
        {
            _dragLayer = UIFactory.CreateEmpty(root, "DragLayer");
            UIFactory.Stretch(_dragLayer);

            // 出牌线。★ 用「屏幕底边 + HandAreaBottom + PlayLineY」定位，
            //   这样它和 PlayLineY 的判定值永远指同一条线（PlayLineY 是手牌区本地坐标）。
            float lineY = HandAreaBottom + PlayLineY;

            _playLine = UIFactory.CreateEmpty(_dragLayer, "PlayLine");
            UIFactory.SetAnchored(_playLine, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, lineY), new Vector2(0, lineY + 34f));

            var bar = UIFactory.CreatePanel(_playLine, "Bar", PlayLineIdle);
            UIFactory.SetAnchored(bar, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(120f, 0f), new Vector2(-120f, 3f));
            _playLineBar = bar.GetComponent<Image>();
            _playLineBar.raycastTarget = false;      // 这条线横穿屏幕，绝不能吃掉点击

            _playLineLabel = UIFactory.CreateText(_playLine, "Label", Loc.T("ui.battle.play_line", "松 手 出 牌"), 18,
                TextAnchor.MiddleCenter, PlayLineIdle);
            UIFactory.SetAnchored(_playLineLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-140f, 6f), new Vector2(140f, 32f));

            _playLine.gameObject.SetActive(false);

            _arrow = TargetArrowView.Create(_dragLayer, "TargetArrow");
            _arrow.gameObject.SetActive(false);
        }

        public void OnCardBeginDrag(CardView view, PointerEventData e)
        {
            if (view == null || view.Card == null || Ctx == null || Ctx.BattleEnded || InputLocked) return;

            // ★ 打不出的牌不给拖：让玩家拖到一半再被拒，比一开始就拖不动更让人困惑。
            //   NeedTarget 不算失败——那正是拖拽要解决的事。
            if (!_controller.CanPlayCard(view.Card, FirstAliveEnemy(), out var reason)
                && reason != PlayFailReason.NeedTarget)
            {
                ShowHint(ReasonText(reason));
                return;
            }

            _selected = null;
            _selectedPotion = null;

            _dragCard = view;
            _dragMode = _controller.NeedsTargetSelection(view.Card) ? DragMode.Aim : DragMode.Free;
            _dragPointer = e.position;
            _dragGrabOffset = ((RectTransform)view.transform).anchoredPosition
                              - ScreenToLocal(_handArea, e.position);
            _dragCardSlot = ((RectTransform)view.transform).anchoredPosition;
            _aimTarget = null;
            _lastAimTarget = null;
            _freeDropReady = false;

            view.SnapPosition = _dragMode == DragMode.Free;

            ShowHint(_dragMode == DragMode.Aim ? Loc.T("ui.battle.drag_aim", "把箭头拖到目标身上，松手出牌") : Loc.T("ui.battle.drag_free", "拖过白线松手出牌"));
        }

        public void OnCardDrag(CardView view, PointerEventData e)
        {
            if (_dragCard != view) return;
            _dragPointer = e.position;      // 其余全部交给 UpdateDragVisuals，只有一处在算
        }

        public void OnCardEndDrag(CardView view, PointerEventData e)
        {
            if (_dragCard != view) return;
            _dragPointer = e.position;

            var card = view.Card;
            var mode = _dragMode;
            var target = _aimTarget;
            bool dropReady = _freeDropReady;

            // ★ 先把拖拽状态收干净再出牌：TryPlayCard 可能同步挂起并弹出选牌面板，
            //   那一刻界面必须已经不在拖拽态，否则箭头会浮在面板上、拖拽状态也再没机会复位。
            EndDrag();

            if (Ctx == null || Ctx.BattleEnded || InputLocked) return;

            if (mode == DragMode.Aim)
            {
                if (target == null) { ShowHint(Loc.T("fail.need_target", "需要选择目标")); return; }
                PlaySelected(card, target);
            }
            else if (mode == DragMode.Free)
            {
                if (!dropReady) { ShowHint(""); return; }
                PlaySelected(card, null);
            }
        }

        /// <summary>
        /// 本界面消失时，把它开过的**全局开关**统统还回去。
        ///
        /// ★ 压制标记是全局静态的。战斗界面在拖拽途中被销毁（战斗结束切界面）时，
        ///   若不在这里放开，整个游戏的 tooltip 都会永久哑掉——而且完全没有报错（铁律 31）。
        ///
        /// ★ 时间缩放同理，而且后果更大（铁律 41）。最典型的一幕：
        ///   致命一击杀死最后一个敌人 → 进入慢放 → 战斗结束 → 玩家点「继续」→ 本界面被 Destroy。
        ///   <see cref="TimeFeedback"/> 自己有 unscaled 的倒计时兜底，所以少了这一句也不会
        ///   永久卡在慢放；但界面都没了还继续慢放毫无意义，地图界面会莫名其妙地黏半秒。
        ///   用 <see cref="TimeFeedback.RestoreIfActive"/> 而不是 <c>Instance.Restore()</c>：
        ///   后者会在退出时把单例**创建**出来，只为了复原一个根本没被改过的 timeScale。
        /// </summary>
        private void OnDisable()
        {
            TooltipView.Suppressed = false;
            TimeFeedback.RestoreIfActive();
            if (_shake != null) _shake.StopNow();
            if (_energyPanel != null) DOTween.Kill(_energyPanel);

            // 三颗牌堆按钮的弹跳 tween 同理：对象要被销毁了，tween 还活在 DOTween 的全局队列里
            KillPileTween(_drawPileButton);
            KillPileTween(_discardPileButton);
            KillPileTween(_exhaustPileButton);
        }

        private static void KillPileTween(Button btn)
        {
            if (btn != null) DOTween.Kill(btn.transform);
        }

        private void EndDrag()
        {
            if (_dragCard != null) _dragCard.SnapPosition = false;

            _dragCard = null;
            _dragMode = DragMode.None;
            _aimTarget = null;
            _lastAimTarget = null;
            _freeDropReady = false;

            if (_arrow != null && _arrow.gameObject.activeSelf) _arrow.gameObject.SetActive(false);
            if (_playLine != null && _playLine.gameObject.activeSelf) _playLine.gameObject.SetActive(false);
        }

        private void UpdateDragVisuals()
        {
            if (_dragMode == DragMode.None) return;

            // ★ 拖拽途中卡牌可能被销毁（战斗结束清手牌、某个效果把它移出手牌）。
            //   对象一死 EventSystem 就不会再发 OnEndDrag，状态只能自己收，
            //   否则会永久留在拖拽态：箭头挂在屏幕上、手牌再也排不回扇形。
            if (_dragCard == null || _dragCard.Card == null || Ctx == null || InputLocked)
            {
                EndDrag();
                return;
            }

            if (_dragMode == DragMode.Aim)
            {
                _aimTarget = EnemyUnderPointer(_dragPointer, _dragCard.Card);
                UpdateArrow();

                if (_aimTarget != _lastAimTarget)
                {
                    _lastAimTarget = _aimTarget;
                    ShowHint(_aimTarget != null
                        ? Loc.T("ui.battle.drag_locked", "松手，对「{0}」打出「{1}」", _aimTarget.DisplayName, _dragCard.Card.DisplayName)
                        : Loc.T("ui.battle.drag_aim", "把箭头拖到目标身上，松手出牌"));
                }
                return;
            }

            _dragCardSlot = ScreenToLocal(_handArea, _dragPointer) + _dragGrabOffset;
            _freeDropReady = _dragCardSlot.y + HandFanLayout.CardHeight * 0.5f >= PlayLineY;

            if (!_playLine.gameObject.activeSelf) _playLine.gameObject.SetActive(true);
            var col = _freeDropReady ? PlayLineReady : PlayLineIdle;
            _playLineBar.color = col;
            _playLineLabel.color = col;
        }

        private void UpdateArrow()
        {
            if (!_arrow.gameObject.activeSelf) _arrow.gameObject.SetActive(true);

            // 箭头从牌的顶边中点出发。用 TransformPoint 而不是直接拿 anchoredPosition，
            // 是因为牌正在被插值搬到举牌位、而且还带着缩放，只有实际的世界坐标是准的。
            var cardRt = (RectTransform)_dragCard.transform;
            Vector3 topWorld = cardRt.TransformPoint(new Vector3(cardRt.rect.center.x, cardRt.rect.yMax, 0f));
            Vector2 topScreen = RectTransformUtility.WorldToScreenPoint(null, topWorld);

            _arrow.From = ScreenToLocal(_arrow.rectTransform, topScreen);
            _arrow.To = ScreenToLocal(_arrow.rectTransform, _dragPointer);
            _arrow.color = _aimTarget != null ? ArrowLocked : ArrowFree;
            _arrow.Refresh();
        }

        /// <summary>
        /// 光标下的合法目标。
        /// ★ 用矩形包含判定而不是 EventSystem 射线：拖拽中射线的第一个命中物永远是被拖的那张牌，
        ///   要靠射线就得先把牌的 raycastTarget 关掉再开回来，多一个必须成对出现的状态。
        /// </summary>
        private BattleUnit EnemyUnderPointer(Vector2 screenPos, CardInstance card)
        {
            for (int i = 0; i < _unitViews.Count; i++)
            {
                var v = _unitViews[i];
                if (v == null || v.Unit == null || v.Unit.IsPlayer || !v.Unit.IsAlive) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)v.transform, screenPos, null))
                    continue;

                // 顺手复查一遍：目标不合法就不该给出「已锁定」的红箭头
                if (_controller.CanPlayCard(card, v.Unit, out _)) return v.Unit;
            }
            return null;
        }

        /// <summary>屏幕坐标 → 某个 RectTransform 的本地坐标。两个 Canvas 都是 Overlay，所以相机传 null。</summary>
        private static Vector2 ScreenToLocal(RectTransform rt, Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out var local);
            return local;
        }

        // ============================================================ 药水栏

        private RectTransform _potionBar;
        private readonly List<Button> _potionButtons = new List<Button>();
        private readonly List<int> _potionSignature = new List<int>();

        /// <summary>正在等玩家点目标的药水。与卡牌的 <c>_selected</c> 是同一套交互。</summary>
        private PotionInstance _selectedPotion;

        private List<PotionInstance> Potions => Ctx?.Run?.Potions;

        private void RefreshPotionBar()
        {
            var potions = Potions;
            if (potions == null) return;

            // 与手牌同样的「签名比对」：只有真的变了才重建，否则每帧销毁重建按钮
            bool changed = potions.Count != _potionSignature.Count;
            if (!changed)
                for (int i = 0; i < potions.Count; i++)
                    if (potions[i].Uid != _potionSignature[i]) { changed = true; break; }

            if (changed) RebuildPotionBar(potions);

            bool locked = InputLocked;
            for (int i = 0; i < _potionButtons.Count && i < potions.Count; i++)
            {
                bool usable = !locked
                              && (_controller.CanUsePotion(potions[i], FirstAliveEnemy(), out var reason)
                                  || reason == PotionFailReason.NeedTarget);

                // 选中的那瓶高亮，玩家才知道「我现在正拿着它在找目标」
                bool selected = _selectedPotion != null && _selectedPotion == potions[i];
                UIFactory.SetInteractable(_potionButtons[i], usable,
                    selected ? PotionSelectedColor : PotionColor);
            }
        }

        private static readonly Color PotionColor = new Color(0.20f, 0.40f, 0.34f);

        /// <summary>药水图标边长。槽位行高 46，留出上下各 6 的余量。</summary>
        private const float PotionIconSize = 32f;
        private static readonly Color PotionSelectedColor = new Color(0.38f, 0.68f, 0.55f);

        private void RebuildPotionBar(List<PotionInstance> potions)
        {
            for (int i = 0; i < _potionButtons.Count; i++)
                if (_potionButtons[i] != null) Destroy(_potionButtons[i].gameObject);
            _potionButtons.Clear();
            _potionSignature.Clear();
            _selectedPotion = null;

            int slots = Ctx.Run != null ? Ctx.Run.PotionSlots : 0;
            const float rowHeight = 46f;

            for (int i = 0; i < slots; i++)
            {
                float y = -i * rowHeight;

                if (i >= potions.Count)
                {
                    // 空槽也画出来，玩家才知道自己还能拿几瓶
                    var empty = UIFactory.CreatePanel(_potionBar, "PotionSlotEmpty" + i,
                        new Color(1f, 1f, 1f, 0.05f));
                    UIFactory.SetAnchored(empty, new Vector2(0, 1), new Vector2(1, 1),
                        new Vector2(0, y - 40), new Vector2(0, y - 6));

                    var emptyText = UIFactory.CreateText(empty, "EmptyText", Loc.T("ui.battle.empty_slot", "空 槽"), 15,
                        TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.28f));
                    UIFactory.Stretch(emptyText.rectTransform);
                    continue;
                }

                int index = i;
                var potion = potions[i];

                var btn = UIFactory.CreateTextButton(_potionBar, "Potion" + i,
                    potion.DisplayName, 17, PotionColor, () => OnPotionClicked(index));
                UIFactory.SetAnchored((RectTransform)btn.transform, new Vector2(0, 1), new Vector2(1, 1),
                    new Vector2(0, y - 40), new Vector2(-42, y - 6));

                // ★ 图标塞在按钮左端，名字往右让位。图标**不铺满按钮**：
                //   选中态是靠 tween 按钮底色表示的（OnPotionClicked），
                //   铺满就等于把「我正拿着这瓶药水在找目标」这个唯一的可见反馈盖掉。
                var potionIcon = UIFactory.CreateArtWindow(btn.transform, "Icon",
                    potion.Def != null ? potion.Def.Icon : null,
                    PotionIconSize, PotionIconSize, anchorY: 0.5f);

                if (potionIcon != null)
                {
                    potionIcon.anchorMin = potionIcon.anchorMax = new Vector2(0f, 0.5f);
                    potionIcon.pivot = new Vector2(0f, 0.5f);
                    potionIcon.anchoredPosition = new Vector2(6f, 0f);

                    var label = UIFactory.LabelOf(btn);
                    if (label != null)
                    {
                        UIFactory.SetAnchored(label.rectTransform, Vector2.zero, Vector2.one,
                            new Vector2(PotionIconSize + 10f, 0f), Vector2.zero);
                        UIFactory.SetAlignment(label, TextAnchor.MiddleLeft);
                    }
                }

                // 悬停就能读到说明，不必先点一下「选中」。点选那条路依然保留：
                // 药水是一次性资源，「先看清再确认」这一步不能因为有了 tooltip 就砍掉。
                TooltipTarget.Attach(btn.gameObject, new PotionTooltipSource(this, potion));

                // 倒掉按钮。★ 必须有：药水槽满了又买不到想要的东西时，
                //   没有倒掉手段的话玩家会被永久卡住。
                var drop = UIFactory.CreateTextButton(_potionBar, "PotionDrop" + i, "×", 18,
                    new Color(0.40f, 0.20f, 0.20f), () => OnPotionDiscarded(index));
                UIFactory.SetAnchored((RectTransform)drop.transform, new Vector2(1, 1), new Vector2(1, 1),
                    new Vector2(-38, y - 40), new Vector2(0, y - 6));

                _potionButtons.Add(btn);
                _potionSignature.Add(potion.Uid);
            }
        }

        /// <summary>
        /// 点药水。★ 一律「先选中、再确认」两步：
        /// 第一步把药水的说明打在提示栏上，第二步才真的喝掉。
        /// 一步就喝的话，玩家在读到「这瓶是干什么的」之前药水已经没了——
        /// 药水是一次性资源，误触的代价无法挽回。
        /// </summary>
        private void OnPotionClicked(int index)
        {
            var potions = Potions;
            if (potions == null || index >= potions.Count || InputLocked) return;

            var potion = potions[index];
            if (potion.Def == null) return;

            bool needsTarget = potion.Def.NeedsTarget;

            // 第二次点同一瓶：不需要目标的当场喝掉，需要目标的则取消选择
            if (_selectedPotion == potion)
            {
                if (needsTarget) { _selectedPotion = null; ShowHint(""); return; }
                UsePotion(potion, null);
                return;
            }

            _selected = null;              // 药水与卡牌互斥，不能同时处于选目标态
            _selectedPotion = potion;

            string desc = potion.Def.GetDescription(Ctx);
            ShowHint(needsTarget
                ? Loc.T("ui.battle.potion_need_target", "「{0}」{1}　—　点击一个敌人使用", potion.DisplayName, desc)
                : Loc.T("ui.battle.potion_confirm", "「{0}」{1}　—　再点一次使用", potion.DisplayName, desc));
        }

        private void OnPotionDiscarded(int index)
        {
            var potions = Potions;
            if (potions == null || index >= potions.Count || InputLocked) return;

            var potion = potions[index];
            if (_selectedPotion == potion) _selectedPotion = null;
            if (_controller.DiscardPotion(potion)) ShowHint(Loc.T("ui.battle.potion_discarded", "倒掉了「{0}」。", potion.DisplayName));
        }

        private void UsePotion(PotionInstance potion, BattleUnit target)
        {
            if (!_controller.TryUsePotion(potion, target, out var reason))
                ShowHint(PotionReasonText(reason));
            else
                ShowHint("");

            _selectedPotion = null;
        }

        /// <summary>药水的悬停说明。★ 存 <see cref="PotionInstance"/> 而不是存槽位下标——
        /// 喝掉前面一瓶之后下标会整体前移，抓着下标的提示会指到另一瓶药上。</summary>
        private sealed class PotionTooltipSource : ITooltipSource
        {
            private readonly BattleScreen _screen;
            private readonly PotionInstance _potion;

            public PotionTooltipSource(BattleScreen screen, PotionInstance potion)
            {
                _screen = screen;
                _potion = potion;
            }

            public bool BuildTooltip(List<TooltipEntry> buffer)
                => _potion != null && TooltipContent.BuildForPotion(_potion.Def, _screen.Ctx, buffer);
        }

        private static string PotionReasonText(PotionFailReason r) => r switch
        {
            PotionFailReason.NeedTarget => Loc.T("fail.need_target", "需要选择目标"),
            PotionFailReason.InvalidTarget => Loc.T("fail.invalid_target", "目标无效"),
            PotionFailReason.NotPlayerTurn => Loc.T("fail.not_your_turn", "现在不是你的回合"),
            PotionFailReason.BattleEnded => Loc.T("fail.battle_ended", "战斗已结束"),
            PotionFailReason.WaitingForSelection => Loc.T("fail.waiting_selection", "请先完成选牌"),
            _ => ""
        };

        // ============================================================ 选牌面板

        private RectTransform _modalLayer;
        private CardPickerScreen _picker;

        /// <summary>
        /// 让面板的存在与否始终跟随 <see cref="BattleContext.PendingSelection"/>。
        /// 写成「每帧对齐」而不是「请求时弹一次」，是因为请求可能在面板还开着的时候被作废
        /// （战斗结束），那时面板必须自己收掉，否则会浮在结算界面上。
        /// </summary>
        private void SyncSelectionPicker()
        {
            var pending = _controller.PendingSelection;

            if (pending == null)
            {
                if (_picker != null)
                {
                    Destroy(_picker.gameObject);
                    _picker = null;
                }
                return;
            }

            if (_picker != null) return;

            var panel = UIFactory.CreatePanel(_modalLayer, "SelectionPicker",
                new Color(0.05f, 0.05f, 0.07f, 0.93f));
            UIFactory.Stretch(panel);

            _picker = panel.gameObject.AddComponent<CardPickerScreen>();

            // ★ 拍一份候选快照：回调是下一帧之后才跑的，届时 PendingSelection 已经被清掉，
            //   靠它反查候选会拿到 null。
            var candidates = new List<CardInstance>(pending.Candidates);

            _picker.Open(null, pending.Title, candidates, null,
                pending.PickCount, pending.Request.Cancellable,
                indices =>
                {
                    _picker = null;

                    var chosen = new List<CardInstance>(indices.Count);
                    for (int i = 0; i < indices.Count; i++)
                    {
                        int idx = indices[i];
                        if (idx >= 0 && idx < candidates.Count) chosen.Add(candidates[idx]);
                    }

                    _controller.ResolveSelection(chosen);
                });
        }

        // ============================================================ 辅助

        private BattleUnit FirstAliveEnemy()
        {
            Ctx.GetAliveEnemies(_enemyBuffer);
            return _enemyBuffer.Count > 0 ? _enemyBuffer[0] : null;
        }

        /// <summary>震一下战场。由 <see cref="BattlePresenter"/> 按伤害大小调用。</summary>
        public void Shake(float amplitude, float duration)
        {
            if (_shake != null) _shake.Shake(amplitude, duration);
        }

        /// <summary>播一条回合过场横幅。</summary>
        public void ShowBanner(string text, Color color)
        {
            if (_overlayFx != null) _overlayFx.ShowBanner(text, color);
        }

        /// <summary>全屏闪一下（玩家挨了一记重的）。</summary>
        public void ScreenFlash(Color color, float alpha)
        {
            if (_overlayFx != null) _overlayFx.Flash(color, alpha);
        }

        public UnitView FindUnitView(int uid)
        {
            for (int i = 0; i < _unitViews.Count; i++)
                if (_unitViews[i] != null && _unitViews[i].Unit != null && _unitViews[i].Unit.Uid == uid)
                    return _unitViews[i];
            return null;
        }

        /// <summary>把单位面板的位置转换成 PopupLayer 的本地坐标，用于生成飘字。</summary>
        public Vector2 AnchoredPosOf(UnitView v)
        {
            var rt = (RectTransform)v.transform;
            Vector3 world = rt.TransformPoint(rt.rect.center);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(PopupLayer, screen, null, out var local);
            return local;
        }

        private float _hintTimer;

        private void ShowHint(string text)
        {
            _hintText.text = text;
            _hintTimer = string.IsNullOrEmpty(text) ? 0f : 2f;
        }

        private void FixedUpdate()
        {
            if (_hintTimer > 0f)
            {
                _hintTimer -= Time.fixedDeltaTime;
                // 正在选目标（牌 / 药水 / 拖拽中）时提示必须一直挂着，否则玩家会忘了自己在选什么
                if (_hintTimer <= 0f && _selected == null && _selectedPotion == null
                    && _dragMode == DragMode.None)
                    _hintText.text = "";
            }
        }

        private static string PhaseText(BattlePhase p) => p switch
        {
            BattlePhase.PlayerTurn => Loc.T("phase.player_turn", "你的回合"),
            BattlePhase.EnemyTurn => Loc.T("phase.enemy_turn", "敌人回合"),
            BattlePhase.TurnStart => Loc.T("phase.turn_start", "回合开始"),
            BattlePhase.TurnEnd => Loc.T("phase.turn_end", "回合结束"),
            BattlePhase.Victory => Loc.T("phase.victory", "胜利"),
            BattlePhase.Defeat => Loc.T("phase.defeat", "失败"),
            _ => p.ToString()
        };

        private static string ReasonText(PlayFailReason r) => r switch
        {
            PlayFailReason.NotEnoughEnergy => Loc.T("fail.not_enough_energy", "能量不足"),
            PlayFailReason.NeedTarget => Loc.T("fail.need_target", "需要选择目标"),
            PlayFailReason.InvalidTarget => Loc.T("fail.invalid_target", "目标无效"),
            PlayFailReason.Unplayable => Loc.T("fail.unplayable", "这张牌无法打出"),
            PlayFailReason.EffectCannotApply => Loc.T("fail.cannot_apply", "当前无法生效"),
            PlayFailReason.NotPlayerTurn => Loc.T("fail.not_your_turn", "现在不是你的回合"),
            PlayFailReason.BattleEnded => Loc.T("fail.battle_ended", "战斗已结束"),
            PlayFailReason.WaitingForSelection => Loc.T("fail.waiting_selection", "请先完成选牌"),
            _ => ""
        };
    }
}
