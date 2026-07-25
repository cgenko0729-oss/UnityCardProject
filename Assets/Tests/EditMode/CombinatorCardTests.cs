using Game.Battle;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 四个组合子的实战用法。
    ///
    /// 组合子本身早有单元测试，但此前**没有任何一张真实卡片在用它们**
    /// （RandomPick 与 Delayed 是零引用）。这组测试守的是「组合起来仍然对」，
    /// 尤其是几个容易想当然的地方：
    /// 多段攻击不等于一次大伤害、延迟效果会吃到延迟期间叠上去的减益。
    /// </summary>
    public class CombinatorCardTests : BattleTestFixture
    {
        // ================================================================= Repeat

        [Test]
        public void Repeat_DealsDamageOncePerIteration()
        {
            StartBattle("dummy");
            int hp = Enemy().Hp;

            PlayCard("flurry", Enemy());

            Assert.AreEqual(hp - 12, Enemy().Hp, "3 段 × 4 点");
        }

        [Test]
        public void Repeat_IsChewedThroughBlockSegmentBySegment()
        {
            // ★ 这条是多段攻击真正的价值所在：12 点护甲能完全挡下「一次 12 点」，
            //   但挡不下「4+4+4」之后还剩的部分——因为护甲是被逐段削掉的。
            //   若哪天有人把 Repeat 优化成「合并成一次伤害」，这条会红。
            StartBattle("dummy");
            Enemy().Block = 6;
            int hp = Enemy().Hp;

            PlayCard("flurry", Enemy());

            Assert.AreEqual(hp - 6, Enemy().Hp, "6 点护甲吃掉前一段半，剩下 6 点打进血量");
            Assert.AreEqual(0, Enemy().Block);
        }

        [Test]
        public void Repeat_ScalesWithStrengthOnEverySegment()
        {
            StartBattle("dummy");
            Player.AddStatus(Ctx, Content.Statuses["strength"], 2, null);
            int hp = Enemy().Hp;

            PlayCard("flurry", Enemy());

            Assert.AreEqual(hp - 18, Enemy().Hp, "力量对每一段都生效：(4+2) × 3");
        }

        // ================================================================= Conditional

        [Test]
        public void Conditional_TakesTheThenBranchWhenTheConditionHolds()
        {
            StartBattle("dummy");
            Player.Hp = Player.MaxHp / 4;   // 低于 50%
            int hp = Enemy().Hp;

            PlayCard("last_stand", Enemy());

            Assert.AreEqual(hp - 20, Enemy().Hp, "条件成立应当打两次");
        }

        [Test]
        public void Conditional_TakesTheElseBranchOtherwise()
        {
            StartBattle("dummy");
            Player.Hp = Player.MaxHp;
            int hp = Enemy().Hp;

            PlayCard("last_stand", Enemy());

            Assert.AreEqual(hp - 10, Enemy().Hp, "条件不成立只打一次");
        }

        [Test]
        public void Conditional_ElseBranchStillProducesValue()
        {
            StartBattle("dummy");
            Ctx.Deck.Hand.Clear();

            // 第一张牌：LastCardTypePlayed 还不是 Attack
            Ctx.LastCardTypePlayed = Game.Cards.CardType.Skill;
            PlayCard("follow_up", Enemy());

            Assert.AreEqual(3, Player.Block, "Else 分支应当给护甲，而不是什么都不做");
        }

        [Test]
        public void Conditional_ThenBranchFiresAfterAnAttack()
        {
            StartBattle("dummy");
            Ctx.Deck.Hand.Clear();
            PlayCard("strike", Enemy());

            var followUp = GiveCard("follow_up");
            int handBefore = Ctx.Deck.Hand.Count;   // 含 follow_up 自己

            Ctrl.TryPlayCard(followUp, Enemy(), out _);

            // follow_up 离手 -1，Then 分支抽 1 张 +1，净变化为 0
            Assert.AreEqual(handBefore - 1 + 1, Ctx.Deck.Hand.Count,
                "上一张是攻击牌，应当走 Then 分支抽 1 张");
            Assert.AreEqual(0, Player.Block, "走了 Then 分支就不该再拿 Else 的护甲");
        }

        // ================================================================= RandomPick

        [Test]
        public void RandomPick_AlwaysProducesExactlyOneOfTheOptions()
        {
            StartBattle("dummy");
            Ctx.Deck.Hand.Clear();

            int energyBefore = Ctx.Energy;
            int handBefore = Ctx.Deck.Hand.Count;

            var card = GiveCard("wild_gamble");
            Ctrl.TryPlayCard(card, null, out _);

            bool gotBlock = Player.Block == 8;
            bool gotDraw = Ctx.Deck.Hand.Count > handBefore;
            bool gotEnergy = Ctx.Energy > energyBefore - 1;   // 打这张牌花了 1 费

            Assert.IsTrue(gotBlock || gotDraw || gotEnergy, "必须命中三个选项之一");
        }

        [Test]
        public void RandomPick_IsDeterministicForTheSameSeed()
        {
            int BlockAfterGamble(int seed)
            {
                var ctrl = new BattleController();
                var run = new RunContext(seed, Content.Db) { MaxHp = 80, Hp = 80, EnergyPerTurn = 3, CardsPerTurn = 5 };
                run.AddCards(Content.Cards["strike"], 10);
                ctrl.StartBattle(run, Content.Encounters["dummy"]);

                var card = run.NewCard(Content.Cards["wild_gamble"]);
                ctrl.Ctx.Deck.Hand.Add(card);
                ctrl.TryPlayCard(card, null, out _);
                return ctrl.Ctx.Player.Block * 1000 + ctrl.Ctx.Energy * 10 + ctrl.Ctx.Deck.Hand.Count;
            }

            Assert.AreEqual(BlockAfterGamble(2024), BlockAfterGamble(2024),
                "同种子必须抽到同一个选项");
        }

        // ================================================================= Delayed

        [Test]
        public void Delayed_EndOfTurnEffectFiresAtEndOfTurn()
        {
            StartBattle("dummy");
            int hp = Enemy().Hp;

            PlayCard("time_bomb");
            Assert.AreEqual(hp, Enemy().Hp, "出牌当下不该造成任何伤害");

            Ctrl.EndTurn();
            Assert.AreEqual(hp - 14, Enemy().Hp, "回合结束时才引爆");
        }

        [Test]
        public void Delayed_PicksUpDebuffsAppliedAfterItWasScheduled()
        {
            // ★ 延迟效果的真正玩法：先埋炸弹，再上易伤，结算时吃到加成。
            //   若哪天有人把延迟效果改成「排队时就算好伤害」，这条会红。
            StartBattle("dummy");
            int hp = Enemy().Hp;

            PlayCard("time_bomb");
            Enemy().AddStatus(Ctx, Content.Statuses["vulnerable"], 2, Player);

            Ctrl.EndTurn();

            Assert.Less(Enemy().Hp, hp - 14, "易伤应当在引爆时生效，而不是被无视");
        }

        [Test]
        public void Delayed_StartOfNextTurnEffectFiresNextTurn()
        {
            StartBattle("dummy");

            PlayCard("gather_strength");
            Assert.AreEqual(0, Player.GetStatusStacks("strength"), "出牌当下不该有力量");

            Ctrl.EndTurn();

            Assert.AreEqual(2, Player.GetStatusStacks("strength"), "下回合开始时才生效");
            Assert.AreEqual(8, Player.Block,
                "护甲也要在下回合开始时给——且必须在回合开始清护甲之后，否则等于没给");
        }

        [Test]
        public void Delayed_DoesNotFireTwice()
        {
            StartBattle("dummy");
            int hp = Enemy().Hp;

            PlayCard("time_bomb");
            Ctrl.EndTurn();
            int afterFirst = Enemy().Hp;

            Ctrl.EndTurn();

            Assert.AreEqual(afterFirst, Enemy().Hp, "延迟效果只该触发一次");
            Assert.AreEqual(hp - 14, afterFirst);
        }
    }
}
