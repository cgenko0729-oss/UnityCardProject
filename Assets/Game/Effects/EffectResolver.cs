using System;
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

        /// <summary>
        /// 结算一组效果。
        ///
        /// <para><paramref name="onComplete"/> 在这一组**全部**跑完之后调用。
        /// ★ 结算可能中途挂起等玩家选牌，所以「跑完之后要做的事」不能写在调用点的下一行，
        ///   必须交给这个回调——下一行在挂起时会被提前执行，那正是这套改造要消灭的 bug。</para>
        /// </summary>
        public static void ResolveAll(IReadOnlyList<CardEffect> effects, EffectContext ctx,
                                      Action onComplete = null)
        {
            var stack = ctx?.Battle?.Resolution;
            if (stack == null)
            {
                // 没有战斗上下文（描述预览等场合）时退回最朴素的顺序执行。
                // 这条路径永远不会挂起，因为挂起需要 BattleContext 来承载请求。
                if (effects != null)
                    for (int i = 0; i < effects.Count; i++) Step(effects[i], ctx);
                onComplete?.Invoke();
                return;
            }

            stack.Push(effects, ctx, onComplete);
            stack.Pump();
        }

        /// <summary>结算单个效果（组合子用）。语义等价于只有一个元素的 ResolveAll。</summary>
        public static void Resolve(CardEffect effect, EffectContext ctx)
        {
            if (effect == null) return;
            var stack = ctx?.Battle?.Resolution;
            if (stack == null) { Step(effect, ctx); return; }

            stack.Push(new[] { effect }, ctx);
            stack.Pump();
        }

        /// <summary>
        /// 执行一个效果。★ 只由 <see cref="EffectResolutionStack.Pump"/> 调用，
        /// 外部一律走 Resolve / ResolveAll。
        /// </summary>
        internal static void Step(CardEffect effect, EffectContext ctx)
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
