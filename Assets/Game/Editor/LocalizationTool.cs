using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core;
using Game.Localization;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 本地化的导出 / 导入工具。
    ///
    /// 工作流：
    /// <list type="number">
    /// <item>「导出本地化 CSV」→ 得到一张 <c>key, 来源, 简中原文, en, ja, …</c> 的表。</item>
    /// <item>在 Excel / 表格软件里填翻译列。</item>
    /// <item>「导入本地化 CSV」→ 写回 <c>Assets/GameData/Locales/Locale_&lt;code&gt;.asset</c>。</item>
    /// </list>
    ///
    /// ★ CSV 带 UTF-8 BOM：不带 BOM 的话 Excel 会用系统 ANSI 打开，
    ///   整张表的中文变乱码，而译者多半会以为是文件坏了而不是编码问题。
    /// </summary>
    public static class LocalizationTool
    {
        private const string LocalesFolder = "Assets/GameData/Locales";
        private const string DefaultCsvName = "localization.csv";

        /// <summary>要导出哪些语言列。源语言不在其中——它是原文列。</summary>
        private static readonly string[] TargetLanguages = { "en", "zh-Hant", "ja" };

        // ============================================================ 导出

        [MenuItem("Tools/卡牌游戏/5. 导出本地化 CSV", priority = 20)]
        public static void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("导出本地化 CSV", "", DefaultCsvName, "csv");
            if (string.IsNullOrEmpty(path)) return;

            var entries = LocalizationKeys.CollectAll();
            var tables = LoadTables();

            var sb = new StringBuilder(entries.Count * 96);

            sb.Append("key,origin,zh-Hans");
            for (int i = 0; i < TargetLanguages.Length; i++) sb.Append(',').Append(TargetLanguages[i]);
            sb.Append('\n');

            foreach (var e in entries)
            {
                sb.Append(Csv(e.Key)).Append(',')
                  .Append(Csv(e.Origin)).Append(',')
                  .Append(Csv(e.Source));

                foreach (var lang in TargetLanguages)
                {
                    string value = "";
                    if (tables.TryGetValue(lang, out var table) && table.TryGet(e.Key, out var v))
                    {
                        value = v;

                        // ★ 原文改过、译文没跟着改 —— 这种「过期译文」不会报任何错，
                        //   key 还在、值还在，只是意思对不上了。在导出的表里显式标出来，
                        //   否则译者根本不知道该重看哪几条。
                        if (IsStale(table, e.Key, e.Source)) value = "[STALE] " + value;
                    }
                    sb.Append(',').Append(Csv(value));
                }
                sb.Append('\n');
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[Localization] 已导出 {entries.Count} 条到 {path}");
            EditorUtility.DisplayDialog("导出完成", $"共 {entries.Count} 条文案。\n{path}", "好");
        }

        private static bool IsStale(LocaleTable table, string key, string currentSource)
        {
            for (int i = 0; i < table.Entries.Count; i++)
            {
                if (table.Entries[i].Key != key) continue;
                var snap = table.Entries[i].SourceSnapshot;
                return !string.IsNullOrEmpty(snap) && snap != currentSource;
            }
            return false;
        }

        // ============================================================ 导入

        [MenuItem("Tools/卡牌游戏/6. 导入本地化 CSV", priority = 21)]
        public static void ImportCsv()
        {
            string path = EditorUtility.OpenFilePanel("导入本地化 CSV", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var rows = ParseCsv(File.ReadAllText(path));
            if (rows.Count < 2)
            {
                EditorUtility.DisplayDialog("导入失败", "文件是空的，或者只有表头。", "好");
                return;
            }

            var header = rows[0];
            int keyCol = header.IndexOf("key");
            int srcCol = header.IndexOf("zh-Hans");
            if (keyCol < 0)
            {
                EditorUtility.DisplayDialog("导入失败", "表头里找不到 key 列。", "好");
                return;
            }

            EnsureFolder();

            int totalWritten = 0;
            var report = new StringBuilder();

            for (int col = 0; col < header.Count; col++)
            {
                string lang = header[col];
                if (col == keyCol || lang == "origin" || lang == "zh-Hans" || string.IsNullOrEmpty(lang)) continue;
                if (lang == Loc.SourceLanguage) continue;   // 源语言不建表

                var table = LoadOrCreateTable(lang);
                var list = new List<LocaleTable.Entry>(rows.Count);

                for (int r = 1; r < rows.Count; r++)
                {
                    var row = rows[r];
                    if (keyCol >= row.Count) continue;

                    string key = row[keyCol];
                    if (string.IsNullOrEmpty(key)) continue;

                    string value = col < row.Count ? row[col] : "";

                    // 导出时加的标记不能被当成译文写回去
                    if (value.StartsWith("[STALE] ", StringComparison.Ordinal)) value = value.Substring(8);
                    if (string.IsNullOrEmpty(value)) continue;   // 没翻的条目不写，让它回退到中文

                    list.Add(new LocaleTable.Entry
                    {
                        Key = key,
                        Value = value,
                        SourceSnapshot = srcCol >= 0 && srcCol < row.Count ? row[srcCol] : "",
                    });
                }

                table.SetEntries(list);
                EditorUtility.SetDirty(table);
                totalWritten += list.Count;
                report.AppendLine($"{lang}：{list.Count} 条");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            LinkTablesIntoDatabase();

            Debug.Log($"[Localization] 导入完成，共 {totalWritten} 条：\n{report}");
            EditorUtility.DisplayDialog("导入完成", report.ToString(), "好");
        }

        // ============================================================ 资产

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(LocalesFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/GameData")) AssetDatabase.CreateFolder("Assets", "GameData");
            AssetDatabase.CreateFolder("Assets/GameData", "Locales");
        }

        private static Dictionary<string, LocaleTable> LoadTables()
        {
            var map = new Dictionary<string, LocaleTable>();
            foreach (var t in ContentValidator.LoadAllPublic<LocaleTable>())
            {
                if (t == null || string.IsNullOrEmpty(t.LanguageCode)) continue;
                t.BuildIndex();
                map[t.LanguageCode] = t;
            }
            return map;
        }

        private static LocaleTable LoadOrCreateTable(string lang)
        {
            foreach (var t in ContentValidator.LoadAllPublic<LocaleTable>())
                if (t != null && t.LanguageCode == lang) return t;

            var created = ScriptableObject.CreateInstance<LocaleTable>();
            created.LanguageCode = lang;
            created.DisplayName = NativeNameOf(lang);
            AssetDatabase.CreateAsset(created, $"{LocalesFolder}/Locale_{lang}.asset");
            return created;
        }

        /// <summary>
        /// 语言在<b>它自己语言里</b>的名字。
        /// ★ 选语言的按钮上必须写这个，而不是用当前语言去描述它——
        ///   看不懂当前界面语言的人才最需要那个按钮。
        /// </summary>
        private static string NativeNameOf(string lang) => lang switch
        {
            "en" => "English",
            "zh-Hant" => "繁體中文",
            "ja" => "日本語",
            "ko" => "한국어",
            _ => lang,
        };

        /// <summary>
        /// 把语言表挂进 GameDatabase.Locales。
        /// ★ 不挂上去的话资产存在但运行时找不到（GetLocale 遍历的是这个列表），
        ///   表现是「明明导入成功了，游戏里还是中文」。
        /// </summary>
        private static void LinkTablesIntoDatabase()
        {
            var dbs = ContentValidator.LoadAllPublic<GameDatabase>();
            var tables = ContentValidator.LoadAllPublic<LocaleTable>();
            if (dbs.Count == 0 || tables.Count == 0) return;

            foreach (var db in dbs)
            {
                bool changed = false;
                foreach (var t in tables)
                {
                    if (t == null || string.IsNullOrEmpty(t.LanguageCode)) continue;
                    if (db.Locales.Contains(t)) continue;
                    db.Locales.Add(t);
                    changed = true;
                }
                if (changed)
                {
                    db.Invalidate();
                    EditorUtility.SetDirty(db);
                }
            }
            AssetDatabase.SaveAssets();
        }

        // ============================================================ CSV

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool needsQuote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuote) return s;
            return '"' + s.Replace("\"", "\"\"") + '"';
        }

        /// <summary>
        /// RFC 4180 解析。手写而不是用现成库，是因为工程不引第三方依赖，
        /// 而这份表里必然出现「带逗号的句子」和「带换行的多行描述」——
        /// 用 <c>Split(',')</c> 会把它们切碎，且切碎得很隐蔽（只有长句子才出事）。
        /// </summary>
        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(field.ToString()); field.Clear();
                        break;
                    case '\r':
                        break;   // \r\n 里的 \r 直接丢
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        rows.Add(row); row = new List<string>();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            // BOM 会粘在第一个字段头上，让表头里的 "key" 匹配不到
            if (rows.Count > 0 && rows[0].Count > 0)
                rows[0][0] = rows[0][0].TrimStart('﻿');

            return rows;
        }
    }
}
