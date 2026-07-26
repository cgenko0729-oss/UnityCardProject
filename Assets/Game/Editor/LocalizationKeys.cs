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
        /// 匹配 <c>Loc.T("key", "源文"</c>。字符串体允许转义（<c>\"</c> / <c>\n</c>）。
        ///
        /// ★ 只认「源文**紧跟在** key 后面」这一种形状，中间除空白不许有别的东西。
        ///   <c>Loc.T</c> 的签名就是 <c>(string key, string fallback, params object[] args)</c>，
        ///   第二个参数永远是源文，所以这个约束不会漏掉任何合法写法。
        ///
        /// ★ 原来的写法是「先抓到调用的右括号，再去括号里找第一个字符串」，
        ///   而「找到匹配的右括号」正则本来就做不到——括号会嵌套。
        ///   结果是惰性的 rest 一路吃到下一个 <c>);</c>，
        ///   于是**一行里第二个及以后的 Loc.T 会被第一个整个吞掉**：
        ///   <code>a ? Loc.T("ui.battle.victory", "…") : Loc.T("ui.battle.defeat", "…");</code>
        ///   只有 victory 进得了清单。全工程有 10 行是这种写法，涉及 24 条 key。
        ///
        ///   它们当时都碰巧已经有译文，所以没坏——真正的风险在将来：
        ///   **任何新加在第二个位置的文案都不会进待翻译清单**，
        ///   表现是那句话在别的语言下永远是中文，且不报任何错。
        ///   这正是本类存在的理由（见类注释里「为什么扫源码而不是维护注册表」），
        ///   也正是铁律 40 想防的失效模式——扫描器自己漏了，比注册表漏了更难发现。
        /// </summary>
        private static readonly Regex LocT = new Regex(
            @"Loc\.T\s*\(\s*""(?<key>(?:[^""\\]|\\.)*)""\s*,\s*""(?<src>(?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);

        /// <summary>
        /// 匹配 <c>Loc.TPlural("key", n, "单数", "复数"</c>。
        /// ★ 中间那个 n 是整数表达式，**不可能含字符串字面量**，
        ///   所以用「不含引号也不含分号」把它框住就够精确，不会越过语句边界去抓别处的字符串。
        /// ★ <c>Loc\.T</c> 不会误匹配到 <c>Loc.TPlural</c>：后者的 T 之后是 P，不是空白或左括号。
        /// </summary>
        private static readonly Regex LocTPlural = new Regex(
            @"Loc\.TPlural\s*\(\s*""(?<key>(?:[^""\\]|\\.)*)""\s*,\s*[^"";]*?,\s*""(?<one>(?:[^""\\]|\\.)*)""\s*,\s*""(?<other>(?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);

        /// <summary>行注释与块注释。★ 见 <see cref="StripComments"/>。</summary>
        private static readonly Regex Comments = new Regex(
            @"/\*.*?\*/|//[^\r\n]*", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// 扫描前先把注释去掉。
        ///
        /// ★ 不去的话，文档注释里写的示例（本文件顶部那句 <c>Loc.T("key", "原文")</c>）
        ///   会被当成真调用收进待翻译清单，于是表里凭空多出一条叫「key」的文案。
        ///   这条谁也不知道是什么、翻不翻都没影响，但它会一直待在那儿，
        ///   并且让「还有几条没翻译」这个数字永远差一。
        ///
        /// ★ 这是个粗糙的剥法——字符串字面量里的 <c>"http://…"</c> 会被误当成注释开头。
        ///   对本工程够用（没有这种字面量），真出问题时表现是某条 key 收不到，
        ///   而校验器的「缺翻译」警告会把它顶出来。
        /// </summary>
        private static string StripComments(string source) => Comments.Replace(source, " ");

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
                try { text = StripComments(File.ReadAllText(file)); }
                catch (IOException) { continue; }

                string rel = file.Replace('\\', '/');
                int idx = rel.LastIndexOf("/Assets/", StringComparison.Ordinal);
                if (idx >= 0) rel = rel.Substring(idx + 1);

                // ★ 两条正则各扫一遍，互不干扰。
                //   源文不是字面量（用变量拼的）的调用两边都匹配不上，于是被跳过——
                //   这是**故意**的，铁律 40 要求 key 与源文都写成字面量，
                //   拼出来的那条本来就进不了任何语言的清单。
                foreach (Match m in LocT.Matches(text))
                    Add(list, seen, Unescape(m.Groups["key"].Value), Unescape(m.Groups["src"].Value), rel);

                foreach (Match m in LocTPlural.Matches(text))
                {
                    string key = Unescape(m.Groups["key"].Value);
                    Add(list, seen, key + ".one", Unescape(m.Groups["one"].Value), rel);
                    Add(list, seen, key + ".other", Unescape(m.Groups["other"].Value), rel);
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
