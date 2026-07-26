using System;
using System.Collections.Generic;
using Game.Battle;
using Game.Map;
using Game.RunEffects;

namespace Game.Core
{
    /// <summary>
    /// 局外流程状态机。
    ///
    /// ★ 职责边界（路线图点名的头号风险是「RunManager 变成第二个上帝类」）：
    ///   本类**只做两件事**——① 决定当前该处于哪个 RunPhase；② 把数据准备好交给对应的界面。
    ///   具体逻辑（奖励怎么领、商店怎么买、事件选项怎么算）一律在各自的 Screen 和 RunEffect 里。
    ///   本类不引用任何 UI 类型，可以在 EditMode 测试里完整跑完一局。
    /// </summary>
    public class RunManager
    {
        public RunContext Run { get; private set; }
        public GameDatabase Db { get; private set; }

        /// <summary>当前正在进行的战斗。非战斗阶段为 null。</summary>
        public BattleController Battle { get; private set; }

        /// <summary>阶段变化通知。GameApp 订阅它来切换界面。</summary>
        public event Action<RunPhase> PhaseChanged;

        /// <summary>
        /// 「现在是一个存档安全点」的通知。<c>Game.UI.SaveService</c> 订阅它去写盘。
        ///
        /// <para>★ 为什么是事件而不是直接调 <c>SaveSystem.SaveRun</c>：
        ///   本类在 <c>Game.Runtime</c> 里，被 166 个 EditMode 用例和将来的自动对战模拟器
        ///   反复实例化。直接写盘意味着跑一次测试就往 <c>persistentDataPath</c> 砸几百个文件。
        ///   没有订阅者时这个事件什么也不做，这正是测试想要的默认行为。</para>
        /// </summary>
        public event Action AutosaveRequested;

        private void RequestAutosave() => AutosaveRequested?.Invoke();

        public RunPhase Phase => Run != null ? Run.Phase : RunPhase.MainMenu;

        private readonly List<string> _idBuffer = new List<string>(16);

        // ================================================================= 开新局

        public RunContext StartNewRun(GameDatabase db, int seed, IReadOnlyList<StarterDeckEntry> starterDeck,
                                      string starterRelicId = null, int maxHp = 80)
        {
            Db = db;
            Run = new RunContext(seed, db)
            {
                MaxHp = maxHp,
                Hp = maxHp,
                Gold = 99,
                EnergyPerTurn = 3,
                CardsPerTurn = 5,
            };

            if (starterDeck != null)
            {
                for (int i = 0; i < starterDeck.Count; i++)
                {
                    var def = db.GetCard(starterDeck[i].CardId);
                    if (def == null)
                    {
                        UnityEngine.Debug.LogWarning($"[RunManager] 找不到起始卡「{starterDeck[i].CardId}」，已跳过。");
                        continue;
                    }
                    Run.AddCards(def, starterDeck[i].Count);
                }
            }

            if (!string.IsNullOrEmpty(starterRelicId))
                Run.AddRelic(db.GetRelic(starterRelicId));

            Run.Map = GenerateMap(db, Run.Rng);
            Run.CurrentNodeId = -1;

            SetPhase(RunPhase.Map);
            RequestAutosave();
            return Run;
        }

        private GameMap GenerateMap(GameDatabase db, Rng rng)
        {
            var cfg = MapGenerationConfig.Default;

            var normal = new List<string>();
            db.GetEncounterIds(normal, elite: false, boss: false);
            var elite = new List<string>();
            db.GetEncounterIds(elite, elite: true, boss: false);
            var boss = new List<string>();
            db.GetEncounterIds(boss, elite: false, boss: true);
            var events = new List<string>();
            db.GetEventIds(events);

            cfg.NormalEncounterIds = normal;
            cfg.EliteEncounterIds = elite;
            cfg.BossEncounterIds = boss;
            cfg.EventIds = events;

            return MapGenerator.Generate(rng, cfg);
        }

        /// <summary>供测试 / 存档恢复用：直接接管一个已有的 RunContext。</summary>
        public void Adopt(GameDatabase db, RunContext run)
        {
            Db = db;
            Run = run;
            SetPhase(run.Phase);
        }

