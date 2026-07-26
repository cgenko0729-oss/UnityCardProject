using System.Collections.Generic;

namespace Game.Save
{
    /// <summary>
    /// 存档版本迁移。★ 纯函数，不碰文件。
    ///
    /// <para>目前只有版本 1，所以这里没有任何一段真正的迁移代码——
    /// 但**入口必须先存在**。等到第一次改字段语义时，如果还没有这道关，
    /// 老存档会以「字段读成默认值」的形式静默读进来，而不是被拦下来。</para>
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>
        /// 把存档就地迁到当前版本。返回 false 表示这份存档不能用。
        /// </summary>
        public static bool TryMigrate(RunSave save, List<string> warnings = null)
        {
            if (save == null) return false;

            if (save.Version <= 0)
            {
                warnings?.Add("存档缺少版本号，无法识别（多半是文件损坏或不是本游戏的存档）。");
                return false;
            }

            // ★ 老客户端读不了新存档：新版本可能加了字段，读进来是默认值，
            //   表现为「玩家的东西凭空少了」——那比直接拒绝糟糕得多
            if (save.Version > SaveConstants.CurrentVersion)
            {
                warnings?.Add($"存档版本 {save.Version} 高于当前游戏支持的 {SaveConstants.CurrentVersion}，" +
                              "请更新游戏后再读取。");
                return false;
            }

            // 将来在这里逐版本往上推：
            // if (save.Version == 1) { ...改字段...; save.Version = 2; }

            save.Version = SaveConstants.CurrentVersion;
            return true;
        }

        /// <summary>
        /// meta 存档的迁移。★ 与 run 存档不同：meta 读不出来时**不该拦住玩家**，
        /// 里面装的只是语言这类偏好，最坏情况是退回默认值重新选一次。
        /// 所以高版本 meta 也返回 true，只是把不认识的字段留在原地。
        /// </summary>
        public static bool TryMigrate(MetaSave save, List<string> warnings = null)
        {
            if (save == null) return false;

            if (save.Version <= 0)
            {
                warnings?.Add("meta 存档缺少版本号，已按默认设置处理。");
                return false;
            }

            if (save.Version > SaveConstants.CurrentVersion)
            {
                warnings?.Add($"meta 存档版本 {save.Version} 高于当前游戏，已按兼容方式读取。");
                return true;
            }

            save.Version = SaveConstants.CurrentVersion;
            return true;
        }
    }
}
