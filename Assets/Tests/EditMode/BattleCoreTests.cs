using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>阶段 1 的验收测试：最小可玩战斗。</summary>
    public class BattleCoreTests : BattleTestFixture
    {
        [Test]
        public void StartBattle_DealsOpeningHand()
        {
            StartBattle("dummy");

            Assert.AreEqual(BattlePhase.PlayerTurn, Ctx.Phase);
            Assert.AreEqual(1, Ctx.TurnNumber);
            Assert.AreEqual(5, Ctx.Deck.Hand.Count, "开局应该抽 5 张");
            Assert.AreEqual(3, Ctx.Energy);
            Assert.AreEqual(10, Ctx.Deck.TotalCards, "牌库总数不应该变");
        }

        [Test]
        public void PlayStrike_DealsSixDamage()
        {
            StartBattle("dummy");
            var enemy = Enemy();
            int before = enemy.Hp;

            var strike = HandCard("strike");
            Assert.IsTrue(Ctrl.TryPlayCard(strike, enemy, out var reason), reason.ToString());

            Assert.AreEqual(before - 6, enemy.Hp);
            Assert.AreEqual(2, Ctx.Energy, "打击花 1 点能量");
            CollectionAssert.Contains(Ctx.Deck.DiscardPile, strike);
            CollectionAssert.DoesNotContain(Ctx.Deck.Hand, strike);
        }

        [Test]
        public void PlayDefend_GainsBlock_AndBlockAbsorbsDamage()
        {
            StartBattle("dummy");

            PlayCard("defend");
            Assert.AreEqual(5, Player.Block);

            Player.TakeDamage(Ctx, new DamageInfo { Amount = 3, Kind = DamageKind.Attack });
            Assert.AreEqual(2, Player.Block);
            Assert.AreEqual(Player.MaxHp, Player.Hp, "护甲够时不应该掉血");

            Player.TakeDamage(Ctx, new DamageInfo { Amount = 5, Kind = DamageKind.Attack });
            Assert.AreEqual(0, Player.Block);
            Assert.AreEqual(Player.MaxHp - 3, Player.Hp);
        }

        [Test]
        public void CannotPlay_WithoutEnergy()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });

            PlayCard("strike", Enemy());
            PlayCard("strike", Enemy());
            PlayCard("strike", Enemy());

            Assert.AreEqual(0, Ctx.Energy);
            var last = HandCard("strike");
            Assert.IsFalse(Ctrl.CanPlayCard(last, Enemy(), out var reason));
            Assert.AreEqual(PlayFailReason.NotEnoughEnergy, reason);
        }

        [Test]
        public void SingleEnemyCard_RequiresTarget()
        {
            StartBattle("dummy");
            var strike = HandCard("strike");

            Assert.IsFalse(Ctrl.CanPlayCard(strike, null, out var reason));
            Assert.AreEqual(PlayFailReason.NeedTarget, reason);
        }

        [Test]
        public void EndTurn_DiscardsHand_AndRefillsEnergy()
        {
            StartBattle("dummy");
            int handBefore = Ctx.Deck.Hand.Count;

            Ctrl.EndTurn();

            Assert.AreEqual(2, Ctx.TurnNumber);
            Assert.AreEqual(3, Ctx.Energy);
            Assert.AreEqual(5, Ctx.Deck.Hand.Count);
            Assert.GreaterOrEqual(Ctx.Deck.DiscardPile.Count, handBefore);
        }

        [Test]
        public void Draw_ReshufflesDiscardWhenDrawPileEmpty()
        {
            StartBattle("dummy", deck: new[] { ("strike", 6) });

            Assert.AreEqual(5, Ctx.Deck.Hand.Count);
            Assert.AreEqual(1, Ctx.Deck.DrawPile.Count);

            Ctrl.EndTurn();   // 弃 5 张，抽 5 张 → 需要洗牌

            Assert.AreEqual(5, Ctx.Deck.Hand.Count);
            Assert.AreEqual(6, Ctx.Deck.TotalCards);
        }

        [Test]
        public void Victory_WhenAllEnemiesDead()
        {
            StartBattle("slime", deck: new[] { ("strike", 10) });
            var enemy = Enemy();
            enemy.Hp = 6;

            PlayCard("strike", enemy);

            Assert.IsFalse(enemy.IsAlive);
            Assert.IsTrue(Ctx.BattleEnded);
            Assert.IsTrue(Ctx.Victory);
            Assert.AreEqual(BattlePhase.Victory, Ctx.Phase);
        }

        [Test]
        public void Defeat_WhenPlayerDies()
        {
            StartBattle("brute", maxHp: 30, deck: new[] { ("defend", 10) });

            Ctrl.EndTurn();   // 巨兽打 40，玩家 30 血

            Assert.IsTrue(Ctx.BattleEnded);
            Assert.IsFalse(Ctx.Victory);
            Assert.AreEqual(BattlePhase.Defeat, Ctx.Phase);
        }

        [Test]
        public void EnemyFixedSequence_IsFollowed()
        {
            StartBattle("slime", maxHp: 200, deck: new[] { ("strike", 10) });
            var enemy = Enemy();

            // 序列 0,0,1 → 咬、咬、腐蚀
            Assert.AreEqual(Enemies.IntentKind.Attack, enemy.CurrentIntent.Kind);
            Ctrl.EndTurn();
            Assert.AreEqual(Enemies.IntentKind.Attack, enemy.CurrentIntent.Kind);
            Ctrl.EndTurn();
            Assert.AreEqual(Enemies.IntentKind.Debuff, enemy.CurrentIntent.Kind);
            Ctrl.EndTurn();
            Assert.AreEqual(Enemies.IntentKind.Attack, enemy.CurrentIntent.Kind, "序列应该循环");
        }

        [Test]
        public void RunHp_IsWrittenBack_AfterBattle()
        {
            StartBattle("slime", deck: new[] { ("strike", 10) });
            Enemy().Hp = 1;
            Player.Hp = 42;

            PlayCard("strike", Enemy());

            Assert.IsTrue(Ctx.BattleEnded);
            Assert.AreEqual(42, Run.Hp, "战斗结束要把血量写回 RunContext");
        }

        [Test]
        public void SameSeed_ProducesSameDrawOrder()
        {
            var first = new List<string>();
            var second = new List<string>();

            StartBattle("dummy", seed: 777, deck: new[] { ("strike", 6), ("defend", 6) });
            for (int i = 0; i < Ctx.Deck.Hand.Count; i++) first.Add(Ctx.Deck.Hand[i].Id);

            BaseTearDown();
            BaseSetUp();

            StartBattle("dummy", seed: 777, deck: new[] { ("strike", 6), ("defend", 6) });
            for (int i = 0; i < Ctx.Deck.Hand.Count; i++) second.Add(Ctx.Deck.Hand[i].Id);

            CollectionAssert.AreEqual(first, second, "同种子必须产生同样的抽牌顺序");
        }

        [Test]
        public void DifferentSeed_ProducesDifferentDrawOrder()
        {
            var first = new List<string>();
            var second = new List<string>();

            StartBattle("dummy", seed: 1, deck: new[] { ("strike", 10), ("defend", 10) });
            for (int i = 0; i < Ctx.Deck.Hand.Count; i++) first.Add(Ctx.Deck.Hand[i].Id);

            BaseTearDown();
            BaseSetUp();

            StartBattle("dummy", seed: 999999, deck: new[] { ("strike", 10), ("defend", 10) });
            for (int i = 0; i < Ctx.Deck.Hand.Count; i++) second.Add(Ctx.Deck.Hand[i].Id);

            CollectionAssert.AreNotEqual(first, second, "不同种子应该产生不同顺序（理论上有极小概率相同）");
        }
    }
}
