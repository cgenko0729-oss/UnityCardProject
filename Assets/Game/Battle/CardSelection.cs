using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core;

namespace Game.Battle
{
    /// <summary>
    /// 选完牌之后对选中的牌做什么。
    /// ★ 刻意做成一个枚举而不是五个效果类：五种处置的「怎么选」完全一样，
    ///   只有最后一步不同，拆成五个类会把选牌请求的构造逻辑抄五遍。
    /// </summary>
    public enum CardSelectionAction
    {
        /// <summary>只选不动，由调用方自己处理选中的牌。</summary>
        None,
        Discard,
        Exhaust,
        /// <summary>本回合结束时不弃掉（临时获得「保留」关键字）。</summary>
        Retain,
        /// <summary>复制一张到手牌（复制品是临时卡，战斗结束消失）。</summary>
        Duplicate,
        ToDrawTop,
        ToDrawBottom,
        ToHand,
        ToDiscard,
    }

    /// <summary>一次选牌请求的全部参数。</summary>
    [Serializable]
    public struct CardSelectionRequest
    {
        /// <summary>从哪个牌堆里选。</summary>
        public CardPile Source;

        /// <summary>要选几张。</summary>
        public int Count;

        /// <summary>候选不足 Count 时是否允许少选。false 表示不足就一张都不选。</summary>
        public bool AllowFewer;

        /// <summary>是否允许玩家一张都不选直接跳过。</summary>
        public bool Cancellable;

        public CardSelectionAction Action;

        /// <summary>给玩家看的标题。为空时按 Action 自动生成。</summary>
        public string Prompt;
    }

    /// <summary>
    /// 选牌策略。UI 之外的一切场合（EditMode 测试、自动模拟器、敌人回合）都靠它
    /// **同步**给出答案，于是整套结算不需要挂起，89 个既有测试一个字都不用改。
    /// </summary>
    public interface ICardSelector
    {
        void Select(BattleContext ctx, in CardSelectionRequest req,
                    IReadOnlyList<CardInstance> candidates, List<CardInstance> result);
    }

    /// <summary>默认策略：随机选。走 <see cref="RngStream.CardEffect"/>，保证同种子同结果。</summary>
    public sealed class RandomCardSelector : ICardSelector
    {
        private readonly List<CardInstance> _pool = new List<CardInstance>(16);

        public void Select(BattleContext ctx, in CardSelectionRequest req,
                           IReadOnlyList<CardInstance> candidates, List<CardInstance> result)
        {
            result.Clear();
            if (candidates == null || candidates.Count == 0) return;

            _pool.Clear();
            for (int i = 0; i < candidates.Count; i++) _pool.Add(candidates[i]);

            int n = Math.Min(req.Count, _pool.Count);
            for (int k = 0; k < n; k++)
            {
                int idx = ctx.Rng.Range(RngStream.CardEffect, 0, _pool.Count);
                result.Add(_pool[idx]);
                _pool.RemoveAt(idx);
            }
            _pool.Clear();
        }
    }

    /// <summary>一个正在等待玩家作答的选牌请求。UI 看到它非 null 就弹面板。</summary>
    public sealed class PendingCardSelection
    {
        public CardSelectionRequest Request;

        /// <summary>可供选择的牌。UI 直接拿去建面板。</summary>
        public readonly List<CardInstance> Candidates = new List<CardInstance>(16);

        /// <summary>实际要选几张（已按候选数量钳过）。</summary>
        public int PickCount;

        /// <summary>玩家作答后要跑的回调。★ 由 <see cref="BattleContext.ResolveSelection"/> 统一调用。</summary>
        public Action<List<CardInstance>> OnResolved;

        public string Title
        {
            get
            {
                if (!string.IsNullOrEmpty(Request.Prompt)) return Request.Prompt;
                string verb = Request.Action switch
                {
                    CardSelectionAction.Discard => "弃掉",
                    CardSelectionAction.Exhaust => "消耗",
                    CardSelectionAction.Retain => "保留",
                    CardSelectionAction.Duplicate => "复制",
                    CardSelectionAction.ToDrawTop => "放回牌堆顶",
                    CardSelectionAction.ToDrawBottom => "放回牌堆底",
                    CardSelectionAction.ToHand => "拿回手牌",
                    CardSelectionAction.ToDiscard => "放进弃牌堆",
                    _ => "选择",
                };
                return $"选择 {PickCount} 张牌{verb}";
            }
        }
    }

    /// <summary>把 <see cref="CardSelectionAction"/> 真正作用到选中的牌上。</summary>
    public static class CardSelectionOps
    {
        public static void Apply(BattleContext ctx, CardSelectionAction action, List<CardInstance> cards)
        {
            if (ctx == null || cards == null || cards.Count == 0) return;
            var deck = ctx.Deck;
            if (deck == null) return;

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null) continue;

                switch (action)
                {
                    case CardSelectionAction.None:
                        break;

                    case CardSelectionAction.Discard:
                        deck.Discard(card);
                        break;

                    case CardSelectionAction.Exhaust:
                        deck.Exhaust(card);
                        break;

                    case CardSelectionAction.Retain:
                        // 「保留」只是给这张牌临时挂个关键字，回合结束的弃牌逻辑会读它。
                        // CardInstance.OnBattleEnd 会清掉 ExtraKeywords，不会渗到下一场。
                        card.ExtraKeywords |= CardKeyword.Retain;
                        ctx.Post(BattleEventType.CardRetained, 0, card.Uid, 0, card.Id);
                        break;

                    case CardSelectionAction.Duplicate:
                    {
                        // ★ Uid 必须从 RunContext 取，不能自己 ++：读档后会撞号。
                        if (ctx.Run == null) break;
                        var copy = card.Clone(ctx.Run.NextCardUid());
                        deck.AddCard(copy, CardPile.Hand);
                        break;
                    }

                    case CardSelectionAction.ToDrawTop:
                        MoveOutOfPiles(deck, card);
                        deck.AddCard(card, CardPile.Draw, toTop: true);
                        break;

                    case CardSelectionAction.ToDrawBottom:
                        MoveOutOfPiles(deck, card);
                        deck.AddCard(card, CardPile.Draw, toTop: false);
                        break;

                    case CardSelectionAction.ToHand:
                        MoveOutOfPiles(deck, card);
                        deck.AddCard(card, CardPile.Hand);
                        break;

                    case CardSelectionAction.ToDiscard:
                        MoveOutOfPiles(deck, card);
                        deck.AddCard(card, CardPile.Discard);
                        break;
                }
            }
        }

        /// <summary>
        /// 把一张牌从它现在所在的堆里摘出来。
        /// ★ 移动类处置必须先摘再放，否则同一张牌会同时存在于两个堆里，
        ///   <c>DeckController.TotalCards</c> 的牌数守恒断言会当场失败。
        /// </summary>
        private static void MoveOutOfPiles(DeckController deck, CardInstance card)
        {
            deck.Hand.Remove(card);
            deck.DrawPile.Remove(card);
            deck.DiscardPile.Remove(card);
            deck.ExhaustPile.Remove(card);
        }
    }
}
