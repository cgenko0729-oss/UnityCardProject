using System;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>获得或消耗能量。正数为获得，负数为消耗。</summary>
    [Serializable]
    public class EnergyEffect : CardEffect
    {
        [Tooltip("正数为获得能量，负数为消耗能量")]
        public EffectValue Amount = EffectValue.Flat(1);

        public EnergyEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override bool CanApply(EffectContext ctx)
        {
            if (ctx.Battle == null) return false;
            int a = Amount.Evaluate(ctx, ctx.Source);
            return a >= 0 || ctx.Battle.Energy >= -a;
        }

        public override void Apply(EffectContext ctx)
        {
            int a = Amount.Evaluate(ctx, ctx.Source);
            if (a >= 0) ctx.Battle.GainEnergy(a);
            else ctx.Battle.SpendEnergy(-a);
        }

        public override string Describe(EffectContext ctx) => Math.Abs(Amount.Evaluate(ctx, ctx.Source)).ToString();
    }
}
