using Game.Battle;
using Game.Cards;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Retain / Innate / Ethereal 三个关键字。
    ///
    /// 运行时实现从阶段 2 就在 <c>DeckController</c> 里了，但此前没有任何内容用上，
    /// 因此这三条路径**从未被真正执行过**。这组测试同时是它们的首次覆盖。
    /// </summary>
    public class KeywordCardTests : BattleTestFixture
    {
        // ================================================================= 保留

        [Test]
        public void Retain_KeepsTheCardThroughEndOfTurn()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            var kept = GiveCard("spare_blade");
            var dropped = GiveCard("strike");

            Ctrl.EndTurn();

            CollectionAssert.Contains(Ctx.Deck.Hand, kept);
            CollectionAssert.DoesNotContain(Ctx.Deck.Hand, dropped);
            CollectionAssert.Contains(Ctx.Deck.DiscardPile, dropped);
        }

        [Test]
        public void Retain_SurvivesMultipleTurns()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            var kept = GiveCard("spare_blade");

            Ctrl.EndTurn();
            Ctrl.EndTurn();
            Ctrl.EndTurn();

            CollectionAssert.Contains(Ctx.Deck.Hand, kept, "保留不是「只保留一回合」");
        }

        [Test]
        public void Retain_StillGoesToDiscardWhenActuallyPlayed()
        {
            StartBattle("slime");
            Ctx.Deck.Hand.Clear();
            var card = GiveCard("spare_blade");

            Ctrl.TryPlayCard(card, Enemy(), out _);

            CollectionAssert.Contains(Ctx.Deck.DiscardPile, card,
                "保留只影响回合结束的弃牌，不影响打出后的归宿");
        }

        [Test]
        public void AmbushScalesWithHandSize()
        {
            StartBattle("dummy");
            Ctx.Deck.Hand.Clear();

            var ambush = GiveCard("ambush");
            GiveCard("strike");
            GiveCard("defend");
            // 手牌此时 3 张（含伏击自己）

            int hpBefore = Enemy().Hp;
            Ctrl.TryPlayCard(ambush, Enemy(), out _);

            // ★ 出牌时伏击已先离手，所以按 2 张手牌算
            Assert.AreEqual(hpBefore - 6, Enemy().Hp,
                "伏击应当按「结算时的手牌数」计算，而不是按出牌前");
        }

        // ================================================================= 固有

        [Test]
        public void Innate_IsAlwaysInTheOpeningHand()
        {
            // 塞满干扰牌，固有牌仍然必须出现在起手
            StartBattle("dummy", 12345, 80, ("strike", 20), ("vigil", 1));

            Assert.IsNotNull(Ctx.Deck.FindInHand("vigil"),
                "固有牌必须出现在开局手牌里，无论牌库多大");
        }

        [Test]
        public void Innate_IsStillInTheOpeningHandWithMultipleInnates()
        {
            StartBattle("dummy", 999, 80, ("strike", 20), ("vigil", 1), ("opening", 1));

            Assert.IsNotNull(Ctx.Deck.FindInHand("vigil"));
            Assert.IsNotNull(Ctx.Deck.FindInHand("opening"));
        }

        [Test]
        public void Innate_BehavesNormallyAfterTheFirstTurn()
        {
            StartBattle("dummy", 12345, 80, ("strike", 20), ("vigil", 1));
            var vigil = Ctx.Deck.FindInHand("vigil");

            Ctrl.EndTurn();

            // 固有只管开局，之后就是普通牌，会被正常弃掉
            CollectionAssert.DoesNotContain(Ctx.Deck.Hand, vigil);
        }

        // ================================================================= 虚无

        [Test]
        public void Ethereal_ExhaustsItselfIfUnusedAtEndOfTurn()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            var phantom = GiveCard("phantom_blade");

            Ctrl.EndTurn();

            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, phantom);
            CollectionAssert.DoesNotContain(Ctx.Deck.DiscardPile, phantom,
                "虚无牌应当被消耗，而不是进弃牌堆——否则它还会再抽到");
        }

        [Test]
        public void Ethereal_GoesToDiscardNormallyWhenPlayed()
        {
            StartBattle("slime");
            Ctx.Deck.Hand.Clear();
            var phantom = GiveCard("phantom_blade");

            Ctrl.TryPlayCard(phantom, Enemy(), out _);

            CollectionAssert.Contains(Ctx.Deck.DiscardPile, phantom,
                "用掉的虚无牌走普通归宿，代价只在「没用掉」时才付");
        }

        [Test]
        public void EtherealAndExhaust_EndUpInExhaustEitherWay()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            var a = GiveCard("fleeting_insight");
            Ctrl.EndTurn();
            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, a, "没用掉：虚无消耗");

            StartBattle();
            Ctx.Deck.Hand.Clear();
            var b = GiveCard("fleeting_insight");
            Ctrl.TryPlayCard(b, null, out _);
            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, b, "用掉了：消耗关键字");
        }

        // ================================================================= 守恒

        [Test]
        public void KeywordCards_PreserveTotalCardCount()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            int before = Ctx.Deck.TotalCards;

            GiveCard("spare_blade");
            GiveCard("phantom_blade");
            GiveCard("strike");

            Ctrl.EndTurn();

            Assert.AreEqual(before + 3, Ctx.Deck.TotalCards,
                "保留 / 虚无 / 普通三种归宿加起来必须守恒");
        }

        [Test]
        public void RetainedCardDoesNotBlockDrawingNextTurn()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            GiveCard("spare_blade");

            Ctrl.EndTurn();

            Assert.Greater(Ctx.Deck.Hand.Count, 1,
                "保留的牌之外，下回合照常抽牌");
        }
    }
}
