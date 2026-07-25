using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Effects;
using Game.Effects.Impl;
using Game.Statuses;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>
    /// 组合子卡：Repeat / Conditional / RandomPick / Delayed。
    ///
    /// ★ 这四个组合子在阶段 2 就写好并有测试，但**内容侧几乎没用上**
    ///   （生成器里只有 ConditionalEffect 出现过一次，RandomPick 与 Delayed 是零引用）。
    ///   组合子正是「加一张卡不用写代码」这个架构承诺的兑现处——
    ///   下面 6 张卡的机制复杂度已经明显高于前 24 张，而 EffectResolver 一行没改。
    ///
    /// <para>顺带说明为什么组合子值得单独存在：如果没有它们，
    /// 「重复 3 次」「若血量过半则……」「随机三选一」「回合结束时……」
    /// 每一个都要新写一个效果类，而且互相无法嵌套。</para>
    /// </summary>
    internal static class SampleContentCombinators
    {
        internal static void CreateCombinatorCards(Dictionary<string, StatusDefinition> statuses,
                                                   Dictionary<string, CardDefinition> cards)
        {
            // ============================================================ Repeat

            var flurryPlus = Make(cards, "flurry_plus", "疾风连打+", 1,
                CardType.Attack, CardTargetKind.SingleEnemy, CardRarity.Special,
                "造成 4 点伤害，重复 {0} 次。",
                new RepeatEffect
                {
                    Times = EffectValue.Flat(5),
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(4) }
                    }
                });

            // ★ 多段攻击不是「一次打 12」：每一段都单独走一遍伤害管线，
            //   因此易伤/力量按段结算，护甲也被逐段削——这正是 Repeat 存在的意义。
            var flurry = Make(cards, "flurry", "疾风连打", 1,
                CardType.Attack, CardTargetKind.SingleEnemy, CardRarity.Common,
                "造成 4 点伤害，重复 {0} 次。",
                new RepeatEffect
                {
                    Times = EffectValue.Flat(3),
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(4) }
                    }
                });
            flurry.UpgradedVersion = flurryPlus;
            EditorUtility.SetDirty(flurry);

            // ============================================================ Conditional

            // 「越危险越强」：条件成立时追加一段伤害，而不是简单地乘二。
            Make(cards, "last_stand", "背水一战", 2,
                CardType.Attack, CardTargetKind.SingleEnemy, CardRarity.Uncommon,
                "造成 {0} 点伤害。若你的生命低于 50%，再造成一次。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(10) },
                new ConditionalEffect
                {
                    Condition = new EffectCondition { Kind = ConditionKind.SelfHpBelowPercent, Value = 50 },
                    Then = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Previous, Amount = EffectValue.Flat(10) }
                    },
                    Else = new List<CardEffect>(),
                });

            // 条件的另一半也要有用：Else 分支给一个安慰奖，避免「条件不成立时这张牌是废牌」。
            Make(cards, "follow_up", "乘胜追击", 1,
                CardType.Attack, CardTargetKind.SingleEnemy, CardRarity.Uncommon,
                "造成 {0} 点伤害。若上一张打出的是攻击牌，抽 1 张牌；否则获得 3 点护甲。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(7) },
                new ConditionalEffect
                {
                    Condition = new EffectCondition { Kind = ConditionKind.LastCardWasAttack },
                    Then = new List<CardEffect>
                    {
                        new DrawEffect { Count = EffectValue.Flat(1) }
                    },
                    Else = new List<CardEffect>
                    {
                        new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(3) }
                    },
                });

            // ============================================================ RandomPick

            // ★ 全工程第一张使用 RandomPickEffect 的卡。
            //   权重刻意不均：弱效果权重高，强效果权重低，让期望值合理。
            Make(cards, "wild_gamble", "孤注一掷", 1,
                CardType.Skill, CardTargetKind.None, CardRarity.Uncommon,
                "随机获得下列之一：8 点护甲 / 抽 2 张牌 / 2 点能量。",
                new RandomPickEffect
                {
                    PickCount = 1,
                    Options = new List<RandomPickEffect.Option>
                    {
                        new RandomPickEffect.Option
                        {
                            Note = "护甲", Weight = 40,
                            Effect = new BlockEffect
                            {
                                Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(8)
                            },
                        },
                        new RandomPickEffect.Option
                        {
                            Note = "抽牌", Weight = 40,
                            Effect = new DrawEffect { Count = EffectValue.Flat(2) },
                        },
                        new RandomPickEffect.Option
                        {
                            Note = "能量", Weight = 20,
                            Effect = new EnergyEffect { Amount = EffectValue.Flat(2) },
                        },
                    },
                });

            // ============================================================ Delayed

            // ★ 全工程第一张使用 DelayedEffect 的卡。
            //   延迟到回合末结算，意味着它会吃到本回合后续叠上去的易伤——
            //   「先上易伤再引爆」是这张卡真正的用法。
            Make(cards, "time_bomb", "延时炸弹", 1,
                CardType.Skill, CardTargetKind.None, CardRarity.Rare,
                "回合结束时，对所有敌人造成 14 点伤害。",
                new DelayedEffect
                {
                    Timing = DelayTiming.EndOfThisTurn,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect
                        {
                            Target = TargetSelector.AllEnemies, Amount = EffectValue.Flat(14)
                        }
                    }
                });

            // 延迟到下回合开始：代价是这回合什么都不发生，收益是不占下回合的能量。
            Make(cards, "gather_strength", "蓄力", 0,
                CardType.Skill, CardTargetKind.None, CardRarity.Uncommon,
                "下回合开始时，获得 2 层力量与 8 点护甲。消耗。",
                new DelayedEffect
                {
                    Timing = DelayTiming.StartOfNextTurn,
                    Effects = new List<CardEffect>
                    {
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.SelfOnly,
                            Status = statuses["strength"],
                            Stacks = EffectValue.Flat(2),
                        },
                        new BlockEffect
                        {
                            Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(8)
                        },
                    }
                }).Keywords = CardKeyword.Exhaust;
        }

        private static CardDefinition Make(Dictionary<string, CardDefinition> cards,
                                           string id, string name, int cost, CardType type,
                                           CardTargetKind targetKind, CardRarity rarity,
                                           string template, params CardEffect[] effects)
        {
            var so = SampleContentGenerator.LoadOrCreateAsset<CardDefinition>(
                $"Assets/GameData/Cards/Card_{SampleContentGenerator.Capitalize(id)}.asset");

            so.Id = id;
            so.DisplayName = name;
            so.Cost = cost;
            so.CostMode = CostMode.Fixed;
            so.Type = type;
            so.TargetKind = targetKind;
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
