using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 单场景常驻的应用入口。持有 <see cref="GameDatabase"/> 与 <see cref="RunManager"/>，
    /// 订阅 <c>PhaseChanged</c> 切换界面。
    ///
    /// ★ 为什么是单场景：整个 UI 都是运行时程序化搭建的，场景里除了这个组件什么都没有。
    ///   拆成多场景只会换来「加载时序 + 跨场景传数据」两个新问题，换不来任何东西。
    /// </summary>
    public class GameApp : MonoBehaviour
    {
        public static GameApp Instance { get; private set; }

        [Header("数据")]
        public GameDatabase Database;

        [Header("起始配置")]
        public int StartingMaxHp = 80;
        public string StarterRelicId = "burning_blood";

        public List<StarterDeckEntryConfig> StarterDeck = new List<StarterDeckEntryConfig>
        {
            new StarterDeckEntryConfig { CardId = "strike", Count = 5 },
            new StarterDeckEntryConfig { CardId = "defend", Count = 4 },
            new StarterDeckEntryConfig { CardId = "bash", Count = 1 },
        };

        [Header("随机")]
        [Tooltip("0 表示每局按时间随机；非 0 则固定种子，便于复现问题")]
        public int FixedSeed = 0;

        public RunManager Manager { get; private set; }

        private RectTransform _uiRoot;
        private TopBarView _topBar;
        private ScreenBase _current;

        /// <summary>叠在所有界面之上的弹窗层（选牌面板等）。</summary>
        public RectTransform OverlayLayer { get; private set; }

        [Serializable]
        public struct StarterDeckEntryConfig
        {
            public string CardId;
            public int Count;
        }

        // ================================================================= 生命周期

        private void Awake()
        {
            Instance = this;

            if (Database == null)
            {
                Debug.LogError("[GameApp] 没有指定 GameDatabase。请在 Inspector 里拖入，" +
                               "或先执行菜单 Tools/卡牌游戏/1. 生成示例内容。");
                enabled = false;
                return;
            }
            Database.Invalidate();

            var canvas = UIFactory.CreateCanvas("GameCanvas");
            canvas.transform.SetParent(transform, false);
            canvas.sortingOrder = 0;
            InputCompat.EnsureEventSystem();

            _uiRoot = (RectTransform)canvas.transform;

            var bg = UIFactory.CreatePanel(_uiRoot, "AppBackground", new Color(0.06f, 0.07f, 0.09f));
            UIFactory.Stretch(bg);

            var screenLayer = UIFactory.CreateEmpty(_uiRoot, "ScreenLayer");
            UIFactory.Stretch(screenLayer);
            _screenLayer = screenLayer;

            _topBar = TopBarView.Create(_uiRoot, this);

            OverlayLayer = UIFactory.CreateEmpty(_uiRoot, "OverlayLayer");
            UIFactory.Stretch(OverlayLayer);

            Manager = new RunManager();
            Manager.PhaseChanged += OnPhaseChanged;
            Manager.GoToMainMenu();
        }

        private RectTransform _screenLayer;

        private void OnDestroy()
        {
            if (Manager != null) Manager.PhaseChanged -= OnPhaseChanged;
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (_topBar != null) _topBar.Refresh(Manager.Run, ManagerPhaseShowsTopBar());
        }

        private bool ManagerPhaseShowsTopBar()
            => Manager.Run != null && _current != null && _current.ShowTopBar;

        // ================================================================= 开新局

        public void StartNewRun()
        {
            int seed = FixedSeed != 0 ? FixedSeed : Environment.TickCount;

            var deck = new List<StarterDeckEntry>(StarterDeck.Count);
            for (int i = 0; i < StarterDeck.Count; i++)
                deck.Add(new StarterDeckEntry(StarterDeck[i].CardId, StarterDeck[i].Count));

            var run = Manager.StartNewRun(Database, seed, deck, StarterRelicId, StartingMaxHp);

            // ★ 真人在玩：战斗内的选牌要挂起等他点，而不是系统替他随机选。
            //   放在这里而不是界面里，是因为界面的绑定时机会变，而「谁开的这一局」不会变。
            run.InteractivePlayer = true;

            Debug.Log($"[GameApp] 新游戏开始，种子 = {seed}。");
        }

        // ================================================================= 界面切换

        private void OnPhaseChanged(RunPhase phase)
        {
            // 切界面时把还开着的弹窗一并收掉，否则选牌面板会浮在新界面上
            if (_activePicker != null)
            {
                Destroy(_activePicker.gameObject);
                _activePicker = null;
            }

            if (_current != null)
            {
                Destroy(_current.gameObject);
                _current = null;
            }

            _current = CreateScreen(phase);
            if (_current != null) _current.Initialize(this);
        }

        private ScreenBase CreateScreen(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.MainMenu: return NewScreen<MainMenuScreen>();
                case RunPhase.Map: return NewScreen<MapScreen>();
                case RunPhase.Battle: return NewScreen<BattleHostScreen>();
                case RunPhase.Reward:
                case RunPhase.Treasure: return NewScreen<RewardScreen>();
                case RunPhase.Shop: return NewScreen<ShopScreen>();
                case RunPhase.Event: return NewScreen<EventScreen>();
                case RunPhase.Rest: return NewScreen<RestScreen>();
                case RunPhase.GameOver:
                case RunPhase.Victory: return NewScreen<GameOverScreen>();
                default:
                    Debug.LogError($"[GameApp] 没有对应 {phase} 的界面。");
                    return null;
            }
        }

        /// <summary>
        /// 建一个铺满屏幕的根节点，并把界面组件挂在它自己身上。
        /// ★ 组件与 UI 根必须是同一个 GameObject——否则销毁界面时 UI 会留在场景里。
        /// </summary>
        private T NewScreen<T>() where T : ScreenBase
        {
            var root = UIFactory.CreateEmpty(_screenLayer, typeof(T).Name);
            UIFactory.Stretch(root);
            return root.gameObject.AddComponent<T>();
        }

        // ================================================================= 弹窗

        /// <summary>
        /// 弹一个选牌面板。选完（或取消）后回调。
        /// 奖励三选一、删卡、升级卡、事件里的选牌全部共用这一个。
        /// </summary>
        public CardPickerScreen ShowCardPicker(string title, IReadOnlyList<Cards.CardInstance> deckCards,
                                               IReadOnlyList<Cards.CardDefinition> defCards,
                                               int pickCount, bool cancellable,
                                               Action<List<int>> onConfirm)
        {
            if (_activePicker != null) Destroy(_activePicker.gameObject);

            // 同上：组件挂在遮罩根自己身上，关闭时一个 Destroy 就干净了
            var root = UIFactory.CreateOverlay(OverlayLayer, "CardPicker");
            var picker = root.gameObject.AddComponent<CardPickerScreen>();
            _activePicker = picker;

            picker.Open(this, title, deckCards, defCards, pickCount, cancellable, result =>
            {
                if (_activePicker == picker) _activePicker = null;
                onConfirm?.Invoke(result);
            });
            return picker;
        }

        private CardPickerScreen _activePicker;
    }
}
