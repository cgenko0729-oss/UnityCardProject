using System;
using System.Collections.Generic;
using Game.Core;
using Game.Localization;
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

        /// <summary>存档。主菜单靠它决定「继续游戏」能不能按。</summary>
        public SaveService Save { get; private set; }

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

            // ★ 顺序有讲究：Manager 和 SaveService 必须在建任何 UI 之前就位。
            //   ① SaveService 要订阅 Manager 的事件，所以 Manager 先建；
            //   ② 语言现在从 MetaSave 读，而字体资产是按语言建的——
            //      先建界面再切语言等于把所有文字节点重做一遍。
            Manager = new RunManager();
            Save = new SaveService(Database, Manager);

            ApplySavedLanguage();
            Loc.LanguageChanged += OnLanguageChanged;

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

            Manager.PhaseChanged += OnPhaseChanged;
            Manager.GoToMainMenu();
        }

        private RectTransform _screenLayer;

        private void OnDestroy()
        {
            if (Manager != null) Manager.PhaseChanged -= OnPhaseChanged;
            if (Save != null) Save.Dispose();
            Loc.LanguageChanged -= OnLanguageChanged;
            if (Instance == this) Instance = null;
        }

        // ================================================================= 语言

        private void ApplySavedLanguage()
        {
            string code = SaveService.LoadLanguage();
            Loc.Use(Database.GetLocale(code));
            UIFactory.CurrentFontLanguage = Loc.Current;
        }

        /// <summary>切语言。会重建当前界面——程序化 UI 没有别的办法刷新已经写进去的文字。</summary>
        public void SetLanguage(string code)
        {
            SaveService.SaveLanguage(code);
            Loc.Use(Database.GetLocale(code));
        }

        /// <summary>
        /// 语言变了：换字体、把整棵界面重建一遍。
        ///
        /// ★ 为什么是「重建」而不是「让每个 Text 自己订阅 LanguageChanged」：
        ///   本工程的 UI 全是运行时代码搭的，一个界面里有上百个文字节点，
        ///   逐个订阅意味着每个都要记住自己的 key 和参数，还要在销毁时退订
        ///   （忘一个就是一条指向已销毁对象的悬空委托）。
        ///   直接重建只有一处代码，且天然正确。
        /// </summary>
        private void OnLanguageChanged()
        {
            UIFactory.CurrentFontLanguage = Loc.Current;
            UIFactory.InvalidateFont();

            if (_topBar != null)
            {
                Destroy(_topBar.gameObject);
                _topBar = TopBarView.Create(_uiRoot, this);
                // ★ 顶栏是在 ScreenLayer 之后建的（遮挡顺序 = 兄弟顺序），
                //   重建会把它排到最后，正好保持原来的层级关系。
                //   OverlayLayer 必须再排到它之后，否则弹窗会被顶栏盖住。
                if (OverlayLayer != null) OverlayLayer.SetAsLastSibling();
            }

            OnPhaseChanged(Manager != null ? Manager.Phase : RunPhase.MainMenu);
        }

        private void Update()
        {
            // ★ 卡组面板优先吃掉 Esc / 右键。局外界面目前没有任何一个自己处理 Esc，
            //   所以这里没有优先级冲突；写成拉取式是为了与 BattleScreen 保持同一形状
            //   （理由见 CardListView.ConsumeCancelInput）。
            if (_cardList != null) _cardList.ConsumeCancelInput();
        }

        private void LateUpdate()
        {
            if (_topBar != null)
                _topBar.Refresh(Manager.Run, ManagerPhaseShowsTopBar(), ManagerPhaseShowsDeckButton());
        }

        private bool ManagerPhaseShowsTopBar()
            => Manager.Run != null && _current != null && _current.ShowTopBar;

        private bool ManagerPhaseShowsDeckButton()
            => Manager.Run != null && _current != null && _current.ShowTopBar && _current.ShowDeckButton;

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

            // ★ 卡组面板同理。少了这一段，「在商店里点开卡组 → 买一张牌弹出选牌面板」
            //   两个面板会叠在一起，正是第三次会话那个界面泄漏 bug 的同一形状。
            if (_cardList != null)
            {
                Destroy(_cardList.gameObject);
                _cardList = null;
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

        // ================================================================= 查看卡组

        /// <summary>当前开着的卡组浏览面板。null = 没开。</summary>
        private CardListView _cardList;

        /// <summary>
        /// 弹出「当前卡组」浏览面板。由顶栏那颗按钮调用。
        ///
        /// ★ 排序显示而不是按 <c>Run.Deck</c> 的真实顺序：母牌组的顺序对玩家没有任何意义
        ///   （每场战斗开头都会重洗），按费用/类型排开才看得出这副牌的形状。
        /// </summary>
        public void ShowDeckView()
        {
            if (Manager?.Run == null) return;

            // ★ 这不是「再点一次关掉」——玩家点不到第二次：OverlayLayer 建在 _topBar 之后
            //   （兄弟顺序 = 遮挡顺序），面板一开就把顶栏连按钮一起盖住了。
            //   这一段纯粹是防「同一时刻冒出两个面板」：真发生了的话旧的那个会永远留在
            //   OverlayLayer 里没人收（_cardList 只存得下一个），就是第三次会话那种界面泄漏。
            if (_cardList != null)
            {
                _cardList.Close();
                _cardList = null;
            }

            var run = Manager.Run;
            _cardList = CardListView.Open(OverlayLayer,
                Loc.T("ui.cardlist.title.deck", "卡组（{0}）", run.Deck.Count),
                run.Deck, Database, CardListOrder.Sorted,
                onClosed: () => _cardList = null);
        }
    }
}
