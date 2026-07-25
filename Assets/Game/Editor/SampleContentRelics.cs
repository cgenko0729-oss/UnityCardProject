using System.Collections.Generic;
using Game.Cards;
using Game.Relics;
using Game.Relics.Impl;
using Game.Statuses;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 遗物示例内容。拆成单独的文件，避免 SampleContentGenerator 继续膨胀
    /// （阶段 3 结束时它已经是全工程最大的单文件了）。
    ///
    /// 这批遗物刻意覆盖了阶段 4 新增的四个 Hook 拦截点，既是内容也是回归样本：
    ///   IResourceHook  → 备战包 / 提灯
    ///   ICardFlowHook  → 回响护符（PreCardPlay）、回收器（ModifyCardDestination）
    ///   IStatusHook    → 神器图腾（经由「神器」状态）
    ///   IFatalHook     → 蜥蜴尾巴（经由「回光」状态）
    /// </summary>
    internal static class SampleContentRelics
    {
        internal static void CreateRelics(string dir,
                                          Dictionary<string, StatusDefinition> statuses,
                                          Dictionary<string, RelicDefinition> output)
        {
            // ---- 起始遗物
            Make(dir, output, "burning_blood", "燃烧之血", RelicRarity.Starter,
                "战斗胜利后回复 6 点生命。",
                new BattleRewardBehaviour { HealOnVictory = 6 });

            // ---- 普通
            Make(dir, output, "vajra", "金刚杵", RelicRarity.Common,
                "每场战斗开始时获得 1 点力量。",
                new GrantStatusOnBattleStartBehaviour { Status = statuses["strength"], Stacks = 1 });

            Make(dir, output, "anchor", "铁锚", RelicRarity.Common,
                "每场战斗的第一回合获得 10 点护甲。",
                new BattleStartResourceBehaviour { Block = 10 });

            Make(dir, output, "blood_vial", "血瓶", RelicRarity.Common,
                "每场战斗开始时回复 2 点生命。",
                new BattleStartResourceBehaviour { Heal = 2 });

            Make(dir, output, "bronze_scales", "青铜鳞片", RelicRarity.Common,
                "每场战斗开始时获得 3 层荆棘。",
                new GrantStatusOnBattleStartBehaviour { Status = statuses["thorns"], Stacks = 3 });

            // ---- 罕见：开始改变规则
            Make(dir, output, "bag_of_preparation", "备战包", RelicRarity.Uncommon,
                "每场战斗的第一回合额外抽 2 张牌。",
                new TurnResourceBehaviour { ExtraDraw = 2, FirstTurnOnly = true });

            Make(dir, output, "lantern", "提灯", RelicRarity.Uncommon,
                "每场战斗的第一回合额外获得 1 点能量。",
                new TurnResourceBehaviour { ExtraEnergy = 1, FirstTurnOnly = true });

            Make(dir, output, "pen_nib", "笔尖", RelicRarity.Uncommon,
                "每回合打出的第一张攻击牌费用 -1。",
                new FirstCardCostReductionBehaviour { CardType = CardType.Attack, Reduction = 1 });

            Make(dir, output, "artifact_totem", "神器图腾", RelicRarity.Uncommon,
                "每场战斗开始时获得 1 层神器（抵消下一次施加到你身上的减益）。",
                new GrantStatusOnBattleStartBehaviour { Status = statuses["artifact"], Stacks = 1 });

            Make(dir, output, "meditation_beads", "冥想念珠", RelicRarity.Uncommon,
                "每打出 3 张技能牌，回复 2 点生命。",
                new EveryNCardsHealBehaviour { CardType = CardType.Skill, Threshold = 3, HealAmount = 2 });

            // ---- 稀有：显著改变构筑方向
            Make(dir, output, "echo_charm", "回响护符", RelicRarity.Rare,
                "每回合打出的第一张攻击牌会额外结算一次。",
                new EchoFirstCardBehaviour { CardType = CardType.Attack });

            Make(dir, output, "recycler", "回收器", RelicRarity.Rare,
                "打出的技能牌洗回抽牌堆，而不是进入弃牌堆。",
                new CardDestinationBehaviour { CardType = CardType.Skill, Destination = CardPile.Draw });

            Make(dir, output, "lizard_tail", "蜥蜴尾巴", RelicRarity.Rare,
                "每场战斗中，第一次受到致死伤害时保留 1 点生命，并回复 10 点生命。",
                new GrantStatusOnBattleStartBehaviour { Status = statuses["revive"], Stacks = 1 });

            // ---- Boss 掉落
            Make(dir, output, "black_star", "黑星", RelicRarity.Boss,
                "每回合额外获得 1 点能量。",
                new TurnResourceBehaviour { ExtraEnergy = 1 });

            Make(dir, output, "philosophers_stone", "贤者之石", RelicRarity.Boss,
                "每回合额外抽 1 张牌。",
                new TurnResourceBehaviour { ExtraDraw = 1 });

            // ---- 商店专属
            var toolbox = Make(dir, output, "toolbox", "工具箱", RelicRarity.Shop,
                "战斗胜利后额外获得 12 金币。",
                new BattleRewardBehaviour { HealOnVictory = 0, GoldOnVictory = 12 });
            toolbox.ShopPrice = 180;
            EditorUtility.SetDirty(toolbox);
        }

        private static RelicDefinition Make(string dir, Dictionary<string, RelicDefinition> output,
                                            string id, string name, RelicRarity rarity, string desc,
                                            params StatusBehaviour[] behaviours)
        {
            var so = SampleContentGenerator.LoadOrCreateAsset<RelicDefinition>(
                $"{dir}/Relic_{SampleContentGenerator.Capitalize(id)}.asset");
            so.Id = id;
            so.DisplayName = name;
            so.Rarity = rarity;
            so.Description = desc;
            so.ShopPrice = 0;
            so.Behaviours = new List<StatusBehaviour>(behaviours);
            EditorUtility.SetDirty(so);
            output[id] = so;
            return so;
        }
    }
}
