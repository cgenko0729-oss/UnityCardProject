using System.Collections.Generic;
using Game.Cards;
using Game.Relics;

namespace Game.Core
{
    /// <summary>一份战斗奖励。纯数据，由 RewardScreen 呈现。</summary>
    public class BattleReward
    {
        public int Gold;

        /// <summary>三选一的候选卡。玩家可以一张都不拿。</summary>
        public readonly List<CardDefinition> CardChoices = new List<CardDefinition>(3);

        /// <summary>精英 / Boss 掉落的遗物，可能为 null。</summary>
        public RelicDefinition Relic;

        /// <summary>玩家已经领过卡了（三选一只能拿一张）。</summary>
        public bool CardTaken;
        public bool GoldTaken;
        public bool RelicTaken;

        public bool AllTaken => GoldTaken
                                && (CardTaken || CardChoices.Count == 0)
                                && (RelicTaken || Relic == null);
    }

    /// <summary>
    /// 战斗奖励的生成规则。★ 全部走 <c>RngStream.Reward</c>，
    /// 这样重打一场战斗（消耗 Battle / CardDraw 流）不会改变后续的奖励序列。
    /// </summary>
    public static class RewardGenerator
    {
        public const int NormalGoldMin = 10;
        public const int NormalGoldMax = 21;    // 上界开区间
        public const int EliteGoldMin = 25;
        public const int EliteGoldMax = 36;
        public const int BossGoldMin = 75;
        public const int BossGoldMax = 101;

        public const int CardChoiceCount = 3;

        public static BattleReward Generate(RunContext run, EncounterDefinition encounter)
        {
            var reward = new BattleReward();
            if (run == null) return reward;

            bool elite = encounter != null && encounter.IsElite;
            bool boss = encounter != null && encounter.IsBoss;

            int min = boss ? BossGoldMin : elite ? EliteGoldMin : NormalGoldMin;
            int max = boss ? BossGoldMax : elite ? EliteGoldMax : NormalGoldMax;
            reward.Gold = run.Rng.Range(RngStream.Reward, min, max);

            // 精英战斗稀有卡概率更高
            int rareBonus = boss ? 20 : elite ? 10 : 0;
            ContentPicker.PickCards(run.Rng, run.Database, RngStream.Reward,
                                    CardChoiceCount, reward.CardChoices, rareBonus);

            if (elite || boss)
            {
                var rarity = boss ? RelicRarity.Boss : (RelicRarity?)null;
                reward.Relic = ContentPicker.PickRelic(run.Rng, run.Database, RngStream.Reward, run, rarity);
            }

            return reward;
        }

        /// <summary>宝箱节点的奖励：一个遗物 + 少量金币，没有卡。</summary>
        public static BattleReward GenerateTreasure(RunContext run)
        {
            var reward = new BattleReward();
            if (run == null) return reward;

            reward.Gold = run.Rng.Range(RngStream.Reward, 25, 51);
            reward.Relic = ContentPicker.PickRelic(run.Rng, run.Database, RngStream.Reward, run);
            return reward;
        }
    }
}
