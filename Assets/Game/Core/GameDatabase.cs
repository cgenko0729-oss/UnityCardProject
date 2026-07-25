using System.Collections.Generic;
using Game.Cards;
using Game.Enemies;
using Game.Events;
using Game.Relics;
using Game.Statuses;
using UnityEngine;

namespace Game.Core
{
    /// <summary>所有 Definition 的索引。只读，运行时不写入（除首次建索引）。</summary>
    [CreateAssetMenu(menuName = "Game/Database", fileName = "GameDatabase")]
    public class GameDatabase : ScriptableObject
    {
        public List<CardDefinition> Cards = new List<CardDefinition>();
        public List<EnemyDefinition> Enemies = new List<EnemyDefinition>();
        public List<StatusDefinition> Statuses = new List<StatusDefinition>();
        public List<EncounterDefinition> Encounters = new List<EncounterDefinition>();
        public List<RelicDefinition> Relics = new List<RelicDefinition>();
        public List<EventDefinition> Events = new List<EventDefinition>();

        private Dictionary<string, CardDefinition> _cards;
        private Dictionary<string, EnemyDefinition> _enemies;
        private Dictionary<string, StatusDefinition> _statuses;
        private Dictionary<string, EncounterDefinition> _encounters;
        private Dictionary<string, RelicDefinition> _relics;
        private Dictionary<string, EventDefinition> _events;

        public void BuildIndex()
        {
            _cards = Index(Cards, c => c.Id);
            _enemies = Index(Enemies, e => e.Id);
            _statuses = Index(Statuses, s => s.Id);
            _encounters = Index(Encounters, e => e.Id);
            _relics = Index(Relics, r => r.Id);
            _events = Index(Events, e => e.Id);
        }

        private static Dictionary<string, T> Index<T>(List<T> list, System.Func<T, string> idOf)
            where T : ScriptableObject
        {
            var dict = new Dictionary<string, T>(list != null ? list.Count : 0);
            if (list == null) return dict;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                var id = idOf(list[i]);
                if (!string.IsNullOrEmpty(id)) dict[id] = list[i];
            }
            return dict;
        }

        private void EnsureIndex()
        {
            if (_cards == null) BuildIndex();
        }

        public CardDefinition GetCard(string id) { EnsureIndex(); return Get(_cards, id); }
        public EnemyDefinition GetEnemy(string id) { EnsureIndex(); return Get(_enemies, id); }
        public StatusDefinition GetStatus(string id) { EnsureIndex(); return Get(_statuses, id); }
        public EncounterDefinition GetEncounter(string id) { EnsureIndex(); return Get(_encounters, id); }
        public RelicDefinition GetRelic(string id) { EnsureIndex(); return Get(_relics, id); }
        public EventDefinition GetEvent(string id) { EnsureIndex(); return Get(_events, id); }

        private static T Get<T>(Dictionary<string, T> dict, string id) where T : ScriptableObject
            => id != null && dict != null && dict.TryGetValue(id, out var v) ? v : null;

        // ================================================================= 按条件取集合

        /// <summary>取出符合条件的 Encounter Id，供 MapGenerator 用。</summary>
        public void GetEncounterIds(List<string> buffer, bool elite, bool boss)
        {
            buffer.Clear();
            for (int i = 0; i < Encounters.Count; i++)
            {
                var e = Encounters[i];
                if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                if (e.IsElite == elite && e.IsBoss == boss) buffer.Add(e.Id);
            }
        }

        public void GetEventIds(List<string> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < Events.Count; i++)
                if (Events[i] != null && !string.IsNullOrEmpty(Events[i].Id)) buffer.Add(Events[i].Id);
        }

        /// <summary>取出某稀有度的全部卡牌。rarity 为 null 表示「奖励池」（排除 Basic / Special）。</summary>
        public void GetCardsByRarity(List<CardDefinition> buffer, CardRarity? rarity)
        {
            buffer.Clear();
            for (int i = 0; i < Cards.Count; i++)
            {
                var c = Cards[i];
                if (c == null) continue;
                if (rarity.HasValue) { if (c.Rarity == rarity.Value) buffer.Add(c); }
                else if (c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Special) buffer.Add(c);
            }
        }

        /// <summary>取出某稀有度的全部遗物。rarity 为 null 表示「掉落池」（排除 Starter）。</summary>
        public void GetRelicsByRarity(List<RelicDefinition> buffer, RelicRarity? rarity)
        {
            buffer.Clear();
            for (int i = 0; i < Relics.Count; i++)
            {
                var r = Relics[i];
                if (r == null) continue;
                if (rarity.HasValue) { if (r.Rarity == rarity.Value) buffer.Add(r); }
                else if (r.Rarity != RelicRarity.Starter) buffer.Add(r);
            }
        }

        /// <summary>资产被外部工具改动后调用，强制重建索引。</summary>
        public void Invalidate() => _cards = null;
    }
}
