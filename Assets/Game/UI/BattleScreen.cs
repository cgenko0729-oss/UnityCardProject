using System.Collections.Generic;
using System.Text;
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

        private RectTransform _enemyRow;
        private RectTransform _playerSlot;
        private RectTransform _handArea;
        private RectTransform _resultPanel;
        public RectTransform PopupLayer { get; private set; }

        private TMP_Text _turnText;
        private TMP_Text _energyText;
        private TMP_Text _pileText;
        private TMP_Text _logText;
        private TMP_Text _hintText;
        private TMP_Text _resultText;
        private Button _endTurnButton;

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

            // ---- 敌人区
            _enemyRow = UIFactory.CreateEmpty(root, "EnemyRow");
            UIFactory.SetAnchored(_enemyRow, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-700, top - 330), new Vector2(700, top - 80));

            // ---- 玩家区
            _playerSlot = UIFactory.CreateEmpty(root, "PlayerSlot");
            UIFactory.SetAnchored(_playerSlot, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(60, -100), new Vector2(320, 100));

            // ---- 能量
            var energyBg = UIFactory.CreatePanel(root, "Energy", new Color(0.85f, 0.7f, 0.2f, 0.9f));
            UIFactory.SetAnchored(energyBg, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 120), new Vector2(150, 230));
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

            // ---- 牌堆信息
            _pileText = UIFactory.CreateText(root, "Piles", "", 20, TextAnchor.LowerLeft);
            UIFactory.SetAnchored(_pileText.rectTransform, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(40, 20), new Vector2(420, 110));

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
            if (!_controller.TryPlayCard(card, target, out var reason))
                ShowHint(ReasonText(reason));
            else
                ShowHint("");

            _selected = null;
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
            // 取消选择永远允许——即使在播动画，玩家也该能反悔
            if (InputCompat.RightMouseDown || InputCompat.EscapeDown)
            {
                CancelTargeting();
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

            RefreshHandViews();

            // ★ 顺序有讲究：先算拖拽状态（它会写 _dragCardSlot / _aimTarget），
            //   LayoutHand 才排得出被拖那张牌的位置。
            UpdateDragVisuals();

            // 正举着一张牌找目标时不该有提示框跳出来碍事
            TooltipView.Suppressed = _dragMode != DragMode.None;

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

        /// <summary>新建手牌视图的起点：抽牌堆信息所在的左下角，牌从那里飞进扇形。</summary>
        private static readonly Vector2 SpawnSlot = new Vector2(-720f, -20f);
        private const float SpawnRotation = -28f;
        private const float SpawnScale = 0.55f;

        /// <summary>
        /// 手牌视图与 <c>Deck.Hand</c> 对齐。
        ///
        /// ★ 增量复用而不是全量重建：原来手牌一变就 Destroy 全部 CardView 再重建，
        ///   于是打出一张牌之后其余牌是「瞬移」到新的扇形位置的——
        ///   扇形排列的手感几乎全部来自这段滑动，全量重建等于把它抹掉。
        ///   顺带这也是抽牌/出牌动画的硬前置（原「已知遗留 #1」）。
        /// </summary>
        private void RefreshHandViews()
        {
            var hand = Ctx.Deck.Hand;

            bool changed = hand.Count != _handSignature.Count;
            if (!changed)
            {
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i].Uid != _handSignature[i]) { changed = true; break; }
            }
            if (!changed) return;

            _liveUids.Clear();
            for (int i = 0; i < hand.Count; i++) _liveUids.Add(hand[i].Uid);

            // ---- 回收已经离手的牌
            for (int i = 0; i < _cardViews.Count; i++)
            {
                var v = _cardViews[i];
                if (v == null) continue;
                if (v.Card != null && _liveUids.Contains(v.Card.Uid)) continue;

                if (v.Card != null) _viewByUid.Remove(v.Card.Uid);
                ForgetCardView(v);
                Destroy(v.gameObject);
            }

            // ---- 按手牌顺序重排，缺的现建
            _viewBuffer.Clear();
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                if (!_viewByUid.TryGetValue(card.Uid, out var v) || v == null)
                {
                    v = CardView.Create(_handArea, this, card);
                    v.SnapTo(SpawnSlot, SpawnRotation, SpawnScale);
                    _viewByUid[card.Uid] = v;
                }
                _viewBuffer.Add(v);
            }

            _cardViews.Clear();
            _cardViews.AddRange(_viewBuffer);

            _handSignature.Clear();
            for (int i = 0; i < hand.Count; i++) _handSignature.Add(hand[i].Uid);

            _orderDirty = true;
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

        private void RefreshHud()
        {
            _turnText.text = Loc.T("ui.battle.turn_header", "第 {0} 回合    —    {1}", Ctx.TurnNumber, PhaseText(Ctx.Phase));
            _energyText.text = $"{Ctx.Energy}/{Ctx.EnergyPerTurn}";
            _pileText.text = Loc.T("ui.battle.piles", "抽牌堆 {0}    弃牌堆 {1}    消耗堆 {2}", Ctx.Deck.DrawPile.Count, Ctx.Deck.DiscardPile.Count, Ctx.Deck.ExhaustPile.Count);

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
        /// ★ 压制标记是**全局静态**的。战斗界面在拖拽途中被销毁（战斗结束切界面）时，
        ///   若不在这里放开，整个游戏的 tooltip 都会永久哑掉——而且完全没有报错。
        /// </summary>
        private void OnDisable() => TooltipView.Suppressed = false;

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
