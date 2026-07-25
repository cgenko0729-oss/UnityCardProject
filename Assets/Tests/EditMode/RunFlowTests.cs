using System.Collections.Generic;
using Game.Core;
using Game.Map;
using Game.RunEffects;
using Game.RunEffects.Impl;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 局外流程（RunManager / RunEffect / 奖励 / 商店）的测试。
    /// ★ 全程没有任何 UI——RunManager 不引用界面类型，正是为了能这样测。
    /// </summary>
    public class RunFlowTests
    {
        private TestContent _content;
        private RunManager _mgr;

        [SetUp]
        public void SetUp()
        {
            _content = TestContent.Build();
            _mgr = new RunManager();
        }

        [TearDown]
        public void TearDown() => _content?.Dispose();

        private RunContext NewRun(int seed = 777)
        {
            var deck = new List<StarterDeckEntry>
            {
                new StarterDeckEntry("strike", 5),
                new StarterDeckEntry("defend", 4),
                new StarterDeckEntry("bash", 1),
            };
            return _mgr.StartNewRun(_content.Db, seed, deck, "burning_blood", 80);
        }

        // ============================================================ 开局

        [Test]
        public void StartNewRun_BuildsDeckRelicAndMap()
        {
            var run = NewRun();

            Assert.AreEqual(10, run.Deck.Count);
            Assert.IsTrue(run.HasRelic("burning_blood"));
            Assert.IsNotNull(run.Map);
            Assert.AreEqual(RunPhase.Map, _mgr.Phase);
            Assert.AreEqual(-1, run.CurrentNodeId, "开局还没进任何节点");
        }

        [Test]
        public void CardUids_AreUnique_AndNotStatic()
        {
            // 两局游戏各自从 1 开始编号，互不干扰——这正是把计数器从 static 挪进 RunContext 的目的
            var runA = NewRun(1);
            var uidsA = new HashSet<int>();
            for (int i = 0; i < runA.Deck.Count; i++)
                Assert.IsTrue(uidsA.Add(runA.Deck[i].Uid), "同一局内出现了重复的卡牌 Uid");

            var mgrB = new RunManager();
            var runB = mgrB.StartNewRun(_content.Db, 2,
                new List<StarterDeckEntry> { new StarterDeckEntry("strike", 3) }, null, 80);

            Assert.AreEqual(1, runB.Deck[0].Uid, "新的一局应该从 1 开始编号，不受上一局影响");
        }

        [Test]
        public void SameSeed_ProducesSameRun()
        {
            var a = NewRun(31415);
            var mgrB = new RunManager();
            var b = mgrB.StartNewRun(_content.Db, 31415,
                new List<StarterDeckEntry> { new StarterDeckEntry("strike", 5), new StarterDeckEntry("defend", 4), new StarterDeckEntry("bash", 1) },
                "burning_blood", 80);

            Assert.AreEqual(a.Map.Nodes.Count, b.Map.Nodes.Count);
            for (int i = 0; i < a.Map.Nodes.Count; i++)
            {
                Assert.AreEqual(a.Map.Nodes[i].Type, b.Map.Nodes[i].Type);
                Assert.AreEqual(a.Map.Nodes[i].ContentId, b.Map.Nodes[i].ContentId);
            }
        }

        // ============================================================ 地图推进

        [Test]
        public void EnterNode_RejectsUnreachableNodes()
        {
            var run = NewRun();
            int boss = run.Map.Boss.Id;

            Assert.IsFalse(_mgr.EnterNode(boss), "不该能从起点直接跳到 Boss");
            Assert.AreEqual(-1, run.CurrentNodeId);
            Assert.AreEqual(RunPhase.Map, _mgr.Phase);
        }

        [Test]
        public void EnterBattleNode_StartsBattle()
        {
            var run = NewRun();
            int first = run.Map.Rows[0][0];

            Assert.IsTrue(_mgr.EnterNode(first));
            Assert.AreEqual(RunPhase.Battle, _mgr.Phase);
            Assert.IsNotNull(_mgr.Battle);
            Assert.AreEqual(first, run.CurrentNodeId);
            CollectionAssert.Contains(run.VisitedNodeIds, first);
        }

        /// <summary>
        /// 阶段变化广播出去的那一刻，界面需要的数据必须**已经准备好**。
        ///
        /// ★ 这条是回归测试：原实现先 SetPhase 再 StartBattle，
        ///   而 SetPhase 是同步的——GameApp 立刻建出战斗界面并 Bind 到一个 Ctx 还是 null 的
        ///   BattleController，导致敌人和玩家面板一个都建不出来（手牌因为每帧刷新反而正常，
        ///   于是故障看起来像「敌人不见了」而不是「界面绑早了」）。
        ///   原来的测试只在 EnterNode 返回后检查状态，抓不到这个时序问题。
        /// </summary>
        [Test]
        public void PhaseChanged_FiresOnlyAfterItsDataIsReady()
        {
            var run = NewRun();
            var problems = new List<string>();

            _mgr.PhaseChanged += phase =>
            {
                switch (phase)
                {
                    case RunPhase.Battle:
                        if (_mgr.Battle == null) problems.Add("Battle 阶段广播时 Battle 为 null");
                        else if (_mgr.Battle.Ctx == null) problems.Add("Battle 阶段广播时 Battle.Ctx 为 null（界面会绑到空战斗）");
                        else if (_mgr.Battle.Ctx.Player == null) problems.Add("Battle 阶段广播时 Player 还没建出来");
                        else if (_mgr.Battle.Ctx.AllUnits.Count < 2) problems.Add("Battle 阶段广播时敌人还没建出来");
                        break;

                    case RunPhase.Reward:
                    case RunPhase.Treasure:
                        if (run.PendingReward == null) problems.Add($"{phase} 阶段广播时 PendingReward 为 null");
                        break;

                    case RunPhase.Shop:
                        if (_mgr.CurrentShop == null) problems.Add("Shop 阶段广播时库存为 null");
                        break;

                    case RunPhase.Event:
                        if (_mgr.CurrentEvent == null) problems.Add("Event 阶段广播时事件配置为 null");
                        break;
                }
            };

            // 把地图上每一类节点都走一遍
            var buffer = new List<int>();
            int guard = 0;
            while (guard++ < 100)
            {
                _mgr.GetAvailableNodes(buffer);
                if (buffer.Count == 0) break;

                _mgr.EnterNode(buffer[0]);

                if (_mgr.Phase == RunPhase.Battle)
                {
                    var ctx = _mgr.Battle.Ctx;
                    for (int i = 0; i < ctx.AllUnits.Count; i++)
                        if (!ctx.AllUnits[i].IsPlayer) ctx.AllUnits[i].Hp = 0;
                    _mgr.Battle.CheckBattleEnd();
                    _mgr.AcknowledgeBattleEnd();
                    if (_mgr.Phase == RunPhase.Victory) break;
                }

                run.Hp = run.MaxHp;
                _mgr.ReturnToMap();
                if (_mgr.Phase == RunPhase.Victory || _mgr.Phase == RunPhase.GameOver) break;
            }

            CollectionAssert.IsEmpty(problems);
        }

        [Test]
        public void BattleVictory_GoesToReward_OnlyAfterAcknowledge()
        {
            var run = NewRun();
            _mgr.EnterNode(run.Map.Rows[0][0]);

            // 直接把敌人打死
            var ctx = _mgr.Battle.Ctx;
            for (int i = 0; i < ctx.AllUnits.Count; i++)
                if (!ctx.AllUnits[i].IsPlayer) ctx.AllUnits[i].Hp = 0;
            _mgr.Battle.CheckBattleEnd();

            Assert.AreEqual(RunPhase.Battle, _mgr.Phase,
                "BattleFinished 只记录结果，不该立刻切界面——表现层还在播最后几个事件");

            _mgr.AcknowledgeBattleEnd();

            Assert.AreEqual(RunPhase.Reward, _mgr.Phase);
            Assert.IsNotNull(run.PendingReward);
            Assert.Greater(run.PendingReward.Gold, 0);
            Assert.AreEqual(1, run.BattlesWon);
        }

        [Test]
        public void BattleDefeat_GoesToGameOver()
        {
            var run = NewRun();
            _mgr.EnterNode(run.Map.Rows[0][0]);

            _mgr.Battle.Ctx.Player.Hp = 0;
            _mgr.Battle.CheckBattleEnd();
            _mgr.AcknowledgeBattleEnd();

            Assert.AreEqual(RunPhase.GameOver, _mgr.Phase);
        }

        [Test]
        public void ShopStock_IsGeneratedOnce_AndReusedOnReentry()
        {
            var run = NewRun();
            var stock = ShopStock.Generate(run);
            run.ShopStocks[42] = stock;
            run.CurrentNodeId = 42;

            Assert.AreSame(stock, _mgr.CurrentShop,
                "商店库存必须复用，否则玩家反复进出就能刷到想要的商品");
        }

        // ============================================================ 奖励

        [Test]
        public void EliteReward_GivesMoreGoldAndARelic()
        {
            var run = NewRun();
            var elite = _content.Encounters["dummy"];
            elite.IsElite = true;

            var reward = RewardGenerator.Generate(run, elite);

            Assert.GreaterOrEqual(reward.Gold, RewardGenerator.EliteGoldMin);
            Assert.Less(reward.Gold, RewardGenerator.EliteGoldMax);
            Assert.IsNotNull(reward.Relic, "精英战斗应该掉遗物");

            elite.IsElite = false;   // 还原，避免影响其它用例
        }

        [Test]
        public void NormalReward_HasThreeDistinctCardChoices()
        {
            var run = NewRun();
            var reward = RewardGenerator.Generate(run, _content.Encounters["dummy"]);

            Assert.AreEqual(RewardGenerator.CardChoiceCount, reward.CardChoices.Count);
            CollectionAssert.AllItemsAreUnique(reward.CardChoices, "三选一不能出现重复的牌");
            Assert.IsNull(reward.Relic, "普通战斗不该掉遗物");
        }

        [Test]
        public void PickRelic_NeverReturnsAnAlreadyOwnedRelic()
        {
            var run = NewRun();

            // 把所有非起始遗物都拿到手
            var pool = new List<Relics.RelicDefinition>();
            run.Database.GetRelicsByRarity(pool, null);
            for (int i = 0; i < pool.Count; i++) run.AddRelic(pool[i]);

            var picked = ContentPicker.PickRelic(run.Rng, run.Database, RngStream.Reward, run);
            Assert.IsNull(picked, "全部拿过之后应该返回 null，而不是重复给一个");
        }

        // ============================================================ RunEffect

        [Test]
        public void GoldEffect_CannotOverdraw()
        {
            var run = NewRun();
            run.Gold = 30;
            var ctx = new RunEffectContext(run);

            var spend = new GoldRunEffect { Amount = -50 };
            Assert.IsFalse(spend.CanApply(ctx), "金币不够时选项应该不可用");

            RunEffectResolver.Resolve(spend, ctx);
            Assert.AreEqual(30, run.Gold, "不可用的效果不该被执行");
        }

        [Test]
        public void HpEffect_PercentOfMax_ScalesWithMaxHp()
        {
            var run = NewRun();
            run.MaxHp = 200;
            run.Hp = 200;

            var ctx = new RunEffectContext(run);
            RunEffectResolver.Resolve(new HpRunEffect { Amount = -10, PercentOfMax = true }, ctx);

            Assert.AreEqual(180, run.Hp, "10% 的 200 应该是 20 点");
        }

        [Test]
        public void RemoveCardEffect_RefusesToEmptyTheDeck()
        {
            var run = NewRun();
            run.Deck.Clear();
            run.AddCard(_content.Cards["strike"]);

            var ctx = new RunEffectContext(run);
            var effect = new RemoveCardRunEffect { Count = 1, PlayerChooses = false };

            Assert.IsFalse(effect.CanApply(ctx), "不该允许把牌库删空");
        }

        [Test]
        public void RemoveCardEffect_WithPlayerChoice_QueuesARequestInsteadOfActing()
        {
            var run = NewRun();
            var ctx = new RunEffectContext(run);
            int before = run.Deck.Count;

            RunEffectResolver.Resolve(new RemoveCardRunEffect { Count = 1, PlayerChooses = true }, ctx);

            Assert.AreEqual(before, run.Deck.Count, "需要玩家选择时效果本身不该动牌库");
            Assert.AreEqual(1, ctx.Choices.Count);
            Assert.AreEqual(RunChoiceKind.RemoveCard, ctx.Choices.Peek().Kind);
        }

        [Test]
        public void ConditionalRunEffect_PicksBranchAndPropagatesLog()
        {
            var run = NewRun();
            run.Hp = 20;   // 25%，低于 50%
            var ctx = new RunEffectContext(run);

            RunEffectResolver.Resolve(new ConditionalRunEffect
            {
                Condition = new RunCondition { Kind = RunConditionKind.HpBelowPercent, Value = 50 },
                Then = new List<RunEffect> { new GoldRunEffect { Amount = 100 } },
                Else = new List<RunEffect> { new GoldRunEffect { Amount = 1 } },
            }, ctx);

            Assert.AreEqual(run.Gold, 99 + 100);
            Assert.AreEqual(1, ctx.Log.Count, "子上下文的日志必须并回父级，否则界面上什么都不显示");
        }

        [Test]
        public void StartBattleEffect_OnlyRecordsIntent_RunManagerDecidesFlow()
        {
            var run = NewRun();
            var ctx = new RunEffectContext(run);

            RunEffectResolver.Resolve(new StartBattleRunEffect { EncounterId = "slime" }, ctx);

            Assert.AreEqual("slime", run.PendingBattleEncounterId,
                "效果只该写下意图，流程跳转的权力归 RunManager");
            Assert.AreEqual(RunPhase.Map, _mgr.Phase, "效果本身不该切换阶段");

            _mgr.ReturnToMap();
            Assert.AreEqual(RunPhase.Battle, _mgr.Phase, "ReturnToMap 应该消费掉这个意图并开战");
            Assert.IsNull(run.PendingBattleEncounterId);
        }

        [Test]
        public void UpgradeCardEffect_RandomMode_ActuallyUpgrades()
        {
            var run = NewRun();
            _content.Cards["strike"].UpgradedVersion = _content.Cards["bash"];

            var ctx = new RunEffectContext(run);
            RunEffectResolver.Resolve(new UpgradeCardRunEffect { Count = 1, PlayerChooses = false }, ctx);

            int upgraded = 0;
            for (int i = 0; i < run.Deck.Count; i++) if (run.Deck[i].UpgradeLevel > 0) upgraded++;
            Assert.AreEqual(1, upgraded);

            _content.Cards["strike"].UpgradedVersion = null;
        }

        // ============================================================ 走完整局

        [Test]
        public void CanWalkFromStartToBoss_WithoutGettingStuck()
        {
            // 验收标准第 1 条的自动化版本：从开局一路走到 Boss，每一步都必须有合法的下一步。
            var run = NewRun(20260725);
            var buffer = new List<int>();
            int guard = 0;

            while (guard++ < 100)
            {
                _mgr.GetAvailableNodes(buffer);
                if (buffer.Count == 0) break;

                var node = run.Map.GetNode(buffer[0]);

                // 战斗节点：直接判胜以便继续走图
                if (node.Type == MapNodeType.Battle || node.Type == MapNodeType.Elite
                    || node.Type == MapNodeType.Boss)
                {
                    _mgr.EnterNode(node.Id);
                    Assert.AreEqual(RunPhase.Battle, _mgr.Phase, $"节点 {node} 没能开战");

                    var ctx = _mgr.Battle.Ctx;
                    for (int i = 0; i < ctx.AllUnits.Count; i++)
                        if (!ctx.AllUnits[i].IsPlayer) ctx.AllUnits[i].Hp = 0;
                    _mgr.Battle.CheckBattleEnd();
                    _mgr.AcknowledgeBattleEnd();

                    if (_mgr.Phase == RunPhase.Victory) break;
                }
                else
                {
                    _mgr.EnterNode(node.Id);
                }

                run.Hp = run.MaxHp;    // 免得中途被事件扣死
                _mgr.ReturnToMap();
                if (_mgr.Phase == RunPhase.Victory || _mgr.Phase == RunPhase.GameOver) break;
            }

            Assert.AreEqual(RunPhase.Victory, _mgr.Phase, "应该能一路走到 Boss 并通关");
            Assert.Less(guard, 100, "走图出现了死循环");
        }
    }
}
