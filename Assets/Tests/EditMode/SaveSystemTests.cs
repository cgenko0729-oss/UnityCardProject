using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Map;
using Game.Potions;
using Game.Relics;
using Game.Save;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 存档系统的测试。
    ///
    /// ★ 绝大多数用例走 <see cref="RunSaveWriter"/> / <see cref="RunSaveReader"/> / <see cref="SaveJson"/>
    ///   三个纯函数，**不碰磁盘**。这正是把序列化逻辑与文件 IO 拆开的目的：
    ///   存档系统的全部风险都在「哪些字段被存了、读回来对不对」，
    ///   而那一部分不该因为要写文件而变得难测。
    ///   只有 AtomicWrite / 损坏文件两条真的需要文件，它们把
    ///   <see cref="SaveSystem.OverrideDirectory"/> 指到临时目录。
    /// </summary>
    public class SaveSystemTests
    {
        private TestContent _content;
        private RunManager _mgr;

        /// <summary>最近一次 AutosaveRequested 时的存档 JSON。模拟 SaveService 的行为。</summary>
        private string _snapshot;

        [SetUp]
        public void SetUp()
        {
            _content = TestContent.Build();
            _mgr = new RunManager();
            _snapshot = null;
        }

        /// <summary>用例自己临时造的 SO，TearDown 里一起销毁（TestContent 管不到它们）。</summary>
        private readonly List<UnityEngine.Object> _extraAssets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _extraAssets.Count; i++)
                if (_extraAssets[i] != null) UnityEngine.Object.DestroyImmediate(_extraAssets[i]);
            _extraAssets.Clear();

            _content?.Dispose();
            SaveSystem.OverrideDirectory = null;
        }

        // ============================================================ 脚手架

        private static List<StarterDeckEntry> StarterDeck() => new List<StarterDeckEntry>
        {
            new StarterDeckEntry("strike", 5),
            new StarterDeckEntry("defend", 4),
            new StarterDeckEntry("bash", 1),
        };

        private RunContext NewRun(int seed = 777)
        {
            _mgr.AutosaveRequested += CaptureSnapshot;
            return _mgr.StartNewRun(_content.Db, seed, StarterDeck(), "burning_blood", 80);
        }

        private void CaptureSnapshot() => _snapshot = SaveJson.ToJson(RunSaveWriter.Write(_mgr.Run));

        /// <summary>把当前 Run 存一遍再读回来，全程只经过 JSON 字符串。</summary>
        private RunContext RoundTrip(RunContext run, List<string> warnings = null)
        {
            string json = SaveJson.ToJson(RunSaveWriter.Write(run));
            var save = SaveJson.RunFromJson(json);
            Assert.IsNotNull(save, "JSON 解析失败");
            Assert.IsTrue(SaveMigration.TryMigrate(save, warnings), "迁移失败");
            return RunSaveReader.Read(save, _content.Db, warnings);
        }

        private RunContext LoadSnapshot(out RunManager mgr)
        {
            Assert.IsNotNull(_snapshot, "还没有产生过任何存档快照");
            var save = SaveJson.RunFromJson(_snapshot);
            Assert.IsNotNull(save);
            Assert.IsTrue(SaveMigration.TryMigrate(save));
            var run = RunSaveReader.Read(save, _content.Db, null);
            Assert.IsNotNull(run, "快照读不出来");

            mgr = new RunManager();
            mgr.Resume(_content.Db, run);
            return run;
        }

        /// <summary>把当前战斗里的敌人全部打死并推进流程。</summary>
        private static void WinCurrentBattle(RunManager mgr)
        {
            var ctx = mgr.Battle.Ctx;
            for (int i = 0; i < ctx.AllUnits.Count; i++)
                if (!ctx.AllUnits[i].IsPlayer) ctx.AllUnits[i].Hp = 0;
            mgr.Battle.CheckBattleEnd();
            mgr.AcknowledgeBattleEnd();
        }

        /// <summary>
        /// 走固定的 steps 步：永远选可达节点里的第一个，战斗一律打赢，
        /// 奖励只领金币，然后回地图。★ 必须是完全确定性的，
        /// 「等价性」用例靠对同一段脚本跑两遍来比对。
        /// </summary>
        private static void Walk(RunManager mgr, int steps)
        {
            var buffer = new List<int>();
            for (int s = 0; s < steps; s++)
            {
                if (mgr.Phase != RunPhase.Map) break;

                mgr.GetAvailableNodes(buffer);
                if (buffer.Count == 0) break;
                if (!mgr.EnterNode(buffer[0])) break;

                if (mgr.Phase == RunPhase.Battle) WinCurrentBattle(mgr);

                if (mgr.Phase == RunPhase.Reward || mgr.Phase == RunPhase.Treasure)
                {
                    var reward = mgr.Run.PendingReward;
                    if (reward != null && !reward.GoldTaken)
                    {
                        mgr.Run.Gold += reward.Gold;
                        reward.GoldTaken = true;
                    }
                }

                if (mgr.Phase == RunPhase.Victory || mgr.Phase == RunPhase.GameOver) break;

                mgr.Run.Hp = mgr.Run.MaxHp;   // 别让脚本因为暴毙提前结束
                mgr.ReturnToMap();
            }
        }

        // ============================================================ 1 往返保真

        [Test]
        public void RoundTrip_PreservesEverything()
        {
            var run = NewRun(20260726);

            // 升一张牌：Def 会被换成 *_plus，这是最容易存错的一项
            var upgradable = new List<CardInstance>();
            run.GetUpgradableCards(upgradable);
            if (upgradable.Count > 0) upgradable[0].Upgrade();

            run.Gold = 314;
            run.Hp = 41;
            run.MaxHp = 92;
            run.EnergyPerTurn = 4;
            run.CardsPerTurn = 6;
            run.PotionSlots = 4;
            run.BattlesWon = 7;
            run.CardRemovalsPurchased = 2;
            run.LastBattleVictory = true;
            run.LastEncounter = _content.Encounters["slime"];

            run.AddRelic(_content.Relics["vajra"]);
            run.AddRelic(_content.Relics["beads"]);
            run.Relics[1].Counter = 5;                 // 跨战斗计数（铁律 12）

            run.AddPotion(_content.Potions["fire"]);
            run.AddPotion(_content.Potions["energy"]);

            run.CurrentNodeId = run.Map.Rows[0][0];
            run.VisitedNodeIds.Add(run.CurrentNodeId);
            run.Phase = RunPhase.Shop;

            var stock = ShopStock.Generate(run);
            stock.Items[0].Sold = true;
            run.ShopStocks[run.CurrentNodeId] = stock;

            run.PendingReward = new BattleReward { Gold = 42, GoldTaken = true };
            run.PendingReward.CardChoices.Add(_content.Cards["flex"]);
            run.PendingReward.Relic = _content.Relics["lantern"];

            var loaded = RoundTrip(run);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(Signature(run), Signature(loaded));
        }

        // ============================================================ 2 等价性（本文件最值钱的一条）

        /// <summary>
        /// 存档 → 读档 → 继续走，与「根本没存过档、一路走下去」必须完全一致。
        ///
        /// ★ 这条用例的价值在于**它不枚举字段**。往 RunContext 加一个新字段却忘了写进存档时，
        ///   逐字段断言的用例一条都不会红（它们只断言自己认识的那些字段），
        ///   而这条会：漏存的字段会让读档后的那一条分支走出不同的结果。
        /// </summary>
        [Test]
        public void LoadedRun_ContinuesExactlyLikeAnUninterruptedRun()
        {
            // A：不存档，一路走 6 步
            var runA = NewRun(4242);
            Walk(_mgr, 3);
            string midA = Signature(runA);
            Walk(_mgr, 3);
            string endA = Signature(runA);

            // B：同样走 3 步，从那一刻的快照读档，再走 3 步
            var mgrB = new RunManager();
            string snapshotB = null;
            mgrB.AutosaveRequested += () => snapshotB = SaveJson.ToJson(RunSaveWriter.Write(mgrB.Run));
            mgrB.StartNewRun(_content.Db, 4242, StarterDeck(), "burning_blood", 80);
            Walk(mgrB, 3);
            Assert.AreEqual(midA, Signature(mgrB.Run), "走到第 3 步时两局就该是一样的");

            var saveB = SaveJson.RunFromJson(snapshotB);
            Assert.IsTrue(SaveMigration.TryMigrate(saveB));
            var loaded = RunSaveReader.Read(saveB, _content.Db, null);
            Assert.IsNotNull(loaded);

            var mgrC = new RunManager();
            mgrC.Resume(_content.Db, loaded);
            Walk(mgrC, 3);

            Assert.AreEqual(endA, Signature(mgrC.Run),
                "读档后继续走出的结果与从未中断的那一局不一致——多半是某个字段没被存进存档");
        }

        // ============================================================ 3 随机流连续性

        [Test]
        public void Rng_ContinuesFromWhereItStopped_NotFromTheSeed()
        {
            var run = NewRun(555);

            // 先消耗掉一部分随机流，让它离「开局状态」足够远
            for (int i = 0; i < 37; i++) run.Rng.Range(RngStream.Reward, 0, 1000);

            var expected = new List<int>(100);
            var probe = RoundTrip(run);
            for (int i = 0; i < 100; i++) expected.Add(run.Rng.Range(RngStream.Reward, 0, 1000));

            var actual = new List<int>(100);
            for (int i = 0; i < 100; i++) actual.Add(probe.Rng.Range(RngStream.Reward, 0, 1000));

            CollectionAssert.AreEqual(expected, actual,
                "读档后的随机序列与不存档时不同——多半是只存了 Seed 没存各条流的状态");
        }

        [Test]
        public void Rng_StreamsMissingFromTheSaveKeepTheirSeededDefault()
        {
            // 老存档里没有的流（将来新增 RngStream 时就是这个情形）应该保持
            // Hash(seed, stream+1) 的初值，而不是变成 0 或者抛异常
            var run = NewRun(99);
            var save = RunSaveWriter.Write(run);
            save.RngStates.RemoveAll(s => s.Stream == (int)RngStream.Potion);

            var loaded = RunSaveReader.Read(save, _content.Db, null);
            Assert.IsNotNull(loaded);

            var fresh = new Rng(99);
            Assert.AreEqual(fresh.Range(RngStream.Potion, 0, 100000),
                            loaded.Rng.Range(RngStream.Potion, 0, 100000));
        }

        // ============================================================ 4 重打战斗的确定性

        /// <summary>
        /// 战斗中途退出 → 读档 → 重打，起手牌必须与第一次逐张相同。
        ///
        /// ★ 这条盯的是「快照打在 Battle.StartBattle 之前」这个刀口。
        ///   在它之后存盘的话，起手牌已经抽完、RngStream.CardDraw 已经推进，
        ///   于是每次读档重打都会拿到一副不同的手牌——等于白送玩家一个刷起手牌的按钮。
        /// </summary>
        [Test]
        public void ReplayingABattleAfterLoad_DealsTheSameOpeningHand()
        {
            var run = NewRun(31337);
            _mgr.EnterNode(run.Map.Rows[0][0]);
            Assert.AreEqual(RunPhase.Battle, _mgr.Phase);

            var firstHand = new List<string>();
            foreach (var c in _mgr.Battle.Ctx.Deck.Hand) firstHand.Add($"{c.Id}#{c.Uid}");
            Assert.IsNotEmpty(firstHand);

            LoadSnapshot(out var mgrB);
            Assert.AreEqual(RunPhase.Battle, mgrB.Phase, "读档应该重新回到这场战斗里");

            var secondHand = new List<string>();
            foreach (var c in mgrB.Battle.Ctx.Deck.Hand) secondHand.Add($"{c.Id}#{c.Uid}");

            CollectionAssert.AreEqual(firstHand, secondHand,
                "读档重打抽到的起手牌不同——存档快照多半打在了 Battle.StartBattle 之后");
        }

        [Test]
        public void ResumingABattleStartedByAnEvent_RestartsThatBattle_NotTheNodesOne()
        {
            var run = NewRun(2024);

            // 模拟事件里的 StartBattleRunEffect：停在一个非战斗节点上，却要开一场指定的战斗
            var restNode = FindNode(run.Map, MapNodeType.Rest);
            if (restNode < 0) Assert.Ignore("这张地图没有休息节点，跳过");

            run.CurrentNodeId = restNode;
            _mgr.StartBattle("thorny", givesReward: false);
            Assert.AreEqual(RunPhase.Battle, _mgr.Phase);

            LoadSnapshot(out var mgrB);

            Assert.AreEqual(RunPhase.Battle, mgrB.Phase);
            Assert.AreEqual("thorny", mgrB.Run.LastEncounter.Id,
                "读档重开的是当前节点的战斗，而不是事件真正开的那一场");
        }

        // ============================================================ 5 商店库存

        [Test]
        public void ShopStock_SurvivesSaveLoad_WithPricesAndSoldFlags()
        {
            var run = NewRun(8080);

            var stock = ShopStock.Generate(run);
            stock.Items[0].Sold = true;
            stock.Items[1].Price = 12345;
            run.ShopStocks[3] = stock;

            var loaded = RoundTrip(run);
            Assert.IsTrue(loaded.ShopStocks.ContainsKey(3));

            var after = loaded.ShopStocks[3];
            Assert.AreEqual(stock.Items.Count, after.Items.Count, "商品数量对不上");
            for (int i = 0; i < stock.Items.Count; i++)
            {
                Assert.AreEqual(ItemKey(stock.Items[i]), ItemKey(after.Items[i]), $"第 {i} 件商品变了");
                Assert.AreEqual(stock.Items[i].Price, after.Items[i].Price);
                Assert.AreEqual(stock.Items[i].Sold, after.Items[i].Sold);
            }
        }

        [Test]
        public void ResumingAShopNode_DoesNotRerollTheStock()
        {
            var run = NewRun(6161);
            int shop = FindNode(run.Map, MapNodeType.Shop);
            if (shop < 0) Assert.Ignore("这张地图没有商店节点，跳过");

            // 直接把玩家放到商店节点的前一格，然后进去
            EnterNodeDirectly(_mgr, run, shop);
            Assert.AreEqual(RunPhase.Shop, _mgr.Phase);

            var before = new List<string>();
            foreach (var it in _mgr.CurrentShop.Items) before.Add(ItemKey(it) + "@" + it.Price);

            LoadSnapshot(out var mgrB);
            Assert.AreEqual(RunPhase.Shop, mgrB.Phase);

            var after = new List<string>();
            foreach (var it in mgrB.CurrentShop.Items) after.Add(ItemKey(it) + "@" + it.Price);

            CollectionAssert.AreEqual(before, after,
                "读档后商店重掷了一批商品——玩家反复存读就能刷到想要的东西（铁律 13）");
        }

        // ============================================================ 6 快照语义：半完成态回滚

        /// <summary>
        /// 进了节点之后、回地图之前所做的一切修改，都不该出现在磁盘上。
        ///
        /// ★ 这就是「快照语义」要解决的那个漏洞：EventScreen 里效果已经跑完
        ///   （金币 / 生命已扣），而选牌请求还挂在 UI 层的队列里。
        ///   如果那时候存过盘，读档回到事件界面就能**再选一次选项**，代价扣两次或好处拿两次。
        ///   不存盘的话，磁盘上留的是进事件之前那一份，代价与好处一起回滚。
        /// </summary>
        [Test]
        public void MidNodeChanges_AreRolledBackByTheSnapshot()
        {
            var run = NewRun(1234);
            int rest = FindNode(run.Map, MapNodeType.Rest);
            if (rest < 0) Assert.Ignore("这张地图没有休息节点，跳过");

            EnterNodeDirectly(_mgr, run, rest);
            int goldAtEntry = run.Gold;
            int deckAtEntry = run.Deck.Count;

            // 模拟事件效果：扣钱、加牌。此时**没有**任何存档点
            run.Gold -= 55;
            run.AddCard(_content.Cards["flex"]);

            LoadSnapshot(out var mgrB);

            Assert.AreEqual(goldAtEntry, mgrB.Run.Gold, "半途退出后金币没有回滚，玩家等于白被扣钱");
            Assert.AreEqual(deckAtEntry, mgrB.Run.Deck.Count, "半途拿到的牌不该留下");
        }

        // ============================================================ 7 不能跳过节点

        /// <summary>
        /// 节点快照里的 Phase 必须已经是目标阶段，不能还留着 Map。
        /// 留着 Map 的话，CurrentNodeId 已经指向新节点却回到地图界面，
        /// 玩家等于白嫖跳过了一个节点。
        /// </summary>
        [Test]
        public void NodeSnapshot_DoesNotLetThePlayerSkipTheNode()
        {
            var run = NewRun(4321);
            int rest = FindNode(run.Map, MapNodeType.Rest);
            if (rest < 0) Assert.Ignore("这张地图没有休息节点，跳过");

            EnterNodeDirectly(_mgr, run, rest);

            var save = SaveJson.RunFromJson(_snapshot);
            Assert.AreEqual(RunPhase.Rest, save.Phase, "快照里的阶段还停在进节点之前");
            Assert.AreEqual(rest, save.CurrentNodeId);

            LoadSnapshot(out var mgrB);
            Assert.AreEqual(RunPhase.Rest, mgrB.Phase, "读档应该回到那个休息点，而不是回到地图");
        }

        // ============================================================ 8 Uid

        [Test]
        public void Uids_DoNotCollideAfterLoad()
        {
            var run = NewRun(1111);

            // 制造 Uid 空洞：删掉中间几张牌。用「最大 Uid + 1」恢复计数器的实现会在这里撞号
            run.RemoveCard(run.Deck[3]);
            run.RemoveCard(run.Deck[3]);

            var loaded = RoundTrip(run);

            var used = new HashSet<int>();
            for (int i = 0; i < loaded.Deck.Count; i++)
                Assert.IsTrue(used.Add(loaded.Deck[i].Uid), "读档后的牌库里有重复 Uid");

            for (int i = 0; i < 5; i++)
            {
                var fresh = loaded.AddCard(_content.Cards["flex"]);
                Assert.IsTrue(used.Add(fresh.Uid), $"读档后新发的 Uid {fresh.Uid} 与既有的牌撞号");
            }

            Assert.AreEqual(run.PeekNextCardUid, RoundTrip(run).PeekNextCardUid);
        }

        [Test]
        public void PotionUids_SurviveTheRoundTrip()
        {
            var run = NewRun(2222);
            run.AddPotion(_content.Potions["fire"]);
            run.AddPotion(_content.Potions["healing"]);
            run.RemovePotion(run.Potions[0]);
            run.AddPotion(_content.Potions["energy"]);

            var loaded = RoundTrip(run);

            Assert.AreEqual(run.Potions.Count, loaded.Potions.Count);
            for (int i = 0; i < run.Potions.Count; i++)
            {
                Assert.AreEqual(run.Potions[i].Id, loaded.Potions[i].Id);
                Assert.AreEqual(run.Potions[i].Uid, loaded.Potions[i].Uid);
            }
            Assert.AreEqual(run.PeekNextPotionUid, loaded.PeekNextPotionUid);
        }

        // ============================================================ 9 升级卡不双重升级

        /// <summary>
        /// ★ 架构文档 03 的草稿写的是「存基础 Id + 读档时 Upgrade N 次」，
        ///   而 CardInstance.Upgrade() 会把 Def 换成 UpgradedVersion，
        ///   于是存下来的 Def.Id 已经是 *_plus，再升一次就是双重升级。
        /// </summary>
        [Test]
        public void UpgradedCard_IsNotUpgradedTwiceOnLoad()
        {
            var plus = CreateStrikePlus();
            _content.Cards["strike"].UpgradedVersion = plus;
            _content.Db.Invalidate();

            var run = NewRun(3333);
            var strike = run.Deck.Find(c => c.Id == "strike");
            Assert.IsNotNull(strike);
            strike.Upgrade();
            Assert.AreEqual("strike_plus", strike.Id);

            var loaded = RoundTrip(run);
            var restored = loaded.Deck.Find(c => c.Uid == strike.Uid);

            Assert.IsNotNull(restored, "升级过的那张牌读档后不见了");
            Assert.AreEqual("strike_plus", restored.Id, "读档后被再升了一级");
            Assert.AreEqual(1, restored.UpgradeLevel);
        }

        private CardDefinition CreateStrikePlus()
        {
            var plus = UnityEngine.ScriptableObject.CreateInstance<CardDefinition>();
            plus.name = "Card_strike_plus";
            plus.Id = "strike_plus";
            plus.DisplayName = "打击+";
            plus.Cost = 1;
            plus.Type = CardType.Attack;
            plus.Rarity = CardRarity.Special;   // 升级版一律 Special，免得混进奖励池（铁律 14）
            plus.Effects = new List<CardEffect>();

            _extraAssets.Add(plus);
            _content.Cards["strike_plus"] = plus;
            _content.Db.Cards.Add(plus);
            return plus;
        }

        // ============================================================ 10 内容缺失

        [Test]
        public void MissingContent_IsSkipped_AndReported()
        {
            var run = NewRun(4444);
            run.AddRelic(_content.Relics["vajra"]);
            run.AddPotion(_content.Potions["fire"]);

            var save = RunSaveWriter.Write(run);
            int deckBefore = save.Deck.Count;
            int relicsBefore = save.Relics.Count;     // 起始遗物 burning_blood 也在里面
            int potionsBefore = save.Potions.Count;

            // 把三样东西改成数据库里不存在的 Id，模拟「策划把这些内容删了」
            save.Deck[0].DefId = "card_that_no_longer_exists";
            save.Relics[0].Id = "relic_that_no_longer_exists";
            save.Potions[0].Id = "potion_that_no_longer_exists";

            var warnings = new List<string>();
            RunContext loaded = null;
            Assert.DoesNotThrow(() => loaded = RunSaveReader.Read(save, _content.Db, warnings));

            Assert.IsNotNull(loaded, "内容缺失不该让整份存档作废");
            Assert.AreEqual(deckBefore - 1, loaded.Deck.Count, "缺失的卡应该被跳过");
            Assert.AreEqual(relicsBefore - 1, loaded.Relics.Count, "只该丢掉那一个遗物，其余照常读入");
            Assert.AreEqual(potionsBefore - 1, loaded.Potions.Count);

            // 每一次跳过都必须留下一条能对上号的警告，否则「我的牌怎么少了一张」就是无头案
            foreach (var id in new[] { "card_that_no_longer_exists", "relic_that_no_longer_exists", "potion_that_no_longer_exists" })
                Assert.IsTrue(warnings.Exists(w => w.Contains(id)), $"没有报告跳过了「{id}」。实际警告：{string.Join(" / ", warnings)}");
        }

        [Test]
        public void MissingRewardRelic_IsMarkedTakenSoTheScreenCanClose()
        {
            var run = NewRun(4545);
            run.PendingReward = new BattleReward { Gold = 10 };
            run.PendingReward.Relic = _content.Relics["vajra"];

            var save = RunSaveWriter.Write(run);
            save.PendingReward.RelicId = "gone";

            var loaded = RunSaveReader.Read(save, _content.Db, new List<string>());

            Assert.IsNull(loaded.PendingReward.Relic);
            Assert.IsTrue(loaded.PendingReward.RelicTaken,
                "画不出来的奖励必须标成已领，否则奖励界面永远停在「还有东西没领」");
        }

        [Test]
        public void SaveWithoutAMap_IsRejected()
        {
            var run = NewRun(4646);
            var save = RunSaveWriter.Write(run);
            save.Map = null;

            var warnings = new List<string>();
            Assert.IsNull(RunSaveReader.Read(save, _content.Db, warnings));
            Assert.IsNotEmpty(warnings);
        }

        // ============================================================ 11 版本

        [Test]
        public void HigherVersionSave_IsRejected()
        {
            var run = NewRun(5555);
            var save = RunSaveWriter.Write(run);
            save.Version = SaveConstants.CurrentVersion + 1;

            var warnings = new List<string>();
            Assert.IsFalse(SaveMigration.TryMigrate(save, warnings),
                "老客户端读新存档会把不认识的字段读成默认值，表现为「玩家的东西凭空少了」");
            Assert.IsNotEmpty(warnings);
        }

        [Test]
        public void SaveWithoutAVersion_IsRejected()
        {
            var save = SaveJson.RunFromJson("{ \"Seed\": 5 }");
            Assert.IsNotNull(save, "这是合法 JSON，解析本身应该成功");
            Assert.AreEqual(0, save.Version, "缺 Version 时必须读成 0，不能默认成当前版本");
            Assert.IsFalse(SaveMigration.TryMigrate(save, new List<string>()));
        }

        // ============================================================ 12 损坏的输入

        [Test]
        public void CorruptInput_ReturnsNullInsteadOfThrowing()
        {
            foreach (var bad in new[]
            {
                null, "", "   ", "{", "not json at all", "[1,2,3]",
                "{ \"Version\": 1, \"Deck\": \"这里本该是个数组\" }",
                "{ \"Version\": 1, \"Phase\": \"NoSuchPhase\" }",
            })
            {
                Assert.DoesNotThrow(() => SaveJson.RunFromJson(bad), $"输入「{bad}」把解析弄炸了");
            }
        }

        [Test]
        public void TruncatedFileOnDisk_DoesNotThrow_AndLoadReturnsNull()
        {
            SaveSystem.OverrideDirectory = Path.Combine(Path.GetTempPath(), "TryCardSaveTests_" + Guid.NewGuid().ToString("N"));

            var run = NewRun(6666);
            Assert.IsTrue(SaveSystem.SaveRun(run));
            Assert.IsTrue(SaveSystem.HasRunSave);

            string json = File.ReadAllText(SaveSystem.RunPath);
            File.WriteAllText(SaveSystem.RunPath, json.Substring(0, json.Length / 2));

            RunContext loaded = null;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => loaded = SaveSystem.LoadRun(_content.Db));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(loaded, "半截存档不该被当成一份合法存档读进来");

            SaveSystem.DeleteRun();
            Assert.IsFalse(SaveSystem.HasRunSave);
            TryCleanDirectory(SaveSystem.OverrideDirectory);
        }

        [Test]
        public void AtomicWrite_KeepsTheOldSaveAsBackup()
        {
            SaveSystem.OverrideDirectory = Path.Combine(Path.GetTempPath(), "TryCardSaveTests_" + Guid.NewGuid().ToString("N"));

            var run = NewRun(7777);
            run.Gold = 100;
            Assert.IsTrue(SaveSystem.SaveRun(run));

            run.Gold = 200;
            Assert.IsTrue(SaveSystem.SaveRun(run));

            string backup = SaveSystem.RunPath + SaveConstants.BackupSuffix;
            Assert.IsTrue(File.Exists(backup), "第二次写入应该把上一份正档留成 .bak");
            StringAssert.Contains("\"Gold\": 100", File.ReadAllText(backup));
            StringAssert.Contains("\"Gold\": 200", File.ReadAllText(SaveSystem.RunPath));

            // .tmp 不该留在原地——留着说明替换那一步没跑完
            Assert.IsFalse(File.Exists(SaveSystem.RunPath + SaveConstants.TempSuffix));

            SaveSystem.DeleteRun();
            TryCleanDirectory(SaveSystem.OverrideDirectory);
        }

        // ============================================================ 13 反射守卫

        /// <summary>
        /// <see cref="RunContext"/> 的公开字段集合被钉死在这里。
        ///
        /// ★ 存档系统的头号死因是**字段漂移**：以后有人给 RunContext 加一个字段，
        ///   不会有任何东西提醒他去 RunSaveWriter / RunSaveReader 同步一份。
        ///   表现是「读档后某个东西回到了初始值」，而且往往几周后才被发现。
        ///   这条用例让「加字段」这个动作当场变红，逼你在
        ///   「存它」与「明确决定不存它（加进下面的 NotSaved 列表并写清楚为什么）」之间做个选择。
        ///
        /// <para>它与「等价性」用例是互补的：那条从行为上抓漏，这条从结构上抓漏，
        ///   而且这条给得出人话的错误信息。</para>
        /// </summary>
        [Test]
        public void EveryRunContextFieldIsAccountedForBySave()
        {
            // 存进存档的字段
            var saved = new HashSet<string>
            {
                "Seed", "Rng", "Hp", "MaxHp", "Gold", "EnergyPerTurn", "CardsPerTurn",
                "Deck", "Relics", "PotionSlots", "Potions",
                "Map", "CurrentNodeId", "VisitedNodeIds", "Phase",
                "BattlesWon", "LastBattleVictory", "LastEncounter",
                "PendingBattleEncounterId", "PendingBattleGivesReward", "ActiveBattleEncounterId",
                "PendingReward", "ShopStocks", "CardRemovalsPurchased",
            };

            // 刻意不存的字段，每一条都要说得出理由
            var notSaved = new Dictionary<string, string>
            {
                ["Database"] = "只读资产索引，读档时由调用方注入，存进 JSON 毫无意义",
                ["InteractivePlayer"] = "「这一局是不是真人在玩」由创建这一局的人设置；能读档的必然是真人，由 SaveService 置 true",
            };

            var actual = new List<string>();
            foreach (var f in typeof(RunContext).GetFields(BindingFlags.Public | BindingFlags.Instance))
                actual.Add(f.Name);

            var missing = new List<string>();
            foreach (var name in actual)
                if (!saved.Contains(name) && !notSaved.ContainsKey(name)) missing.Add(name);

            CollectionAssert.IsEmpty(missing,
                "RunContext 多了字段：" + string.Join("、", missing) +
                "。请决定它要不要进存档——要就同步 RunSave/Writer/Reader，" +
                "不要就把它加进本用例的 notSaved 并写清楚理由。");

            var vanished = new List<string>();
            foreach (var name in saved)
                if (!actual.Contains(name)) vanished.Add(name);

            CollectionAssert.IsEmpty(vanished,
                "存档声称要存这些字段，但 RunContext 上已经没有了：" + string.Join("、", vanished));
        }

        // ============================================================ 14 广播时机

        /// <summary>
        /// <see cref="RunManager.Resume"/> 也必须遵守「广播时数据已就位」。
        ///
        /// ★ RunFlowTests 里那条同名用例只覆盖 EnterNode 这条路径。
        ///   Resume 是本次新加的**第二条**进入各阶段的路径，
        ///   而第三次会话那个「进战斗看不到敌人」的 bug 正是这类时序问题。
        /// </summary>
        [Test]
        public void Resume_BroadcastsOnlyAfterItsDataIsReady()
        {
            var run = NewRun(909090);
            var problems = new List<string>();
            var buffer = new List<int>();

            int guard = 0;
            while (guard++ < 40)
            {
                if (_mgr.Phase != RunPhase.Map) break;
                _mgr.GetAvailableNodes(buffer);
                if (buffer.Count == 0) break;
                if (!_mgr.EnterNode(buffer[0])) break;

                // 每进一个节点就立刻从快照读一次档，检查 Resume 广播的那一刻数据齐不齐
                var save = SaveJson.RunFromJson(_snapshot);
                SaveMigration.TryMigrate(save);
                var loaded = RunSaveReader.Read(save, _content.Db, null);
                Assert.IsNotNull(loaded);

                var mgrB = new RunManager();
                mgrB.PhaseChanged += phase => CheckPhaseData(mgrB, phase, problems);
                mgrB.Resume(_content.Db, loaded);

                if (_mgr.Phase == RunPhase.Battle) WinCurrentBattle(_mgr);
                if (_mgr.Phase == RunPhase.Victory || _mgr.Phase == RunPhase.GameOver) break;

                _mgr.Run.Hp = _mgr.Run.MaxHp;
                _mgr.ReturnToMap();
            }

            CollectionAssert.IsEmpty(problems);
        }

        private static void CheckPhaseData(RunManager mgr, RunPhase phase, List<string> problems)
        {
            switch (phase)
            {
                case RunPhase.Battle:
                    if (mgr.Battle == null) problems.Add("Resume 到 Battle 时 Battle 为 null");
                    else if (mgr.Battle.Ctx == null) problems.Add("Resume 到 Battle 时 Ctx 为 null（界面会绑到空战斗）");
                    else if (mgr.Battle.Ctx.Player == null) problems.Add("Resume 到 Battle 时 Player 还没建出来");
                    else if (mgr.Battle.Ctx.AllUnits.Count < 2) problems.Add("Resume 到 Battle 时敌人还没建出来");
                    break;

                case RunPhase.Reward:
                case RunPhase.Treasure:
                    if (mgr.Run.PendingReward == null) problems.Add($"Resume 到 {phase} 时 PendingReward 为 null");
                    break;

                case RunPhase.Shop:
                    if (mgr.CurrentShop == null) problems.Add("Resume 到 Shop 时库存为 null");
                    break;
            }
        }

        // ============================================================ 工具

        private static int FindNode(GameMap map, MapNodeType type)
        {
            for (int i = 0; i < map.Nodes.Count; i++)
                if (map.Nodes[i].Type == type) return i;
            return -1;
        }

        /// <summary>
        /// 绕过可达性检查把玩家送进某个节点。测试要针对特定节点类型，
        /// 而地图是随机生成的，一路走过去既慢又不一定走得到。
        /// </summary>
        private static void EnterNodeDirectly(RunManager mgr, RunContext run, int nodeId)
        {
            var node = run.Map.GetNode(nodeId);
            Assert.IsNotNull(node);

            // 站到它的某个前驱上，让 IsAvailable 通过
            run.CurrentNodeId = node.Prev.Count > 0 ? node.Prev[0] : -1;
            if (run.CurrentNodeId < 0 && !run.Map.Rows[0].Contains(nodeId))
                Assert.Ignore($"节点 #{nodeId} 既不在第 0 行也没有前驱，跳过");

            Assert.IsTrue(mgr.EnterNode(nodeId), $"进不去节点 #{nodeId}");
        }

        private static string ItemKey(ShopItem it)
        {
            if (it.IsCardRemoval) return "removal";
            if (it.Card != null) return "card:" + it.Card.Id;
            if (it.Relic != null) return "relic:" + it.Relic.Id;
            if (it.Potion != null) return "potion:" + it.Potion.Id;
            return "?";
        }

        private static void TryCleanDirectory(string dir)
        {
            try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
            catch (IOException) { /* 临时目录清不掉不该让用例红 */ }
        }

        // ============================================================ 签名

        /// <summary>
        /// 把一个 <see cref="RunContext"/> 压成一个字符串，用于「两局是不是一模一样」的比对。
        ///
        /// ★ 它靠**反射**遍历公开字段，而不是手写一串字段名。
        ///   手写的话，新加的字段既不会被存档存下来、也不会进签名，
        ///   于是「等价性」用例会安静地放它过去——那正是这条用例要防的事。
        ///   遇到不认识的字段类型直接抛异常，逼作者回来处理它。
        /// </summary>
        private static string Signature(RunContext run)
        {
            var sb = new StringBuilder(4096);

            var names = new List<string>();
            foreach (var f in typeof(RunContext).GetFields(BindingFlags.Public | BindingFlags.Instance))
                names.Add(f.Name);
            names.Sort(StringComparer.Ordinal);

            foreach (var name in names)
            {
                var f = typeof(RunContext).GetField(name);
                sb.Append(name).Append('=').Append(Describe(name, f.GetValue(run))).Append('\n');
            }

            // 私有的 Uid 计数器不在字段遍历里，但它必须进签名——
            // 忘了存它的表现正是「读档后新发的牌与旧牌撞号」
            sb.Append("nextCardUid=").Append(run.PeekNextCardUid).Append('\n');
            sb.Append("nextPotionUid=").Append(run.PeekNextPotionUid).Append('\n');

            return sb.ToString();
        }

        private static string Describe(string fieldName, object v)
        {
            switch (v)
            {
                case null: return "<null>";
                case bool b: return b ? "1" : "0";
                case int i: return i.ToString();
                case string s: return s;
                case Enum e: return e.ToString();
            }

            switch (v)
            {
                case GameDatabase _: return "<db>";                       // 只读资产，不进签名
                case Rng rng: return DescribeRng(rng);
                case EncounterDefinition enc: return enc.Id;
                case BattleReward reward: return DescribeReward(reward);
                case GameMap map: return DescribeMap(map);
                case List<int> ids: return string.Join(",", ids);
                case List<CardInstance> deck: return DescribeDeck(deck);
                case List<RelicInstance> relics: return DescribeRelics(relics);
                case List<PotionInstance> potions: return DescribePotions(potions);
                case Dictionary<int, ShopStock> shops: return DescribeShops(shops);
            }

            throw new NotSupportedException(
                $"RunContext.{fieldName} 是签名里没见过的类型 {v.GetType().Name}。" +
                "请在 SaveSystemTests.Describe 里补一段——顺带确认它已经进了存档。");
        }

        private static string DescribeRng(Rng rng)
        {
            var states = rng.Save();
            states.Sort((a, b) => a.Stream.CompareTo(b.Stream));
            var sb = new StringBuilder();
            foreach (var s in states) sb.Append(s.Stream).Append(':').Append(s.State).Append(' ');
            return sb.ToString();
        }

        private static string DescribeDeck(List<CardInstance> deck)
        {
            var sb = new StringBuilder();
            foreach (var c in deck) sb.Append(c.Id).Append('#').Append(c.Uid).Append('+').Append(c.UpgradeLevel).Append(' ');
            return sb.ToString();
        }

        private static string DescribeRelics(List<RelicInstance> relics)
        {
            var sb = new StringBuilder();
            foreach (var r in relics) sb.Append(r.Id).Append(':').Append(r.Counter).Append(' ');
            return sb.ToString();
        }

        private static string DescribePotions(List<PotionInstance> potions)
        {
            var sb = new StringBuilder();
            foreach (var p in potions) sb.Append(p.Id).Append('#').Append(p.Uid).Append(' ');
            return sb.ToString();
        }

        private static string DescribeShops(Dictionary<int, ShopStock> shops)
        {
            var keys = new List<int>(shops.Keys);
            keys.Sort();

            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                sb.Append(k).Append('{');
                foreach (var it in shops[k].Items)
                    sb.Append(ItemKey(it)).Append('@').Append(it.Price).Append(it.Sold ? "!" : "").Append(' ');
                sb.Append('}');
            }
            return sb.ToString();
        }

        private static string DescribeReward(BattleReward r)
        {
            var sb = new StringBuilder();
            sb.Append("gold=").Append(r.Gold).Append(r.GoldTaken ? "!" : "").Append(' ');
            foreach (var c in r.CardChoices) sb.Append(c.Id).Append(' ');
            sb.Append(r.CardTaken ? "cardTaken " : "");
            sb.Append("relic=").Append(r.Relic != null ? r.Relic.Id : "-").Append(r.RelicTaken ? "!" : "").Append(' ');
            sb.Append("potion=").Append(r.Potion != null ? r.Potion.Id : "-").Append(r.PotionTaken ? "!" : "");
            return sb.ToString();
        }

        private static string DescribeMap(GameMap map)
        {
            var sb = new StringBuilder();
            foreach (var n in map.Nodes)
            {
                sb.Append(n.Id).Append(':').Append(n.Row).Append(',').Append(n.Column).Append(',')
                  .Append(n.Type).Append(',').Append(n.ContentId ?? "-").Append('[')
                  .Append(string.Join("/", n.Next)).Append('|').Append(string.Join("/", n.Prev)).Append("] ");
            }
            foreach (var row in map.Rows) sb.Append('(').Append(string.Join(",", row)).Append(')');
            return sb.ToString();
        }
    }
}
