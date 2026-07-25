using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Statuses;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 遗物测试。
    ///
    /// ★ 这一整个文件都是路线图第 4 阶段验收标准第 3 条的兑现：
    ///   「遗物通过 Hook 生效，没有改动 BattleController 一行代码」。
    ///   这些遗物覆盖了阶段 4 新增的四个拦截点，如果哪天有人为了某个遗物去改战斗流程，
    ///   这些用例不会红——但那说明架构约定被破坏了，Code Review 要盯住这一点。
    /// </summary>
    public class RelicTests : BattleTestFixture
    {
        /// <summary>带指定遗物开一场战斗。</summary>
        private void StartWithRelics(string encounterId, params string[] relicIds)
        {
            MakeRun(12345, 80, ("strike", 5), ("defend", 5));
            for (int i = 0; i < relicIds.Length; i++)
                Run.AddRelic(Content.Relics[relicIds[i]]);
            Ctrl.StartBattle(Run, Content.Encounters[encounterId]);
        }

        // ============================================================ IBattleFlowHook

        [Test]
        public void Vajra_GrantsStrengthAtBattleStart()
        {
            StartWithRelics("dummy", "vajra");

            Assert.AreEqual(1, Player.GetStatusStacks("strength"), "金刚杵没有在开局给力量");

            // 力量真的生效：打击 6 点 → 7 点
            int before = Enemy().Hp;
            PlayCard("strike", Enemy());
            Assert.AreEqual(7, before - Enemy().Hp);
        }

        [Test]
        public void BurningBlood_HealsOnlyOnVictory()
        {
            StartWithRelics("slime", "burning_blood");
            Run.Hp = 40;
            Player.Hp = 40;

            // 史莱姆 12 血，两张打击足够
            for (int i = 0; i < 4 && Enemy().IsAlive; i++) PlayCard("strike", Enemy());

            Assert.IsTrue(Ctx.Victory, "应该已经胜利");
            Assert.AreEqual(46, Run.Hp,
                "燃烧之血应该在胜利后回 6 血，且必须写回 RunContext（EndBattle 里 Run.Hp = Player.Hp 会覆盖直接改 Run.Hp 的写法）");
        }

        // ============================================================ IResourceHook

        [Test]
        public void BagOfPreparation_DrawsExtraOnFirstTurnOnly()
        {
            StartWithRelics("dummy", "bag");
            Assert.AreEqual(7, Ctx.Deck.Hand.Count, "第一回合应该抽 5 + 2 张");

            Ctrl.EndTurn();
            Assert.AreEqual(5, Ctx.Deck.Hand.Count, "第二回合应该恢复成 5 张");
        }

        [Test]
        public void Lantern_GivesExtraEnergyOnFirstTurnOnly()
        {
            StartWithRelics("dummy", "lantern");
            Assert.AreEqual(4, Ctx.Energy, "第一回合应该是 3 + 1 点能量");

            Ctrl.EndTurn();
            Assert.AreEqual(3, Ctx.Energy, "第二回合应该恢复成 3 点");
        }

        [Test]
        public void TwoResourceRelics_Stack()
        {
            StartWithRelics("dummy", "bag", "lantern");
            Assert.AreEqual(7, Ctx.Deck.Hand.Count);
            Assert.AreEqual(4, Ctx.Energy);
        }

        // ============================================================ ICardPlayHook.ModifyCardCost

        [Test]
        public void PenNib_ReducesCostOfFirstAttackEachTurn()
        {
            StartWithRelics("dummy", "pen_nib");

            var first = GiveCard("strike");
            var second = GiveCard("strike");

            Assert.AreEqual(0, first.GetCost(Ctx), "本回合第一张攻击牌应该降到 0 费");

            Ctrl.TryPlayCard(first, Enemy());
            Assert.AreEqual(3, Ctx.Energy, "0 费的牌不该扣能量");

            Assert.AreEqual(1, second.GetCost(Ctx), "第二张攻击牌应该恢复原价");
        }

        [Test]
        public void PenNib_DoesNotAffectSkills()
        {
            StartWithRelics("dummy", "pen_nib");
            var defend = GiveCard("defend");
            Assert.AreEqual(1, defend.GetCost(Ctx), "笔尖只影响攻击牌");
        }

        [Test]
        public void PenNib_ResetsEachTurn()
        {
            StartWithRelics("dummy", "pen_nib");
            var a = GiveCard("strike");
            Ctrl.TryPlayCard(a, Enemy());

            Ctrl.EndTurn();

            var b = GiveCard("strike");
            Assert.AreEqual(0, b.GetCost(Ctx), "新回合的第一张攻击牌应该重新降费");
        }

        // ============================================================ ICardFlowHook.PreCardPlay

        [Test]
        public void EchoCharm_ResolvesFirstAttackTwice()
        {
            StartWithRelics("dummy", "echo");

            int before = Enemy().Hp;
            PlayCard("strike", Enemy());
            Assert.AreEqual(12, before - Enemy().Hp, "回响护符应该让第一张打击结算两次（6 x 2）");

            before = Enemy().Hp;
            PlayCard("strike", Enemy());
            Assert.AreEqual(6, before - Enemy().Hp, "第二张攻击牌不该有回响");
        }

        [Test]
        public void EchoCharm_DoesNotDoubleTheEnergyCost()
        {
            StartWithRelics("dummy", "echo");
            int energyBefore = Ctx.Energy;
            PlayCard("strike", Enemy());
            Assert.AreEqual(energyBefore - 1, Ctx.Energy, "回响只重复效果，不该重复扣费");
        }

        // ============================================================ ICardFlowHook.ModifyCardDestination

        [Test]
        public void Recycler_SendsSkillsBackToDrawPile()
        {
            StartWithRelics("dummy", "recycler");

            int discardBefore = Ctx.Deck.DiscardPile.Count;
            int drawBefore = Ctx.Deck.DrawPile.Count;

            var defend = GiveCard("defend");
            Ctrl.TryPlayCard(defend, null);

            Assert.AreEqual(discardBefore, Ctx.Deck.DiscardPile.Count, "技能牌不该进弃牌堆");
            Assert.AreEqual(drawBefore + 1, Ctx.Deck.DrawPile.Count, "技能牌应该回到抽牌堆");
            Assert.Contains(defend, Ctx.Deck.DrawPile);
        }

        [Test]
        public void Recycler_DoesNotAffectAttacks()
        {
            StartWithRelics("dummy", "recycler");

            int discardBefore = Ctx.Deck.DiscardPile.Count;
            var strike = GiveCard("strike");
            Ctrl.TryPlayCard(strike, Enemy());

            Assert.AreEqual(discardBefore + 1, Ctx.Deck.DiscardPile.Count, "攻击牌仍应进弃牌堆");
        }

        [Test]
        public void Recycler_DoesNotRescueExhaustCards()
        {
            // 「消耗」必须优先于「洗回抽牌堆」，否则临时卡会被永久留在牌库里
            StartWithRelics("dummy", "recycler");

            var adrenaline = GiveCard("adrenaline");   // 技能 + 消耗关键字
            Ctrl.TryPlayCard(adrenaline, null);

            Assert.Contains(adrenaline, Ctx.Deck.ExhaustPile, "带消耗的技能牌应该被消耗掉");
            Assert.IsFalse(Ctx.Deck.DrawPile.Contains(adrenaline));
        }

        // ============================================================ IStatusHook

        [Test]
        public void ArtifactTotem_BlocksFirstDebuffOnly()
        {
            StartWithRelics("dummy", "totem");
            Assert.AreEqual(1, Player.GetStatusStacks("artifact"));

            var enemy = Enemy();
            Player.AddStatus(Ctx, Content.Statuses["weak"], 2, enemy);

            Assert.AreEqual(0, Player.GetStatusStacks("weak"), "第一次减益应该被神器抵消");
            Assert.AreEqual(0, Player.GetStatusStacks("artifact"), "神器应该被消耗掉");

            Player.AddStatus(Ctx, Content.Statuses["weak"], 2, enemy);
            Assert.AreEqual(2, Player.GetStatusStacks("weak"), "神器用完后减益应该正常生效");
        }

        [Test]
        public void Artifact_DoesNotBlockBuffs()
        {
            StartWithRelics("dummy", "totem");

            PlayCard("flex", null);   // 自己给自己 +2 力量

            Assert.AreEqual(2, Player.GetStatusStacks("strength"), "神器不该挡增益");
            Assert.AreEqual(1, Player.GetStatusStacks("artifact"), "挡增益不该消耗神器层数");
        }

        // ============================================================ IFatalHook

        [Test]
        public void LizardTail_PreventsDeathOnceAndHeals()
        {
            MakeRun(12345, 30, ("strike", 5), ("defend", 5));
            Run.AddRelic(Content.Relics["tail"]);
            Ctrl.StartBattle(Run, Content.Encounters["brute"]);   // 每回合打 40

            Assert.AreEqual(1, Player.GetStatusStacks("revive"));

            Ctrl.EndTurn();   // 巨兽碾压 40 点，本该直接打死 30 血的玩家

            Assert.IsTrue(Player.IsAlive, "回光应该阻止这次死亡");
            Assert.AreEqual(11, Player.Hp, "应该留 1 血再回 10 血");
            Assert.AreEqual(0, Player.GetStatusStacks("revive"), "回光应该被消耗掉");
            Assert.IsFalse(Ctx.BattleEnded);
        }

        [Test]
        public void LizardTail_OnlyWorksOnce()
        {
            MakeRun(12345, 30, ("strike", 5), ("defend", 5));
            Run.AddRelic(Content.Relics["tail"]);
            Ctrl.StartBattle(Run, Content.Encounters["brute"]);

            Ctrl.EndTurn();   // 第一次：被救
            Assert.IsTrue(Player.IsAlive);

            Ctrl.EndTurn();   // 第二次：11 血挨 40，没救了
            Assert.IsFalse(Player.IsAlive, "回光只能救一次");
            Assert.IsTrue(Ctx.BattleEnded);
            Assert.IsFalse(Ctx.Victory);
        }

        // ============================================================ RelicInstance.Counter

        [Test]
        public void MeditationBeads_CountsAcrossTurns_UsingRelicCounter()
        {
            MakeRun(12345, 80, ("defend", 10));
            Run.AddRelic(Content.Relics["beads"]);
            Ctrl.StartBattle(Run, Content.Encounters["dummy"]);

            Player.Hp = 50;

            var a = GiveCard("defend");
            Ctrl.TryPlayCard(a, null);
            Assert.AreEqual(50, Player.Hp, "打第 1 张技能牌还不该触发");

            var b = GiveCard("defend");
            Ctrl.TryPlayCard(b, null);
            Assert.AreEqual(53, Player.Hp, "打第 2 张技能牌应该回 3 血");

            var c = GiveCard("defend");
            Ctrl.TryPlayCard(c, null);
            Assert.AreEqual(53, Player.Hp, "计数应该已经归零，第 3 张不触发");
        }

        [Test]
        public void RelicCounter_ResetsBetweenBattles()
        {
            MakeRun(12345, 80, ("defend", 10));
            Run.AddRelic(Content.Relics["beads"]);
            Ctrl.StartBattle(Run, Content.Encounters["dummy"]);

            var a = GiveCard("defend");
            Ctrl.TryPlayCard(a, null);   // 计数 = 1

            // 换一场新战斗
            var ctrl2 = new BattleController();
            ctrl2.StartBattle(Run, Content.Encounters["dummy"]);

            Assert.AreEqual(0, Run.Relics[0].Counter,
                "OnBattleStart 应该把计数清零，否则上一场的进度会漏到下一场");
        }

        // ============================================================ 架构约束

        [Test]
        public void RelicBehaviours_HaveNoMutableFields()
        {
            // ContentValidator 在 Editor 里也扫这一条，但那是手动触发的；
            // 放一份在测试里，CI 跑测试就能拦住。
            var offenders = new List<string>();

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(StatusBehaviour).IsAssignableFrom(t)) continue;

                    var fields = t.GetFields(System.Reflection.BindingFlags.Instance
                                             | System.Reflection.BindingFlags.NonPublic
                                             | System.Reflection.BindingFlags.DeclaredOnly);
                    foreach (var f in fields)
                    {
                        if (f.IsInitOnly) continue;
                        if (f.Name.Contains("k__BackingField")) continue;
                        if (f.GetCustomAttributes(typeof(UnityEngine.SerializeField), false).Length > 0) continue;
                        offenders.Add($"{t.FullName}.{f.Name}");
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "行为类被同一份配置的所有持有者共享，不得有可变私有字段——" +
                "可变数据放 StatusInstance / RelicInstance.Counter / BattleContext");
        }
    }
}
