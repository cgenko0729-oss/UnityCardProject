using System;
using System.Collections.Generic;
using System.IO;
using Game.Save;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 存档的文件层。★ 刻意做得很薄：序列化的全部逻辑在
    /// <see cref="RunSaveWriter"/> / <see cref="RunSaveReader"/> / <see cref="SaveJson"/>（都是纯函数、都有测试），
    /// 这里只剩「路径、原子写、出错时别炸」三件事。
    ///
    /// <para>★ 它属于 Game.Runtime，但**没有任何 Runtime 代码调用它**——
    ///   调用点全在 <c>Game.UI.SaveService</c>。这是刻意的：
    ///   一旦 <c>RunManager</c> 直接调 <c>SaveRun</c>，166 个 EditMode 用例和将来的
    ///   自动对战模拟器每跑一局就会往 <c>persistentDataPath</c> 写一次文件。
    ///   开关要挂在「谁开的这一局」身上，不能挂在流程里（第四次会话 InteractivePlayer 的教训）。</para>
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>
        /// 存档目录。测试可以改写它把文件引到临时目录，避免污染真实存档。
        /// null 表示用 <c>Application.persistentDataPath</c>。
        /// </summary>
        public static string OverrideDirectory;

        public static string Directory
            => string.IsNullOrEmpty(OverrideDirectory) ? Application.persistentDataPath : OverrideDirectory;

        public static string RunPath => Path.Combine(Directory, SaveConstants.RunFileName);
        public static string MetaPath => Path.Combine(Directory, SaveConstants.MetaFileName);

        public static bool HasRunSave => File.Exists(RunPath);

        // ================================================================= Run

        /// <summary>存一局。失败只打日志不抛异常——存档失败绝不该打断玩家正在玩的这一局。</summary>
        public static bool SaveRun(RunContext run)
        {
            if (run == null) return false;

            try
            {
                var save = RunSaveWriter.Write(run);
                save.Version = SaveConstants.CurrentVersion;
                return WriteAtomic(RunPath, SaveJson.ToJson(save));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 存档写入失败：{e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读一局。读不出来返回 null（文件不存在 / 损坏 / 版本不符 / 内容对不上）。
        /// 缺内容的警告会打进 Console，一条都不吞——「我的牌怎么少了一张」只能靠这些行查。
        /// </summary>
        public static RunContext LoadRun(GameDatabase db)
        {
            if (db == null) return null;

            var run = TryLoadFrom(RunPath, db, out bool fileExisted);
            if (run != null) return run;

            // 正档读不出来时退到备份。备份是 File.Replace 在上一次成功写入时自动留下的，
            // 因此它一定是一份「曾经完整写完过」的存档，最多旧一个存档点。
            string backup = RunPath + SaveConstants.BackupSuffix;
            if (File.Exists(backup))
            {
                Debug.LogWarning("[SaveSystem] 主存档读取失败，改用备份存档。");
                run = TryLoadFrom(backup, db, out _);
                if (run != null) return run;
            }

            if (fileExisted) Debug.LogError("[SaveSystem] 存档无法读取，已放弃。");
            return null;
        }

        private static RunContext TryLoadFrom(string path, GameDatabase db, out bool fileExisted)
        {
            fileExisted = File.Exists(path);
            if (!fileExisted) return null;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 读取「{path}」失败：{e.Message}");
                return null;
            }

            var save = SaveJson.RunFromJson(json);
            if (save == null)
            {
                Debug.LogError($"[SaveSystem] 「{path}」不是一份合法的存档（JSON 解析失败）。");
                return null;
            }

            var warnings = new List<string>();

            if (!SaveMigration.TryMigrate(save, warnings))
            {
                LogWarnings(warnings);
                return null;
            }

            var run = RunSaveReader.Read(save, db, warnings);
            LogWarnings(warnings);
            return run;
        }

        public static void DeleteRun()
        {
            TryDelete(RunPath);
            TryDelete(RunPath + SaveConstants.BackupSuffix);
            TryDelete(RunPath + SaveConstants.TempSuffix);
        }

        // ================================================================= Meta

        public static bool SaveMeta(MetaSave meta)
        {
            if (meta == null) return false;
            meta.Version = SaveConstants.CurrentVersion;

            try
            {
                return WriteAtomic(MetaPath, SaveJson.ToJson(meta));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] meta 写入失败：{e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读 meta。★ 永远返回一个非 null 对象——meta 装的是偏好设置，
        /// 读不出来的正确行为是「用默认值继续」，而不是让调用点到处判空。
        /// </summary>
        public static MetaSave LoadMeta()
        {
            if (!File.Exists(MetaPath)) return new MetaSave();

            try
            {
                var meta = SaveJson.MetaFromJson(File.ReadAllText(MetaPath));
                if (meta == null) return new MetaSave();

                var warnings = new List<string>();
                if (!SaveMigration.TryMigrate(meta, warnings)) { LogWarnings(warnings); return new MetaSave(); }
                LogWarnings(warnings);
                return meta;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] meta 读取失败，改用默认设置：{e.Message}");
                return new MetaSave();
            }
        }

        // ================================================================= 原子写

        /// <summary>
        /// 先写 .tmp 再替换正档。
        ///
        /// ★ 直接 <c>File.WriteAllText(RunPath, ...)</c> 的问题是：写到一半断电 / 崩溃，
        ///   正档就是一个半截 JSON，**玩家整局游戏没了**。
        ///   走 tmp 的话，最坏情况只是 tmp 半截，正档纹丝不动。
        ///   <c>File.Replace</c> 还会顺手把被替换掉的旧正档留成 .bak，
        ///   于是 <see cref="LoadRun"/> 有个退路。
        /// </summary>
        private static bool WriteAtomic(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            string tmp = path + SaveConstants.TempSuffix;
            File.WriteAllText(tmp, content);

            if (File.Exists(path))
                File.Replace(tmp, path, path + SaveConstants.BackupSuffix);
            else
                File.Move(tmp, path);

            return true;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 删除「{path}」失败：{e.Message}");
            }
        }

        private static void LogWarnings(List<string> warnings)
        {
            for (int i = 0; i < warnings.Count; i++)
                Debug.LogWarning($"[SaveSystem] {warnings[i]}");
        }
    }
}
