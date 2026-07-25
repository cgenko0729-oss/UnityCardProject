using System;

namespace Game.Effects.Impl
{
    /// <summary>抽牌。</summary>
    [Serializable]
    public class DrawEffect : CardEffect
    {
        public EffectValue Count = EffectValue.Flat(1);

        public DrawEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        /// <summary>手牌已满，或抽牌堆和弃牌堆都空时不可打出（借鉴 Monster Train 的 TestEffect）。</summary>
        public override bool CanApply(EffectContext ctx)
        {
            var d = ctx.Battle?.Deck;
            if (d == null) return false;
            return d.Hand.Count < d.MaxHandSize && (d.DrawPile.Count + d.DiscardPile.Count) > 0;
        }

        public override void Apply(EffectContext ctx)
        {
            ctx.Battle.Deck.Draw(Count.Evaluate(ctx, ctx.Source));
        }

        public override string Describe(EffectContext ctx) => Count.Evaluate(ctx, ctx.Source).ToString();
    }
}
