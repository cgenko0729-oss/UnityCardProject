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
                    EnsureShopStock(nodeId);
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

            return true;
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

            if (!victory)
            {
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
