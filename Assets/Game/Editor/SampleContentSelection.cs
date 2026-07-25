using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Effects;
using Game.Effects.Impl;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>
    /// 战斗内选牌卡。
    ///
    /// ★ 选牌机制（<see cref="SelectCardsEffect"/> + 可挂起的结算栈）已经上线并有 17 个用例，
    ///   但 GameData 里一张卡都没用上——机制没有内容等于没有。这批 6 张把它接通。
    ///
    /// <para>六张卡刚好覆盖六种处置，也就是说：
    /// **五种「选牌怎么处置」全部只是同一个效果类的不同配置**，
    /// 没有 SelectDiscardEffect / SelectExhaustEffect 这类重复的类。</para>
    ///
    /// <para>设计上有一条统一原则：选牌是**代价**，后面跟着的才是收益。
    /// 因此 Cancellable 一律留 false——代价（能量）在出牌时已经付掉了，
    /// 允许跳过等于白拿收益。</para>
    /// </summary>
    internal static class SampleContentSelection
    {
        internal static void CreateSelectionCards(Dictionary<string, CardDefinition> cards)
        {
            // 弃牌换抽牌：最经典的「过牌」手段，把废牌换成新牌。
            var siftPlus = Make(cards, "sift_plus", "筛选+", 1, CardRarity.Special,
                "选择弃掉 {0} 张手牌，然后抽 {1} 张牌。",
                Select(CardPile.Hand, 1, CardSelectionAction.Discard),
                new DrawEffect { Count = EffectValue.Flat(3) });

            var sift = Make(cards, "sift", "筛选", 1, CardRarity.Common,
                "选择弃掉 {0} 张手牌，然后抽 {1} 张牌。",
                Select(CardPile.Hand, 1, CardSelectionAction.Discard),
                new DrawEffect { Count = EffectValue.Flat(2) });
            sift.UpgradedVersion = siftPlus;
            EditorUtility.SetDirty(sift);

            // 消耗：诅咒牌与状态牌唯一的战斗内解法。
            // ★ 上一批做了诅咒/状态牌，这张就是配套的答案——
            //   只加负面牌不给解法的话，那些牌只会让人烦而不是让人做决策。
            var purge = Make(cards, "purge", "净除", 0, CardRarity.Common,
                "选择消耗 {0} 张手牌。消耗。",
                Select(CardPile.Hand, 1, CardSelectionAction.Exhaust));
            purge.Keywords = CardKeyword.Exhaust;
            EditorUtility.SetDirty(purge);

            // 保留：把这回合用不掉的强牌留到下回合。
            Make(cards, "hold_fast", "把持", 0, CardRarity.Common,
                "选择 {0} 张手牌，本回合结束时保留它。抽 {1} 张牌。",
                Select(CardPile.Hand, 1, CardSelectionAction.Retain),
                new DrawEffect { Count = EffectValue.Flat(1) });

            // 复制：复制品是临时卡，本场战斗结束就消失，不会污染牌库。
            Make(cards, "transcribe", "映写", 1, CardRarity.Uncommon,
                "选择 {0} 张手牌，复制一份到手牌（本场战斗后消失）。",
                Select(CardPile.Hand, 1, CardSelectionAction.Duplicate));

            // 回牌堆顶：把关键牌塞回去，下回合第一张必定抽到。
            Make(cards, "stash", "藏牌", 0, CardRarity.Common,
                "选择 {0} 张手牌放回抽牌堆顶。抽 {1} 张牌。",
                Select(CardPile.Hand, 1, CardSelectionAction.ToDrawTop),
                new DrawEffect { Count = EffectValue.Flat(1) });

            // ★ 唯一一张从弃牌堆里选的卡：证明 Source 是可配的，
            //   「从消耗堆回收」「从抽牌堆挑」将来都只是改一个枚举。
            Make(cards, "recall", "回收", 2, CardRarity.Uncommon,
                "从弃牌堆中选择 {0} 张牌加入手牌。",
                Select(CardPile.Discard, 1, CardSelectionAction.ToHand));
        }

        private static SelectCardsEffect Select(CardPile source, int count, CardSelectionAction action)
            => new SelectCardsEffect
            {
                Source = source,
                Count = EffectValue.Flat(count),
                Action = action,
                AllowFewer = true,
                // 代价在出牌时已经付了，不给跳过——见类注释
                Cancellable = false,
            };

        private static CardDefinition Make(Dictionary<string, CardDefinition> cards,
                                           string id, string name, int cost, CardRarity rarity,
                                           string template, params CardEffect[] effects)
        {
            var so = SampleContentGenerator.LoadOrCreateAsset<CardDefinition>(
                $"Assets/GameData/Cards/Card_{SampleContentGenerator.Capitalize(id)}.asset");

            so.Id = id;
            so.DisplayName = name;
            so.Cost = cost;
            so.CostMode = CostMode.Fixed;
            so.Type = CardType.Skill;
            so.TargetKind = CardTargetKind.None;
            so.Keywords = CardKeyword.None;
            so.Rarity = rarity;
            so.DescriptionTemplate = template;
            so.Effects = new List<CardEffect>(effects);
            so.InHandEndOfTurnEffects = new List<CardEffect>();
            so.UpgradedVersion = null;

            EditorUtility.SetDirty(so);
            cards[id] = so;
            return so;
        }
    }
}
