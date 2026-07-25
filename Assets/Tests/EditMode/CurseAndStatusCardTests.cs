using Game.Battle;
using Game.Cards;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 诅咒牌 / 状态牌。
    ///
    /// 这些卡本身没有新机制——它们组合的全是既有能力（Unplayable / Ethereal / Exhaust /
    /// 回合结束留手代价）。因此这组测试守的是**组合的正确性**：
    /// 不可打出的牌真的打不出、虚无牌真的会自己烧掉、留手代价真的按回合结算。
    /// </summary>
    public class CurseAndStatusCardTests : BattleTestFixture
    {
        [Test]
        public void UnplayableCard_CannotBePlayed()
        {
            StartBattle();
            var wound = GiveCard("wound");

            Assert.IsFalse(Ctrl.CanPlayCard(wound, null, out var reason));
            Assert.AreEqual(PlayFailReason.Unplayable, reason);
            Assert.IsFalse(Ctrl.TryPlayCard(wound, null, out _));
            CollectionAssert.Contains(Ctx.Deck.Hand, wound, "打不出的牌必须留在手上");
        }

        [Test]
        public void UnplayableCard_IsDiscardedAtEndOfTurnLikeAnyOtherCard()
        {
            StartBattle();
            var wound = GiveCard("wound");

            Ctrl.EndTurn();

            CollectionAssert.Contains(Ctx.Deck.DiscardPile, wound);
        }

        [Test]
        public void EtherealCard_ExhaustsItselfAtEndOfTurn()
        {
            StartBattle();
            var dazed = GiveCard("dazed");

            Ctrl.EndTurn();

            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, dazed,
                "虚无牌回合结束应当自我消耗，而不是进弃牌堆");
            CollectionAssert.DoesNotContain(Ctx.Deck.DiscardPile, dazed);
        }

        [Test]
        public void InHandEndOfTurnEffect_FiresWhileTheCardIsStillInHand()
        {
            StartBattle();
            int hpBefore = Player.Hp;
            GiveCard("burn");

            Ctrl.EndTurn();

            Assert.AreEqual(hpBefore - 2, Player.Hp, "灼烧留在手上应当造成 2 点伤害");
        }

        [Test]
        public void InHandEndOfTurnEffect_DoesNotFireIfTheCardLeftTheHand()
        {
            StartBattle();
            var burn = GiveCard("burn");
            int hpBefore = Player.Hp;

            // 先把它弃掉，再结束回合
            Ctx.Deck.Discard(burn);
            Ctrl.EndTurn();

            Assert.AreEqual(hpBefore, Player.Hp, "已经离手的灼烧不该再造成伤害");
        }

        [Test]
        public void BurnDamage_IgnoresBlock()
        {
            StartBattle();
            Player.Block = 20;
            int hpBefore = Player.Hp;
            GiveCard("burn");

            Ctrl.EndTurn();

            Assert.AreEqual(hpBefore - 2, Player.Hp,
                "灼烧是 Loss 类伤害，护甲挡不住——否则它对任何带防御的构筑都毫无意义");
        }

        [Test]
        public void InHandEndOfTurnEffect_CanApplyStatuses()
        {
            StartBattle();
            GiveCard("doubt");

            Ctrl.EndTurn();

            Assert.GreaterOrEqual(Player.GetStatusStacks("weak"), 1, "疑虑应当施加虚弱");
        }

        [Test]
        public void MultipleBurns_StackTheirDamage()
        {
            StartBattle();
            int hpBefore = Player.Hp;
            GiveCard("burn");
            GiveCard("burn");
            GiveCard("burn");

            Ctrl.EndTurn();

            Assert.AreEqual(hpBefore - 6, Player.Hp, "三张灼烧各结算一次");
        }

        [Test]
        public void SlimedCard_IsPlayableAndExhausts()
        {
            StartBattle();
            var slimed = GiveCard("slimed");

            Assert.IsTrue(Ctrl.CanPlayCard(slimed, null, out var reason), reason.ToString());

            int energyBefore = Ctx.Energy;
            Ctrl.TryPlayCard(slimed, null, out _);

            Assert.AreEqual(energyBefore - 1, Ctx.Energy, "粘液的代价就是那 1 点能量");
            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, slimed, "打出后应当被消耗，不再回到牌组");
        }

        [Test]
        public void CurseAndStatusCards_AreMarkedSpecialSoTheyStayOutOfRewardPools()
        {
            // ★ 与升级版同一条规则。漏标就会在战斗奖励三选一里看到「伤口」。
            string[] ids = { "wound", "dazed", "slimed", "burn", "injury", "doubt" };
            foreach (var id in ids)
                Assert.AreEqual(CardRarity.Special, Content.Cards[id].Rarity,
                    $"「{id}」必须是 Special，否则会混进奖励池和商店");
        }

        [Test]
        public void CardTypes_AreTaggedCorrectly()
        {
            Assert.AreEqual(CardType.Status, Content.Cards["wound"].Type);
            Assert.AreEqual(CardType.Status, Content.Cards["dazed"].Type);
            Assert.AreEqual(CardType.Status, Content.Cards["burn"].Type);
            Assert.AreEqual(CardType.Curse, Content.Cards["injury"].Type);
            Assert.AreEqual(CardType.Curse, Content.Cards["doubt"].Type);
        }

        [Test]
        public void BurnKillingThePlayer_EndsTheBattle()
        {
            StartBattle();
            Player.Hp = 1;
            GiveCard("burn");

            Ctrl.EndTurn();

            Assert.IsTrue(Ctx.BattleEnded, "留手代价打死玩家必须正常结束战斗");
            Assert.IsFalse(Ctx.Victory);
        }

        [Test]
        public void CardCountIsConservedWhenStatusCardsAreAdded()
        {
            StartBattle();
            int before = Ctx.Deck.TotalCards;

            GiveCard("wound");
            GiveCard("dazed");

            Ctrl.EndTurn();

            Assert.AreEqual(before + 2, Ctx.Deck.TotalCards,
                "临时牌进哪个堆都算数，总数不能漏");
        }
    }
}
