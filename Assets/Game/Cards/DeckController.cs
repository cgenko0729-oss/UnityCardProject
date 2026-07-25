using System.Collections.Generic;
using Game.Battle;
using Game.Core;

namespace Game.Cards
{
    /// <summary>四个牌堆 + 抽 / 洗 / 弃 / 消耗。不直接改单位属性。</summary>
    public class DeckController
    {
        public readonly List<CardInstance> DrawPile = new List<CardInstance>(32);
        public readonly List<CardInstance> Hand = new List<CardInstance>(12);
        public readonly List<CardInstance> DiscardPile = new List<CardInstance>(32);
        public readonly List<CardInstance> ExhaustPile = new List<CardInstance>(16);

        public int MaxHandSize = 10;

        private BattleContext _ctx;

        public void Init(BattleContext ctx, IReadOnlyList<CardInstance> deck)
        {
            _ctx = ctx;
            DrawPile.Clear();
            Hand.Clear();
            DiscardPile.Clear();
            ExhaustPile.Clear();

            // 固有牌开局必在手：先抽牌堆洗好，再把固有牌插到牌堆顶
            var innate = new List<CardInstance>();
            for (int i = 0; i < deck.Count; i++)
            {
                var c = deck[i];
                if (c.HasKeyword(CardKeyword.Innate)) innate.Add(c);
                else DrawPile.Add(c);
            }

            ctx.Rng.Shuffle(RngStream.CardDraw, DrawPile);

            // 牌堆顶 = 列表末尾
            for (int i = 0; i < innate.Count; i++) DrawPile.Add(innate[i]);

            ctx.Post(BattleEventType.DeckShuffled);
        }

        // ===================================================== 抽牌

        public int Draw(int count)
        {
            int drawn = 0;
            for (int i = 0; i < count; i++)
                if (DrawOne()) drawn++;
            return drawn;
        }

        public bool DrawOne()
        {
            if (Hand.Count >= MaxHandSize) return false;

            if (DrawPile.Count == 0)
            {
                if (DiscardPile.Count == 0) return false;
                ReshuffleDiscardIntoDraw();
            }

            int last = DrawPile.Count - 1;
            var card = DrawPile[last];
            DrawPile.RemoveAt(last);
            Hand.Add(card);

            _ctx.Post(BattleEventType.CardDrawn, 0, card.Uid, 0, card.Id);

            using (var hooks = _ctx.Collect<ICardPlayHook>())
            {
                for (int i = 0; i < hooks.Count; i++)
                    hooks[i].Impl.OnCardDrawn(_ctx, hooks[i].Src, card);
            }
            return true;
        }

        public void ReshuffleDiscardIntoDraw()
        {
            DrawPile.AddRange(DiscardPile);
            DiscardPile.Clear();
            _ctx.Rng.Shuffle(RngStream.CardDraw, DrawPile);
            _ctx.Post(BattleEventType.DeckShuffled);
        }

        // ===================================================== 弃牌 / 消耗

        public void Discard(CardInstance card, bool fromHand = true)
        {
            if (card == null) return;
            if (fromHand && !Hand.Remove(card)) return;

            DiscardPile.Add(card);
            _ctx.Post(BattleEventType.CardDiscarded, 0, card.Uid, 0, card.Id);

            using (var hooks = _ctx.Collect<ICardPlayHook>())
            {
                for (int i = 0; i < hooks.Count; i++)
                    hooks[i].Impl.OnCardDiscarded(_ctx, hooks[i].Src, card);
            }
        }

        public void Exhaust(CardInstance card)
        {
            if (card == null) return;
            Hand.Remove(card);
            DrawPile.Remove(card);
            DiscardPile.Remove(card);
            ExhaustPile.Add(card);
            _ctx.Post(BattleEventType.CardExhausted, 0, card.Uid, 0, card.Id);
        }

        /// <summary>回合结束的手牌清理：虚无牌消耗，保留牌留下，其余弃掉。</summary>
        public void EndTurnDiscard()
        {
            for (int i = Hand.Count - 1; i >= 0; i--)
            {
                var c = Hand[i];
                c.OnTurnEnd();

                if (c.HasKeyword(CardKeyword.Ethereal)) { Exhaust(c); continue; }
                if (c.HasKeyword(CardKeyword.Retain)) continue;
                Discard(c);
            }
        }

        private readonly List<CardInstance> _endOfTurnBuffer = new List<CardInstance>(8);

        /// <summary>
        /// 结算所有「回合结束时仍在手牌」的效果（灼烧、疑虑一类）。
        ///
        /// ★ 由 <c>BattleController.EndTurn</c> 在**状态衰减之后、清理手牌之前**调用。
        ///   两个时机都不能挪：
        ///   - 放在衰减之前，「疑虑」施加的 1 层虚弱会当场被同一次衰减扣掉，等于没上；
        ///   - 放在清理手牌之后，牌已经离手，「仍在手牌」的判定就永远为假。
        ///
        /// 先摘快照再执行，理由同 <c>BattleContext.FireDelayed</c>：效果会改动手牌。
        /// </summary>
        public void FireInHandEndOfTurnEffects()
        {
            _endOfTurnBuffer.Clear();
            for (int i = 0; i < Hand.Count; i++)
                if (Hand[i].Def != null && Hand[i].Def.HasInHandEndOfTurnEffects)
                    _endOfTurnBuffer.Add(Hand[i]);

            if (_endOfTurnBuffer.Count == 0) return;

            for (int i = 0; i < _endOfTurnBuffer.Count; i++)
            {
                var card = _endOfTurnBuffer[i];

                var ectx = new Game.Effects.EffectContext();
                ectx.Reset(_ctx, _ctx.Player, null, card);
                ectx.PreviewMode = false;

                Game.Effects.EffectResolver.ResolveAll(card.Def.InHandEndOfTurnEffects, ectx);
                _ctx.RunTriggerQueue();

                if (_ctx.BattleEnded) break;
            }

            _endOfTurnBuffer.Clear();
        }

        // ===================================================== 加牌

        public void AddCard(CardInstance card, CardPile pile, bool toTop = true)
        {
            if (card == null || pile == CardPile.None) return;

            switch (pile)
            {
                case CardPile.Hand:
                    // 手牌已满时牌会掉进弃牌堆。★ 必须如实上报最终落点，
                    // 否则表现层会显示「进手牌」而玩家看不到牌，无从理解发生了什么。
                    if (Hand.Count >= MaxHandSize)
                    {
                        DiscardPile.Add(card);
                        _ctx.Post(BattleEventType.CardAdded, 0, card.Uid, (int)CardPile.Discard, card.Id);
                        _ctx.Post(BattleEventType.Message, 0, card.Uid, 0, "HandFull");
                        return;
                    }
                    Hand.Add(card);
                    break;

                case CardPile.Draw:
                    if (toTop) DrawPile.Add(card);
                    else DrawPile.Insert(_ctx.Rng.Range(RngStream.CardDraw, 0, DrawPile.Count + 1), card);
                    break;

                case CardPile.Discard:
                    DiscardPile.Add(card);
                    break;

                case CardPile.Exhaust:
                    ExhaustPile.Add(card);
                    break;
            }

            _ctx.Post(BattleEventType.CardAdded, 0, card.Uid, (int)pile, card.Id);
        }

        public CardInstance FindInHand(string cardId)
        {
            for (int i = 0; i < Hand.Count; i++)
                if (Hand[i].Id == cardId) return Hand[i];
            return null;
        }

        public int TotalCards => DrawPile.Count + Hand.Count + DiscardPile.Count + ExhaustPile.Count;
    }
}
