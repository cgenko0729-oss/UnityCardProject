using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core;
using Game.Localization;
using Game.Relics;
using UnityEngine;

namespace Game.RunEffects.Impl
{
    /// <summary>往牌库里加牌。可以指定具体的牌，也可以按稀有度随机。</summary>
    [Serializable]
    public class AddCardRunEffect : RunEffect
    {
        [Tooltip("指定要加的牌。留空则按 RandomRarity 随机抽")]
        public CardDefinition Card;

        [Tooltip("Card 为空时按这个稀有度随机。None 表示整个奖励池")]
        public CardRarity RandomRarity = CardRarity.Common;

        [Tooltip("Card 为空时是否忽略 RandomRarity，直接从整个奖励池抽")]
        public bool AnyRarity = true;

        public int Count = 1;

        [Tooltip("加入的牌是否已升级")]
        public bool Upgraded;

        [Tooltip("勾选后不直接给牌，而是弹面板让玩家从 Count 张候选里选一张")]
        public bool PlayerChooses;

        public override void Apply(RunEffectContext ctx)
        {
            if (Count <= 0) return;

            if (Card != null && !PlayerChooses)
            {
                for (int i = 0; i < Count; i++) AddOne(ctx, Card);
                ctx.AddLog(DescribeGainCard(Card, Count));
                return;
            }

            var picks = new List<CardDefinition>();
            if (Card != null) picks.Add(Card);
            else ContentPicker.PickCards(ctx.Rng, ctx.Db, RngStream.Event,
                                         Mathf.Max(1, Count), picks);

            if (picks.Count == 0) return;

            if (PlayerChooses)
            {
                ctx.RequestChoice(new RunChoiceRequest
                {
                    Kind = RunChoiceKind.AddOneOfCards,
                    Count = 1,
                    Options = picks,
                    Title = Loc.T("run.addcard.choose", "选择一张卡加入牌库"),
                });
                return;
            }

            for (int i = 0; i < picks.Count; i++)
            {
                AddOne(ctx, picks[i]);
                ctx.AddLog(DescribeGainCard(picks[i], 1));
            }
        }

        private static string DescribeGainCard(CardDefinition def, int count)
            => count > 1
                ? Loc.T("run.addcard.many", "获得 {0} 张「{1}」", count, def.LocalizedName)
                : Loc.T("run.addcard.one", "获得「{0}」", def.LocalizedName);

        private void AddOne(RunEffectContext ctx, CardDefinition def)
        {
            var inst = ctx.Run.AddCard(def);
            if (inst != null && Upgraded) inst.Upgrade();
        }

        public override string Describe(RunEffectContext ctx)
        {
            if (PlayerChooses) return Loc.T("run.addcard.choose", "选择一张卡加入牌库");
            if (Card != null) return DescribeGainCard(Card, Count);
            return Count > 1
                ? Loc.T("run.addcard.random_many", "随机获得 {0} 张卡", Count)
                : Loc.T("run.addcard.random_one", "随机获得一张卡");
        }
    }

    /// <summary>从牌库移除卡牌。默认让玩家自己选。</summary>
    [Serializable]
    public class RemoveCardRunEffect : RunEffect
    {
        public int Count = 1;

        [Tooltip("勾选则弹面板让玩家选；否则随机移除")]
        public bool PlayerChooses = true;

        public override bool CanApply(RunEffectContext ctx)
            => ctx.Run != null && ctx.Run.Deck.Count > Count;   // 不允许把牌库删空

        public override void Apply(RunEffectContext ctx)
        {
            if (Count <= 0) return;

            if (PlayerChooses)
            {
                ctx.RequestChoice(new RunChoiceRequest
                {
                    Kind = RunChoiceKind.RemoveCard,
                    Count = Count,
                    Title = DescribeRemove(Count),
                });
                return;
            }

            var picks = new List<CardInstance>();
            ContentPicker.PickFromDeck(ctx.Rng, ctx.Run, RngStream.Event, Count, picks);
            for (int i = 0; i < picks.Count; i++)
            {
                ctx.Run.RemoveCard(picks[i]);
                ctx.AddLog(Loc.T("run.removecard.done", "移除了「{0}」", picks[i].DisplayName));
            }
        }

