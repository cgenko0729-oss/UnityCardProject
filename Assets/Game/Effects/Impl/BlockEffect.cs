using System;

namespace Game.Effects.Impl
{
    /// <summary>获得护甲。</summary>
    [Serializable]
    public class BlockEffect : CardEffect
    {
        public EffectValue Amount = EffectValue.Flat(5);

        public BlockEffect()
        {
            Target = TargetSelector.SelfOnly;
        }

        public override void Apply(EffectContext ctx)
        {
            for (int i = 0; i < ctx.Targets.Count; i++)
                ctx.Targets[i].AddBlock(ctx.Battle, Amount.Evaluate(ctx, ctx.Targets[i]));
        }

        public override string Describe(EffectContext ctx) => Amount.Evaluate(ctx, ctx.Source).ToString();
    }
}
