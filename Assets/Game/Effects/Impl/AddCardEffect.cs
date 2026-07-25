using System;
using Game.Cards;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>生成卡牌到指定牌堆。默认生成临时卡，战斗结束不会进牌库。</summary>
    [Serializable]
    public class AddCardEffect : CardEffect
    {
        public CardDefinition Card;
        public CardPile Pile = CardPile.Hand;
        public EffectValue Count = EffectValue.Flat(1);

        [Tooltip("true = 生成的卡战斗结束后消失，不进牌库")]
        public bool Temporary = true;

        [Tooltip("生成升级版")]
        public bool Upgraded;

        [Tooltip("复制来源卡而不是使用 Card 字段（用于「复制这张牌」）")]
        public bool CopySourceCard;

        public AddCardEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override bool CanApply(EffectContext ctx) => CopySourceCard ? ctx.Card != null : Card != null;

        public override void Apply(EffectContext ctx)
        {
            var run = ctx.Battle?.Run;
            if (run == null) return;

            int n = Count.Evaluate(ctx, ctx.Source);
            for (int i = 0; i < n; i++)
            {
                CardInstance inst = CopySourceCard
                    ? ctx.Card.Clone(run.NextCardUid(), Temporary)
                    : run.NewCard(Card, Temporary);

                if (Upgraded) inst.Upgrade();
                ctx.Battle.Deck.AddCard(inst, Pile);
            }
        }

        public override string Describe(EffectContext ctx) => Count.Evaluate(ctx, ctx.Source).ToString();
    }
}