        public override string Describe(RunEffectContext ctx) => DescribeRemove(Count);

        private static string DescribeRemove(int count)
            => count > 1
                ? Loc.T("run.removecard.many", "移除 {0} 张卡", count)
                : Loc.T("run.removecard.one", "移除一张卡");
    }

    /// <summary>升级牌库里的卡牌。</summary>
    [Serializable]
    public class UpgradeCardRunEffect : RunEffect
    {
        public int Count = 1;

        [Tooltip("勾选则弹面板让玩家选；否则随机升级")]
        public bool PlayerChooses = true;

        [Tooltip("勾选则升级牌库里所有可升级的牌（忽略 Count）")]
        public bool All;

        public override bool CanApply(RunEffectContext ctx)
        {
            if (ctx.Run == null) return false;
            for (int i = 0; i < ctx.Run.Deck.Count; i++)
                if (ctx.Run.Deck[i].CanUpgrade) return true;
            return false;
        }

        public override void Apply(RunEffectContext ctx)
        {
            if (All)
            {
                int n = 0;
                for (int i = 0; i < ctx.Run.Deck.Count; i++)
                    if (ctx.Run.Deck[i].CanUpgrade) { ctx.Run.Deck[i].Upgrade(); n++; }
                ctx.AddLog(Loc.T("run.upgradecard.done_many", "升级了 {0} 张卡", n));
                return;
            }

            if (Count <= 0) return;

            if (PlayerChooses)
            {
                ctx.RequestChoice(new RunChoiceRequest
                {
                    Kind = RunChoiceKind.UpgradeCard,
                    Count = Count,
                    Title = DescribeUpgrade(Count),
                });
                return;
            }

            var picks = new List<CardInstance>();
            ContentPicker.PickFromDeck(ctx.Rng, ctx.Run, RngStream.Event, Count, picks, c => c.CanUpgrade);
            for (int i = 0; i < picks.Count; i++)
            {
                picks[i].Upgrade();
                ctx.AddLog(Loc.T("run.upgradecard.done", "升级了「{0}」", picks[i].DisplayName));
            }
        }

        public override string Describe(RunEffectContext ctx)
        {
            if (All) return Loc.T("run.upgradecard.all", "升级牌库里所有的卡");
            return DescribeUpgrade(Count);
        }

        private static string DescribeUpgrade(int count)
            => count > 1
                ? Loc.T("run.upgradecard.many", "升级 {0} 张卡", count)
                : Loc.T("run.upgradecard.one", "升级一张卡");
    }

    /// <summary>获得遗物。可以指定具体遗物，也可以按稀有度随机。</summary>
    [Serializable]
    public class GainRelicRunEffect : RunEffect
    {
        [Tooltip("指定要给的遗物。留空则随机")]
        public RelicDefinition Relic;

        [Tooltip("Relic 为空时按这个稀有度随机")]
        public RelicRarity RandomRarity = RelicRarity.Common;

        [Tooltip("Relic 为空时是否忽略 RandomRarity，直接从整个掉落池抽")]
        public bool AnyRarity;

        public override void Apply(RunEffectContext ctx)
        {
            var def = Relic;
            if (def == null)
            {
                RelicRarity? rarity = AnyRarity ? (RelicRarity?)null : RandomRarity;
                def = ContentPicker.PickRelic(ctx.Rng, ctx.Db, RngStream.Reward, ctx.Run, rarity);
            }

            if (def == null)
            {
                // 遗物全都拿过了。给点金币兜底，总比什么都不给强。
                ctx.Run.Gold += 25;
                ctx.AddLog(Loc.T("run.gainrelic.exhausted", "没有可获得的遗物了，改为获得 {0} 金币", 25));
                return;
            }

            if (ctx.Run.AddRelic(def))
                ctx.AddLog(Loc.T("run.gainrelic.done", "获得遗物「{0}」", def.LocalizedName));
        }

        public override string Describe(RunEffectContext ctx)
            => Relic != null
                ? Loc.T("run.gainrelic.done", "获得遗物「{0}」", Relic.LocalizedName)
                : Loc.T("run.gainrelic.random", "获得一个随机遗物");
    }
}
