using Game.Core;
using Game.Localization;
using Game.Save;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 决定「什么时候写盘」的那一方。
    ///
    /// <para>★ 存档的调用点全部集中在这个类里，<c>Game.Runtime</c> 一行都不碰文件系统。
    ///   <see cref="RunManager"/> 只负责在安全点发 <c>AutosaveRequested</c>，
    ///   谁都没订阅时它什么也不做——166 个 EditMode 用例与将来的自动对战模拟器
    ///   因此天然不会往 <c>persistentDataPath</c> 写东西。
    ///   这是第四次会话那条教训的直接套用：开关挂在「谁开的这一局」身上，不挂在流程里。</para>
    ///
    /// <para>它是普通 C# 类而不是 MonoBehaviour：没有任何一帧要做的事，
    ///   全部逻辑都由事件驱动。由 <see cref="GameApp"/> 持有并负责退订。</para>
    /// </summary>
    public class SaveService
    {
        /// <summary>
        /// 语言设置在迁进 <see cref="MetaSave"/> 之前住的地方。
        /// ★ 保留这个常量是为了做一次性迁移，不是为了继续写它。
        /// </summary>
        public const string LegacyLanguagePrefKey = "game.language";

        private readonly GameDatabase _db;
        private readonly RunManager _manager;

        public SaveService(GameDatabase db, RunManager manager)
        {
            _db = db;
            _manager = manager;

            if (_manager != null)
            {
                _manager.AutosaveRequested += OnAutosaveRequested;
                _manager.PhaseChanged += OnPhaseChanged;
            }
        }

        public void Dispose()
        {
            if (_manager == null) return;
            _manager.AutosaveRequested -= OnAutosaveRequested;
            _manager.PhaseChanged -= OnPhaseChanged;
        }

        /// <summary>磁盘上有没有一局可以继续。主菜单据此决定「继续游戏」能不能按。</summary>
        public bool HasSave => SaveSystem.HasRunSave;

        // ================================================================= 自动存档

        private void OnAutosaveRequested()
        {
            if (_manager?.Run == null) return;
            SaveSystem.SaveRun(_manager.Run);
        }

        private void OnPhaseChanged(RunPhase phase)
        {
            // ★ 一局结束就删档。不删的话主菜单会一直亮着「继续游戏」，
            //   点进去是一份已经通关 / 已经死了的局——读回来只会立刻再弹一次结算界面。
            if (phase == RunPhase.GameOver || phase == RunPhase.Victory)
                SaveSystem.DeleteRun();
        }

        // ================================================================= 读档

        /// <summary>
        /// 继续上一局。成功时界面会被 <c>PhaseChanged</c> 带到存档所在的阶段。
        /// 返回 false 表示没有存档、或者存档已经读不出来了（原因已打进 Console）。
        /// </summary>
        public bool TryContinue()
        {
            var run = SaveSystem.LoadRun(_db);
            if (run == null) return false;

            // ★ 能点到「继续游戏」这颗按钮的必然是真人，于是战斗里的选牌要挂起等他点。
            //   与 GameApp.StartNewRun 里那一行是同一件事：
            //   两条「为真人创建一局」的路径都在 UI 层，且都必须自己负责打开这个开关。
            run.InteractivePlayer = true;

            _manager.Resume(_db, run);
            return true;
        }

        /// <summary>放弃本局：只删存档，不动当前内存里的状态（调用方随后回主菜单）。</summary>
        public void Abandon() => SaveSystem.DeleteRun();

        // ================================================================= 语言

        /// <summary>
        /// 取玩家上次选的语言。
        ///
        /// <para>★ meta 里没有时会去 <see cref="LegacyLanguagePrefKey"/> 找一次并搬进 meta。
        ///   没有这段迁移的话，所有老玩家的语言设置会在这次更新后被静默重置回中文——
        ///   而这类「静默恢复默认值」的 bug 没有人会来报，只会被当成「这游戏记不住设置」。</para>
        /// </summary>
        public static string LoadLanguage()
        {
            var meta = SaveSystem.LoadMeta();
            if (!string.IsNullOrEmpty(meta.Language)) return meta.Language;

            string legacy = PlayerPrefs.GetString(LegacyLanguagePrefKey, string.Empty);
            if (!string.IsNullOrEmpty(legacy))
            {
                meta.Language = legacy;
                SaveSystem.SaveMeta(meta);
                return legacy;
            }

            return Loc.SourceLanguage;
        }

        public static void SaveLanguage(string code)
        {
            var meta = SaveSystem.LoadMeta();
            meta.Language = string.IsNullOrEmpty(code) ? Loc.SourceLanguage : code;
            SaveSystem.SaveMeta(meta);
        }
    }
}
