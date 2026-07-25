using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Effects.Impl;
using Game.Enemies;
using Game.Statuses;
using Game.Statuses.Impl;
using UnityEngine;
using RelicDef = Game.Relics.RelicDefinition;
using RelicRarity = Game.Relics.RelicRarity;
using RelicImpl = Game.Relics.Impl;
// ★ 必须走别名：下面的 Potions 字段会遮蔽 Game.Potions 命名空间，
//   直接写 PotionRarity 会被解析成「字段的成员」。
using PotionDef = Game.Potions.PotionDefinition;
using PotionRarity = Game.Potions.PotionRarity;

namespace Game.Tests
{
    /// <summary>
    /// 测试用的内容工厂。全部在内存里 CreateInstance，不依赖 Assets/GameData 下的资产，
    /// 这样测试可以独立运行，也不会因为策划改数值而红。
    /// </summary>
    public class TestContent
    {
        public GameDatabase Db;

        public readonly Dictionary<string, StatusDefinition> Statuses = new Dictionary<string, StatusDefinition>();
        public readonly Dictionary<string, CardDefinition> Cards = new Dictionary<string, CardDefinition>();
        public readonly Dictionary<string, EnemyDefinition> Enemies = new Dictionary<string, EnemyDefinition>();
        public readonly Dictionary<string, EncounterDefinition> Encounters = new Dictionary<string, EncounterDefinition>();
        public readonly Dictionary<string, RelicDef> Relics = new Dictionary<string, RelicDef>();
        public readonly Dictionary<string, PotionDef> Potions =
            new Dictionary<string, PotionDef>();

        private readonly List<Object> _created = new List<Object>();

        public static TestContent Build()
        {
            var c = new TestContent();
            c.CreateStatuses();
            c.CreateCards();
            c.CreateEnemies();
            c.CreateEncounters();
            c.CreateRelics();
            c.CreatePotions();

            c.Db = c.New<GameDatabase>("Db");
            c.Db.Statuses = new List<StatusDefinition>(c.Statuses.Values);
            c.Db.Cards = new List<CardDefinition>(c.Cards.Values);
            c.Db.Enemies = new List<EnemyDefinition>(c.Enemies.Values);
            c.Db.Encounters = new List<EncounterDefinition>(c.Encounters.Values);
            c.Db.Relics = new List<RelicDef>(c.Relics.Values);
            c.Db.Potions = new List<PotionDef>(c.Potions.Values);
            c.Db.BuildIndex();
            return c;
        }

        public void Dispose()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private T New<T>(string name) where T : ScriptableObject
        {
            var so = ScriptableObject.CreateInstance<T>();
            so.name = name;
            _created.Add(so);
            return so;
        }

        // ================================================================ 状态

        private void CreateStatuses()
        {
            Statuses["strength"] = MakeStatus("strength", "力量", StatusDecay.None, new StrengthBehaviour());
            Statuses["vulnerable"] = MakeStatus("vulnerable", "易伤", StatusDecay.LoseOneAtTurnEnd, new VulnerableBehaviour());
            Statuses["weak"] = MakeStatus("weak", "虚弱", StatusDecay.LoseOneAtTurnEnd, new WeakBehaviour());
            Statuses["poison"] = MakeStatus("poison", "中毒", StatusDecay.LoseOneAtTurnEnd, new PoisonBehaviour());
            Statuses["thorns"] = MakeStatus("thorns", "荆棘", StatusDecay.None, new ThornsBehaviour());

            var barricade = MakeStatus("barricade", "壁垒", StatusDecay.None, new BarricadeBehaviour());
            barricade.MaxStacks = 1;
            Statuses["barricade"] = barricade;

            // ---- 阶段 4：三个演示新 Hook 的状态
            var artifact = MakeStatus("artifact", "神器", StatusDecay.None, new ArtifactBehaviour());
            artifact.Polarity = StatusPolarity.Buff;
            Statuses["artifact"] = artifact;

            var revive = MakeStatus("revive", "回光", StatusDecay.None, new ReviveBehaviour { HealAfter = 10 });
            Statuses["revive"] = revive;

            Statuses["regenerate"] = MakeStatus("regenerate", "再生",
                StatusDecay.LoseOneAtTurnEnd, new RegenerateBehaviour());

            // ★ 极性必须正确标注：ArtifactBehaviour 靠 Polarity == Debuff 判断该不该挡
            Statuses["vulnerable"].Polarity = StatusPolarity.Debuff;
            Statuses["weak"].Polarity = StatusPolarity.Debuff;
            Statuses["poison"].Polarity = StatusPolarity.Debuff;
            Statuses["strength"].Polarity = StatusPolarity.Buff;
        }

