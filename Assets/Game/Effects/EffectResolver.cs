using System.Collections.Generic;

namespace Game.Effects
{
    /// <summary>效果结算入口。卡牌、敌人行动、遗物、状态全部走这里。</summary>
    public static class EffectResolver
    {
        public const int MaxDepth = 8;

        public static bool CanApplyAll(IReadOnlyList<CardEffect> effects, EffectContext ctx)
        {
            if (effects == null) return true;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] == null) continue;
                // CanApply 需要目标信息，先解析一次
                TargetResolver.Resolve(effects[i].Target, ctx, ctx.Targets);
                if (!effects[i].CanApply(ctx)) return false;
            }
            ctx.Targets.Clear();
            return true;
        }

        public static void ResolveAll(IReadOnlyList<CardEffect> effects, EffectContext ctx)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
                Resolve(effects[i], ctx);
        }

        public static void Resolve(CardEffect effect, EffectContext ctx)
        {
            if (effect == null) return;

            if (ctx.Depth > MaxDepth)
            {
                UnityEngine.Debug.LogWarning(
                    $"[EffectResolver] 递归深度超过 {MaxDepth}，已中断：{effect.GetType().Name}");
                return;
            }

            TargetResolver.Resolve(effect.Target, ctx, ctx.Targets);

            if (!effect.CanApply(ctx)) return;

            effect.Apply(ctx);
            ctx.CarryTargetsForward();

            // 效果可能造成死亡 / 连锁触发，在这里统一消费，避免递归。
            ctx.Battle?.RunTriggerQueue();
        }
    }
}
