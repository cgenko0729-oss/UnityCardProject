using System;
using Game.Battle;
using Game.Cards;
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
            var battle = ctx.Battle;
            var deck = battle?.Deck;
            if (deck == null) return;

            if (DiscardMode == Mode.All)
            {
                for (int i = deck.Hand.Count - 1; i >= 0; i--) deck.Discard(deck.Hand[i]);
                return;
            }

            int n = Count.Evaluate(ctx, ctx.Source);
            if (n <= 0) return;

            if (DiscardMode == Mode.ChooseByPlayer)
            {
                if (ctx.PreviewMode) return;

                // 交给统一的选牌管线：有 UI 时挂起问玩家，无 UI 时当场随机作答。
                var req = new CardSelectionRequest
                {
                    Source = CardPile.Hand,
                    Count = n,
                    AllowFewer = true,
                    Action = CardSelectionAction.Discard,
                };
                battle.RequestCardSelection(req,
                    cards => CardSelectionOps.Apply(battle, CardSelectionAction.Discard, cards));
                return;
            }

            for (int k = 0; k < n && deck.Hand.Count > 0; k++)
            {
                int idx = battle.Rng.Range(RngStream.CardEffect, 0, deck.Hand.Count);
                deck.Discard(deck.Hand[idx]);
            }
        }

        public override string Describe(EffectContext ctx) => Count.Evaluate(ctx, ctx.Source).ToString();
    }
}
