using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 随机流。每条流独立推进，互不干扰——重打一场战斗不会改变后续奖励序列。
    /// 借鉴 Monster Train 的 RngId。
    /// </summary>
    public enum RngStream
    {
        Map = 0,
        Encounter = 1,
        CardDraw = 2,
        Battle = 3,
        EnemyAction = 4,
        Reward = 5,
        Shop = 6,
        Event = 7,
        CardEffect = 8,
        /// <summary>
        /// 药水掉落 / 抽取。★ 单独开一条流而不是复用 Reward：
        /// 复用会让「加了药水」这件事改变所有既有种子的卡牌奖励序列。
        /// 每条流由 Hash(seed, stream+1) 独立起头，新增一条不影响任何旧流。
        /// </summary>
        Potion = 9,
    }

    /// <summary>确定性随机数。xorshift32，可存档可恢复。</summary>
    [Serializable]
    public class Rng
    {
        [Serializable]
        public struct StreamState
        {
            public int Stream;
            public uint State;
        }

        public int MasterSeed;

        private readonly Dictionary<RngStream, uint> _states = new Dictionary<RngStream, uint>();

        public Rng(int masterSeed)
        {
            MasterSeed = masterSeed;
            foreach (RngStream s in Enum.GetValues(typeof(RngStream)))
                _states[s] = Hash((uint)masterSeed, (uint)s + 1u);
        }

        private static uint Hash(uint a, uint b)
        {
            unchecked
            {
                uint h = a * 2654435761u ^ b * 2246822519u;
                h ^= h >> 15;
                h *= 2246822507u;
                h ^= h >> 13;
                return h == 0 ? 1u : h;
            }
        }

        private uint Next(RngStream s)
        {
            unchecked
            {
                if (!_states.TryGetValue(s, out uint x)) x = Hash((uint)MasterSeed, (uint)s + 1u);
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                if (x == 0) x = 1u;
                _states[s] = x;
                return x;
            }
        }

        /// <summary>[minInclusive, maxExclusive) 区间整数。</summary>
        public int Range(RngStream s, int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(Next(s) % (uint)(maxExclusive - minInclusive));
        }

        public bool Chance(RngStream s, int percent) => Range(s, 0, 100) < percent;

        public T Pick<T>(RngStream s, IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[Range(s, 0, list.Count)];
        }

        public void Shuffle<T>(RngStream s, IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(s, 0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        public List<StreamState> Save()
        {
            var l = new List<StreamState>(_states.Count);
            foreach (var kv in _states) l.Add(new StreamState { Stream = (int)kv.Key, State = kv.Value });
            return l;
        }

        public void Restore(List<StreamState> saved)
        {
            if (saved == null) return;
            for (int i = 0; i < saved.Count; i++) _states[(RngStream)saved[i].Stream] = saved[i].State;
        }
    }
}