        // ================================================================ 遗物

        private void CreateRelics()
        {
            Relics["vajra"] = MakeRelic("vajra", "金刚杵", RelicRarity.Common,
                new RelicImpl.GrantStatusOnBattleStartBehaviour { Status = Statuses["strength"], Stacks = 1 });

            Relics["bag"] = MakeRelic("bag", "备战包", RelicRarity.Uncommon,
                new RelicImpl.TurnResourceBehaviour { ExtraDraw = 2, FirstTurnOnly = true });

            Relics["lantern"] = MakeRelic("lantern", "提灯", RelicRarity.Uncommon,
                new RelicImpl.TurnResourceBehaviour { ExtraEnergy = 1, FirstTurnOnly = true });

            Relics["pen_nib"] = MakeRelic("pen_nib", "笔尖", RelicRarity.Uncommon,
                new RelicImpl.FirstCardCostReductionBehaviour { CardType = CardType.Attack, Reduction = 1 });

            Relics["echo"] = MakeRelic("echo", "回响护符", RelicRarity.Rare,
                new RelicImpl.EchoFirstCardBehaviour { CardType = CardType.Attack });

            Relics["recycler"] = MakeRelic("recycler", "回收器", RelicRarity.Rare,
                new RelicImpl.CardDestinationBehaviour { CardType = CardType.Skill, Destination = CardPile.Draw });

            Relics["totem"] = MakeRelic("totem", "神器图腾", RelicRarity.Uncommon,
                new RelicImpl.GrantStatusOnBattleStartBehaviour { Status = Statuses["artifact"], Stacks = 1 });

            Relics["tail"] = MakeRelic("tail", "蜥蜴尾巴", RelicRarity.Rare,
                new RelicImpl.GrantStatusOnBattleStartBehaviour { Status = Statuses["revive"], Stacks = 1 });

            Relics["burning_blood"] = MakeRelic("burning_blood", "燃烧之血", RelicRarity.Starter,
                new RelicImpl.BattleRewardBehaviour { HealOnVictory = 6 });

            Relics["beads"] = MakeRelic("beads", "冥想念珠", RelicRarity.Uncommon,
                new RelicImpl.EveryNCardsHealBehaviour { CardType = CardType.Skill, Threshold = 2, HealAmount = 3 });
        }

        private RelicDef MakeRelic(string id, string name, RelicRarity rarity,
                                                 params StatusBehaviour[] behaviours)
        {
            var so = New<RelicDef>("Relic_" + id);
            so.Id = id;
            so.DisplayName = name;
            so.Rarity = rarity;
            so.Description = name;
            so.Behaviours = new List<StatusBehaviour>(behaviours);
            return so;
        }

        // ================================================================ 药水

        private void CreatePotions()
        {
            MakePotion("healing", "治疗药水", PotionRarity.Common, CardTargetKind.None,
                "回复 {0} 点生命。",
                new HealEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(15) });

            MakePotion("fire", "火焰药水", PotionRarity.Common, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(20) });

            MakePotion("energy", "活力药水", PotionRarity.Uncommon, CardTargetKind.None,
                "获得 {0} 点能量。",
                new EnergyEffect { Amount = EffectValue.Flat(2) });

