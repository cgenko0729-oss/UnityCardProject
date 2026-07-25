using System;
using Game.Battle;
using Game.Units;
using UnityEngine;

namespace Game.Statuses.Impl
{
    // 说明：这些行为类刻意放在同一个文件里，因为它们都很短且属于同一组「基础战斗状态」。
    // 每个类仍然只做一件事。新增复杂行为时请单独建文件。

    /// <summary>力量：造成的攻击伤害 + 层数。</summary>
    [Serializable]
    public class StrengthBehaviour : StatusBehaviour, IDamageHook
    {
        public override int Order => HookOrder.AddFlat;

        public void ModifyOutgoingDamage(BattleContext ctx, in HookSource src, BattleUnit target, ref DamageInfo dmg)
        {
            if (dmg.Kind != DamageKind.Attack) return;
            dmg.Amount += src.Stacks;
        }

        public void ModifyIncomingDamage(BattleContext ctx, in HookSource src, BattleUnit attacker, ref DamageInfo dmg) { }
        public void OnDamaged(BattleContext ctx, in HookSource src, in DamageInfo dmg, int hpLost) { }
    }

    /// <summary>易伤：受到的攻击伤害 +50%。</summary>
    [Serializable]
    public class VulnerableBehaviour : StatusBehaviour, IDamageHook
    {
        [Tooltip("增伤百分比")]
        public int PercentBonus = 50;

        public override int Order => HookOrder.Multiply;

        public void ModifyOutgoingDamage(BattleContext ctx, in HookSource src, BattleUnit target, ref DamageInfo dmg) { }

        public void ModifyIncomingDamage(BattleContext ctx, in HookSource src, BattleUnit attacker, ref DamageInfo dmg)
        {
            if (dmg.Kind != DamageKind.Attack) return;
            dmg.Amount = dmg.Amount * (100 + PercentBonus) / 100;
        }

        public void OnDamaged(BattleContext ctx, in HookSource src, in DamageInfo dmg, int hpLost) { }
    }

    /// <summary>虚弱：造成的攻击伤害 -25%。</summary>
    [Serializable]
    public class WeakBehaviour : StatusBehaviour, IDamageHook
    {
        [Tooltip("减伤百分比")]
        public int PercentPenalty = 25;

        public override int Order => HookOrder.Multiply;

        public void ModifyOutgoingDamage(BattleContext ctx, in HookSource src, BattleUnit target, ref DamageInfo dmg)
        {
            if (dmg.Kind != DamageKind.Attack) return;
            dmg.Amount = dmg.Amount * (100 - PercentPenalty) / 100;
        }

        public void ModifyIncomingDamage(BattleContext ctx, in HookSource src, BattleUnit attacker, ref DamageInfo dmg) { }
        public void OnDamaged(BattleContext ctx, in HookSource src, in DamageInfo dmg, int hpLost) { }
    }

    /// <summary>
    /// 中毒：回合结束受到等于层数的无视护甲伤害。
    /// ★ 减层不在这里做，由 BattleController.TickStatusDecay 统一处理，保证「先结算再衰减」。
    /// </summary>
    [Serializable]
    public class PoisonBehaviour : StatusBehaviour, ITurnHook
    {
        public override int Order => HookOrder.AddFlat;

        public void OnTurnStart(BattleContext ctx, in HookSource src) { }

        public void OnTurnEnd(BattleContext ctx, in HookSource src)
        {
            var owner = src.Owner;
            if (owner == null || !owner.IsAlive || src.Stacks <= 0) return;

            ctx.Post(BattleEventType.StatusTriggered, owner.Uid, owner.Uid, src.Stacks, "poison");
            owner.TakeDamage(ctx, new DamageInfo
            {
                Amount = src.Stacks,
                Kind = DamageKind.Status,
                IgnoreBlock = true,
                Attacker = null,
            });
        }
    }

    /// <summary>壁垒：护甲不再于回合开始时消失。</summary>
    [Serializable]
    public class BarricadeBehaviour : StatusBehaviour, IBlockHook
    {
        public override int Order => HookOrder.Late;

        public void ModifyBlockGain(BattleContext ctx, in HookSource src, ref int amount) { }

        public void ModifyBlockDecay(BattleContext ctx, in HookSource src, ref bool clear) => clear = false;
    }

    /// <summary>荆棘：被攻击时反弹伤害给攻击者。演示 OnDamaged + 触发队列防递归。</summary>
    [Serializable]
    public class ThornsBehaviour : StatusBehaviour, IDamageHook
    {
        public override int Order => HookOrder.AddFlat;

        public void ModifyOutgoingDamage(BattleContext ctx, in HookSource src, BattleUnit target, ref DamageInfo dmg) { }
        public void ModifyIncomingDamage(BattleContext ctx, in HookSource src, BattleUnit attacker, ref DamageInfo dmg) { }

        public void OnDamaged(BattleContext ctx, in HookSource src, in DamageInfo dmg, int hpLost)
        {
            // ★ 只反弹「攻击」伤害。否则荆棘会反弹荆棘伤害，两个带荆棘的单位互打会乒乓到守卫上限。
            if (dmg.Kind != DamageKind.Attack) return;

            var attacker = dmg.Attacker;
            if (attacker == null || !attacker.IsAlive) return;

            int n = src.Stacks;
            var owner = src.Owner;
            var victim = attacker;

            // ★ 排队而非立即执行：否则 A 打 B、B 反伤 A、A 也有荆棘 → 无限递归
            ctx.EnqueueTrigger(() => victim.TakeDamage(ctx, new DamageInfo
            {
                Amount = n,
                Kind = DamageKind.Thorns,
                IgnoreBlock = false,
                Attacker = owner,
            }));
        }
    }
}
