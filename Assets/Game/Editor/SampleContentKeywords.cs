using System.Collections.Generic;
using Game.Cards;
using Game.Effects;
using Game.Effects.Impl;
using Game.Statuses;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>
    /// 关键字卡：Retain / Innate / Ethereal。
    ///
    /// ★ 这三个关键字在 <c>DeckController</c> 里从阶段 2 起就完整实现了
    ///   （固有牌插牌堆顶、虚无牌回合结束自我消耗、保留牌不弃），
    ///   `CardView` 也一直能显示它们——但 24 张示例卡里只用过 Exhaust，
    ///   三个关键字实际上是死代码。这批 8 张把它们变成真正的构筑选项。
    ///
    /// <para>设计上刻意让每个关键字都有**明确的取舍**，而不是白送的好处：
    ///   Retain   → 数值偏弱或情境化，价值在于「攒到需要的那个回合」
    ///   Innate   → 强，但占掉开局手牌一格，抽牌节奏被固定
    ///   Ethereal → 数值明显超模，代价是这回合不用掉就永远没了</para>
    /// </summary>
    internal static class SampleContentKeywords
    {
        internal static void CreateKeywordCards(Dictionary<string, StatusDefinition> statuses,
                                                Dictionary<string, CardDefinition> cards)
        {
            // ============================================================ 保留（Retain）

            // 0 费小攻击。单独看毫无价值，但可以一路攒到「爆发回合」一次全打出去。
            var spareBladePlus = Make(cards, "spare_blade_plus", "备用刀刃+", 0,
                CardType.Attack, CardTargetKind.SingleEnemy, CardKeyword.Retain, CardRarity.Special,
                "造成 {0} 点伤害。保留。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(8) });

            var spareBlade = Make(cards, "spare_blade", "备用刀刃", 0,
                CardType.Attack, CardTargetKind.SingleEnemy, CardKeyword.Retain, CardRarity.Common,
                "造成 {0} 点伤害。保留。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(5) });
            spareBlade.UpgradedVersion = spareBladePlus;
            EditorUtility.SetDirty(spareBlade);

            // 护甲牌带保留：可以提前握着，等看到 Boss 的大招意图再放。
            var composurePlus = Make(cards, "composure_plus", "静心+", 1,
                CardType.Skill, CardTargetKind.None, CardKeyword.Retain, CardRarity.Special,
                "获得 {0} 点护甲。保留。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(11) });

            var composure = Make(cards, "composure", "静心", 1,
                CardType.Skill, CardTargetKind.None, CardKeyword.Retain, CardRarity.Common,
                "获得 {0} 点护甲。保留。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(8) });
            composure.UpgradedVersion = composurePlus;
            EditorUtility.SetDirty(composure);

            // 情境牌的典型：手牌越多打得越疼，所以需要留到攒够手牌的那一回合。
            var ambushPlus = Make(cards, "ambush_plus", "伏击+", 2,
                CardType.Attack, CardTargetKind.SingleEnemy, CardKeyword.Retain, CardRarity.Special,
                "每有一张手牌造成 {0} 点伤害。保留。",
                new DamageEffect
                {
                    Target = TargetSelector.Chosen,
                    Amount = new EffectValue { Base = 0, Scale = ValueScale.PerCardInHand, PerUnit = 5 },
                });

            var ambush = Make(cards, "ambush", "伏击", 2,
                CardType.Attack, CardTargetKind.SingleEnemy, CardKeyword.Retain, CardRarity.Uncommon,
                "每有一张手牌造成 {0} 点伤害。保留。",
                new DamageEffect
                {
                    Target = TargetSelector.Chosen,
                    Amount = new EffectValue { Base = 0, Scale = ValueScale.PerCardInHand, PerUnit = 3 },
                });
            ambush.UpgradedVersion = ambushPlus;
            EditorUtility.SetDirty(ambush);

            // ============================================================ 固有（Innate）

            // 开局必在手的起手牌。消耗，所以只影响第一回合的节奏，不会一直占位。
            var opening = Make(cards, "opening", "先手", 0,
                CardType.Skill, CardTargetKind.None,
                CardKeyword.Innate | CardKeyword.Exhaust, CardRarity.Common,
                "抽 {0} 张牌。固有。消耗。",
                new DrawEffect { Count = EffectValue.Flat(2) });

            // 能力牌带固有：能力牌越早打出收益越高，固有正好解决「抽不到」的问题。
            var fervor = Make(cards, "battle_fervor", "战意", 1,
                CardType.Power, CardTargetKind.None, CardKeyword.Innate, CardRarity.Rare,
                "获得 {0} 层力量。固有。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = statuses["strength"],
                    Stacks = EffectValue.Flat(2),
                });

            // ★ 固有的代价必须真实存在：这张 2 费护甲牌开局一定在手，
            //   意味着第一回合的 3 点能量里有 2 点是被它「预定」的。
            var vigil = Make(cards, "vigil", "守夜", 2,
                CardType.Skill, CardTargetKind.None, CardKeyword.Innate, CardRarity.Uncommon,
                "获得 {0} 点护甲。固有。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(12) });

            // ============================================================ 虚无（Ethereal）

            // 明显超模的 0 费攻击，代价是这回合不用就永远消失。
            var phantomPlus = Make(cards, "phantom_blade_plus", "幻影之刃+", 0,
                CardType.Attack, CardTargetKind.SingleEnemy, CardKeyword.Ethereal, CardRarity.Special,
                "造成 {0} 点伤害。虚无。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(16) });

            var phantom = Make(cards, "phantom_blade", "幻影之刃", 0,
                CardType.Attack, CardTargetKind.SingleEnemy, CardKeyword.Ethereal, CardRarity.Uncommon,
                "造成 {0} 点伤害。虚无。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(12) });
            phantom.UpgradedVersion = phantomPlus;
            EditorUtility.SetDirty(phantom);

            // 虚无 + 消耗：无论用不用，这张牌本场都只会出现一次。
            Make(cards, "fleeting_insight", "转瞬顿悟", 0,
                CardType.Skill, CardTargetKind.None,
                CardKeyword.Ethereal | CardKeyword.Exhaust, CardRarity.Uncommon,
                "抽 {0} 张牌，获得 {1} 点能量。虚无。消耗。",
                new DrawEffect { Count = EffectValue.Flat(3) },
                new EnergyEffect { Amount = EffectValue.Flat(1) });

            // 三个关键字同台：留得住、但留着会疼。
            Make(cards, "burden_of_proof", "举证之责", 1,
                CardType.Skill, CardTargetKind.None, CardKeyword.Retain, CardRarity.Rare,
                "获得 {0} 点护甲。保留。回合结束时若仍在手牌，失去 2 点生命。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(14) })
                .InHandEndOfTurnEffects = new List<CardEffect>
                {
                    new DamageEffect
                    {
                        Target = TargetSelector.SelfOnly,
                        Amount = EffectValue.Flat(2),
                        Kind = Battle.DamageKind.Loss,
                        IgnoreBlock = true,
                    }
                };

            _ = opening; _ = fervor; _ = vigil;
        }

        private static CardDefinition Make(Dictionary<string, CardDefinition> cards,
                                           string id, string name, int cost, CardType type,
                                           CardTargetKind targetKind, CardKeyword keywords,
                                           CardRarity rarity, string template,
                                           params CardEffect[] effects)
        {
            var so = SampleContentGenerator.LoadOrCreateAsset<CardDefinition>(
                $"Assets/GameData/Cards/Card_{SampleContentGenerator.Capitalize(id)}.asset");

            so.Id = id;
            so.DisplayName = name;
            so.Cost = cost;
            so.CostMode = CostMode.Fixed;
            so.Type = type;
            so.TargetKind = targetKind;
            so.Keywords = keywords;
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
