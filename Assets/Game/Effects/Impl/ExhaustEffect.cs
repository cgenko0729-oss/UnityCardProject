using System;
using Game.Cards;
using Game.Core;

namespace Game.Effects.Impl
{
    /// <summary>消耗卡牌。</summary>
    [Serializable]
    public class ExhaustEffect : CardEffect
    {
        public enum Mode
        {
            /// <summary>让当前这张卡结算完后进消耗堆。</summary>
            SelfCard,
            RandomInHand,
            AllInHand
        }

        public Mode ExhaustMode = Mode.SelfCard;
        public EffectValue Count = EffectValue.Flat(1);

        public ExhaustEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override void Apply(EffectContext ctx)
        {
            var deck = ctx.Battle?.Deck;
            if (deck == null) return;

            switch (ExhaustMode)
            {
                case Mode.SelfCard:
                    if (ctx.Card != null) ctx.Card.ExtraKeywords |= CardKeyword.Exhaust;
                    break;

                case Mode.AllInHand:
                    for (int i = deck.Hand.Count - 1; i >= 0; i--) deck.Exhaust(deck.Hand[i]);
                    break;

                case Mode.RandomInHand:
                {
                    int n = Count.Evaluate(ctx, ctx.Source);
                    for (int k = 0; k < n && deck.Hand.Count > 0; k++)
                    {
                        int idx = ctx.Battle.Rng.Range(RngStream.CardEffect, 0, deck.Hand.Count);
                        deck.Exhaust(deck.Hand[idx]);
                    }
                    break;
                }
            }
        }

        public override string Describe(EffectContext ctx) => Count.Evaluate(ctx, ctx.Source).ToString();
    }
}
