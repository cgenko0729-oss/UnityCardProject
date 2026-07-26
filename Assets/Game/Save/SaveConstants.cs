namespace Game.Save
{
    /// <summary>
    /// 存档的常量与文件名。★ 单槽：整个游戏只有一份 run 存档和一份 meta 存档。
    /// </summary>
    public static class SaveConstants
    {
        /// <summary>
        /// 存档格式版本。**每次改动 DTO 的字段含义都要 +1**，并在
        /// <see cref="SaveMigration"/> 里补一段迁移。
        ///
        /// <para>只加字段不需要升版本——Newtonsoft 读老存档时新字段保持默认值，
        /// 这正是我们想要的行为。改名 / 改语义 / 删字段才需要。</para>
        /// </summary>
        public const int CurrentVersion = 1;

        public const string RunFileName = "run.json";
        public const string MetaFileName = "meta.json";

        /// <summary>原子写的中转文件后缀。写到一半断电只会毁掉它。</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>上一份存档的备份后缀，由 <c>File.Replace</c> 自动产出。</summary>
        public const string BackupSuffix = ".bak";
    }
}
