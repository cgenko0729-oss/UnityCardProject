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

            // ★ 「把目标带回父级」必须写成 onComplete 回调，不能写在 ResolveAll 的下一行：
            //   子效果里若有选牌，ResolveAll 会挂起并立刻返回，下一行就会在子效果
            //   真正跑完之前先执行，带回一组还没产生的目标。
            EffectResolver.ResolveAll(Condition.Test(ctx, target) ? Then : Else, child, () =>
            {
                if (child.LastTargets.Count > 0)
                {
                    ctx.Targets.Clear();
                    ctx.Targets.AddRange(child.LastTargets);
                }
            });
        }
    }
}
