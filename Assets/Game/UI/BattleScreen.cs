using System.Collections.Generic;
using System.Text;
using Game.Battle;
using Game.Cards;
using Game.Units;
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

        private BattlePresenter _presenter;

        private RectTransform _enemyRow;
        private RectTransform _playerSlot;
        private RectTransform _handArea;
        private RectTransform _resultPanel;
        public RectTransform PopupLayer { get; private set; }

        private Text _turnText;
        private Text _energyText;
        private Text _pileText;
        private Text _logText;
        private Text _hintText;
        private Text _resultText;
        private Button _endTurnButton;

        private readonly List<UnitView> _unitViews = new List<UnitView>();
        private readonly List<CardView> _cardViews = new List<CardView>();
        private readonly List<int> _handSignature = new List<int>();
        private readonly List<BattleUnit> _enemyBuffer = new List<BattleUnit>();
        private readonly StringBuilder _sb = new StringBuilder(512);

        private CardView _selected;

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
            BuildUnitViews();

            _presenter = gameObject.GetComponent<BattlePresenter>();
            if (_presenter == null) _presenter = gameObject.AddComponent<BattlePresenter>();
            _presenter.Init(this, Ctx);

            _boundCtx = Ctx;
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

            // ---- 手牌区
            _handArea = UIFactory.CreateEmpty(root, "HandArea");
            UIFactory.SetAnchored(_handArea, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-800, 20), new Vector2(800, 280));

            // ---- 能量
            var energyBg = UIFactory.CreatePanel(root, "Energy", new Color(0.85f, 0.7f, 0.2f, 0.9f));
            UIFactory.SetAnchored(energyBg, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 120), new Vector2(150, 230));
            _energyText = UIFactory.CreateText(energyBg, "EnergyText", "3/3", 34, TextAnchor.MiddleCenter, Color.black);
            UIFactory.Stretch(_energyText.rectTransform);

            // ---- 牌堆信息
            _pileText = UIFactory.CreateText(root, "Piles", "", 20, TextAnchor.LowerLeft);
            UIFactory.SetAnchored(_pileText.rectTransform, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(40, 20), new Vector2(420, 110));

            // ---- 结束回合按钮
            _endTurnButton = UIFactory.CreateButton(root, "EndTurn", "结束回合", 26, new Color(0.55f, 0.25f, 0.25f));
            UIFactory.SetAnchored((RectTransform)_endTurnButton.transform, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-260, 130), new Vector2(-40, 210));
            _endTurnButton.onClick.AddListener(OnEndTurnClicked);

            // ---- 战斗日志
            var logBg = UIFactory.CreatePanel(root, "LogPanel", new Color(0f, 0f, 0f, 0.4f));
            UIFactory.SetAnchored(logBg, new Vector2(1, 0.5f), new Vector2(1, 1), new Vector2(-420, -280), new Vector2(-20, -70));
            _logText = UIFactory.CreateText(logBg, "LogText", "", 18, TextAnchor.LowerLeft);
            UIFactory.Stretch(_logText.rectTransform, 10);

            // ---- 提示
            _hintText = UIFactory.CreateText(root, "Hint", "", 24, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
            UIFactory.SetAnchored(_hintText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-500, 290), new Vector2(500, 340));

            // ---- 飘字层
            PopupLayer = UIFactory.CreateEmpty(root, "PopupLayer");
            UIFactory.Stretch(PopupLayer);

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
               || (Ctx.Events.Count > 0 && _presenter != null && _presenter.isActiveAndEnabled);

        public void OnCardClicked(CardView view)
        {
            if (Ctx == null || Ctx.BattleEnded || InputLocked) return;

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
                ShowHint("选择一个目标");
            }
            else
            {
                PlaySelected(view.Card, null);
            }
        }

        public void OnUnitClicked(UnitView view)
        {
            if (_selected == null || Ctx == null || Ctx.BattleEnded || InputLocked) return;
            if (view.Unit == null || view.Unit.IsPlayer || !view.Unit.IsAlive) return;

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

        private void OnEndTurnClicked()
        {
            if (Ctx == null || Ctx.BattleEnded || InputLocked) return;
            _selected = null;
            _controller.EndTurn();
        }

        private void Update()
        {
            // 取消选择永远允许——即使在播动画，玩家也该能反悔
            if (InputCompat.RightMouseDown || InputCompat.EscapeDown)
            {
                _selected = null;
                ShowHint("");
                return;
            }

            if (InputCompat.SpaceDown || InputCompat.KeyDown(KeyCode.E))
            {
                // ★ 正在选目标时，空格先取消选择而不是直接结束回合，
                //   否则玩家会莫名其妙地把选好的牌丢掉。
                if (_selected != null) { _selected = null; ShowHint(""); return; }
                OnEndTurnClicked();
            }
        }

        // ============================================================ 刷新

        private void LateUpdate()
        {
            if (Ctx == null) return;

            // Bind 时战斗还没开始的话，在这里补建单位面板并重新接上表现层
            if (!ReferenceEquals(_boundCtx, Ctx))
            {
                _boundCtx = Ctx;
                _intentVersion = -1;
                BuildUnitViews();
                if (_presenter != null) _presenter.Init(this, Ctx);
            }

            // 状态一变就重算敌人意图数值。★ 不这样做的话，玩家给敌人上「虚弱」之后
            // 意图上显示的还是回合开始时算的旧数字，玩家会照着错的数字做决策。
            if (_intentVersion != Ctx.StateVersion)
            {
                _intentVersion = Ctx.StateVersion;
                _controller.RefreshIntents();
            }

            RefreshHandViews();
            LayoutHand();
            RefreshCards();
            RefreshUnits();
            RefreshHud();
        }

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

            for (int i = 0; i < _cardViews.Count; i++)
                if (_cardViews[i] != null) Destroy(_cardViews[i].gameObject);
            _cardViews.Clear();
            _handSignature.Clear();
            _selected = null;

            for (int i = 0; i < hand.Count; i++)
            {
                var v = CardView.Create(_handArea, this, hand[i]);
                var rt = (RectTransform)v.transform;
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                _cardViews.Add(v);
                _handSignature.Add(hand[i].Uid);
            }
        }

        private void LayoutHand()
        {
            int n = _cardViews.Count;
            if (n == 0) return;

            float spacing = Mathf.Min(190f, 1500f / n);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * spacing;
                _cardViews[i].BasePosition = new Vector2(x, 20f);
                _cardViews[i].ApplyLayout(_cardViews[i] == _selected);
            }
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
                v.Refresh(Ctx, playable, v == _selected);
            }
        }

        private void RefreshUnits()
        {
            bool targeting = _selected != null;
            for (int i = 0; i < _unitViews.Count; i++)
            {
                var v = _unitViews[i];
                bool targetable = targeting && v.Unit != null && !v.Unit.IsPlayer && v.Unit.IsAlive;
                v.Refresh(Ctx, targetable, false);
            }
        }

        private void RefreshHud()
        {
            _turnText.text = $"第 {Ctx.TurnNumber} 回合    —    {PhaseText(Ctx.Phase)}";
            _energyText.text = $"{Ctx.Energy}/{Ctx.EnergyPerTurn}";
            _pileText.text = $"抽牌堆 {Ctx.Deck.DrawPile.Count}    弃牌堆 {Ctx.Deck.DiscardPile.Count}    消耗堆 {Ctx.Deck.ExhaustPile.Count}";

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
                _resultText.text = Ctx.Victory ? "战 斗 胜 利" : "战 斗 失 败";
                _resultText.color = Ctx.Victory ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.4f, 0.4f);
            }
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
                if (_hintTimer <= 0f && _selected == null) _hintText.text = "";
            }
        }

        private static string PhaseText(BattlePhase p) => p switch
        {
            BattlePhase.PlayerTurn => "你的回合",
            BattlePhase.EnemyTurn => "敌人回合",
            BattlePhase.TurnStart => "回合开始",
            BattlePhase.TurnEnd => "回合结束",
            BattlePhase.Victory => "胜利",
            BattlePhase.Defeat => "失败",
            _ => p.ToString()
        };

        private static string ReasonText(PlayFailReason r) => r switch
        {
            PlayFailReason.NotEnoughEnergy => "能量不足",
            PlayFailReason.NeedTarget => "需要选择目标",
            PlayFailReason.InvalidTarget => "目标无效",
            PlayFailReason.Unplayable => "这张牌无法打出",
            PlayFailReason.EffectCannotApply => "当前无法生效",
            PlayFailReason.NotPlayerTurn => "现在不是你的回合",
            PlayFailReason.BattleEnded => "战斗已结束",
            _ => ""
        };
    }
}
