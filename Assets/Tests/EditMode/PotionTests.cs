using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Potions;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 药水系统。
    ///
    /// 这一组要守住的核心是「药水没有第二套结算」：
    /// 它走的是与卡牌完全相同的 EffectResolver，因此卡牌能做的（含挂起选牌）它都能做。
    /// 一旦有人给药水另写一条结算路径，<see cref="Potion_WithSelectEffect_SuspendsLikeACard"/> 会先红。
    /// </summary>
    public class PotionTests : BattleTestFixture
    {
        private PotionInstance Give(string potionId)
        {
            var p = Run.AddPotion(Content.Potions[potionId]);
            Assert.IsNotNull(p, $"药水槽装不下「{potionId}」");
            return p;
        }

        // ================================================================= 背包

        [Test]
        public void Bag_RespectsSlotLimit()
        {
            StartBattle();
            Assert.AreEqual(3, Run.PotionSlots, "默认三个槽位");

            for (int i = 0; i < 3; i++) Assert.IsNotNull(Run.AddPotion(Content.Potions["healing"]));

            Assert.IsFalse(Run.HasPotionSpace);
            Assert.IsNull(Run.AddPotion(Content.Potions["healing"]), "满了必须返回 null 而不是静默丢弃");
            Assert.AreEqual(3, Run.Potions.Count);
        }

        [Test]
        public void Bag_AllowsDuplicatePotions()
        {
            StartBattle();
            Give("healing");
            Give("healing");
            Assert.AreEqual(2, Run.Potions.Count, "药水是消耗品，允许重复持有");
        }

        [Test]
        public void EachPotionGetsItsOwnUid()
        {
            StartBattle();
            var a = Give("healing");
            var b = Give("healing");
            Assert.AreNotEqual(a.Uid, b.Uid);
        }

        // ================================================================= 使用

        [Test]
        public void Use_AppliesEffectsAndConsumesThePotion()
        {
            StartBattle();
            Player.Hp = 40;
            var potion = Give("healing");

            Assert.IsTrue(Ctrl.TryUsePotion(potion, null, out var reason), reason.ToString());

            Assert.AreEqual(55, Player.Hp, "治疗药水回 15 点");
            CollectionAssert.DoesNotContain(Run.Potions, potion, "喝完必须从背包消失");
        }

        [Test]
        public void Use_TargetedPotion_RequiresATarget()
        {
            StartBattle("slime");
            var potion = Give("fire");

            Assert.IsFalse(Ctrl.CanUsePotion(potion, null, out var reason));
            Assert.AreEqual(PotionFailReason.NeedTarget, reason);

            Assert.IsTrue(Ctrl.TryUsePotion(potion, Enemy(), out _));
            Assert.AreEqual(12 - 12, Enemy().Hp, "史莱姆 12 血，被 20 点火焰打死");
        }

        [Test]
        public void Use_TargetedPotion_RejectsThePlayerAsTarget()
        {
            StartBattle("slime");
            var potion = Give("fire");

            Assert.IsFalse(Ctrl.CanUsePotion(potion, Player, out var reason));
            Assert.AreEqual(PotionFailReason.InvalidTarget, reason);
            CollectionAssert.Contains(Run.Potions, potion, "被拒绝的药水不能被消耗掉");
        }

        [Test]
        public void Use_RejectsPotionNotInTheBag()
        {
            StartBattle();
            var stranger = new PotionInstance(9999, Content.Potions["healing"]);

            Assert.IsFalse(Ctrl.CanUsePotion(stranger, null, out var reason));
            Assert.AreEqual(PotionFailReason.NotHeld, reason);
        }

        [Test]
        public void Use_IsRejectedOutsideThePlayerTurn()
        {
            StartBattle("dummy");
            var potion = Give("healing");

            Ctx.Phase = BattlePhase.EnemyTurn;
            Assert.IsFalse(Ctrl.CanUsePotion(potion, null, out var reason));
            Assert.AreEqual(PotionFailReason.NotPlayerTurn, reason);
        }

        [Test]
        public void Use_EnergyPotionGrantsEnergy()
        {
            StartBattle();
            int before = Ctx.Energy;
            Ctrl.TryUsePotion(Give("energy"), null, out _);
            Assert.AreEqual(before + 2, Ctx.Energy);
        }

        [Test]
        public void Discard_FreesTheSlotWithoutApplyingEffects()
        {
            StartBattle();
            Player.Hp = 40;
            var potion = Give("healing");

            Assert.IsTrue(Ctrl.DiscardPotion(potion));
            Assert.AreEqual(40, Player.Hp, "倒掉不该触发任何效果");
            CollectionAssert.DoesNotContain(Run.Potions, potion);
            Assert.IsTrue(Run.HasPotionSpace);
        }

        // ================================================================= 与选牌共存

        [Test]
        public void Potion_WithSelectEffect_SuspendsLikeACard()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            var victim = GiveCard("strike");
            Ctx.Selector = null;   // 交互模式

            var potion = Give("cleanse");
            Assert.IsTrue(Ctrl.TryUsePotion(potion, null, out _));

            Assert.IsNotNull(Ctrl.PendingSelection, "药水里的选牌效果必须同样能挂起结算");
            Ctrl.ResolveSelection(new List<CardInstance> { victim });

            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, victim);
            CollectionAssert.DoesNotContain(Run.Potions, potion);
        }

        [Test]
        public void Use_IsBlockedWhileASelectionIsPending()
        {
            StartBattle();
            Ctx.Deck.Hand.Clear();
            GiveCard("strike");
            Ctx.Selector = null;

            var sift = GiveCard("sift");
            Ctrl.TryPlayCard(sift, null, out _);
            Assert.IsNotNull(Ctrl.PendingSelection);

            var potion = Give("healing");
            Assert.IsFalse(Ctrl.CanUsePotion(potion, null, out var reason));
            Assert.AreEqual(PotionFailReason.WaitingForSelection, reason);
        }

        // ================================================================= 掉落与商店

        [Test]
        public void RewardRoll_IsDeterministicForTheSameSeed()
        {
            string PotionOf(int seed)
            {
                var run = new RunContext(seed, Content.Db);
                var reward = RewardGenerator.Generate(run, Content.Encounters["dummy"]);
                return reward.Potion != null ? reward.Potion.Id : "<none>";
            }

            Assert.AreEqual(PotionOf(4242), PotionOf(4242));
        }

        [Test]
        public void PotionRoll_DoesNotDisturbTheCardRewardStream()
        {
            // ★ 这条守的是「新加一条 Rng 流不会改动旧流」。
            //   如果哪天有人把药水掷骰改回 RngStream.Reward，所有既有种子的卡牌奖励都会错位，
            //   而那种漂移在游戏里几乎不可能被发现。
            var a = new RunContext(31337, Content.Db);
            var rewardA = RewardGenerator.Generate(a, Content.Encounters["dummy"]);

            var b = new RunContext(31337, Content.Db);
            b.Rng.Range(RngStream.Potion, 0, 1000);   // 单独推进药水流
            var rewardB = RewardGenerator.Generate(b, Content.Encounters["dummy"]);

            Assert.AreEqual(rewardA.Gold, rewardB.Gold);
            CollectionAssert.AreEqual(rewardA.CardChoices, rewardB.CardChoices,
                "推进药水流不该影响卡牌奖励");
        }

        [Test]
        public void Shop_StocksPotionsAndPricesThem()
        {
            StartBattle();
            var stock = ShopStock.Generate(Run);

            int potions = 0;
            for (int i = 0; i < stock.Items.Count; i++)
            {
                var item = stock.Items[i];
                if (item.Potion == null) continue;
                potions++;
                Assert.Greater(item.Price, 0, "药水必须有价格");
            }

            Assert.AreEqual(ShopStock.PotionCount, potions);
        }

        [Test]
        public void Reward_PotionIsRolledEvenWhenTheBagIsFull()
        {
            // 背包满不该改变掷骰序列，否则同种子会因为玩家背包状态而分叉
            var full = new RunContext(555, Content.Db);
            for (int i = 0; i < full.PotionSlots; i++) full.AddPotion(Content.Potions["healing"]);
            var rewardFull = RewardGenerator.Generate(full, Content.Encounters["dummy"]);

            var empty = new RunContext(555, Content.Db);
            var rewardEmpty = RewardGenerator.Generate(empty, Content.Encounters["dummy"]);

            Assert.AreEqual(rewardEmpty.Potion, rewardFull.Potion);
        }

        // ================================================================= 描述

        [Test]
        public void Description_FillsTemplatePlaceholders()
        {
            StartBattle();
            var text = Content.Potions["healing"].GetDescription(Ctx);
            Assert.AreEqual("回复 15 点生命。", text);
        }

        [Test]
        public void Description_WorksOutsideBattle()
        {
            StartBattle();
            var text = Content.Potions["fire"].GetDescription(null);
            StringAssert.Contains("20", text, "战斗外也要能显示静态数值（商店 / 奖励界面）");
        }
    }
}
