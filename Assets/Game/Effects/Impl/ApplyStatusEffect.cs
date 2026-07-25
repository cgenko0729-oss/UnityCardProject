using System;
using Game.Statuses;

namespace Game.Effects.Impl
{
    /// <summary>施加状态。Buff 和 Debuff 是同一个效果类，正负由 StatusDefinition 决定。</summary>
    [Serializable]
    public class ApplyStatusEffect : CardEffect
    {
        public StatusDefinition Status;
        public EffectValue Stacks = EffectValue.Flat(1);

        public override bool CanApply(EffectContext ctx) => Status != null;

        public override void Apply(EffectContext ctx)
        {
            int n = Stacks.Evaluate(ctx, ctx.Source);
            if (n == 0) return;

            for (int i = 0; i < ctx.Targets.Count; i++)
            {
                var t = ctx.Targets[i];
                if (t == null || !t.IsAlive) continue;
                t.AddStatus(ctx.Battle, Status, n, ctx.Source);
            }
        }

        public override string Describe(EffectContext ctx) => Stacks.Evaluate(ctx, ctx.Source).ToString();
    }
}
