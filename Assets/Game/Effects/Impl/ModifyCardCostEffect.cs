using System;
using Game.Cards;
using Game.Core;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>修改卡牌费用。</summary>
    [Serializable]
    public class ModifyCardCostEffect : CardEffect
    {
        public enum Scope { AllInHand, RandomInHand, SelfCard }

        public Scope Where = Scope.AllInHand;
        public EffectValue Delta = EffectValue.Flat(-1);

        [Tooltip("true = 只持续本回合；false = 持续整场战斗")]
        public bool ThisTurnOnly = true;

        public ModifyCardCostEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override void Apply(EffectContext ctx)
        {
            int d = Delta.Evaluate(ctx, ctx.Source);
            if (d == 0) return;

            var deck = ctx.Battle?.Deck;
            if (deck == null) return;
            var hand = deck.Hand;

            switch (Where)
            {
                case Scope.SelfCard:
                    if (ctx.Card != null) ApplyTo(ctx.Card, d);
                    break;

                case Scope.AllInHand:
                    for (int i = 0; i < hand.Count; i++) ApplyTo(hand[i], d);
                    break;

                case Scope.RandomInHand:
                    if (hand.Count > 0)
                        ApplyTo(hand[ctx.Battle.Rng.Range(RngStream.CardEffect, 0, hand.Count)], d);
                    break;
            }
        }

        private void ApplyTo(CardInstance c, int d)
        {
            if (ThisTurnOnly) c.TurnCostDelta += d;
            else c.BattleCostDelta += d;
        }

        public override string Describe(EffectContext ctx) => Math.Abs(Delta.Evaluate(ctx, ctx.Source)).ToString();
    }
}
