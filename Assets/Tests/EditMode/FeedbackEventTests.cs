using Game.Battle;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 打击反馈所依赖的**事件信息**。
    ///
    /// ★ 表现层（闪白 / 震屏 / 慢放）本身在 EditMode 里测不到，也不该测——那是手感。
    ///   但「这一下是不是致命一击」「这是攻击伤害还是中毒掉血」是**逻辑层的结论**，
    ///   它必须在 Post 的那一刻就写进事件里（见 BattleEventFlags 的注释）。
    ///   这个文件钉住的就是那些结论，让表现层永远不需要去猜。
    /// </summary>
    public class FeedbackEventTests : BattleTestFixture
    {
        // ============================================================ 致命标记

        [Test]
        public void KillingBlow_IsMarkedLethal()
        {
            StartBattle("slime");            // 史莱姆 12 血，打击 6 点
            var enemy = Enemy();

            PlayCard("strike", enemy);
            DrainEvents();                   // 第一刀：清掉，只看第二刀

            PlayCard("strike", enemy);

            var events = DrainEvents();
            var hit = LastEventOf(events, BattleEventType.DamageDealt);

            Assert.IsNotNull(hit, "应该有一条伤害事件");
            Assert.AreEqual(0, enemy.Hp, "这一刀应该打死它");
            Assert.IsTrue(hit.Value.Has(BattleEventFlags.Lethal), "致命的那一下必须带 Lethal 标记");
        }

        [Test]
        public void NonKillingBlow_IsNotMarkedLethal()
        {
            StartBattle("slime");
            var enemy = Enemy();

            PlayCard("strike", enemy);

            var events = DrainEvents();
            var hit = LastEventOf(events, BattleEventType.DamageDealt);

            Assert.IsNotNull(hit);
            Assert.IsTrue(enemy.IsAlive);
            Assert.IsFalse(hit.Value.Has(BattleEventFlags.Lethal), "没打死就不该带 Lethal");
        }

        [Test]
        public void PreventedDeath_IsNotMarkedLethal()
        {
            // 蜥蜴尾巴 → 回光：致死伤害被拦下，钳到剩 1 血。
            // ★ 这一条是 Lethal 标记最容易写错的地方：如果在致死拦截**之前**判断，
            //   画面会为一个没死的单位播完整的死亡慢放。
            MakeRun(12345, 30, ("strike", 5), ("defend", 5));
            Run.AddRelic(Content.Relics["tail"]);
            Ctrl.StartBattle(Run, Content.Encounters["brute"]);   // 每回合碾压 40 点

            DrainEvents();
            Ctrl.EndTurn();

            var events = DrainEvents();
            var hit = LastEventOf(events, BattleEventType.DamageDealt);

            Assert.IsNotNull(hit);
            Assert.IsTrue(Player.IsAlive, "回光应该救下这一次");
            Assert.IsFalse(hit.Value.Has(BattleEventFlags.Lethal), "被拦下来的致死伤害不算致命一击");
        }

        [Test]
        public void Overkill_IsMarkedWhenDamageExceedsRemainingHp()
        {
            StartBattle("slime");
            var enemy = Enemy();
            enemy.Hp = 2;              // 剩 2 血，挨一记 6 点的打击
            DrainEvents();

            PlayCard("strike", enemy);

            var hit = LastEventOf(DrainEvents(), BattleEventType.DamageDealt);
            Assert.IsNotNull(hit);
            Assert.IsTrue(hit.Value.Has(BattleEventFlags.Lethal));
            Assert.IsTrue(hit.Value.Has(BattleEventFlags.Overkill), "6 点打 2 血应该算过量击杀");
        }

        // ============================================================ 护甲

        [Test]
        public void FullyBlockedDamage_PostsNoDamageDealt()
        {
            // 全被挡下时不该有 DamageDealt——否则表现层会为 0 点伤害闪白并飘一个「-0」。
            StartBattle("slime");
            var enemy = Enemy();
            enemy.Block = 50;
            DrainEvents();

            PlayCard("strike", enemy);   // 6 点，全被吃掉

            var events = DrainEvents();
            var blocked = LastEventOf(events, BattleEventType.DamageBlocked);

            Assert.IsNotNull(blocked, "应该有一条挡下事件");
            Assert.AreEqual(6, blocked.Value.Value);
            Assert.AreEqual(DamageKind.Attack, blocked.Value.Kind, "挡下的是一次攻击伤害");
            Assert.IsNull(LastEventOf(events, BattleEventType.DamageDealt),
                          "一点血都没掉就不该有伤害事件");
        }

        // ============================================================ 伤害类型

        [Test]
        public void PoisonTick_CarriesStatusKind_NotAttack()
        {
            // ★ 表现层要靠这个分辨「被砍了一刀」和「中毒掉血」：
            //   两者该有完全不同的闪光颜色与音色，而事件的 Value 是一样的整数，分不出来。
            StartBattle("slime");
            var enemy = Enemy();

            PlayCard("poisonstab", enemy);   // 施加 3 层中毒
            DrainEvents();

            Ctrl.EndTurn();                  // 回合结束结算中毒

            // ★ 不能用 LastEventOf：EndTurn 之后史莱姆会咬玩家一口，
            //   那也是一条 DamageDealt，而且排在中毒后面。要找的是打在**敌人身上**的那条。
            var events = DrainEvents();
            BattleEvent? tick = null;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Type != BattleEventType.DamageDealt) continue;
                if (events[i].TargetUid != enemy.Uid) continue;
                tick = events[i];
                break;
            }

            Assert.IsNotNull(tick, "中毒应该在回合结束时造成一次伤害");
            Assert.AreEqual(DamageKind.Status, tick.Value.Kind, "中毒掉血的类型必须是 Status");
        }

        [Test]
        public void CardDamage_CarriesAttackKind()
        {
            StartBattle("slime");

            PlayCard("strike", Enemy());

            var hit = LastEventOf(DrainEvents(), BattleEventType.DamageDealt);
            Assert.IsNotNull(hit);
            Assert.AreEqual(DamageKind.Attack, hit.Value.Kind);
        }

        // ============================================================ 治疗上报的数值

        [Test]
        public void Heal_ReportsAmountActuallyRestored_NotTheHealAmount()
        {
            // ★ 原本这里发的是治疗量而不是实际回血量：
            //   差 2 点满血时喝一瓶 10 点治疗，飘字写「+10」而血条只动 2 格，
            //   玩家会以为药水失效了。飘字是信息，不是装饰，数字必须是真的。
            StartBattle("dummy");

            Player.Hp = Player.MaxHp - 2;
            DrainEvents();

            Player.Heal(Ctx, 10);

            var healed = LastEventOf(DrainEvents(), BattleEventType.Healed);

            Assert.IsNotNull(healed, "应该有一条治疗事件");
            Assert.AreEqual(Player.MaxHp, Player.Hp);
            Assert.AreEqual(2, healed.Value.Value, "上报的必须是实际回复的 2 点，不是治疗量 10");
        }

        [Test]
        public void HealAtFullHp_PostsNothing()
        {
            StartBattle("dummy");
            DrainEvents();

            Player.Heal(Ctx, 10);   // 已经满血

            var events = DrainEvents();
            Assert.IsNull(LastEventOf(events, BattleEventType.Healed),
                          "一点都没回就不该发事件，否则界面会飘一个「+0」出来");
        }
    }
}
