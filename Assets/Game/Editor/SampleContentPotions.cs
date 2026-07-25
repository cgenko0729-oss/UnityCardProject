using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Effects;
using Game.Effects.Impl;
using Game.Potions;
using Game.Statuses;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>
    /// 药水示例内容。
    ///
    /// ★ 这一批的意义不只是内容：它同时证明**药水没有引入任何新的效果类**。
    ///   下面 10 瓶药水用的全部是卡牌已有的效果（伤害 / 护甲 / 抽牌 / 能量 / 状态 / 选牌 / 组合子），
    ///   `EffectResolver` 一行没改。如果哪天有人要为药水新写一个效果类，
    ///   那多半意味着这个效果本来就该给卡牌也用上。
    /// </summary>
    internal static class SampleContentPotions
    {
        internal static void CreatePotions(string dir,
                                           Dictionary<string, StatusDefinition> statuses,
                                           Dictionary<string, PotionDefinition> output)
        {
            // ---------------------------------------------------------- 普通
            Make(dir, output, "healing", "治疗药水", PotionRarity.Common,
                "回复 {0} 点生命。", CardTargetKind.None,
                new HealEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(18) });

            Make(dir, output, "block", "铁壁药水", PotionRarity.Common,
                "获得 {0} 点护甲。", CardTargetKind.None,
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(12) });

            Make(dir, output, "energy", "活力药水", PotionRarity.Common,
                "获得 {0} 点能量。", CardTargetKind.None,
                new EnergyEffect { Amount = EffectValue.Flat(2) });

            Make(dir, output, "draw", "灵感药水", PotionRarity.Common,
                "抽 {0} 张牌。", CardTargetKind.None,
                new DrawEffect { Count = EffectValue.Flat(3) });

            Make(dir, output, "fire", "火焰药水", PotionRarity.Common,
                "对一个敌人造成 {0} 点伤害。", CardTargetKind.SingleEnemy,
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(20) });

            // ---------------------------------------------------------- 罕见
            Make(dir, output, "weak", "削弱药水", PotionRarity.Uncommon,
                "对一个敌人施加 {0} 层虚弱。", CardTargetKind.SingleEnemy,
                new ApplyStatusEffect
                {
                    Target = TargetSelector.Chosen,
                    Status = statuses["weak"],
                    Stacks = EffectValue.Flat(3),
                });

            Make(dir, output, "poison", "剧毒药水", PotionRarity.Uncommon,
                "对一个敌人施加 {0} 层中毒。", CardTargetKind.SingleEnemy,
                new ApplyStatusEffect
                {
                    Target = TargetSelector.Chosen,
                    Status = statuses["poison"],
                    Stacks = EffectValue.Flat(6),
                });

            Make(dir, output, "strength", "力量药水", PotionRarity.Uncommon,
                "获得 {0} 层力量。", CardTargetKind.None,
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = statuses["strength"],
                    Stacks = EffectValue.Flat(2),
                });

            // ---------------------------------------------------------- 稀有
            //
            // 这两瓶是刻意放的「能力证明」：
            // 一瓶用选牌效果（药水也能挂起结算），一瓶用组合子（药水也能做多段）。

            Make(dir, output, "cleanse", "澄澈药水", PotionRarity.Rare,
                "选择 {0} 张手牌消耗，然后抽 {1} 张牌。", CardTargetKind.None,
                new SelectCardsEffect
                {
                    Source = CardPile.Hand,
                    Count = EffectValue.Flat(2),
                    Action = CardSelectionAction.Exhaust,
                    AllowFewer = true,
                },
                new DrawEffect { Count = EffectValue.Flat(3) });

            Make(dir, output, "fracture", "碎裂药水", PotionRarity.Rare,
                "对所有敌人造成 5 点伤害，重复 {0} 次。", CardTargetKind.None,
                new RepeatEffect
                {
                    Times = EffectValue.Flat(3),
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.AllEnemies, Amount = EffectValue.Flat(5) }
                    }
                });
        }

        private static void Make(string dir, Dictionary<string, PotionDefinition> output,
                                 string id, string name, PotionRarity rarity,
                                 string template, CardTargetKind targetKind,
                                 params CardEffect[] effects)
        {
            var so = SampleContentGenerator.LoadOrCreateAsset<PotionDefinition>(
                $"{dir}/Potion_{SampleContentGenerator.Capitalize(id)}.asset");

            so.Id = id;
            so.DisplayName = name;
            so.Rarity = rarity;
            so.DescriptionTemplate = template;
            so.TargetKind = targetKind;
            so.Effects = new List<CardEffect>(effects);
            so.ShopPrice = 0;   // 0 = 用稀有度默认价

            EditorUtility.SetDirty(so);
            output[id] = so;
        }
    }
}
