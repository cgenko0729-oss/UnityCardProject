using System;

namespace Game.Effects.Impl
{
    /// <summary>治疗。</summary>
    [Serializable]
    public class HealEffect : CardEffect
    {
        public EffectValue Amount = EffectValue.Flat(5);

        public HealEffect()
        {
            Target = TargetSelector.SelfOnly;
        }

        public override void Apply(EffectContext ctx)
        {
            for (int i = 0; i < ctx.Targets.Count; i++)
                ctx.Targets[i].Heal(ctx.Battle, Amount.Evaluate(ctx, ctx.Targets[i]));
        }

        public override string Describe(EffectContext ctx) => Amount.Evaluate(ctx, ctx.Source).ToString();
    }
}
