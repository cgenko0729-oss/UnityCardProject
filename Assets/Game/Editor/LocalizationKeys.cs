using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Game.Cards;
using Game.Core;
using Game.Enemies;
using Game.Events;
using Game.Potions;
using Game.Relics;
using Game.Statuses;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>一条待翻译的文案：key + 简中原文 + 它是从哪来的（给译者定位用）。</summary>
    public struct LocSourceEntry
    {
        public string Key;
        public string Source;
        public string Origin;

        public LocSourceEntry(string key, string source, string origin)
        {
            Key = key; Source = source; Origin = origin;
        }
    }

    /// <summary>
    /// 全工程「需要翻译的文案」的唯一清单来源。
    ///
    /// 两个来源，缺一不可：
    /// <list type="number">
    /// <item>SO 资产里的文案——key 由 Id 派生，直接遍历资产就能拿全。</item>
    /// <item>代码里的 <c>Loc.T("key", "原文")</c>——这些 key 只存在于源码里，
    ///       运行时无法枚举。所以这里<b>扫源文件</b>。</item>
    /// </list>
    ///
    /// ★ 为什么扫源码而不是维护一张「UI key 注册表」：
    ///   注册表要靠人在每次加一句文案时同步更新，而漏掉的表现是
    ///   「这一句永远不会出现在待翻译清单里」——没有任何报错，
    ///   只有某个语言下突然冒出一句中文。扫源码没有这个失效模式。
    /// </summary>
    public static class LocalizationKeys
    {
        /// <summary>
        /// 匹配 <c>Loc.T("key", "源文"</c> 与 <c>Loc.TPlural("key", n, "单数", "复数"</c>。
        /// 字符串体允许转义（<c>\"</c> / <c>\n</c>）。
        /// </summary>
        private static readonly Regex LocCall = new Regex(
            @"Loc\.(?<m>TPlural|T)\s*\(\s*""(?<key>(?:[^""\\]|\\.)*)""\s*,\s*(?<rest>.*?)(?=\)\s*[;,\)\+]|\)\s*$)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex FirstString = new Regex(
            @"""(?<s>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        public static List<LocSourceEntry> CollectAll()
        {
            var list = new List<LocSourceEntry>(700);
            var seen = new HashSet<string>();

            CollectFromAssets(list, seen);
            CollectFromSource(list, seen);

            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        // ============================================================ SO 资产

        private static void CollectFromAssets(List<LocSourceEntry> list, HashSet<string> seen)
        {
            foreach (var c in ContentValidator.LoadAllPublic<CardDefinition>())
            {
                Add(list, seen, $"card.{c.Id}.name", c.DisplayName, $"卡牌 {c.name}");
                Add(list, seen, $"card.{c.Id}.desc", c.DescriptionTemplate, $"卡牌 {c.name}");
            }

            foreach (var s in ContentValidator.LoadAllPublic<StatusDefinition>())
            {
                Add(list, seen, $"status.{s.Id}.name", s.DisplayName, $"状态 {s.name}");
                Add(list, seen, $"status.{s.Id}.desc", s.Description, $"状态 {s.name}");
            }

            foreach (var r in ContentValidator.LoadAllPublic<RelicDefinition>())
            {
                Add(list, seen, $"relic.{r.Id}.name", r.DisplayName, $"遗物 {r.name}");
                Add(list, seen, $"relic.{r.Id}.desc", r.Description, $"遗物 {r.name}");
            }

            foreach (var p in ContentValidator.LoadAllPublic<PotionDefinition>())
            {
                Add(list, seen, $"potion.{p.Id}.name", p.DisplayName, $"药水 {p.name}");
                Add(list, seen, $"potion.{p.Id}.desc", p.DescriptionTemplate, $"药水 {p.name}");
            }

            foreach (var k in ContentValidator.LoadAllPublic<KeywordDefinition>())
            {
                if (!k.IsSingleBit) continue;
                string stem = "keyword." + k.Keyword.ToString().ToLowerInvariant();
                Add(list, seen, stem + ".name", k.DisplayName, $"关键字 {k.name}");
                Add(list, seen, stem + ".desc", k.Description, $"关键字 {k.name}");
            }

            foreach (var e in ContentValidator.LoadAllPublic<EnemyDefinition>())
            {
                Add(list, seen, $"enemy.{e.Id}.name", e.DisplayName, $"敌人 {e.name}");
                if (e.Actions == null) continue;
                for (int i = 0; i < e.Actions.Count; i++)
                    Add(list, seen, $"enemy.{e.Id}.action.{i}.name", e.Actions[i].Name, $"敌人 {e.name} 行动 {i}");
            }

            foreach (var e in ContentValidator.LoadAllPublic<EncounterDefinition>())
                Add(list, seen, $"encounter.{e.Id}.name", e.DisplayName, $"战斗 {e.name}");

            foreach (var e in ContentValidator.LoadAllPublic<EventDefinition>())
            {
                Add(list, seen, $"event.{e.Id}.title", e.Title, $"事件 {e.name}");
                Add(list, seen, $"event.{e.Id}.desc", e.Description, $"事件 {e.name}");
                if (e.Options == null) continue;
                for (int i = 0; i < e.Options.Count; i++)
                {
                    var o = e.Options[i];
                    Add(list, seen, $"event.{e.Id}.option.{i}.text", o.Text, $"事件 {e.name} 选项 {i}");
                    Add(list, seen, $"event.{e.Id}.option.{i}.hint", o.DisabledHint, $"事件 {e.name} 选项 {i}");
                    Add(list, seen, $"event.{e.Id}.option.{i}.result", o.ResultText, $"事件 {e.name} 选项 {i}");
                }
            }
        }

        // ============================================================ 源码

        private static void CollectFromSource(List<LocSourceEntry> list, HashSet<string> seen)
        {
            string root = Path.Combine(Application.dataPath, "Game");
            if (!Directory.Exists(root)) return;

            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }

                string rel = file.Replace('\\', '/');
                int idx = rel.LastIndexOf("/Assets/", StringComparison.Ordinal);
                if (idx >= 0) rel = rel.Substring(idx + 1);

                foreach (Match m in LocCall.Matches(text))
                {
                    string key = Unescape(m.Groups["key"].Value);
                    string rest = m.Groups["rest"].Value;

                    if (m.Groups["m"].Value == "TPlural")
                    {
                        // TPlural(key, n, 单数, 复数, ...) —— rest 里的头两个字符串就是那两条源文
                        var strings = new List<string>(2);
                        foreach (Match sm in FirstString.Matches(rest))
                        {
                            strings.Add(Unescape(sm.Groups["s"].Value));
                            if (strings.Count == 2) break;
                        }
                        if (strings.Count >= 1) Add(list, seen, key + ".one", strings[0], rel);
                        if (strings.Count >= 2) Add(list, seen, key + ".other", strings[1], rel);
                        continue;
                    }

                    var first = FirstString.Match(rest);
                    if (!first.Success) continue;   // 源文不是字面量（变量拼的），没法收
                    Add(list, seen, key, Unescape(first.Groups["s"].Value), rel);
                }
            }
        }

        private static string Unescape(string s)
            => s.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");

        private static void Add(List<LocSourceEntry> list, HashSet<string> seen,
                                string key, string source, string origin)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(source)) return;
            if (!seen.Add(key)) return;
            list.Add(new LocSourceEntry(key, source, origin));
        }

        // ============================================================ 占位符

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        /// <summary>
        /// 取出一段文案里用到的占位符下标集合。
        /// ★ 校验器拿它做「原文与译文的占位符必须一致」的检查——
        ///   译者把 {0} 翻没了或写成全角，中文下一切正常，切到那个语言才当场炸。
        /// </summary>
        public static SortedSet<int> PlaceholdersOf(string text)
        {
            var set = new SortedSet<int>();
            if (string.IsNullOrEmpty(text)) return set;
            foreach (Match m in Placeholder.Matches(text))
                if (int.TryParse(m.Groups[1].Value, out int n)) set.Add(n);
            return set;
        }

        public static string Describe(SortedSet<int> set)
        {
            if (set.Count == 0) return "（无）";
            var sb = new StringBuilder();
            foreach (var n in set) { if (sb.Length > 0) sb.Append(' '); sb.Append('{').Append(n).Append('}'); }
            return sb.ToString();
        }
    }
}