            // 药水里放选牌效果：证明药水也能让结算挂起
            MakePotion("cleanse", "澄澈药水", PotionRarity.Rare, CardTargetKind.None,
                "选择消耗 {0} 张手牌。",
                new SelectCardsEffect
                {
                    Source = CardPile.Hand,
                    Count = EffectValue.Flat(1),
                    Action = CardSelectionAction.Exhaust,
                });
        }

        private PotionDef MakePotion(string id, string name, PotionRarity rarity,
                                                    CardTargetKind targetKind, string template,
                                                    params CardEffect[] effects)
        {
            var so = New<PotionDef>("Potion_" + id);
            so.Id = id;
            so.DisplayName = name;
            so.Rarity = rarity;
            so.TargetKind = targetKind;
            so.DescriptionTemplate = template;
            so.Effects = new List<CardEffect>(effects);
            Potions[id] = so;
            return so;
        }

        private StatusDefinition MakeStatus(string id, string name, StatusDecay decay, params StatusBehaviour[] bs)
        {
            var so = New<StatusDefinition>("Status_" + id);
            so.Id = id;
            so.DisplayName = name;
            so.Decay = decay;
            so.MaxStacks = 999;
            so.Behaviours = new List<StatusBehaviour>(bs);
            return so;
        }

        // ================================================================ 卡牌

        private void CreateCards()
        {
            Cards["strike"] = MakeCard("strike", "打击", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(6) });

            Cards["defend"] = MakeCard("defend", "防御", 1, CardType.Skill, CardTargetKind.None,
                "获得 {0} 点护甲。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(5) });

            Cards["bash"] = MakeCard("bash", "重击", 2, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害，施加 {1} 层易伤。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(8) },
                new ApplyStatusEffect
                {
                    Target = TargetSelector.Previous,
                    Status = Statuses["vulnerable"],
                    Stacks = EffectValue.Flat(2)
                });

            var whirlwind = MakeCard("whirlwind", "旋风斩", 0, CardType.Attack, CardTargetKind.None,
                "对所有敌人造成 {0} 点伤害，X 次。",
                new DamageEffect
                {
                    Target = TargetSelector.AllEnemies,
                    Amount = EffectValue.Flat(5),
                    Times = new EffectValue { Base = 0, Scale = ValueScale.XValue, PerUnit = 1 }
                });
            whirlwind.CostMode = CostMode.X;
            Cards["whirlwind"] = whirlwind;

            var adrenaline = MakeCard("adrenaline", "肾上腺素", 0, CardType.Skill, CardTargetKind.None,
                "获得 {1} 点能量，抽 {0} 张牌。消耗。",
                new DrawEffect { Count = EffectValue.Flat(2) },
                new EnergyEffect { Amount = EffectValue.Flat(1) });
            adrenaline.Keywords = CardKeyword.Exhaust;
            Cards["adrenaline"] = adrenaline;

            Cards["barricade"] = MakeCard("barricade", "壁垒", 3, CardType.Power, CardTargetKind.None,
                "护甲不再于回合开始时消失。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = Statuses["barricade"],
                    Stacks = EffectValue.Flat(1)
                });

            Cards["poisonstab"] = MakeCard("poisonstab", "毒刺", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "施加 {0} 层中毒。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.Chosen,
                    Status = Statuses["poison"],
                    Stacks = EffectValue.Flat(3)
                });

            Cards["flex"] = MakeCard("flex", "屈伸", 0, CardType.Skill, CardTargetKind.None,
                "获得 {0} 层力量。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = Statuses["strength"],
                    Stacks = EffectValue.Flat(2)
                });

            // 组合子测试用：重复 3 次造成 2 点伤害
            Cards["triplestab"] = MakeCard("triplestab", "三连刺", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 2 点伤害 3 次。",
                new RepeatEffect
                {
                    Times = EffectValue.Flat(3),
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(2) }
                    }
                });

            // ---- 战斗内选牌测试用
            //
            // 「弃 1 张、再抽 2 张」是验证挂起顺序最锋利的一张牌：
            // 如果结算没有真的挂起，抽牌会在玩家选牌之前发生，手牌数和牌堆都对不上。
            Cards["sift"] = MakeCard("sift", "筛选", 1, CardType.Skill, CardTargetKind.None,
                "选择弃掉 {0} 张牌，然后抽 {1} 张牌。",
                new SelectCardsEffect
                {
                    Source = CardPile.Hand,
                    Count = EffectValue.Flat(1),
                    Action = CardSelectionAction.Discard,
                },
                new DrawEffect { Count = EffectValue.Flat(2) });

            Cards["purge"] = MakeCard("purge", "净除", 0, CardType.Skill, CardTargetKind.None,
                "选择消耗 {0} 张手牌。",
                new SelectCardsEffect
                {
                    Source = CardPile.Hand,
                    Count = EffectValue.Flat(1),
                    Action = CardSelectionAction.Exhaust,
                });

            Cards["hold"] = MakeCard("hold", "把持", 0, CardType.Skill, CardTargetKind.None,
                "选择 {0} 张手牌，本回合结束时保留它。",
                new SelectCardsEffect
                {
                    Source = CardPile.Hand,
                    Count = EffectValue.Flat(1),
                    Action = CardSelectionAction.Retain,
                });

            Cards["mirror"] = MakeCard("mirror", "映写", 1, CardType.Skill, CardTargetKind.None,
                "选择 {0} 张手牌，复制一份到手牌。",
                new SelectCardsEffect
                {
                    Source = CardPile.Hand,
                    Count = EffectValue.Flat(1),
                    Action = CardSelectionAction.Duplicate,
                });

            Cards["stash"] = MakeCard("stash", "藏牌", 0, CardType.Skill, CardTargetKind.None,
                "选择 {0} 张手牌放回抽牌堆顶。",
                new SelectCardsEffect
                {
                    Source = CardPile.Hand,
                    Count = EffectValue.Flat(1),
                    Action = CardSelectionAction.ToDrawTop,
                });

            // 组合子里嵌选牌：验证挂起能穿过 Conditional 恢复
            Cards["condselect"] = MakeCard("condselect", "择机弃牌", 0, CardType.Skill, CardTargetKind.None,
                "若手牌至少 2 张，选择弃掉 1 张，然后获得 3 点护甲。",
                new ConditionalEffect
                {
                    Condition = new EffectCondition { Kind = ConditionKind.HandCountAtLeast, Value = 2 },
                    Then = new List<CardEffect>
                    {
                        new SelectCardsEffect
                        {
                            Source = CardPile.Hand,
                            Count = EffectValue.Flat(1),
                            Action = CardSelectionAction.Discard,
                        }
                    },
                    Else = new List<CardEffect>(),
                },
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(3) });

            // Repeat 里嵌选牌：验证 n 次迭代在挂起后仍然全部执行
            Cards["repeatselect"] = MakeCard("repeatselect", "连续筛选", 0, CardType.Skill, CardTargetKind.None,
                "重复 2 次：选择消耗 1 张手牌。",
                new RepeatEffect
                {
                    Times = EffectValue.Flat(2),
                    Effects = new List<CardEffect>
                    {
                        new SelectCardsEffect
                        {
                            Source = CardPile.Hand,
                            Count = EffectValue.Flat(1),
                            Action = CardSelectionAction.Exhaust,
                        }
                    }
                });

            // 递归保护测试用：自己嵌自己
            var deep = new RepeatEffect { Times = EffectValue.Flat(2), Effects = new List<CardEffect>() };
            var cur = deep;
            for (int i = 0; i < 20; i++)
            {
                var next = new RepeatEffect { Times = EffectValue.Flat(2), Effects = new List<CardEffect>() };
                cur.Effects.Add(next);
                cur = next;
            }
            cur.Effects.Add(new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(1) });
            Cards["deepnest"] = MakeCard("deepnest", "深层嵌套", 0, CardType.Attack, CardTargetKind.SingleEnemy, "", deep);
        }

        private CardDefinition MakeCard(string id, string name, int cost, CardType type,
                                        CardTargetKind targetKind, string template, params CardEffect[] effects)
        {
            var so = New<CardDefinition>("Card_" + id);
            so.Id = id;
            so.DisplayName = name;
            so.Cost = cost;
            so.Type = type;
            so.TargetKind = targetKind;
            so.DescriptionTemplate = template;
            so.Effects = new List<CardEffect>(effects);
            return so;
        }

        // ================================================================ 敌人

        private void CreateEnemies()
        {
            var dummy = New<EnemyDefinition>("Enemy_dummy");
            dummy.Id = "dummy";
            dummy.DisplayName = "木桩";
            dummy.MinHp = 100;
            dummy.MaxHp = 100;
            dummy.Actions = new List<EnemyAction>
            {
                new EnemyAction { Name = "发呆", Intent = IntentKind.Sleep, Weight = 10, Effects = new List<CardEffect>() }
            };
            Enemies["dummy"] = dummy;

            var slime = New<EnemyDefinition>("Enemy_slime");
            slime.Id = "slime";
            slime.DisplayName = "史莱姆";
            slime.MinHp = 12;
            slime.MaxHp = 12;
            slime.Actions = new List<EnemyAction>
            {
                new EnemyAction
                {
                    Name = "撕咬", Intent = IntentKind.Attack, Weight = 0,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(5) }
                    }
                },
                new EnemyAction
                {
                    Name = "腐蚀", Intent = IntentKind.Debuff, Weight = 0,
                    Effects = new List<CardEffect>
                    {
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.Chosen,
                            Status = Statuses["weak"], Stacks = EffectValue.Flat(1)
                        }
                    }
                },
            };
            slime.FixedSequence = new List<int> { 0, 0, 1 };
            slime.LoopSequence = true;
            Enemies["slime"] = slime;

            // 强力敌人：每回合打 40，用来测试玩家死亡
            var brute = New<EnemyDefinition>("Enemy_brute");
            brute.Id = "brute";
            brute.DisplayName = "巨兽";
            brute.MinHp = 200;
            brute.MaxHp = 200;
            brute.Actions = new List<EnemyAction>
            {
                new EnemyAction
                {
                    Name = "碾压", Intent = IntentKind.Attack, Weight = 10,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(40) }
                    }
                }
            };
            Enemies["brute"] = brute;

            // 带荆棘的敌人
            var thorny = New<EnemyDefinition>("Enemy_thorny");
            thorny.Id = "thorny";
            thorny.DisplayName = "荆棘怪";
            thorny.MinHp = 50;
            thorny.MaxHp = 50;
            thorny.StartingStatuses = new List<StartingStatus>
            {
                new StartingStatus { Status = Statuses["thorns"], Stacks = 3 }
            };
            thorny.Actions = new List<EnemyAction>
            {
                new EnemyAction { Name = "发呆", Intent = IntentKind.Sleep, Weight = 10, Effects = new List<CardEffect>() }
            };
            Enemies["thorny"] = thorny;
        }

        private void CreateEncounters()
        {
            Encounters["dummy"] = MakeEncounter("dummy", "dummy");
            Encounters["two_dummies"] = MakeEncounter("two_dummies", "dummy", "dummy");
            Encounters["slime"] = MakeEncounter("slime", "slime");
            Encounters["two_slimes"] = MakeEncounter("two_slimes", "slime", "slime");
            Encounters["brute"] = MakeEncounter("brute", "brute");
            Encounters["thorny"] = MakeEncounter("thorny", "thorny");
        }

        private EncounterDefinition MakeEncounter(string id, params string[] enemyIds)
        {
            var so = New<EncounterDefinition>("Encounter_" + id);
            so.Id = id;
            so.DisplayName = id;
            so.Enemies = new List<EnemyDefinition>();
            for (int i = 0; i < enemyIds.Length; i++) so.Enemies.Add(Enemies[enemyIds[i]]);
            return so;
        }
    }
}
