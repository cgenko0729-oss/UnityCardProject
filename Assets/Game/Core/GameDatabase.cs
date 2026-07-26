using System.Collections.Generic;
using Game.Cards;
using Game.Enemies;
using Game.Events;
using Game.Localization;
using Game.Potions;
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
        public List<PotionDefinition> Potions = new List<PotionDefinition>();
        public List<KeywordDefinition> Keywords = new List<KeywordDefinition>();

        /// <summary>
        /// 地图节点的图标。
        ///
        /// ★ 为什么挂在数据库上而不是像别的美术那样挂在 Definition 里：
        ///   节点类型是个**枚举**（<c>MapNodeType</c>），根本没有对应的 ScriptableObject。
        ///   为 7 个固定类型各造一个 SO 只会多出 7 个几乎空白的资产和一套要维护的 Id，
        ///   而它们本来就是一组一起换的东西。
        ///
        /// <para>留空的类型继续用现在的符号（⚔ ☠ ♨ …）。</para>
        /// </summary>
        public List<MapNodeIcon> MapIcons = new List<MapNodeIcon>();

        /// <summary>
        /// 翻译表，一种语言一张。
        /// ★ 简体中文<b>不在</b>这里——它是源语言，文案就写在代码与各 Definition 里。
        /// </summary>
        public List<LocaleTable> Locales = new List<LocaleTable>();

        private Dictionary<string, CardDefinition> _cards;
        private Dictionary<string, EnemyDefinition> _enemies;
        private Dictionary<string, StatusDefinition> _statuses;
        private Dictionary<string, EncounterDefinition> _encounters;
        private Dictionary<string, RelicDefinition> _relics;
        private Dictionary<string, EventDefinition> _events;
        private Dictionary<string, PotionDefinition> _potions;

        /// <summary>关键字用枚举位当键，不像其余定义那样用字符串 Id。</summary>
        private Dictionary<CardKeyword, KeywordDefinition> _keywords;

        public void BuildIndex()
        {
            _potions = Index(Potions, p => p.Id);
            _cards = Index(Cards, c => c.Id);
            _enemies = Index(Enemies, e => e.Id);
            _statuses = Index(Statuses, s => s.Id);
            _encounters = Index(Encounters, e => e.Id);
            _relics = Index(Relics, r => r.Id);
            _events = Index(Events, e => e.Id);

            _keywords = new Dictionary<CardKeyword, KeywordDefinition>(Keywords != null ? Keywords.Count : 0);
            if (Keywords != null)
            {
                for (int i = 0; i < Keywords.Count; i++)
                {
                    var k = Keywords[i];
                    // 只收单一位的定义。组合值（Exhaust | Retain）反查时永远匹配不到单个位，
                    // 收进来只会让「有定义却查不到」变成一个难查的谜；校验器会另行报错。
                    if (k != null && k.IsSingleBit) _keywords[k.Keyword] = k;
                }
            }
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
        public PotionDefinition GetPotion(string id) { EnsureIndex(); return Get(_potions, id); }

        /// <summary>
        /// 按语言标签取翻译表。传源语言（zh-Hans）或未知标签一律返回 null，
        /// 而 <c>Loc.Use(null)</c> 就是「切回源语言」，因此调用点不需要特判。
        /// </summary>
        public LocaleTable GetLocale(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode) || languageCode == Loc.SourceLanguage) return null;
            for (int i = 0; i < Locales.Count; i++)
            {
                var t = Locales[i];
                if (t != null && t.LanguageCode == languageCode) return t;
            }
            return null;
        }

        /// <summary>取某类地图节点的图标。没配就返回 null，调用方退回符号文字。</summary>
        public Sprite GetMapIcon(Game.Map.MapNodeType type)
        {
            for (int i = 0; i < MapIcons.Count; i++)
                if (MapIcons[i].Type == type && MapIcons[i].Icon != null) return MapIcons[i].Icon;
            return null;
        }

        /// <summary>按单个关键字位取定义。传组合值恒返回 null。</summary>
        public KeywordDefinition GetKeyword(CardKeyword keyword)
        {
            EnsureIndex();
            return _keywords != null && _keywords.TryGetValue(keyword, out var v) ? v : null;
        }

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

        /// <summary>取出某稀有度的全部药水。rarity 为 null 表示整个掉落池。</summary>
        public void GetPotionsByRarity(List<PotionDefinition> buffer, PotionRarity? rarity)
        {
            buffer.Clear();
            for (int i = 0; i < Potions.Count; i++)
            {
                var p = Potions[i];
                if (p == null) continue;
                if (!rarity.HasValue || p.Rarity == rarity.Value) buffer.Add(p);
            }
        }

        /// <summary>资产被外部工具改动后调用，强制重建索引。</summary>
        public void Invalidate() => _cards = null;
    }

    /// <summary>一种地图节点类型对应的图标。见 <see cref="GameDatabase.MapIcons"/>。</summary>
    [System.Serializable]
    public struct MapNodeIcon
    {
        public Game.Map.MapNodeType Type;
        public Sprite Icon;
    }
}
