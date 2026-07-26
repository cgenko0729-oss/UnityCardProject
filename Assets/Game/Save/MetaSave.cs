namespace Game.Save
{
    /// <summary>
    /// 跨局持久化的数据。与 <see cref="RunSave"/> 分成两个文件：
    /// 「放弃本局」「打完一局」都要删掉 run 存档，而语言设置显然不该跟着一起没。
    /// </summary>
    public class MetaSave
    {
        /// <summary>默认 0 的理由与 <see cref="RunSave.Version"/> 相同，由写入方显式赋值。</summary>
        public int Version;

        /// <summary>
        /// 语言标签（<c>zh-Hans</c> / <c>en</c>）。空串或 null 表示源语言。
        ///
        /// <para>★ 这一项原先存在 <c>PlayerPrefs</c> 的 <c>game.language</c> 键上，
        /// 由 <c>SaveService</c> 做一次性迁移：meta 文件不存在时先去读 PlayerPrefs，
        /// 否则老玩家的语言设置会在升级后被静默重置回中文。</para>
        /// </summary>
        public string Language;
    }
}
