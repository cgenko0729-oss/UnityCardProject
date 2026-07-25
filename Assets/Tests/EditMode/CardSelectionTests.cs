using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 战斗内选牌。
    ///
    /// 这一组测试真正要守住的不是「弃牌能弃掉」，而是**结算挂起之后还能正确恢复**：
    /// 挂起时 C# 调用栈会展开，凡是写在 ResolveAll 下一行的代码都会抢跑。
    /// 所以每个用例都刻意在选牌效果**后面**再放一个可观测的效果（抽牌 / 加护甲），
    /// 断言它没有在玩家作答之前生效。
    /// </summary>
    public class CardSelectionTests : BattleTestFixture
    {
        /// <summary>切成交互模式：不再自动替玩家选，改为挂起等作答。</summary>
        private void MakeInteractive() => Ctx.Selector = null;

        private void ClearHand() => Ctx.Deck.Hand.Clear();

        // ================================================================= 五种处置

        [Test]
        public void Discard_MovesChosenCardToDiscardPile()
        {
            StartBattle();
            ClearHand();
            var keep = GiveCard("defend");
            var toss = GiveCard("strike");
            MakeInteractive();

            PlayCard("sift");

            Assert.IsNotNull(Ctrl.PendingSelection, "应当挂起等玩家选牌");
            Ctrl.ResolveSelection(new List<CardInstance> { toss });

            CollectionAssert.Contains(Ctx.Deck.DiscardPile, toss);
            CollectionAssert.Contains(Ctx.Deck.Hand, keep);
        }

        [Test]
        public void Exhaust_MovesChosenCardToExhaustPile()
        {
            StartBattle();
            ClearHand();
            var victim = GiveCard("strike");
            MakeInteractive();

            PlayCard("purge");
            Ctrl.ResolveSelection(new List<CardInstance> { victim });

            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, victim);
            CollectionAssert.DoesNotContain(Ctx.Deck.Hand, victim);
        }

        [Test]
        public void Retain_KeepsChosenCardInHandThroughEndOfTurn()
        {
            StartBattle();
            ClearHand();
            var kept = GiveCard("strike");
            var dropped = GiveCard("defend");
            MakeInteractive();

            PlayCard("hold");
            Ctrl.ResolveSelection(new List<CardInstance> { kept });

            Assert.IsTrue(kept.HasKeyword(CardKeyword.Retain), "被选中的牌应当临时获得「保留」");

            Ctrl.EndTurn();

            CollectionAssert.Contains(Ctx.Deck.Hand, kept, "保留的牌应当留在手上");
            CollectionAssert.DoesNotContain(Ctx.Deck.Hand, dropped, "没保留的牌应当被弃掉");
        }

        [Test]
        public void Duplicate_AddsACopyToHand()
        {
            StartBattle();
            ClearHand();
            var original = GiveCard("strike");
            MakeInteractive();

            PlayCard("mirror");
            int before = Ctx.Deck.Hand.Count;
            Ctrl.ResolveSelection(new List<CardInstance> { original });

            Assert.AreEqual(before + 1, Ctx.Deck.Hand.Count, "手牌应当多出一张复制品");

            int copies = 0;
            for (int i = 0; i < Ctx.Deck.Hand.Count; i++)
                if (Ctx.Deck.Hand[i].Id == "strike") copies++;
            Assert.AreEqual(2, copies);

            // 复制品必须是临时卡，否则战斗结束会渗进玩家的永久牌库
            var copy = Ctx.Deck.Hand.Find(c => c.Id == "strike" && c.Uid != original.Uid);
            Assert.IsNotNull(copy);
            Assert.IsTrue(copy.IsTemporary, "复制品必须是临时卡");
            Assert.AreNotEqual(original.Uid, copy.Uid, "复制品必须有自己的 Uid");
        }

        [Test]
        public void ToDrawTop_PutsChosenCardBackOnTopOfDrawPile()
        {
            StartBattle();
            ClearHand();
            var stashed = GiveCard("bash");
            MakeInteractive();

            PlayCard("stash");
            Ctrl.ResolveSelection(new List<CardInstance> { stashed });

            CollectionAssert.DoesNotContain(Ctx.Deck.Hand, stashed);
            Assert.AreSame(stashed, Ctx.Deck.DrawPile[Ctx.Deck.DrawPile.Count - 1],
                "牌堆顶是列表末尾");

            Ctx.Deck.DrawOne();
            CollectionAssert.Contains(Ctx.Deck.Hand, stashed, "下一张抽到的就该是它");
        }

        // ================================================================= 挂起语义

        [Test]
        public void Resolution_SuspendsBeforeLaterEffects()
        {
            StartBattle();
            ClearHand();
            GiveCard("strike");
            GiveCard("defend");
            MakeInteractive();

            var sift = GiveCard("sift");
            int handBefore = Ctx.Deck.Hand.Count;

            Assert.IsTrue(Ctrl.TryPlayCard(sift, null, out _));

            // sift 已离手，但「抽 2 张」绝不能在玩家选牌之前发生
            Assert.IsNotNull(Ctrl.PendingSelection);
            Assert.AreEqual(handBefore - 1, Ctx.Deck.Hand.Count,
                "抽牌抢在选牌之前发生了——说明结算没有真的挂起");

            Ctrl.ResolveSelection(new List<CardInstance> { Ctx.Deck.Hand[0] });

            // 作答后：弃掉 1 张、抽 2 张
            Assert.AreEqual(handBefore - 1 - 1 + 2, Ctx.Deck.Hand.Count);
        }

        [Test]
        public void PlayedCard_ReachesDiscardOnlyAfterSelectionResolved()
        {
            StartBattle();
            ClearHand();
            GiveCard("strike");
            MakeInteractive();

            var sift = GiveCard("sift");
            Ctrl.TryPlayCard(sift, null, out _);

            CollectionAssert.DoesNotContain(Ctx.Deck.DiscardPile, sift,
                "打出的牌在选牌完成前不该进弃牌堆");

            Ctrl.ResolveSelection(new List<CardInstance> { Ctx.Deck.Hand[0] });

            CollectionAssert.Contains(Ctx.Deck.DiscardPile, sift);
        }

        [Test]
        public void PendingSelection_BlocksOtherCardPlaysAndEndTurn()
        {
            StartBattle();
            ClearHand();
            GiveCard("strike");
            MakeInteractive();

            var sift = GiveCard("sift");
            Ctrl.TryPlayCard(sift, null, out _);
            Assert.IsNotNull(Ctrl.PendingSelection);

            var other = GiveCard("defend");
            Assert.IsFalse(Ctrl.TryPlayCard(other, null, out var reason));
            Assert.AreEqual(PlayFailReason.WaitingForSelection, reason);

            int turn = Ctx.TurnNumber;
            Ctrl.EndTurn();
            Assert.AreEqual(turn, Ctx.TurnNumber, "挂起期间不该能结束回合");
            Assert.AreEqual(BattlePhase.PlayerTurn, Ctx.Phase);
        }

        [Test]
        public void Cancel_WithEmptySelection_StillFinishesResolution()
        {
            StartBattle();
            ClearHand();
            GiveCard("strike");
            MakeInteractive();

            var sift = GiveCard("sift");
            int handBefore = Ctx.Deck.Hand.Count;
            Ctrl.TryPlayCard(sift, null, out _);

            // 传空列表 = 一张都不选
            Ctrl.ResolveSelection(new List<CardInstance>());

            Assert.IsNull(Ctrl.PendingSelection);
            Assert.AreEqual(handBefore - 1 + 2, Ctx.Deck.Hand.Count, "没弃牌，但后续的抽牌照常发生");
            CollectionAssert.Contains(Ctx.Deck.DiscardPile, sift);
        }

        [Test]
        public void ResolveSelection_IgnoresCardsOutsideTheCandidateList()
        {
            StartBattle();
            ClearHand();
            GiveCard("strike");
            MakeInteractive();

            var sift = GiveCard("sift");
            Ctrl.TryPlayCard(sift, null, out _);

            // 伪造一张根本不在候选里的牌
            var alien = Run.NewCard(Content.Cards["bash"]);
            Ctrl.ResolveSelection(new List<CardInstance> { alien });

            CollectionAssert.DoesNotContain(Ctx.Deck.DiscardPile, alien);
            Assert.IsNull(Ctrl.PendingSelection, "非法作答也必须解除挂起，否则战斗死锁");
        }

        // ================================================================= 组合子内的挂起

        [Test]
        public void SelectionInsideConditional_ResumesRemainingEffects()
        {
            StartBattle();
            ClearHand();
            GiveCard("strike");
            GiveCard("defend");
            MakeInteractive();

            var card = GiveCard("condselect");
            Ctrl.TryPlayCard(card, null, out _);

            Assert.IsNotNull(Ctrl.PendingSelection, "条件成立，应当挂起");
            Assert.AreEqual(0, Player.Block, "护甲绝不能在选牌之前就加上");

            Ctrl.ResolveSelection(new List<CardInstance> { Ctx.Deck.Hand[0] });

            Assert.AreEqual(3, Player.Block, "作答后，组合子之后的效果必须继续执行");
        }

        [Test]
        public void SelectionInsideRepeat_RunsEveryIteration()
        {
            StartBattle();
            ClearHand();
            var a = GiveCard("strike");
            var b = GiveCard("defend");
            MakeInteractive();

            var card = GiveCard("repeatselect");
            Ctrl.TryPlayCard(card, null, out _);

            // 第 1 次迭代
            Assert.IsNotNull(Ctrl.PendingSelection);
            Ctrl.ResolveSelection(new List<CardInstance> { a });

            // ★ 关键断言：第 2 次迭代必须还在。
            //   若 RepeatEffect 仍用 for 循环内联跑，挂起会让循环变量随调用栈一起消失，
            //   只会执行第 0 次。
            Assert.IsNotNull(Ctrl.PendingSelection, "Repeat 的第 2 次迭代丢失了");
            Ctrl.ResolveSelection(new List<CardInstance> { b });

            Assert.IsNull(Ctrl.PendingSelection);
            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, a);
            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, b);
        }

        // ================================================================= 非交互模式

        [Test]
        public void WithDefaultSelector_ResolutionNeverSuspends()
        {
            StartBattle();
            ClearHand();
            GiveCard("strike");
            GiveCard("defend");

            // 不调 MakeInteractive：保持默认的随机选择器
            var sift = GiveCard("sift");
            Assert.IsTrue(Ctrl.TryPlayCard(sift, null, out _));

            Assert.IsNull(Ctrl.PendingSelection,
                "无 UI 时必须当场作答——否则 EditMode 测试与自动模拟器全部会死锁");
            CollectionAssert.Contains(Ctx.Deck.DiscardPile, sift);
        }

        [Test]
        public void DefaultSelector_IsDeterministicForTheSameSeed()
        {
            int FirstDiscardUidOffset(int seed)
            {
                var ctrl = new BattleController();
                var run = new RunContext(seed, Content.Db) { MaxHp = 80, Hp = 80, EnergyPerTurn = 3, CardsPerTurn = 5 };
                run.AddCards(Content.Cards["strike"], 5);
                run.AddCards(Content.Cards["defend"], 5);
                ctrl.StartBattle(run, Content.Encounters["dummy"]);

                ctrl.Ctx.Deck.Hand.Clear();
                for (int i = 0; i < 4; i++)
                    ctrl.Ctx.Deck.Hand.Add(run.NewCard(Content.Cards[i % 2 == 0 ? "strike" : "defend"]));

                var first = ctrl.Ctx.Deck.Hand[0].Uid;
                var sift = run.NewCard(Content.Cards["sift"]);
                ctrl.Ctx.Deck.Hand.Add(sift);
                ctrl.TryPlayCard(sift, null, out _);

                var discarded = ctrl.Ctx.Deck.DiscardPile.Find(c => c.Id != "sift");
                return discarded != null ? discarded.Uid - first : -1;
            }

            Assert.AreEqual(FirstDiscardUidOffset(777), FirstDiscardUidOffset(777),
                "同种子必须选到同一张牌");
        }

        // ================================================================= 守恒与收尾

        [Test]
        public void CardCountIsConservedAcrossSelection()
        {
            StartBattle();
            MakeInteractive();

            int total = Ctx.Deck.TotalCards;

            var sift = GiveCard("sift");
            total += 1;   // GiveCard 是凭空塞进来的，计入总数

            Ctrl.TryPlayCard(sift, null, out _);
            Assert.AreEqual(total, Ctx.Deck.TotalCards + 1, "挂起期间打出的牌不在任何堆里，正好差 1");

            Ctrl.ResolveSelection(new List<CardInstance> { Ctx.Deck.Hand[0] });
            Assert.AreEqual(total, Ctx.Deck.TotalCards, "作答后牌数必须守恒");
        }

        [Test]
        public void BattleEnd_CancelsPendingSelection()
        {
            StartBattle("slime");
            ClearHand();
            GiveCard("strike");
            MakeInteractive();

            var sift = GiveCard("sift");
            Ctrl.TryPlayCard(sift, null, out _);
            Assert.IsNotNull(Ctrl.PendingSelection);

            // 直接打死敌人结束战斗
            Enemy().Hp = 0;
            Ctrl.CheckBattleEnd();

            Assert.IsTrue(Ctx.BattleEnded);
            Assert.IsNull(Ctrl.PendingSelection,
                "战斗结束必须收掉挂起的选牌，否则面板会浮在结算界面上");
        }

        [Test]
        public void EnemyTurn_NeverSuspendsEvenInInteractiveMode()
        {
            StartBattle("slime");
            MakeInteractive();

            // 敌人回合里若发生挂起，回合循环会被截断。
            // 这里只需确认「结束回合能一路跑完」——挂起会让阶段停在 EnemyTurn。
            Ctrl.EndTurn();

            Assert.IsNull(Ctrl.PendingSelection);
            Assert.AreEqual(BattlePhase.PlayerTurn, Ctx.Phase, "敌人回合必须一口气跑完");
        }
    }
}
