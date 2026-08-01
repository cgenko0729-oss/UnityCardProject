using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Effects.Impl;
using Game.Enemies;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>阶段 2 / 3 的验收测试：效果系统、关键字、状态、敌人 AI。</summary>
    public class EffectAndStatusTests : BattleTestFixture
    {
        // ============================================================ 状态

        [Test]
        public void Vulnerable_Increases_Damage_By_50Percent()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var enemy = Enemy();
            enemy.AddStatus(Ctx, Content.Statuses["vulnerable"], 2, null);

            int before = enemy.Hp;
            PlayCard("strike", enemy);

            Assert.AreEqual(before - 9, enemy.Hp, "6 * 1.5 = 9");
        }

        [Test]
        public void Strength_Adds_Flat_Damage()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            Player.AddStatus(Ctx, Content.Statuses["strength"], 3, null);

            var enemy = Enemy();
            int before = enemy.Hp;
            PlayCard("strike", enemy);

            Assert.AreEqual(before - 9, enemy.Hp, "6 + 3 = 9");
        }

        [Test]
        public void Weak_Reduces_Damage_By_25Percent()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            Player.AddStatus(Ctx, Content.Statuses["weak"], 1, null);

            var enemy = Enemy();
            int before = enemy.Hp;
            PlayCard("strike", enemy);

            Assert.AreEqual(before - 4, enemy.Hp, "6 * 0.75 = 4.5 → 取整 4");
        }

        [Test]
        public void StrengthAndVulnerable_ApplyInCorrectOrder()
        {
            // 力量是加法(Order=AddFlat)，易伤是乘法(Order=Multiply)
            // 正确结果 = (6 + 3) * 1.5 = 13，而不是 6 * 1.5 + 3 = 12
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            Player.AddStatus(Ctx, Content.Statuses["strength"], 3, null);

            var enemy = Enemy();
            enemy.AddStatus(Ctx, Content.Statuses["vulnerable"], 1, null);

            int before = enemy.Hp;
            PlayCard("strike", enemy);

            Assert.AreEqual(before - 13, enemy.Hp);
        }

        [Test]
        public void Bash_AppliesVulnerableTo_PreviousTargets()
        {
            StartBattle("dummy", deck: new[] { ("bash", 10) });
            var enemy = Enemy();
            int before = enemy.Hp;

            PlayCard("bash", enemy);

            Assert.AreEqual(before - 8, enemy.Hp);
            Assert.AreEqual(2, enemy.GetStatusStacks("vulnerable"), "第二个效果应该命中第一个效果的目标");
        }

        [Test]
        public void Poison_DamagesFirst_ThenDecays()
        {
            StartBattle("dummy", deck: new[] { ("poisonstab", 10) });
            var enemy = Enemy();
            PlayCard("poisonstab", enemy);

            Assert.AreEqual(3, enemy.GetStatusStacks("poison"));
            int hpBefore = enemy.Hp;

            Ctrl.EndTurn();

            Assert.AreEqual(hpBefore - 3, enemy.Hp, "先按 3 层结算伤害");
            Assert.AreEqual(2, enemy.GetStatusStacks("poison"), "再减 1 层");
        }

        [Test]
        public void Poison_IgnoresBlock()
        {
            StartBattle("dummy", deck: new[] { ("poisonstab", 10) });
            var enemy = Enemy();
            enemy.AddBlock(Ctx, 50);

            PlayCard("poisonstab", enemy);
            int hpBefore = enemy.Hp;

            Ctrl.EndTurn();

            // 注意：护甲会在敌人自己的回合开始时清空，所以这里只能验证「掉了血」，
            // 不能验证护甲还剩多少。关键点是 50 点护甲没有挡住 3 点中毒伤害。
            Assert.AreEqual(hpBefore - 3, enemy.Hp, "中毒无视护甲");
        }

        [Test]
        public void Vulnerable_DecaysAtTurnEnd()
        {
            StartBattle("dummy", deck: new[] { ("bash", 10) });
            var enemy = Enemy();
            PlayCard("bash", enemy);

            Assert.AreEqual(2, enemy.GetStatusStacks("vulnerable"));
            Ctrl.EndTurn();
            Assert.AreEqual(1, enemy.GetStatusStacks("vulnerable"));
            Ctrl.EndTurn();
            Assert.AreEqual(0, enemy.GetStatusStacks("vulnerable"));
        }

        [Test]
        public void Block_ClearsAtTurnStart_ByDefault()
        {
            StartBattle("dummy", deck: new[] { ("defend", 10) });
            PlayCard("defend");
            Assert.AreEqual(5, Player.Block);

            Ctrl.EndTurn();

            Assert.AreEqual(0, Player.Block, "默认护甲在回合开始清空");
        }

        [Test]
        public void Barricade_KeepsBlock_AcrossTurns()
        {
            StartBattle("dummy", deck: new[] { ("defend", 10), ("barricade", 1) });
            Ctx.GainEnergy(5);   // 壁垒 3 费 + 防御 1 费，默认 3 点能量不够

            var barricade = GiveCard("barricade");
            Assert.IsTrue(Ctrl.TryPlayCard(barricade, null, out var reason), reason.ToString());
            Assert.AreEqual(1, Player.GetStatusStacks("barricade"));

            var defend = GiveCard("defend");
            Ctrl.TryPlayCard(defend, null, out _);
            Assert.AreEqual(5, Player.Block);

            Ctrl.EndTurn();

            Assert.AreEqual(5, Player.Block, "壁垒生效时护甲不清空");
        }

        [Test]
        public void PowerCard_DoesNotGoToAnyPile()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var barricade = GiveCard("barricade");
            Ctrl.TryPlayCard(barricade, null, out _);

            CollectionAssert.DoesNotContain(Ctx.Deck.DiscardPile, barricade);
            CollectionAssert.DoesNotContain(Ctx.Deck.ExhaustPile, barricade);
            CollectionAssert.DoesNotContain(Ctx.Deck.Hand, barricade);
        }

        [Test]
        public void Thorns_DoesNotRecurseInfinitely()
        {
            StartBattle("thorny", maxHp: 100, deck: new[] { ("strike", 10) });
            var enemy = Enemy();

            // 双方都有荆棘 → 若不排队会无限递归
            Player.AddStatus(Ctx, Content.Statuses["thorns"], 3, null);

            int playerBefore = Player.Hp;
            PlayCard("strike", enemy);

            Assert.AreEqual(playerBefore - 3, Player.Hp, "只应该被反弹一次");
            Assert.IsTrue(Player.IsAlive);
            Assert.IsFalse(Ctx.BattleEnded);
        }

        // ============================================================ 效果与关键字

        [Test]
        public void Adrenaline_DrawsAndGainsEnergy_ThenExhausts()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            int handBefore = Ctx.Deck.Hand.Count;
            int energyBefore = Ctx.Energy;

            var adr = GiveCard("adrenaline");
            Assert.IsTrue(Ctrl.TryPlayCard(adr, null, out var reason), reason.ToString());

            Assert.AreEqual(energyBefore + 1, Ctx.Energy, "0 费 + 获得 1 能量");
            Assert.AreEqual(handBefore + 2, Ctx.Deck.Hand.Count, "抽 2 张（自己已离手）");
            CollectionAssert.Contains(Ctx.Deck.ExhaustPile, adr);
        }

        [Test]
        public void XCostCard_ConsumesAllEnergy_AndScalesTimes()
        {
            // 用 100 血的木桩，避免 15 点伤害直接打死导致血量被钳到 0
            StartBattle("two_dummies", deck: new[] { ("strike", 10) });
            var e0 = Enemy(0);
            var e1 = Enemy(1);
            int hp0 = e0.Hp, hp1 = e1.Hp;

            var whirlwind = GiveCard("whirlwind");
            Assert.IsTrue(Ctrl.TryPlayCard(whirlwind, null, out var reason), reason.ToString());

            Assert.AreEqual(0, Ctx.Energy, "X 费卡应该花光能量");
            Assert.AreEqual(hp0 - 15, e0.Hp, "3 能量 → 打 3 次 5 点");
            Assert.AreEqual(hp1 - 15, e1.Hp, "群体攻击每次都打所有敌人");
        }

        [Test]
        public void RepeatEffect_RunsNTimes()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var enemy = Enemy();
            int before = enemy.Hp;

            var card = GiveCard("triplestab");
            Ctrl.TryPlayCard(card, enemy, out _);

            Assert.AreEqual(before - 6, enemy.Hp, "2 点 x 3 次");
        }

        [Test]
        public void DeeplyNestedEffects_AreCutOff_WithoutStackOverflow()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var enemy = Enemy();

            var card = GiveCard("deepnest");
            // 只要不抛异常 / 不栈溢出就算通过；EffectResolver 会打 Warning 并中断
            LogAssert_IgnoreWarnings();
            Assert.DoesNotThrow(() => Ctrl.TryPlayCard(card, enemy, out _));
            Assert.IsTrue(Ctx != null);
        }

        private static void LogAssert_IgnoreWarnings()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        }

        [Test]
        public void DrawEffect_CannotBePlayed_WhenNothingToDraw()
        {
            StartBattle("dummy", deck: new[] { ("strike", 5) });

            // 抽牌堆和弃牌堆都清空
            Ctx.Deck.DrawPile.Clear();
            Ctx.Deck.DiscardPile.Clear();

            var adr = GiveCard("adrenaline");
            Assert.IsFalse(Ctrl.CanPlayCard(adr, null, out var reason));
            Assert.AreEqual(PlayFailReason.EffectCannotApply, reason);
        }

        [Test]
        public void DynamicDescription_ReflectsStrength()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var strike = HandCard("strike");

            Assert.AreEqual("造成 6 点伤害。", strike.GetDescription(Ctx));

            Player.AddStatus(Ctx, Content.Statuses["strength"], 4, null);
            Assert.AreEqual("造成 10 点伤害。", strike.GetDescription(Ctx), "描述要随力量动态变化");
        }

        /// <summary>
        /// 描述上色的**核心契约**：`{N}` 被替换的那一刻，装饰器必须能拿到「是谁产出了这一段」。
        ///
        /// ★★ 这条测试守的是一件在成品字符串上**永远做不到**的事。
        ///    "造成 9 点伤害" 里的 9 与周围字符没有任何区别，UI 想给伤害染色就只能正则找数字，
        ///    而那会在「造成 2 次 9 点伤害」「获得 9 点护甲」「抽 2 张牌」之间全部失效。
        ///    结构信息在格式化之后就永久丢失了 —— 所以只能在丢失之前取。
        ///
        /// ★ 真正的上色实现（RichDescription）在 Game.UI，那个程序集没有测试覆盖（铁律 52），
        ///   所以这里守的是**它依赖的那条契约**，而不是颜色本身。
        /// </summary>
        [Test]
        public void Description_Decorator_ReceivesProducingEffect()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var strike = HandCard("strike");

            var probe = new ProbeDecorator();
            string text = strike.GetDescription(Ctx, null, null, probe);

            Assert.IsTrue(probe.TemplateSeen, "模板必须先经过 DecorateTemplate");
            Assert.AreEqual(1, probe.Values.Count, "「造成 {0} 点伤害。」只有一个占位符");

            // ★ 关键的一条：拿到的不只是字符串「6」，还有产出它的 DamageEffect 本身。
            //   有了它，UI 才谈得上「伤害染红、护甲染蓝」。
            Assert.IsInstanceOf<DamageEffect>(probe.Values[0].Effect);
            Assert.AreEqual("6", probe.Values[0].Text);

            // 装饰的结果确实进了成品
            Assert.AreEqual("[T]造成 [DamageEffect:6] 点伤害。", text);
        }

        /// <summary>
        /// 不传装饰器时输出必须与从前**逐字符相同**。
        /// ★ 自动模拟器、战斗日志、Editor 的卡表预览全走这条路——
        ///   它们一旦拿到富文本标记，日志会变成一堆 &lt;color&gt;，而且没人会立刻发现。
        /// </summary>
        [Test]
        public void Description_WithoutDecorator_StaysPlainText()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var strike = HandCard("strike");

            Assert.AreEqual("造成 6 点伤害。", strike.GetDescription(Ctx, null, null, null));
        }

        private class ProbeDecorator : IDescriptionDecorator
        {
            public bool TemplateSeen;
            public readonly List<(string Text, CardEffect Effect)> Values = new List<(string, CardEffect)>();

            public string DecorateTemplate(string template)
            {
                TemplateSeen = true;
                return "[T]" + template;
            }

            public string DecorateValue(string value, CardEffect effect)
            {
                Values.Add((value, effect));
                return $"[{effect.GetType().Name}:{value}]";
            }
        }

        [Test]
        public void CardUpgrade_SwapsDefinition()
        {
            StartBattle("dummy", deck: new[] { ("strike", 10) });
            var strikePlus = Content.Cards["strike"];
            var upgraded = Content.Cards["defend"];   // 借用一张卡当「升级版」，只验证机制
            strikePlus.UpgradedVersion = upgraded;

            var card = Run.NewCard(strikePlus);
            Assert.IsTrue(card.CanUpgrade);
            card.Upgrade();

            Assert.AreEqual(upgraded, card.Def);
            Assert.AreEqual(1, card.UpgradeLevel);

            strikePlus.UpgradedVersion = null;   // 还原，避免影响其他测试
        }

        // ============================================================ 敌人 AI

        [Test]
        public void IntentPreview_MatchesActualDamage()
        {
            StartBattle("slime", maxHp: 200, deck: new[] { ("strike", 10) });
            var enemy = Enemy();

            // 给玩家上易伤 → 敌人意图数值必须跟着变
            Player.AddStatus(Ctx, Content.Statuses["vulnerable"], 3, null);
            enemy.Brain.DecideIntent(Ctx);

            int intent = enemy.CurrentIntent.Value;
            int hpBefore = Player.Hp;

            Ctrl.EndTurn();

            int actual = hpBefore - Player.Hp;
            Assert.AreEqual(intent, actual, "UI 显示的意图数值必须等于玩家实际受到的伤害");
        }

        /// <summary>
        /// 意图上的数字必须随玩家的行动实时更新。
        ///
        /// ★ 回归测试：意图原本是回合开始时算好的快照，玩家给敌人上「虚弱」之后
        ///   显示的还是旧数字，玩家会照着错的数字决定该挡多少——
        ///   这直接违背了「意图上的数字 == 实际会挨的数字」这条架构承诺。
        /// </summary>
        [Test]
        public void IntentValue_UpdatesAfterPlayerAppliesDebuff()
        {
            StartBattle("slime", maxHp: 200, deck: new[] { ("strike", 10) });

            var enemy = Enemy();
            int before = enemy.CurrentIntent.Value;
            Assert.Greater(before, 0, "史莱姆第一回合应该是攻击意图");

            // 给敌人上虚弱：攻击伤害 -25%
            enemy.AddStatus(Ctx, Content.Statuses["weak"], 2, Player);
            Ctrl.RefreshIntents();

            int after = enemy.CurrentIntent.Value;
            Assert.Less(after, before, "上了虚弱之后意图伤害应该变小");
            Assert.AreEqual(before * 75 / 100, after);

            // 再验证：显示的数字仍然等于玩家实际会挨的伤害
            int hpBefore = Player.Hp;
            Ctrl.EndTurn();
            Assert.AreEqual(after, hpBefore - Player.Hp, "意图显示的数字必须等于实际掉的血");
        }

        [Test]
        public void RefreshIntents_DoesNotRerollTheChosenAction()
        {
            // 只重算数值，绝不能重选行动——否则玩家可以靠反复刷新骗 AI 换招
            StartBattle("slime", maxHp: 200, deck: new[] { ("strike", 10) });

            var enemy = Enemy();
            int action = enemy.CurrentIntent.ActionIndex;

            for (int i = 0; i < 20; i++) Ctrl.RefreshIntents();

            Assert.AreEqual(action, enemy.CurrentIntent.ActionIndex);
        }

        [Test]
        public void RefreshIntents_DoesNotConsumeRandomStream()
        {
            // 意图重算是每帧都会跑的预览路径，一旦消耗随机流，同种子同结果就没了
            StartBattle("slime", maxHp: 200, seed: 4242, deck: new[] { ("strike", 10) });

            var before = Ctx.Rng.Save();
            for (int i = 0; i < 50; i++) Ctrl.RefreshIntents();
            var after = Ctx.Rng.Save();

            Assert.AreEqual(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
                Assert.AreEqual(before[i].State, after[i].State,
                    $"随机流 {(RngStream)before[i].Stream} 被意图重算消耗了");
        }

        [Test]
        public void EnemyBrain_RespectsMaxConsecutive()
        {
            // 构造一个只有两种行动、都限制连续 1 次的敌人
            var def = Content.Enemies["dummy"];
            def.Actions = new List<EnemyAction>
            {
                new EnemyAction { Name = "A", Intent = IntentKind.Attack, Weight = 50, MaxConsecutive = 1,
                    Effects = new List<CardEffect>() },
                new EnemyAction { Name = "B", Intent = IntentKind.Defend, Weight = 50, MaxConsecutive = 1,
                    Effects = new List<CardEffect>() },
            };
            def.FixedSequence = new List<int>();

            StartBattle("dummy", maxHp: 500, deck: new[] { ("strike", 20) });
            var enemy = Enemy();

            int last = enemy.CurrentIntent.ActionIndex;
            for (int i = 0; i < 8; i++)
            {
                Ctrl.EndTurn();
                int cur = enemy.CurrentIntent.ActionIndex;
                Assert.AreNotEqual(last, cur, $"第 {i} 次：连续用了同一个行动，MaxConsecutive 没生效");
                last = cur;
            }
        }

        [Test]
        public void EnemyBrain_UsesPhaseMask()
        {
            var def = Content.Enemies["dummy"];
            def.PhaseHpThresholds = new List<int> { 50 };
            def.FixedSequence = new List<int>();
            def.Actions = new List<EnemyAction>
            {
                new EnemyAction { Name = "阶段0", Intent = IntentKind.Attack, Weight = 50, PhaseMask = 0b01,
                    Effects = new List<CardEffect>() },
                new EnemyAction { Name = "阶段1", Intent = IntentKind.Special, Weight = 50, PhaseMask = 0b10,
                    Effects = new List<CardEffect>() },
            };

            StartBattle("dummy", maxHp: 500, deck: new[] { ("strike", 20) });
            var enemy = Enemy();

            Assert.AreEqual(IntentKind.Attack, enemy.CurrentIntent.Kind, "满血时只能用阶段 0 的行动");

            enemy.Hp = enemy.MaxHp / 4;   // 跌破 50%
            enemy.Brain.DecideIntent(Ctx);

            Assert.AreEqual(IntentKind.Special, enemy.CurrentIntent.Kind, "半血后应该切到阶段 1 的行动");
        }
    }
}
