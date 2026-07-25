using System.Collections.Generic;
using Game.Cards;
using Game.Relics;

namespace Game.Core
{
    /// <summary>
    /// 从数据库里按稀有度随机取卡 / 取遗物。奖励、商店、宝箱、事件全部共用这一份，
    /// 避免「三个地方各写一份稀有度权重」的经典分叉。
    /// ★ 所有随机都必须由调用方指定 RngStream，禁止在这里写死。
    /// </summary>
    public static class ContentPicker
    {
        // 稀有度权重（千分比思路，直接按相对值算即可）
        public const int WeightCommon = 60;
        public const int WeightUncommon = 32;
        public const int WeightRare = 8;

        public static CardRarity RollCardRarity(Rng rng, RngStream stream, int rareBonus = 0)
        {
            int rare = WeightRare + rareBonus;
            if (rare < 0) rare = 0;

            int total = WeightCommon + WeightUncommon + rare;
            int roll = rng.Range(stream, 0, total);

            roll -= rare; if (roll < 0) return CardRarity.Rare;
            roll -= WeightUncommon; if (roll < 0) return CardRarity.Uncommon;
            return CardRarity.Common;
        }

        /// <summary>
        /// 抽 count 张互不相同的卡。若某个稀有度没有内容，会自动退到整个奖励池。
        /// </summary>
        public static void PickCards(Rng rng, GameDatabase db, RngStream stream,
                                     int count, List<CardDefinition> result, int rareBonus = 0)
        {
            result.Clear();
            if (db == null || count <= 0) return;

            var pool = new List<CardDefinition>();
            var fallback = new List<CardDefinition>();
            db.GetCardsByRarity(fallback, null);
            if (fallback.Count == 0) return;

            int guard = 0;
            while (result.Count < count && guard++ < count * 20)
            {
                var rarity = RollCardRarity(rng, stream, rareBonus);
                db.GetCardsByRarity(pool, rarity);
                var source = pool.Count > 0 ? pool : fallback;

                var pick = source[rng.Range(stream, 0, source.Count)];
                if (pick != null && !result.Contains(pick)) result.Add(pick);

                // 池子本身就不够 count 张时提前收手，避免空转
                if (result.Count >= fallback.Count) break;
            }
        }

        /// <summary>
        /// 抽一个玩家还没有的遗物。全都拿过了返回 null。
        /// </summary>
        public static RelicDefinition PickRelic(Rng rng, GameDatabase db, RngStream stream,
                                                RunContext run, RelicRarity? rarity = null)
        {
            if (db == null) return null;

            var pool = new List<RelicDefinition>();
            db.GetRelicsByRarity(pool, rarity);

            // 指定稀有度取不到就退回整个掉落池，宁可给错稀有度也不要给空
            if (pool.Count == 0 && rarity.HasValue) db.GetRelicsByRarity(pool, null);

            for (int i = pool.Count - 1; i >= 0; i--)
                if (run != null && run.HasRelic(pool[i].Id)) pool.RemoveAt(i);

            if (pool.Count == 0) return null;
            return pool[rng.Range(stream, 0, pool.Count)];
        }

        /// <summary>随机取玩家牌库里的 count 张牌（互不相同）。删卡 / 升级卡的随机模式用。</summary>
        public static void PickFromDeck(Rng rng, RunContext run, RngStream stream,
                                        int count, List<CardInstance> result,
                                        System.Func<CardInstance, bool> filter = null)
        {
            result.Clear();
            if (run == null || count <= 0) return;

            var pool = new List<CardInstance>(run.Deck.Count);
            for (int i = 0; i < run.Deck.Count; i++)
            {
                var c = run.Deck[i];
                if (c == null) continue;
                if (filter != null && !filter(c)) continue;
                pool.Add(c);
            }

            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int idx = rng.Range(stream, 0, pool.Count);
                result.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
        }
    }
}