        /// <summary>
        /// 从存档恢复一局。★ 与 <see cref="Adopt"/> 的区别就是这个方法存在的全部理由：
        /// 存档快照是在**节点行为执行之前**打的，所以商店库存、宝箱奖励、战斗本身
        /// 都还没生成，直接 <c>SetPhase</c> 会把玩家丢进一个空界面。
        ///
        /// <para>这里对这些阶段**重放一次节点行为**。因为 Rng 也是同一份快照，
        /// 重放出来的库存、奖励、起手牌与存档那一刻若不中断会发生的完全相同——
        /// 玩家没法靠存读刷商品，也没法刷起手牌。</para>
        /// </summary>
        public void Resume(GameDatabase db, RunContext run)
        {
            if (db == null || run == null) return;

            Db = db;
            Run = run;
            Battle = null;

            switch (run.Phase)
            {
                // 战斗要按「正在打的是哪一场」重开，而不是按当前节点——
                // 事件里开的战斗，当前节点是那个事件（见 RunContext.ActiveBattleEncounterId）
                case RunPhase.Battle:
                    if (!string.IsNullOrEmpty(run.ActiveBattleEncounterId))
                    {
                        StartBattle(run.ActiveBattleEncounterId, run.PendingBattleGivesReward);
                        return;
                    }
                    UnityEngine.Debug.LogWarning("[RunManager] 存档停在战斗阶段却没有记录是哪一场，已退回地图。");
                    ReturnToMap();
                    return;

                case RunPhase.Shop:
                case RunPhase.Event:
                case RunPhase.Rest:
                case RunPhase.Treasure:
                {
                    var node = Run.Map != null ? Run.Map.GetNode(run.CurrentNodeId) : null;
                    if (node != null) { ExecuteNode(node); return; }

                    UnityEngine.Debug.LogWarning($"[RunManager] 存档里的节点 #{run.CurrentNodeId} 已不存在，已退回地图。");
                    SetPhase(RunPhase.Map);
                    return;
                }

                // 奖励阶段的快照打在奖励生成**之后**（AcknowledgeBattleEnd），数据就在存档里
                case RunPhase.Reward:
                    if (run.PendingReward == null)
                    {
                        UnityEngine.Debug.LogWarning("[RunManager] 存档停在奖励阶段却没有奖励数据，已退回地图。");
                        SetPhase(RunPhase.Map);
                        return;
                    }
                    SetPhase(RunPhase.Reward);
                    return;

                default:
                    SetPhase(run.Phase);
                    return;
            }
        }

        // ================================================================= 地图

        /// <summary>玩家在地图上点了一个节点。</summary>
        public bool EnterNode(int nodeId)
        {
            if (Run?.Map == null) return false;
            if (!Run.Map.IsAvailable(Run.CurrentNodeId, nodeId)) return false;

            var node = Run.Map.GetNode(nodeId);
            if (node == null) return false;

            Run.CurrentNodeId = nodeId;
            Run.VisitedNodeIds.Add(nodeId);

            // ★★ 存档快照就打在这一刀上：节点已经锁定，节点的内容还一点没生成。
            //
            //   切早了（在上面两行之前）→ 读档回到地图，玩家可以改选另一个节点，
            //     等于每个节点都能重掷一次。
            //   切晚了（在 ExecuteNode 之后）→ 商店库存已经掷完骰子、宝箱奖励已经生成，
            //     RngStream.Shop / Reward 都推进了。读档重来时结果不同 = 刷商品。
            //
            //   切在中间，读档时由 Resume 拿同一份 Rng 快照把内容重放出来，逐字节相同。
            //
            //   ★ Phase 必须在存盘前就写成目标阶段，而且**只赋值不广播**：
            //     若还留着 Map，读档时 CurrentNodeId 已经指向新节点却回到地图，
            //     玩家就白嫖跳过了一个节点。广播仍然由下面的 ExecuteNode 负责，
            //     时机一帧不差——RunFlowTests.PhaseChanged_FiresOnlyAfterItsDataIsReady 盯着这件事。
            Run.Phase = PhaseOf(node.Type);

            // 战斗的快照由 StartBattle 自己打（它要赶在抽起手牌之前），这里不重复写盘
            if (Run.Phase != RunPhase.Battle) RequestAutosave();

            ExecuteNode(node);
            return true;
        }

