using System.Collections.Generic;
using System.IO;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Effects.Impl;
using Game.Enemies;
using Game.Events;
using Game.Relics;
using Game.Statuses;
using Game.Statuses.Impl;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 一键生成全部示例内容（状态 / 卡牌 / 敌人 / 战斗 / 数据库）。
    /// 用代码生成而不是手工建资产，好处是：AI 可以直接改这个文件来批量调整数值，
    /// 且内容与架构文档第七部分严格一致。
    /// </summary>
    public static class SampleContentGenerator
    {
        private const string RootDir = "Assets/GameData";
        private const string StatusDir = RootDir + "/Statuses";
        private const string CardDir = RootDir + "/Cards";
        private const string EnemyDir = RootDir + "/Enemies";
        private const string EncounterDir = RootDir + "/Encounters";
        private const string RelicDir = RootDir + "/Relics";
        private const string EventDir = RootDir + "/Events";
        private const string PotionDir = RootDir + "/Potions";
        private const string KeywordDir = RootDir + "/Keywords";

        private static readonly Dictionary<string, StatusDefinition> Statuses = new Dictionary<string, StatusDefinition>();
        private static readonly Dictionary<string, CardDefinition> Cards = new Dictionary<string, CardDefinition>();
        private static readonly Dictionary<string, EnemyDefinition> Enemies = new Dictionary<string, EnemyDefinition>();
        private static readonly List<EncounterDefinition> Encounters = new List<EncounterDefinition>();
        private static readonly Dictionary<string, RelicDefinition> Relics = new Dictionary<string, RelicDefinition>();
        private static readonly Dictionary<string, EventDefinition> Events = new Dictionary<string, EventDefinition>();
        private static readonly Dictionary<string, Potions.PotionDefinition> PotionDefs =
            new Dictionary<string, Potions.PotionDefinition>();
        private static readonly List<KeywordDefinition> KeywordDefs = new List<KeywordDefinition>();

        [MenuItem("Tools/卡牌游戏/1. 生成示例内容", priority = 1)]
        public static void Generate()
        {
            Statuses.Clear(); Cards.Clear(); Enemies.Clear(); Encounters.Clear();
            Relics.Clear(); Events.Clear(); PotionDefs.Clear(); KeywordDefs.Clear();

            EnsureDir(RootDir); EnsureDir(StatusDir); EnsureDir(CardDir); EnsureDir(EnemyDir);
            EnsureDir(EncounterDir); EnsureDir(RelicDir); EnsureDir(EventDir); EnsureDir(PotionDir);
            EnsureDir(KeywordDir);

            CreateStatuses();
            CreateKeywordDefinitions();
            CreateCards();
            // ★ 必须在 CreateEnemies 之前：敌人的「塞牌」行动要引用这些卡的资产。
            SampleContentCurses.CreateCurseAndStatusCards(Statuses, Cards);
            SampleContentKeywords.CreateKeywordCards(Statuses, Cards);
            SampleContentCombinators.CreateCombinatorCards(Statuses, Cards);
            SampleContentSelection.CreateSelectionCards(Cards);
            CreateEnemies();
            CreateEncounters();
            SampleContentRelics.CreateRelics(RelicDir, Statuses, Relics);
            SampleContentPotions.CreatePotions(PotionDir, Statuses, PotionDefs);
            SampleContentEvents.CreateEvents(EventDir, Events, Cards);
            var db = CreateDatabase();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SampleContent] 生成完成：{Statuses.Count} 个状态、{Cards.Count} 张卡、" +
                      $"{Enemies.Count} 个敌人、{Encounters.Count} 场战斗、{Relics.Count} 个遗物、" +
                      $"{Events.Count} 个事件、{PotionDefs.Count} 瓶药水、{KeywordDefs.Count} 个关键字。" +
                      $"数据库：{AssetDatabase.GetAssetPath(db)}");
            Selection.activeObject = db;
        }

        // ==================================================================== 状态

        private static void CreateStatuses()
        {
            MakeStatus("strength", "力量", StatusPolarity.Buff, StatusDecay.None,
                "所有攻击伤害 +{stacks}。", new StrengthBehaviour());

            MakeStatus("vulnerable", "易伤", StatusPolarity.Debuff, StatusDecay.LoseOneAtTurnEnd,
                "受到的攻击伤害 +50%，剩余 {stacks} 回合。", new VulnerableBehaviour());

            MakeStatus("weak", "虚弱", StatusPolarity.Debuff, StatusDecay.LoseOneAtTurnEnd,
                "造成的攻击伤害 -25%，剩余 {stacks} 回合。", new WeakBehaviour());

            MakeStatus("poison", "中毒", StatusPolarity.Debuff, StatusDecay.LoseOneAtTurnEnd,
                "回合结束受到 {stacks} 点无视护甲伤害，然后层数 -1。", new PoisonBehaviour());

            var barricade = MakeStatus("barricade", "壁垒", StatusPolarity.Buff, StatusDecay.None,
                "护甲不再于回合开始时消失。", new BarricadeBehaviour());
            barricade.MaxStacks = 1;
            EditorUtility.SetDirty(barricade);

            MakeStatus("thorns", "荆棘", StatusPolarity.Buff, StatusDecay.None,
                "受到攻击时对攻击者造成 {stacks} 点伤害。", new ThornsBehaviour());

            // ---- 阶段 4 新增：这三个各自演示一个新的 Hook 拦截点

            var artifact = MakeStatus("artifact", "神器", StatusPolarity.Buff, StatusDecay.None,
                "抵消接下来 {stacks} 次施加给你的减益。", new ArtifactBehaviour());
            artifact.MaxStacks = 99;
            EditorUtility.SetDirty(artifact);

            var revive = MakeStatus("revive", "回光", StatusPolarity.Buff, StatusDecay.None,
                "受到致死伤害时保留 1 点生命，并回复 10 点生命。消耗一层。",
                new ReviveBehaviour { HealAfter = 10 });
            revive.MaxStacks = 9;
            EditorUtility.SetDirty(revive);

            MakeStatus("regenerate", "再生", StatusPolarity.Buff, StatusDecay.LoseOneAtTurnEnd,
                "回合结束回复 {stacks} 点生命，然后层数 -1。", new RegenerateBehaviour());

            // 恶魔形态：每回合开始给自己叠力量。演示「状态生成状态」。
            var demon = MakeStatus("demon_form", "恶魔形态", StatusPolarity.Buff, StatusDecay.None,
                "每回合开始获得 {stacks} 点力量。",
                new TurnStartGrantStatusBehaviour { Status = Statuses["strength"], StacksPerStack = 2 });
            demon.MaxStacks = 9;
            EditorUtility.SetDirty(demon);
        }

        private static StatusDefinition MakeStatus(string id, string name, StatusPolarity polarity,
                                                   StatusDecay decay, string desc, params StatusBehaviour[] behaviours)
        {
            var so = LoadOrCreate<StatusDefinition>($"{StatusDir}/Status_{Capitalize(id)}.asset");
            so.Id = id;
            so.DisplayName = name;
            so.Polarity = polarity;
            so.Decay = decay;
            so.Description = desc;
            so.MaxStacks = 999;
            so.Behaviours = new List<StatusBehaviour>(behaviours);
            EditorUtility.SetDirty(so);
            Statuses[id] = so;
            return so;
        }

        // ==================================================================== 关键字

        /// <summary>
        /// 五个卡牌关键字的显示名与解释文案。
        ///
        /// ★ 这些文案是 tooltip 唯一的来源。少一个资产，对应关键字的悬停解释就静默消失，
        ///   所以 ContentValidator 会检查「卡池里用到的每个关键字位都配了定义」。
        /// </summary>
        private static void CreateKeywordDefinitions()
        {
            MakeKeyword(CardKeyword.Exhaust, "消耗",
                "打出后不进弃牌堆，而是进入消耗堆。本场战斗剩下的时间里都不会再抽到它。");

            MakeKeyword(CardKeyword.Retain, "保留",
                "回合结束时不会被弃掉，会留在手上带进下一回合。");

            MakeKeyword(CardKeyword.Innate, "固有",
                "战斗开始的第一回合必定在你的起始手牌里。");

            MakeKeyword(CardKeyword.Ethereal, "虚无",
                "回合结束时如果还在手上，直接被消耗掉，而不是弃掉。");

            MakeKeyword(CardKeyword.Unplayable, "不可打出",
                "无法主动打出。只能靠其他效果把它弃掉、消耗掉或转化掉。");
        }

        private static KeywordDefinition MakeKeyword(CardKeyword keyword, string name, string desc)
        {
            var so = LoadOrCreate<KeywordDefinition>($"{KeywordDir}/Keyword_{keyword}.asset");
            so.Keyword = keyword;
            so.DisplayName = name;
            so.Description = desc;
            EditorUtility.SetDirty(so);
            KeywordDefs.Add(so);
            return so;
        }

        // ==================================================================== 卡牌

        private static void CreateCards()
        {
            // ---- Strike / Strike+
            var strikePlus = MakeCard("strike_plus", "打击+", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(9) });

            var strike = MakeCard("strike", "打击", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(6) });
            strike.Rarity = CardRarity.Basic;
            strike.UpgradedVersion = strikePlus;
            EditorUtility.SetDirty(strike);

            // ---- Defend / Defend+
            var defendPlus = MakeCard("defend_plus", "防御+", 1, CardType.Skill, CardTargetKind.None,
                "获得 {0} 点护甲。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(8) });

            var defend = MakeCard("defend", "防御", 1, CardType.Skill, CardTargetKind.None,
                "获得 {0} 点护甲。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(5) });
            defend.Rarity = CardRarity.Basic;
            defend.UpgradedVersion = defendPlus;
            EditorUtility.SetDirty(defend);

            // ---- Bash：伤害 + 对「上一个效果的目标」施加易伤
            MakeCard("bash", "重击", 2, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害，施加 {1} 层易伤。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(8) },
                new ApplyStatusEffect
                {
                    Target = TargetSelector.Previous,
                    Status = Statuses["vulnerable"],
                    Stacks = EffectValue.Flat(2)
                });

            // ---- Whirlwind：X 费，对所有敌人打 X 次
            var whirlwind = MakeCard("whirlwind", "旋风斩", 0, CardType.Attack, CardTargetKind.None,
                "对所有敌人造成 {0} 点伤害，X 次。",
                new DamageEffect
                {
                    Target = TargetSelector.AllEnemies,
                    Amount = EffectValue.Flat(5),
                    Times = new EffectValue { Base = 0, Scale = ValueScale.XValue, PerUnit = 1 }
                });
            whirlwind.CostMode = CostMode.X;
            whirlwind.Rarity = CardRarity.Uncommon;
            EditorUtility.SetDirty(whirlwind);

            // ---- Adrenaline：抽牌 + 能量 + 消耗
            var adrenaline = MakeCard("adrenaline", "肾上腺素", 0, CardType.Skill, CardTargetKind.None,
                "获得 {1} 点能量，抽 {0} 张牌。消耗。",
                new DrawEffect { Count = EffectValue.Flat(2) },
                new EnergyEffect { Amount = EffectValue.Flat(1) });
            adrenaline.Keywords = CardKeyword.Exhaust;
            adrenaline.Rarity = CardRarity.Rare;
            EditorUtility.SetDirty(adrenaline);

            // ---- Barricade：能力牌，改核心规则
            var barricade = MakeCard("barricade", "壁垒", 3, CardType.Power, CardTargetKind.None,
                "护甲不再于回合开始时消失。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = Statuses["barricade"],
                    Stacks = EffectValue.Flat(1)
                });
            barricade.Rarity = CardRarity.Rare;
            EditorUtility.SetDirty(barricade);

            // ---- 额外演示：组合子（条件 + 重复）
            MakeCard("finisher", "终结技", 2, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害。若目标生命低于 50%，再造成一次。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(10) },
                new ConditionalEffect
                {
                    Condition = new EffectCondition { Kind = ConditionKind.TargetHpBelowPercent, Value = 50 },
                    Then = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(10) }
                    }
                });

            // ---- 额外演示：动态数值（按手牌数缩放）
            // ★ TargetKind 必须是 None：效果打的是 RandomEnemy，若声明成 SingleEnemy，
            //   玩家会被要求选一个目标、然后这个选择被完全忽略。ContentValidator 会报这条。
            var bladedance = MakeCard("bladedance", "刀刃之舞", 1, CardType.Attack, CardTargetKind.None,
                "对随机敌人造成 4 点伤害，次数等于你的手牌数。",
                new DamageEffect
                {
                    Target = new TargetSelector { Kind = TargetKind.RandomEnemy, Count = 1 },
                    Amount = EffectValue.Flat(4),
                    Times = new EffectValue { Base = 0, Scale = ValueScale.PerCardInHand, PerUnit = 1, Min = 1 }
                });
            bladedance.Rarity = CardRarity.Uncommon;
            EditorUtility.SetDirty(bladedance);

            CreateExtraCards();
        }

        /// <summary>
        /// 阶段 4 追加的卡池。奖励三选一和商店需要每个稀有度都有足够的内容，
        /// 否则 ContentPicker 会反复抽到同一张，玩家看到的「三选一」其实没得选。
        /// </summary>
        private static void CreateExtraCards()
        {
            // ================================ 普通

            var cleavePlus = MakeCard("cleave_plus", "横扫+", 1, CardType.Attack, CardTargetKind.AllEnemies,
                "对所有敌人造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.AllEnemies, Amount = EffectValue.Flat(11) });
            var cleave = MakeCard("cleave", "横扫", 1, CardType.Attack, CardTargetKind.AllEnemies,
                "对所有敌人造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.AllEnemies, Amount = EffectValue.Flat(8) });
            cleave.UpgradedVersion = cleavePlus;
            EditorUtility.SetDirty(cleave);

            var ironWavePlus = MakeCard("iron_wave_plus", "铁滚波+", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "获得 {1} 点护甲，造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(7) },
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(7) });
            var ironWave = MakeCard("iron_wave", "铁滚波", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "获得 {1} 点护甲，造成 {0} 点伤害。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(5) },
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(5) });
            ironWave.UpgradedVersion = ironWavePlus;
            EditorUtility.SetDirty(ironWave);

            var shrugPlus = MakeCard("shrug_it_off_plus", "耸肩+", 1, CardType.Skill, CardTargetKind.None,
                "获得 {0} 点护甲，抽 {1} 张牌。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(11) },
                new DrawEffect { Count = EffectValue.Flat(1) });
            var shrug = MakeCard("shrug_it_off", "耸肩", 1, CardType.Skill, CardTargetKind.None,
                "获得 {0} 点护甲，抽 {1} 张牌。",
                new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(8) },
                new DrawEffect { Count = EffectValue.Flat(1) });
            shrug.UpgradedVersion = shrugPlus;
            EditorUtility.SetDirty(shrug);

            MakeCard("poison_stab", "淬毒之刺", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成 {0} 点伤害，施加 {1} 层中毒。",
                new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(5) },
                new ApplyStatusEffect
                {
                    Target = TargetSelector.Previous,
                    Status = Statuses["poison"], Stacks = EffectValue.Flat(3)
                });

            // ================================ 罕见

            var bodySlam = MakeCard("body_slam", "肉搏", 1, CardType.Attack, CardTargetKind.SingleEnemy,
                "造成等同于你当前护甲值（{0}）的伤害。",
                new DamageEffect
                {
                    Target = TargetSelector.Chosen,
                    Amount = new EffectValue { Base = 0, Scale = ValueScale.PerBlockOnSelf, PerUnit = 1 }
                });
            bodySlam.Rarity = CardRarity.Uncommon;
            EditorUtility.SetDirty(bodySlam);

            var inflame = MakeCard("inflame", "怒火中烧", 1, CardType.Power, CardTargetKind.None,
                "获得 {0} 点力量。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = Statuses["strength"], Stacks = EffectValue.Flat(2)
                });
            inflame.Rarity = CardRarity.Uncommon;
            EditorUtility.SetDirty(inflame);

            var regenCard = MakeCard("field_dressing", "战地包扎", 1, CardType.Skill, CardTargetKind.None,
                "获得 {0} 层再生。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = Statuses["regenerate"], Stacks = EffectValue.Flat(5)
                });
            regenCard.Rarity = CardRarity.Uncommon;
            EditorUtility.SetDirty(regenCard);

            var wardCard = MakeCard("ward", "守护咒", 1, CardType.Skill, CardTargetKind.None,
                "获得 {0} 层神器。消耗。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = Statuses["artifact"], Stacks = EffectValue.Flat(2)
                });
            wardCard.Rarity = CardRarity.Uncommon;
            wardCard.Keywords = CardKeyword.Exhaust;
            EditorUtility.SetDirty(wardCard);

            // ================================ 稀有

            var offering = MakeCard("offering", "献祭", 0, CardType.Skill, CardTargetKind.None,
                "失去 6 点生命，获得 {0} 点能量，抽 {1} 张牌。消耗。",
                new EnergyEffect { Amount = EffectValue.Flat(2) },
                new DrawEffect { Count = EffectValue.Flat(3) },
                new DamageEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Amount = EffectValue.Flat(6),
                    Kind = DamageKind.Loss,
                    IgnoreBlock = true,
                });
            offering.Rarity = CardRarity.Rare;
            offering.Keywords = CardKeyword.Exhaust;
            EditorUtility.SetDirty(offering);

            var demonForm = MakeCard("demon_form", "恶魔形态", 3, CardType.Power, CardTargetKind.None,
                "每回合开始时获得 2 点力量。",
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = Statuses["demon_form"], Stacks = EffectValue.Flat(1)
                });
            demonForm.Rarity = CardRarity.Rare;
            EditorUtility.SetDirty(demonForm);

            var reinforce = MakeCard("reinforcements", "增援", 1, CardType.Skill, CardTargetKind.None,
                "将 {0} 张打击加入手牌（本场战斗后消失）。抽 {1} 张牌。消耗。",
                new AddCardEffect
                {
                    Card = Cards["strike"], Pile = CardPile.Hand,
                    Count = EffectValue.Flat(2), Temporary = true
                },
                new DrawEffect { Count = EffectValue.Flat(1) });
            reinforce.Rarity = CardRarity.Rare;
            reinforce.Keywords = CardKeyword.Exhaust;
            EditorUtility.SetDirty(reinforce);
        }

        private static CardDefinition MakeCard(string id, string name, int cost, CardType type,
                                               CardTargetKind targetKind, string template, params CardEffect[] effects)
        {
            var so = LoadOrCreate<CardDefinition>($"{CardDir}/Card_{Capitalize(id)}.asset");
            so.Id = id;
            so.DisplayName = name;
            so.Cost = cost;
            so.CostMode = CostMode.Fixed;
            so.Type = type;
            so.TargetKind = targetKind;
            so.Keywords = CardKeyword.None;

            // ★ 升级版一律标 Special：GetCardsByRarity(null) 会排除 Basic / Special，
            //   否则「打击+」会跟「打击」一起出现在奖励三选一和商店里。
            so.Rarity = id.EndsWith("_plus") ? CardRarity.Special : CardRarity.Common;
            so.DescriptionTemplate = template;
            so.Effects = new List<CardEffect>(effects);
            so.UpgradedVersion = null;
            EditorUtility.SetDirty(so);
            Cards[id] = so;
            return so;
        }

        // ==================================================================== 敌人

        private static void CreateEnemies()
        {
            // ---- ① 普通敌人：固定序列
            var slime = LoadOrCreate<EnemyDefinition>($"{EnemyDir}/Enemy_Slime.asset");
            slime.Id = "slime";
            slime.DisplayName = "酸液史莱姆";
            slime.MinHp = 10; slime.MaxHp = 14;
            slime.IsElite = false; slime.IsBoss = false;
            slime.StartingStatuses = new List<StartingStatus>();
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
                // ★ 塞牌行动：状态牌进弃牌堆而不是手牌，代价延后到下次洗牌才显现，
                //   这样玩家不会在同一回合被打乱节奏，但牌组确实被稀释了。
                new EnemyAction
                {
                    Name = "喷吐粘液", Intent = IntentKind.Debuff, Weight = 0,
                    Effects = new List<CardEffect>
                    {
                        new AddCardEffect
                        {
                            Card = Cards["slimed"], Pile = CardPile.Discard,
                            Count = EffectValue.Flat(2), Temporary = true
                        }
                    }
                },
            };
            slime.FixedSequence = new List<int> { 0, 0, 1, 2 };
            slime.LoopSequence = true;
            slime.PhaseHpThresholds = new List<int>();
            slime.CustomBrainType = "";
            EditorUtility.SetDirty(slime);
            Enemies["slime"] = slime;

            // ---- ② 权重行动敌人
            var worm = LoadOrCreate<EnemyDefinition>($"{EnemyDir}/Enemy_JawWorm.asset");
            worm.Id = "jawworm";
            worm.DisplayName = "颚虫";
            worm.MinHp = 40; worm.MaxHp = 44;
            worm.StartingStatuses = new List<StartingStatus>();
            worm.Actions = new List<EnemyAction>
            {
                new EnemyAction
                {
                    Name = "猛击", Intent = IntentKind.Attack, Weight = 45, MaxConsecutive = 2,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(11) }
                    }
                },
                new EnemyAction
                {
                    Name = "轰鸣", Intent = IntentKind.AttackDefend, Weight = 30, MaxConsecutive = 1,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(7) },
                        new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(5) }
                    }
                },
                new EnemyAction
                {
                    Name = "硬化", Intent = IntentKind.Defend, Weight = 25, MaxConsecutive = 1,
                    Effects = new List<CardEffect>
                    {
                        new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(6) },
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.SelfOnly,
                            Status = Statuses["strength"], Stacks = EffectValue.Flat(3)
                        }
                    }
                },
            };
            worm.FixedSequence = new List<int> { 0 };
            worm.LoopSequence = false;
            worm.PhaseHpThresholds = new List<int>();
            worm.CustomBrainType = "";
            EditorUtility.SetDirty(worm);
            Enemies["jawworm"] = worm;

            // ---- ③ 多阶段 Boss
            var guardian = LoadOrCreate<EnemyDefinition>($"{EnemyDir}/Enemy_Guardian.asset");
            guardian.Id = "guardian";
            guardian.DisplayName = "守卫者";
            guardian.MinHp = 240; guardian.MaxHp = 240;
            guardian.IsBoss = true;
            guardian.StartingStatuses = new List<StartingStatus>
            {
                new StartingStatus { Status = Statuses["thorns"], Stacks = 3 }
            };
            guardian.Actions = new List<EnemyAction>
            {
                new EnemyAction  // 0
                {
                    Name = "充能", Intent = IntentKind.Buff, Weight = 20, PhaseMask = 0b01,
                    Effects = new List<CardEffect>
                    {
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.SelfOnly,
                            Status = Statuses["strength"], Stacks = EffectValue.Flat(2)
                        }
                    }
                },
                new EnemyAction  // 1
                {
                    Name = "重击", Intent = IntentKind.Attack, Weight = 40, PhaseMask = 0b01, MaxConsecutive = 2,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(16) }
                    }
                },
                new EnemyAction  // 2
                {
                    Name = "护盾", Intent = IntentKind.Defend, Weight = 40, PhaseMask = 0b01,
                    Effects = new List<CardEffect>
                    {
                        new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(20) }
                    }
                },
                new EnemyAction  // 3
                {
                    Name = "狂暴突刺", Intent = IntentKind.Attack, Weight = 60, PhaseMask = 0b10,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect
                        {
                            Target = TargetSelector.Chosen,
                            Amount = EffectValue.Flat(9),
                            Times = EffectValue.Flat(3)
                        }
                    }
                },
                new EnemyAction  // 4 —— GuardianBrain.ACTION_DESTROY 引用这一条
                {
                    Name = "毁灭", Intent = IntentKind.AttackDebuff, Weight = 40, PhaseMask = 0b10, MaxConsecutive = 1,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(32) },
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.Previous,
                            Status = Statuses["weak"], Stacks = EffectValue.Flat(2)
                        }
                    }
                },
            };
            guardian.FixedSequence = new List<int>();
            guardian.PhaseHpThresholds = new List<int> { 50 };
            guardian.CustomBrainType = "Game.Enemies.Impl.GuardianBrain, Game.Runtime";
            EditorUtility.SetDirty(guardian);
            Enemies["guardian"] = guardian;

            CreateExtraEnemies();
        }

        /// <summary>
        /// 阶段 4 追加的敌人。地图有 15 层、十来场战斗，只有 3 个敌人会重复到玩家想吐。
        /// </summary>
        private static void CreateExtraEnemies()
        {
            // ---- 虱子：低血量，前期垫场
            var louse = LoadOrCreate<EnemyDefinition>($"{EnemyDir}/Enemy_Louse.asset");
            louse.Id = "louse";
            louse.DisplayName = "红色虱子";
            louse.MinHp = 10; louse.MaxHp = 15;
            louse.IsElite = false; louse.IsBoss = false;
            louse.StartingStatuses = new List<StartingStatus>();
            louse.Actions = new List<EnemyAction>
            {
                new EnemyAction
                {
                    Name = "撕咬", Intent = IntentKind.Attack, Weight = 75,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(6) }
                    }
                },
                new EnemyAction
                {
                    Name = "蜷缩", Intent = IntentKind.Defend, Weight = 25, MaxConsecutive = 1,
                    Effects = new List<CardEffect>
                    {
                        new BlockEffect { Target = TargetSelector.SelfOnly, Amount = EffectValue.Flat(6) }
                    }
                },
                // 眩晕直接进抽牌堆顶：下回合必定抽到，但它是虚无牌，会自己烧掉——
                // 代价明确且有上限，不会滚雪球。
                new EnemyAction
                {
                    Name = "扬尘", Intent = IntentKind.Debuff, Weight = 20, MaxConsecutive = 1,
                    Effects = new List<CardEffect>
                    {
                        new AddCardEffect
                        {
                            Card = Cards["dazed"], Pile = CardPile.Draw,
                            Count = EffectValue.Flat(1), Temporary = true
                        }
                    }
                },
            };
            louse.FixedSequence = new List<int>();
            louse.LoopSequence = false;
            louse.PhaseHpThresholds = new List<int>();
            louse.CustomBrainType = "";
            EditorUtility.SetDirty(louse);
            Enemies["louse"] = louse;

            // ---- 邪教徒：先仪式后猛攻，演示「越拖越难打」的节奏压力
            var cultist = LoadOrCreate<EnemyDefinition>($"{EnemyDir}/Enemy_Cultist.asset");
            cultist.Id = "cultist";
            cultist.DisplayName = "邪教徒";
            cultist.MinHp = 48; cultist.MaxHp = 54;
            cultist.IsElite = false; cultist.IsBoss = false;
            cultist.StartingStatuses = new List<StartingStatus>();
            cultist.Actions = new List<EnemyAction>
            {
                new EnemyAction   // 0
                {
                    Name = "仪式", Intent = IntentKind.Buff, Weight = 0,
                    Effects = new List<CardEffect>
                    {
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.SelfOnly,
                            Status = Statuses["strength"], Stacks = EffectValue.Flat(3)
                        }
                    }
                },
                new EnemyAction   // 1
                {
                    Name = "鞭笞", Intent = IntentKind.Attack, Weight = 100,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(6) }
                    }
                },
            };
            cultist.FixedSequence = new List<int> { 0 };   // 第一回合必定仪式，之后走权重
            cultist.LoopSequence = false;
            cultist.PhaseHpThresholds = new List<int>();
            cultist.CustomBrainType = "";
            EditorUtility.SetDirty(cultist);
            Enemies["cultist"] = cultist;

            // ---- 精英：高压输出 + 减益，逼玩家在护甲和输出之间做取舍
            var nob = LoadOrCreate<EnemyDefinition>($"{EnemyDir}/Enemy_GremlinNob.asset");
            nob.Id = "gremlin_nob";
            nob.DisplayName = "小恶魔头目";
            nob.MinHp = 82; nob.MaxHp = 86;
            nob.IsElite = true; nob.IsBoss = false;
            nob.StartingStatuses = new List<StartingStatus>();
            nob.Actions = new List<EnemyAction>
            {
                new EnemyAction   // 0
                {
                    Name = "咆哮", Intent = IntentKind.Buff, Weight = 0,
                    Effects = new List<CardEffect>
                    {
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.SelfOnly,
                            Status = Statuses["strength"], Stacks = EffectValue.Flat(2)
                        }
                    }
                },
                new EnemyAction   // 1
                {
                    Name = "重拳", Intent = IntentKind.Attack, Weight = 66, MaxConsecutive = 2,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(14) }
                    }
                },
                new EnemyAction   // 2
                {
                    Name = "跺脚", Intent = IntentKind.AttackDebuff, Weight = 34, MaxConsecutive = 1,
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect { Target = TargetSelector.Chosen, Amount = EffectValue.Flat(8) },
                        new ApplyStatusEffect
                        {
                            Target = TargetSelector.Previous,
                            Status = Statuses["vulnerable"], Stacks = EffectValue.Flat(2)
                        }
                    }
                },
            };
            nob.FixedSequence = new List<int> { 0 };
            nob.LoopSequence = false;
            nob.PhaseHpThresholds = new List<int>();
            nob.CustomBrainType = "";
            EditorUtility.SetDirty(nob);
            Enemies["gremlin_nob"] = nob;
        }

        // ==================================================================== 战斗

        private static void CreateEncounters()
        {
            // ---- 普通战斗（MapGenerator 的 Battle 节点从这些里抽）
            MakeEncounter("slime", "史莱姆", false, false, "slime");
            MakeEncounter("double_slime", "两只史莱姆", false, false, "slime", "slime");
            MakeEncounter("jawworm", "颚虫", false, false, "jawworm");
            MakeEncounter("louse_pack", "虱子群", false, false, "louse", "louse", "louse");
            MakeEncounter("cultist", "邪教徒", false, false, "cultist");
            MakeEncounter("slime_and_louse", "史莱姆与虱子", false, false, "slime", "louse");
            MakeEncounter("worm_and_cultist", "颚虫与邪教徒", false, false, "jawworm", "cultist");

            // ---- 精英（Elite 节点）
            MakeEncounter("mixed", "混合小队", true, false, "jawworm", "slime");
            MakeEncounter("gremlin_nob", "小恶魔头目", true, false, "gremlin_nob");
            MakeEncounter("nob_and_louse", "头目与随从", true, false, "gremlin_nob", "louse");

            // ---- Boss
            MakeEncounter("guardian", "守卫者", false, true, "guardian");
        }

        private static void MakeEncounter(string id, string name, bool elite, bool boss, params string[] enemyIds)
        {
            var so = LoadOrCreate<EncounterDefinition>($"{EncounterDir}/Encounter_{Capitalize(id)}.asset");
            so.Id = id;
            so.DisplayName = name;
            so.IsElite = elite;
            so.IsBoss = boss;
            so.Enemies = new List<EnemyDefinition>();
            for (int i = 0; i < enemyIds.Length; i++)
                if (Enemies.TryGetValue(enemyIds[i], out var e)) so.Enemies.Add(e);
            EditorUtility.SetDirty(so);
            Encounters.Add(so);
        }

        // ==================================================================== 数据库

        private static GameDatabase CreateDatabase()
        {
            var db = LoadOrCreate<GameDatabase>($"{RootDir}/GameDatabase.asset");
            db.Cards = new List<CardDefinition>(Cards.Values);
            db.Statuses = new List<StatusDefinition>(Statuses.Values);
            db.Enemies = new List<EnemyDefinition>(Enemies.Values);
            db.Encounters = new List<EncounterDefinition>(Encounters);
            db.Relics = new List<RelicDefinition>(Relics.Values);
            db.Events = new List<EventDefinition>(Events.Values);
            db.Potions = new List<Potions.PotionDefinition>(PotionDefs.Values);
            db.Keywords = new List<KeywordDefinition>(KeywordDefs);
            db.BuildIndex();
            EditorUtility.SetDirty(db);
            return db;
        }

        // ==================================================================== 工具

        internal static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject => LoadOrCreate<T>(path);

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static void EnsureDir(string dir)
        {
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        internal static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var parts = s.Split('_');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join("", parts);
        }
    }
}
