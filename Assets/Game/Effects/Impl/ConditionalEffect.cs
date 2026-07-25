using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>组合子：条件判断。满足条件跑 Then，否则跑 Else。</summary>
    [Serializable]
    public class ConditionalEffect : CardEffect
    {
        public EffectCondition Condition;

        [SerializeReference]
        public List<CardEffect> Then = new List<CardEffect>();

        [SerializeReference]
        public List<CardEffect> Else = new List<CardEffect>();

        public ConditionalEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        /// <summary>★ 条件效果永远「可施放」，否则条件不满足时会让整张卡不可打。</summary>
        public override bool CanApply(EffectContext ctx) => true;

        public override void Apply(EffectContext ctx)
        {
            var target = ctx.ChosenTarget;
            if (target == null && ctx.LastTargets.Count > 0) target = ctx.LastTargets[0];

            var child = ctx.Child();
            EffectResolver.ResolveAll(Condition.Test(ctx, target) ? Then : Else, child);

            // 把子上下文命中的目标带回父级，保证后续的 PreviousTargets 仍然有效
            if (child.LastTargets.Count > 0)
            {
                ctx.Targets.Clear();
                ctx.Targets.AddRange(child.LastTargets);
            }
        }
    }
}
