using System;
using Game.Battle;
using Game.Units;
using UnityEngine;

namespace Game.Statuses.Impl
{
    // 阶段 4 新增的两个「改变规则」的状态。它们演示了新加的 IStatusHook / IFatalHook 两个拦截点。
    //
    // ★ 为什么这两个做成「状态」而不是直接做成遗物行为：
    //   它们都需要**每场战斗独立的计数**（挡了几次、复活过没有）。行为类禁止有可变字段，
    //   而 StatusInstance.Stacks 天生就是每场战斗独立的可变存储。
    //   于是遗物只负责「战斗开始时给玩家挂一层这个状态」，计数交给状态自己管。

    /// <summary>
    /// 神器：抵消接下来 {stacks} 次施加到自己身上的减益，每抵消一次消耗一层。
    /// </summary>
    [Serializable]
    public class ArtifactBehaviour : StatusBehaviour, IStatusHook
    {
        // 要早于其它 IStatusHook 生效：抵消掉了就不该再让别的 Hook 看到这次施加
        public override int Order => HookOrder.Early;

        public void ModifyStatusApply(BattleContext ctx, in HookSource src,
                                      StatusDefinition def, BattleUnit target, BattleUnit applier, ref int stacks)
        {
            if (def == null || src.Status == null) return;
            if (def.Polarity != StatusPolarity.Debuff) return;   // 只挡减益
            if (stacks <= 0) return;                             // 驱散/减层不算减益
            if (src.Status.Stacks <= 0) return;

            // 自己给自己上的减益不挡（例如某些代价型卡牌），否则玩家会莫名其妙地少了代价
            if (applier == target) return;

            stacks = 0;
            ctx.Post(BattleEventType.StatusTriggered, target.Uid, target.Uid, 1, "artifact");
            target.ConsumeStatusStack(ctx, src.Status);
        }

        public void OnStatusApplied(BattleContext ctx, in HookSource src,
                                    StatusDefinition def, BattleUnit target, int stacks) { }
    }

    /// <summary>
    /// 回光：受到致死伤害时不会死，改为保留 1 点生命，然后消耗一层。
    /// </summary>
    [Serializable]
    public class ReviveBehaviour : StatusBehaviour, IFatalHook
    {
        [Tooltip("免死之后额外回复的生命值")]
        public int HealAfter = 0;

        public override int Order => HookOrder.Late;

        public void OnLethalDamage(BattleContext ctx, in HookSource src, in DamageInfo dmg,
                                   ref int lethalDamage, ref bool prevent)
        {
            if (prevent) return;                       // 已经被别的 Hook 挡下了
            if (src.Status == null || src.Status.Stacks <= 0) return;

            prevent = true;

            var owner = src.Owner;
            ctx.Post(BattleEventType.StatusTriggered, owner.Uid, owner.Uid, 1, src.Status.Id);
            owner.ConsumeStatusStack(ctx, src.Status);

            // ★ 回血必须排队：此刻还在 TakeDamage 的管线中间，Hp 尚未扣除，
            //   直接 Heal 会被随后的扣血覆盖掉。
            if (HealAfter > 0)
            {
                int amount = HealAfter;
                ctx.EnqueueTrigger(() => owner.Heal(ctx, amount));
            }
        }
    }

    /// <summary>
    /// 每回合开始时给自己叠加另一个状态。「恶魔形态：每回合 +2 力量」用它。
    /// ★ 层数按本状态的层数缩放，所以打两张恶魔形态就是每回合 +4。
    /// </summary>
    [Serializable]
    public class TurnStartGrantStatusBehaviour : StatusBehaviour, ITurnHook
    {
        public StatusDefinition Status;

        [Tooltip("每一层本状态，每回合叠加多少层目标状态")]
        public int StacksPerStack = 1;

        public override int Order => HookOrder.Early;

        public void OnTurnStart(BattleContext ctx, in HookSource src)
        {
            var owner = src.Owner;
            if (Status == null || owner == null || !owner.IsAlive || src.Stacks <= 0) return;

            owner.AddStatus(ctx, Status, StacksPerStack * src.Stacks, owner);
        }

        public void OnTurnEnd(BattleContext ctx, in HookSource src) { }
    }

    /// <summary>
    /// 再生：每回合结束回复 {stacks} 点生命。用来演示「正面版的中毒」。
    /// </summary>
    [Serializable]
    public class RegenerateBehaviour : StatusBehaviour, ITurnHook
    {
        public override int Order => HookOrder.AddFlat;

        public void OnTurnStart(BattleContext ctx, in HookSource src) { }

        public void OnTurnEnd(BattleContext ctx, in HookSource src)
        {
            var owner = src.Owner;
            if (owner == null || !owner.IsAlive || src.Stacks <= 0) return;

            ctx.Post(BattleEventType.StatusTriggered, owner.Uid, owner.Uid, src.Stacks, "regenerate");
            owner.Heal(ctx, src.Stacks);
        }
    }
}