        /// <summary>某类节点进去之后应该处于哪个阶段。只做映射，不做任何副作用。</summary>
        private static RunPhase PhaseOf(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Battle:
                case MapNodeType.Elite:
                case MapNodeType.Boss: return RunPhase.Battle;
                case MapNodeType.Rest: return RunPhase.Rest;
                case MapNodeType.Shop: return RunPhase.Shop;
                case MapNodeType.Event: return RunPhase.Event;
                case MapNodeType.Treasure: return RunPhase.Treasure;
                default: return RunPhase.Map;
            }
        }

        /// <summary>
        /// 真正执行一个节点：把它需要的数据准备好，然后广播阶段变化。
        /// ★ 从 <see cref="EnterNode"/> 里拆出来，是为了让 <see cref="Resume"/> 能重放同一段逻辑。
        ///   两条路径共用一份代码，将来加节点类型不会只改到其中一边。
        /// </summary>
        private void ExecuteNode(MapNode node)
        {
            switch (node.Type)
            {
                case MapNodeType.Battle:
                case MapNodeType.Elite:
                case MapNodeType.Boss:
                    StartBattle(node.ContentId, givesReward: true);
                    break;

                case MapNodeType.Rest:
                    SetPhase(RunPhase.Rest);
                    break;

                case MapNodeType.Shop:
                    EnsureShopStock(node.Id);
                    SetPhase(RunPhase.Shop);
                    break;

                case MapNodeType.Event:
                    SetPhase(RunPhase.Event);
                    break;

                case MapNodeType.Treasure:
                    Run.PendingReward = RewardGenerator.GenerateTreasure(Run);
                    SetPhase(RunPhase.Treasure);
                    break;
            }
        }

        public MapNode CurrentNode => Run?.Map?.GetNode(Run.CurrentNodeId);

        public void GetAvailableNodes(List<int> buffer)
        {
            buffer.Clear();
            Run?.Map?.GetAvailableNodes(Run.CurrentNodeId, buffer);
        }

        private void EnsureShopStock(int nodeId)
        {
            if (Run.ShopStocks.ContainsKey(nodeId)) return;
            Run.ShopStocks[nodeId] = ShopStock.Generate(Run);
        }

        public ShopStock CurrentShop
            => Run != null && Run.ShopStocks.TryGetValue(Run.CurrentNodeId, out var s) ? s : null;

        public Events.EventDefinition CurrentEvent
        {
            get
            {
                var node = CurrentNode;
                return node != null && Db != null ? Db.GetEvent(node.ContentId) : null;
            }
        }

        // ================================================================= 战斗

        public void StartBattle(string encounterId, bool givesReward)
        {
            var encounter = Db != null ? Db.GetEncounter(encounterId) : null;
            if (encounter == null)
            {
                UnityEngine.Debug.LogError($"[RunManager] 找不到战斗配置「{encounterId}」，跳过该节点。");
                ReturnToMap();
                return;
            }

            Run.LastEncounter = encounter;
            Run.PendingBattleGivesReward = givesReward;
            Run.ActiveBattleEncounterId = encounterId;
            Run.Phase = RunPhase.Battle;   // 只赋值，广播仍在最后（理由同 EnterNode）

            // ★★ 战斗的存档快照必须打在 Battle.StartBattle 之前。
            //   它一跑就会抽起手牌，推进 RngStream.CardDraw 与 Battle。
            //   在它之后存盘的话，玩家每次读档重打都会拿到一副不同的起手牌——
            //   等于白送一个「刷起手牌」的按钮。
            //   打在这里，重打时抽到的牌与第一次逐张相同，重打就只是重打。
            RequestAutosave();

            Battle = new BattleController();
            Battle.BattleFinished += OnBattleFinished;

            // ★ 必须先真正开战，再广播阶段变化。
            //   SetPhase 是同步的：它会立刻让 GameApp 建出战斗界面并 Bind。
            //   如果这时候 StartBattle 还没跑，Battle.Ctx 还是 null，
            //   界面就会绑到一个空战斗上——敌人和玩家面板一个都建不出来。
            Battle.StartBattle(Run, encounter);
            SetPhase(RunPhase.Battle);
        }

