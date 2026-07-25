using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>组合子：把一组效果重复执行 N 次。用于「连续效果」「重复执行」。</summary>
    [Serializable]
    public class RepeatEffect : CardEffect
    {
        public EffectValue Times = EffectValue.Flat(2);

        [SerializeReference]
        public List<CardEffect> Effects = new List<CardEffect>();

        public RepeatEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override bool CanApply(EffectContext ctx) => true;

        public override void Apply(EffectContext ctx)
        {
            int n = Times.Evaluate(ctx, ctx.Source);
            if (n <= 0) return;

            var child = ctx.Child();
            for (int i = 0; i < n; i++)
            {
                child.RepeatIndex = i;
                EffectResolver.ResolveAll(Effects, child);
            }
        }

        public override string Describe(EffectContext ctx) => Times.Evaluate(ctx, ctx.Source).ToString();
    }
}
