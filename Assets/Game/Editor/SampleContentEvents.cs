using System.Collections.Generic;
using Game.Events;
using Game.RunEffects;
using Game.RunEffects.Impl;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 事件示例内容。★ 这批事件全部只用配置，没有一行专属代码——
    /// 这正是引入 RunEffect 多态体系要买到的东西：加事件 = 建资产。
    /// </summary>
    internal static class SampleContentEvents
    {
        internal static void CreateEvents(string dir, Dictionary<string, EventDefinition> output)
        {
            // ---- ① 纯取舍：血换牌
            Make(dir, output, "shrine_of_blood", "血之神龛",
                "石台上刻着一行字：「以痛楚换取力量。」\n凹槽里还残留着暗红色的痕迹。",
                new EventOption
                {
                    Text = "割破手掌",
                    ResultText = "刺痛之后，你感到某种东西被点燃了。",
                    Effects = new List<RunEffect>
                    {
                        new HpRunEffect { Amount = -12, PercentOfMax = false },
                        // Card 留空 + PlayerChooses = 抽 3 张候选让玩家挑 1 张
                        new AddCardRunEffect { Count = 3, PlayerChooses = true },
                    },
                },
                new EventOption
                {
                    Text = "离开",
                    ResultText = "你决定不去碰它。",
                    Effects = new List<RunEffect>(),
                });

            // ---- ② 花钱办事：删卡
            Make(dir, output, "wandering_smith", "游方铁匠",
                "一个背着巨大工具箱的老人拦住了你。\n「想让你的家伙什更趁手些吗？当然，不白干。」",
                new EventOption
                {
                    Text = "打磨一件（50 金币）",
                    Condition = new RunCondition { Kind = RunConditionKind.GoldAtLeast, Value = 50 },
                    DisabledHint = "金币不足",
                    ResultText = "老人叮叮当当敲了一阵。",
                    Effects = new List<RunEffect>
                    {
                        new GoldRunEffect { Amount = -50 },
                        new UpgradeCardRunEffect { Count = 1, PlayerChooses = true },
                    },
                },
                new EventOption
                {
                    Text = "丢掉累赘（75 金币）",
                    Condition = new RunCondition { Kind = RunConditionKind.GoldAtLeast, Value = 75 },
                    DisabledHint = "金币不足",
                    ResultText = "「这玩意儿早该扔了。」",
                    Effects = new List<RunEffect>
                    {
                        new GoldRunEffect { Amount = -75 },
                        new RemoveCardRunEffect { Count = 1, PlayerChooses = true },
                    },
                },
                new EventOption
                {
                    Text = "谢绝",
                    Effects = new List<RunEffect>(),
                });

            // ---- ③ 赌博：演示 RandomPickRunEffect
            Make(dir, output, "gambling_ghost", "赌鬼幽魂",
                "一团半透明的影子在空中掷着骰子。\n「押一把？输赢各半，但赢的话——嘿嘿。」",
                new EventOption
                {
                    Text = "押上 50 金币",
                    Condition = new RunCondition { Kind = RunConditionKind.GoldAtLeast, Value = 50 },
                    DisabledHint = "金币不足",
                    Effects = new List<RunEffect>
                    {
                        new GoldRunEffect { Amount = -50 },
                        new RandomPickRunEffect
                        {
                            Options = new List<RandomPickRunEffect.Option>
                            {
                                new RandomPickRunEffect.Option
                                {
                                    Note = "赢", Weight = 50,
                                    Effects = new List<RunEffect> { new GoldRunEffect { Amount = 130 } },
                                },
                                new RandomPickRunEffect.Option
                                {
                                    Note = "输", Weight = 50,
                                    Effects = new List<RunEffect>(),
                                },
                            },
                        },
                    },
                },
                new EventOption
                {
                    Text = "不赌",
                    ResultText = "影子发出失望的嘘声，消散了。",
                    Effects = new List<RunEffect>(),
                });

            // ---- ④ 条件分支：演示 ConditionalRunEffect
            Make(dir, output, "hidden_spring", "隐秘泉眼",
                "岩壁的裂缝里渗出温热的泉水，水面泛着微光。",
                new EventOption
                {
                    Text = "痛饮",
                    ResultText = "泉水带着铁锈味，但确实有效。",
                    Effects = new List<RunEffect>
                    {
                        new ConditionalRunEffect
                        {
                            // 残血时给更多回复：事件的价值应该随处境变化，否则永远是同一个最优解
                            Condition = new RunCondition { Kind = RunConditionKind.HpBelowPercent, Value = 50 },
                            Then = new List<RunEffect> { new RestHealRunEffect { PercentOfMax = 40 } },
                            Else = new List<RunEffect> { new MaxHpRunEffect { Amount = 5, AlsoHeal = true } },
                        },
                    },
                },
                new EventOption
                {
                    Text = "装满水囊后离开",
                    ResultText = "也许以后用得上。",
                    Effects = new List<RunEffect> { new GoldRunEffect { Amount = 25 } },
                });

            // ---- ⑤ 风险事件：演示 StartBattleRunEffect
            Make(dir, output, "sleeping_guardian", "沉睡的守卫",
                "一个巨大的身影靠在墙边一动不动。\n它脚边散落着不少金币，还有一个包裹。",
                new EventOption
                {
                    Text = "悄悄拿走金币",
                    ResultText = "你屏住呼吸，收获颇丰。",
                    Effects = new List<RunEffect> { new GoldRunEffect { Amount = 90 } },
                },
                new EventOption
                {
                    Text = "打开那个包裹",
                    ResultText = "包裹里是个好东西——但你弄出了声响。",
                    Effects = new List<RunEffect>
                    {
                        new GainRelicRunEffect { AnyRarity = true },
                        new StartBattleRunEffect { EncounterId = "", GiveReward = true },
                    },
                },
                new EventOption
                {
                    Text = "绕开它",
                    Effects = new List<RunEffect>(),
                });

            // ---- ⑥ 纯收益：给玩家喘息
            Make(dir, output, "abandoned_camp", "废弃营地",
                "熄灭的火堆旁还留着一些补给，看来主人走得很急。",
                new EventOption
                {
                    Text = "搜刮补给",
                    Effects = new List<RunEffect>
                    {
                        new GoldRunEffect { Amount = 35 },
                        new HpRunEffect { Amount = 8 },
                    },
                });
        }

        private static EventDefinition Make(string dir, Dictionary<string, EventDefinition> output,
                                            string id, string title, string desc, params EventOption[] options)
        {
            var so = SampleContentGenerator.LoadOrCreateAsset<EventDefinition>(
                $"{dir}/Event_{SampleContentGenerator.Capitalize(id)}.asset");
            so.Id = id;
            so.Title = title;
            so.Description = desc;
            so.MinRow = 0;
            so.Options = new List<EventOption>(options);
            EditorUtility.SetDirty(so);
            output[id] = so;
            return so;
        }
    }
}
