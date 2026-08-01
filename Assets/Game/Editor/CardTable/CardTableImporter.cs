using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Cards;
using Game.Effects;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.CardTables
{
    /// <summary>
    /// <c>CardTable.json</c> → <c>Assets/GameData/Cards/Authored/*.asset</c> 的编译器。
    ///
    /// <para><b>所有权规则</b>（使用者拍板：表是唯一事实来源，单向）：
    /// <see cref="OutDir"/> 这个目录**完全归本导入器所有**。
    /// 表里删掉一张卡，下次导入就会把对应资产从磁盘删掉——
    /// 否则按 ProjectUseGuide 2.4 的删除规则，那个 <c>.asset</c> 会作为「手工资产」
    /// 被重新发现并继续留在游戏里，表现是「我明明把它删了，它还在」。</para>
    ///
    /// <para><b>为什么要独占一个子目录</b>：使用者选择让现有 57 张卡继续留在
    /// <c>SampleContent*.cs</c> 里，所以生成器和本导入器必须物理隔离——
    /// 生成器写 <c>Cards/</c> 根目录，我们写 <c>Cards/Authored/</c>。
    /// 共用一个目录的话，「删掉表里没有的资产」会把 57 张生成卡全部删掉。
    /// 子目录仍会被 <c>MergeGeneratedAndDiscovered</c> 递归发现（ProjectUseGuide 2.1），
    /// 所以隔离不影响它们进入 GameDatabase。</para>
    ///
    /// <para><b>「上次导入产出了哪些资产」靠扫目录回答，不维护清单文件。</b>
    /// 这是铁律 56 的同一条：清单是跨会话状态，漏一条就是一个永远删不掉的幽灵资产，
    /// 而且不报任何错。扫目录没有状态，天然自洽。</para>
    /// </summary>
    public static class CardTableImporter
    {
        private const string TablePath = "Assets/GameData/CardTable.json";
        private const string OutDir = "Assets/GameData/Cards/Authored";

        /// <summary>
        /// 「Assets/…」→ 绝对路径。
        ///
        /// ★★ 所有 <c>System.IO</c> 调用都必须走这里，**不能直接把 "Assets/…" 交给
        ///   File / Directory**。<c>-batchmode</c> 下进程的工作目录不是工程根目录，
        ///   于是 <c>File.Exists("Assets/GameData/CardTable.json")</c> 恒为 false——
        ///   <see cref="ImportBatch"/> 会「成功」地创建一张空表、把 Authored 目录里
        ///   的卡全部当成孤儿删掉，然后报告导入成功。
        ///   这是 <see cref="ContentValidator"/> 里那条目录回退注释点名的同一个坑（铁律 15 的邻居）。
        ///
        /// <para>注意 <c>AssetDatabase</c> 的 API 反过来——它只接受 "Assets/…" 相对路径，
        /// 所以两种路径必须同时存在，不能统一。</para>
        /// </summary>
        private static string Abs(string assetPath)
        {
            const string prefix = "Assets/";
            string rel = assetPath.StartsWith(prefix, StringComparison.Ordinal)
                ? assetPath.Substring(prefix.Length)
                : assetPath;

            return Path.Combine(Application.dataPath, rel).Replace('\\', '/');
        }

        [MenuItem("Tools/卡牌游戏/7. 导入卡表", priority = 7)]
        public static void Import()
        {
            var report = new StringBuilder();
            bool ok = Run(report, out int cardCount);

            if (ok)
            {
                Debug.Log($"[CardTable] 导入成功：{cardCount} 张卡。\n{report}");
                EditorUtility.DisplayDialog("卡表导入成功",
                    $"共 {cardCount} 张卡已写入 {OutDir} 并登记进 GameDatabase。", "好");
            }
            else
            {
                Debug.LogError($"[CardTable] 导入失败：\n{report}");
                EditorUtility.DisplayDialog("卡表导入失败", "详情见 Console。", "好");
            }
        }

        /// <summary>命令行 / CI 入口。有错误时退出码非 0，形状与 ContentValidator.ValidateBatch 一致。</summary>
        public static void ImportBatch()
        {
            var report = new StringBuilder();
            bool ok = Run(report, out int cardCount);

            Debug.Log($"[CardTable] 导入{(ok ? "成功" : "失败")}：{cardCount} 张卡。\n{report}");

            if (!ok) EditorApplication.Exit(1);
        }

        // ================================================================== 主流程

        private static bool Run(StringBuilder report, out int cardCount)
        {
            cardCount = 0;

            // ---------------------------------------------------------- 读表
            if (!File.Exists(Abs(TablePath)))
            {
                CreateEmptyTable();
                report.AppendLine($"{TablePath} 不存在，已创建一张空表。往里面加卡后重新导入。");
                return true;
            }

            CardTable table;
            try
            {
                // ★ 必须在解析之前作废索引：本次导入会创建新资产，而后面的行可能引用它们。
                //   索引跨导入存活的话，第一次导入某个引用会解析失败（或解析到旧资产）。
                AssetIndex.Invalidate();
                table = CardTableJson.FromJson(File.ReadAllText(Abs(TablePath), Encoding.UTF8));
            }
            catch (CardTableFormatException e)
            {
                report.AppendLine(e.Message);
                return false;
            }
            catch (Exception e)
            {
                report.AppendLine($"读表时发生意外错误：{e}");
                return false;
            }

            // ---------------------------------------------------------- 展开升级版
            //
            // 一行卡可能产出两个资产（基础版 + _plus）。后面所有步骤都只认这份展开后的列表。
            List<CardRow> rows;
            try
            {
                rows = Expand(table);
            }
            catch (CardTableFormatException e)
            {
                report.AppendLine(e.Message);
                return false;
            }

            // ---------------------------------------------------------- 落盘前的硬检查
            //
            // ★ 这三条必须在**碰磁盘之前**拦住，因为它们的后果是「静默丢卡」：
            //   同 Id 冲突时 MergeGeneratedAndDiscovered 只在 Console 打一条 LogError 就跳过
            //   （ProjectUseGuide 2.4 规则 4），淹在日志里没人看见，
            //   表现是「我明明加了这张卡，游戏里没有」。
            if (!PreflightIds(rows, report)) return false;

            // ---------------------------------------------------------- 写资产
            EnsureDir(OutDir);

            var produced = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
            var assetPaths = new HashSet<string>(StringComparer.Ordinal);

            // 先把全部资产的壳建出来（或加载已存在的），这样同一张表里的互相引用
            // （B 卡的 addCard 指向本次新建的 A 卡）才解析得到。
            foreach (var row in rows)
            {
                string path = $"{OutDir}/Card_{PascalCase(row.Id)}.asset";
                assetPaths.Add(path);
                produced[row.Id] = LoadOrCreate(path);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var so = produced[row.Id];

                CardDefinition upgraded = null;
                if (!string.IsNullOrEmpty(row.UpgradeTargetId))
                    produced.TryGetValue(row.UpgradeTargetId, out upgraded);

                try
                {
                    Apply(row, so, upgraded);
                }
                catch (CardTableFormatException e)
                {
                    report.AppendLine($"「{row.Id}」：{e.Message}");
                    return false;
                }
            }

            // ---------------------------------------------------------- 删除孤儿
            int deleted = DeleteOrphans(assetPaths, report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            cardCount = rows.Count;

            // ---------------------------------------------------------- 内容规则校验
            //
            // ★ 这一步在写盘**之后**跑，因为规则要作用在真实的 CardDefinition 上
            //   （复用 CardRules，不另写一份——见 CardRules 的类注释）。
            //   有错误时**跳过 GameDatabase 登记**：磁盘上留着那份产物无所谓
            //   （它是 build 产物，下次导入会覆盖），但它绝不能进入数据库被玩家摸到。
            int errors = ValidateProduced(rows, produced, report);

            if (errors > 0)
            {
                report.AppendLine();
                report.AppendLine($"有 {errors} 个错误，**已跳过 GameDatabase 登记**——" +
                                  $"这些卡不会进入游戏。修好后重新导入。");
                return false;
            }

            // ---------------------------------------------------------- 登记进数据库
            //
            // ★ 直接调完整的生成器：它已经幂等，且「合并生成内容与手工内容」那套逻辑
            //   （含同 Id 冲突处理）已经被验证过。为了省几秒钟去复制一份合并逻辑，
            //   等于让 GameDatabase 有两条写入路径——那是迟早会分叉的地方。
            SampleContentGenerator.Generate();

            report.AppendLine($"写入 {rows.Count} 个资产到 {OutDir}" +
                              (deleted > 0 ? $"，删除 {deleted} 个表里已移除的资产" : "") +
                              "，并重建了 GameDatabase。");
            return true;
        }

        // ================================================================== 展开

        /// <summary>
        /// 把每行的 <see cref="UpgradeRow"/> 展开成一个独立的 <see cref="CardRow"/>。
        /// </summary>
        private static List<CardRow> Expand(CardTable table)
        {
            var rows = new List<CardRow>();

            foreach (var row in table.Cards)
            {
                if (row == null) continue;

                if (row.Upgrade != null)
                {
                    string plusId = row.Id + "_plus";
                    rows.Add(BuildUpgrade(row, row.Upgrade, plusId));
                    row.UpgradeTargetId = plusId;
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// 由基础版 + 差量构造升级版。省略的标量字段继承基础版；
        /// <c>effects</c> 只要出现就整体替换（理由见 <see cref="UpgradeRow"/> 的注释）。
        /// </summary>
        private static CardRow BuildUpgrade(CardRow b, UpgradeRow u, string plusId)
        {
            return new CardRow
            {
                Id = plusId,
                Name = u.Name ?? (b.Name + "+"),
                Cost = u.Cost ?? b.Cost,
                CostMode = b.CostMode,
                Type = b.Type,

                // ★ 强制 Special，不给表作者留出错的机会（铁律 14）。
                //   这是内嵌 upgrade 块相对「手建第二张卡」的主要价值之一。
                Rarity = CardRarity.Special,

                Target = b.Target,
                Keywords = u.Keywords ?? b.Keywords,
                Desc = u.Desc ?? b.Desc,

                // 继承时必须深拷贝，否则两个资产共享同一批效果实例（见 CloneEffects 的注释）。
                Effects = u.Effects ?? CardTableJson.CloneEffects(b.Effects),
                InHandEndOfTurn = u.InHandEndOfTurn ?? CardTableJson.CloneEffects(b.InHandEndOfTurn),
            };
        }

        // ================================================================== 检查

        private static bool PreflightIds(List<CardRow> rows, StringBuilder report)
        {
            bool ok = true;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                if (string.IsNullOrWhiteSpace(row.Id))
                {
                    report.AppendLine($"第 {i + 1} 行的 id 是空的。");
                    ok = false;
                    continue;
                }

                if (seen.TryGetValue(row.Id, out int firstIndex))
                {
                    report.AppendLine(
                        $"id「{row.Id}」在表里出现了两次（第 {firstIndex + 1} 行和第 {i + 1} 行）。" +
                        $"注意 upgrade 块会自动占用 <id>_plus，别再手写一张同名的卡。");
                    ok = false;
                    continue;
                }

                seen[row.Id] = i;
            }

            // 与生成器 / 其它手工资产撞 Id。
            foreach (var existing in ContentValidator.LoadAllPublic<CardDefinition>())
            {
                if (existing == null || string.IsNullOrEmpty(existing.Id)) continue;

                string path = AssetDatabase.GetAssetPath(existing);

                // 我们自己上次的产物不算冲突——那正是要被覆盖的东西。
                if (path != null && path.StartsWith(OutDir + "/", StringComparison.Ordinal)) continue;

                if (seen.ContainsKey(existing.Id))
                {
                    report.AppendLine(
                        $"id「{existing.Id}」已经被「{path}」占用了。" +
                        $"同 Id 的两份资产只有一份能进 GameDatabase，而生成资产优先——" +
                        $"表里这张会被静默丢弃。请改一个 id。");
                    ok = false;
                }
            }

            return ok;
        }

        private static int ValidateProduced(List<CardRow> rows,
                                           Dictionary<string, CardDefinition> produced,
                                           StringBuilder report)
        {
            int errors = 0;

            foreach (var row in rows)
            {
                if (!produced.TryGetValue(row.Id, out var so) || so == null) continue;

                foreach (var issue in CardRules.Validate(so))
                {
                    report.AppendLine(issue.ToString());
                    if (issue.Level == CardIssueLevel.Error) errors++;
                }
            }

            return errors;
        }

        // ================================================================== 写资产

        private static void Apply(CardRow row, CardDefinition so, CardDefinition upgraded)
        {
            so.Id = row.Id;
            so.DisplayName = row.Name;
            so.Cost = row.Cost;
            so.CostMode = row.CostMode;
            so.Type = row.Type;
            so.Rarity = row.Rarity;
            so.TargetKind = row.Target;
            so.Keywords = ParseKeywords(row.Keywords);
            so.DescriptionTemplate = row.Desc;
            so.Effects = row.Effects ?? new List<CardEffect>();
            so.InHandEndOfTurnEffects = row.InHandEndOfTurn ?? new List<CardEffect>();
            so.UpgradedVersion = upgraded;

            // ★ 刻意不碰 so.Art：铁律 47「有图才换，没图走原路」，
            //   以及 ProjectUseGuide 2.2「生成器不写 Art，因此手配的图能保留」。
            //   在这里写 null 会让每次导入都清掉美术手配的立绘，且不报任何错。

            EditorUtility.SetDirty(so);
        }

        private static CardKeyword ParseKeywords(List<string> names)
        {
            var result = CardKeyword.None;
            if (names == null) return result;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!Enum.TryParse<CardKeyword>(name.Trim(), ignoreCase: true, out var kw)
                    || kw == CardKeyword.None)
                {
                    throw new CardTableFormatException(
                        $"未知的关键字「{name}」。合法取值：" +
                        $"Exhaust, Retain, Innate, Ethereal, Unplayable");
                }

                result |= kw;
            }

            return result;
        }

        private static int DeleteOrphans(HashSet<string> keep, StringBuilder report)
        {
            string dir = Abs(OutDir);
            if (!Directory.Exists(dir)) return 0;

            int deleted = 0;

            foreach (var file in Directory.GetFiles(dir, "*.asset", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                int idx = path.LastIndexOf("Assets/", StringComparison.Ordinal);
                if (idx >= 0) path = path.Substring(idx);

                if (keep.Contains(path)) continue;

                // 只删真的是 CardDefinition 的资产。这个目录理论上只有我们在写，
                // 但「误删使用者顺手放进来的东西」的代价远大于留一个孤儿。
                if (AssetDatabase.LoadAssetAtPath<CardDefinition>(path) == null) continue;

                if (AssetDatabase.DeleteAsset(path))
                {
                    deleted++;
                    report.AppendLine($"删除（表里已移除）：{path}");
                }
            }

            return deleted;
        }

        // ================================================================== 工具

        private static CardDefinition LoadOrCreate(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
            if (existing != null) return existing;

            var so = ScriptableObject.CreateInstance<CardDefinition>();
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        /// <summary>flame_guard → FlameGuard。与生成器的资产命名约定保持一致。</summary>
        private static string PascalCase(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;

            var sb = new StringBuilder(id.Length);
            bool upper = true;

            foreach (char c in id)
            {
                if (c == '_' || c == '-' || c == ' ') { upper = true; continue; }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }

            return sb.ToString();
        }

        private static void EnsureDir(string assetDir)
        {
            string abs = Abs(assetDir);
            if (Directory.Exists(abs)) return;
            Directory.CreateDirectory(abs);
            AssetDatabase.Refresh();
        }

        private static void CreateEmptyTable()
        {
            EnsureDir("Assets/GameData");
            File.WriteAllText(Abs(TablePath), CardTableJson.ToJson(new CardTable()),
                              new UTF8Encoding(false));
            AssetDatabase.Refresh();
        }
    }
}