        private void OnBattleFinished(bool victory)
        {
            // ★ 只记录结果，不在这里切界面：此刻表现层还在播放最后几个事件，
            //   立刻切走会让玩家看不到致命一击。由界面层调用 AcknowledgeBattleEnd 推进。
            Run.LastBattleVictory = victory;
        }

        /// <summary>
        /// 战斗表现播放完毕、玩家点了「继续」之后调用，才真正推进流程。
        /// </summary>
        public void AcknowledgeBattleEnd()
        {
            if (Battle == null) return;

            bool victory = Run.LastBattleVictory;

            Battle.BattleFinished -= OnBattleFinished;
            Battle = null;

            // 这一场已经打完了，读档不该再把玩家送回战斗里
            Run.ActiveBattleEncounterId = null;

            if (!victory)
            {
                // 不存盘：GameOver / Victory 由 SaveService 负责删档
                SetPhase(RunPhase.GameOver);
                return;
            }

            var node = CurrentNode;
            bool wasBoss = node != null && node.Type == MapNodeType.Boss;

            if (wasBoss)
            {
                SetPhase(RunPhase.Victory);
                return;
            }

            if (Run.PendingBattleGivesReward)
            {
                Run.PendingReward = RewardGenerator.Generate(Run, Run.LastEncounter);
                SetPhase(RunPhase.Reward);

                // ★ 这个快照打在奖励生成**之后**，与节点快照相反。
                //   奖励一旦生成就该定死：打在生成之前的话，玩家读一次档就重掷一次三选一。
                //   代价是「领了一半退出」会回到刚打完的状态重领一次，但领的还是同一批东西。
                RequestAutosave();
            }
            else
            {
                ReturnToMap();
            }
        }

        // ================================================================= 节点结束

        /// <summary>奖励领完 / 商店逛完 / 事件处理完 / 休息完，回到地图。</summary>
        public void ReturnToMap()
        {
            Run.PendingReward = null;

            // 事件里的「进入战斗」在这里被消费。放在这里而不是效果内部，
            // 是为了让流程跳转只有 RunManager 一个出口。
            if (!string.IsNullOrEmpty(Run.PendingBattleEncounterId))
            {
                var id = Run.PendingBattleEncounterId;
                bool reward = Run.PendingBattleGivesReward;
                Run.PendingBattleEncounterId = null;
                StartBattle(id, reward);
                return;
            }

            if (Run.IsDead) { SetPhase(RunPhase.GameOver); return; }

            SetPhase(RunPhase.Map);

            // ★ 回到地图 = 上一个节点里发生的一切（买了什么、事件选了什么、休息回了多少血）
            //   全部落盘。在此之前退出游戏，那个节点整体作废重来——
            //   这正是「快照语义」：一个节点内部的修改要么整体落盘，要么整体丢弃。
            //   事件里那种「代价已扣、选牌面板还没答完」的半完成态，就是被这条规则吃掉的。
            RequestAutosave();
        }

        /// <summary>创建一个作用于当前 Run 的效果上下文。事件 / 休息 / 商店都用它。</summary>
        public RunEffectContext NewEffectContext() => new RunEffectContext(Run);

        // ================================================================= 阶段

        public void SetPhase(RunPhase phase)
        {
            if (Run != null) Run.Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        public void GoToMainMenu()
        {
            Battle = null;
            SetPhase(RunPhase.MainMenu);
        }
    }

    /// <summary>起始牌组的一项。放在这里而不是 UI 层，测试也要用。</summary>
    [Serializable]
    public struct StarterDeckEntry
    {
        public string CardId;
        public int Count;

        public StarterDeckEntry(string cardId, int count)
        {
            CardId = cardId;
            Count = count;
        }
    }
}
