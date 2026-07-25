using System;
using Game.Core;

namespace Game.Effects.Impl
{
    /// <summary>丢弃手牌。</summary>
    [Serializable]
    public class DiscardEffect : CardEffect
    {
        public enum Mode { Random, All, ChooseByPlayer }

        public Mode DiscardMode = Mode.Random;
        public EffectValue Count = EffectValue.Flat(1);

        public DiscardEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override void Apply(EffectContext ctx)
        {
            var deck = ctx.Battle?.Deck;
            if (deck == null) return;

            if (DiscardMode == Mode.All)
            {
                for (int i = deck.Hand.Count - 1; i >= 0; i--) deck.Discard(deck.Hand[i]);
                return;
            }

            // ChooseByPlayer 需要手牌选择 UI（阶段 4 接入）。在此之前按随机处理，保证逻辑始终可跑。
            int n = Count.Evaluate(ctx, ctx.Source);
            for (int k = 0; k < n && deck.Hand.Count > 0; k++)
            {
                int idx = ctx.Battle.Rng.Range(RngStream.CardEffect, 0, deck.Hand.Count);
                deck.Discard(deck.Hand[idx]);
            }
        }

        public override string Describe(EffectContext ctx) => Count.Evaluate(ctx, ctx.Source).ToString();
    }
}
