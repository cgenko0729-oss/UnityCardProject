using System;
using UnityEngine;

namespace Game.Localization
{
    /// <summary>
    /// 全工程唯一的取文案入口。
    ///
    /// <para>
    /// ★ 核心设计：<b>简体中文是源语言，不建表。</b>
    /// 每个调用点都写成 <c>Loc.T("ui.battle.end_turn", "结束回合")</c>——
    /// 中文原文留在代码里当 fallback，查不到 key 就用它。
    /// </para>
    /// <para>
    /// 为什么不给中文也建一张表（那样看起来更「对称」）：
    /// 中文是唯一每天都在看的语言。一旦它也变成查表结果，key 写错时中文下
    /// 会显示成 key 本身或空串，而这类错误在其它语言里根本没人会发现。
    /// 让源语言走一条<b>不可能坏</b>的路径，比形式上的对称值钱得多。
    /// </para>
    /// <para>
    /// ★ 这是纯 C# + 一个 SO，没有任何异步加载，因此不违反「逻辑同步执行」。
    /// 没调用过 <see cref="Use"/> 时它恒等于「返回 fallback」，
    /// 所以 EditMode 测试与自动模拟器完全不需要知道它存在。
    /// </para>
    /// </summary>
    public static class Loc
    {
        /// <summary>源语言。它的文案就是代码 / SO 里的原文，没有翻译表。</summary>
        public const string SourceLanguage = "zh-Hans";

        private static LocaleTable _table;

        /// <summary>当前语言的 BCP-47 标签。</summary>
        public static string Current { get; private set; } = SourceLanguage;

        /// <summary>当前语言表；源语言时为 null。</summary>
        public static LocaleTable Table => _table;

        /// <summary>
        /// 语言变了。
        /// ★ 订阅方要负责在自己销毁时退订，并在回调里重建<b>已经显示出来的</b>文字——
        /// <see cref="T"/> 只影响下一次取值，不会去追已经写进 TMP_Text 的那一份。
        /// </summary>
        public static event Action LanguageChanged;

        /// <summary>切到某张翻译表。传 null 等于切回源语言。</summary>
        public static void Use(LocaleTable table)
        {
            string next = table != null && !string.IsNullOrEmpty(table.LanguageCode)
                ? table.LanguageCode
                : SourceLanguage;

            if (_table == table && Current == next) return;

            _table = table;
            Current = next;
            if (_table != null) _table.BuildIndex();

            LanguageChanged?.Invoke();
        }

        /// <summary>切回简体中文（源语言）。</summary>
        public static void UseSourceLanguage() => Use(null);

        public static bool IsSourceLanguage => _table == null;

        // ================================================================= 取文案

        /// <summary>
        /// 取一条文案。<paramref name="source"/> 是简中原文，同时充当 fallback。
        /// </summary>
        public static string T(string key, string source)
        {
            if (_table == null || string.IsNullOrEmpty(key)) return source;
            return _table.TryGet(key, out var v) ? v : source;
        }

        /// <summary>
        /// 取一条带参数的文案，参数按 <see cref="string.Format(string, object[])"/> 填。
        ///
        /// ★ 译文里的占位符坏掉（少写一个 <c>{0}</c>、写成全角 <c>｛0｝</c>、把 <c>{0}</c>
        ///   写成 <c>{2}</c>）时 <c>string.Format</c> 会抛 <see cref="FormatException"/>。
        ///   文案是每帧都在取的东西，一条配错的译文不该把整个界面打爆——
        ///   所以这里兜住异常，退回用原文再格式化一次。
        ///   真正该拦住这种错误的地方是 ContentValidator 的占位符一致性检查。
        /// </summary>
        public static string T(string key, string source, params object[] args)
        {
            string template = T(key, source);
            if (args == null || args.Length == 0) return template;

            try { return string.Format(template, args); }
            catch (FormatException)
            {
                Debug.LogWarning($"[Loc] key「{key}」在语言 {Current} 下的译文占位符与原文不符，已退回原文。");
                try { return string.Format(source, args); }
                catch (FormatException) { return source; }
            }
        }

        /// <summary>
        /// 单复数。<paramref name="n"/> 为 1 时取 <c>key + ".one"</c>，否则取 <c>key + ".other"</c>。
        ///
        /// ★ 中文没有单复数，两个源文案通常是同一句；英文才需要分开。
        ///   刻意<b>不</b>引入 ICU MessageFormat：那套是为俄语（4 种复数形式）
        ///   这类语言准备的，本工程的语言集用不上，引入只会多一个没人看得懂的 DSL。
        /// </summary>
        public static string TPlural(string key, int n, string sourceOne, string sourceOther, params object[] args)
        {
            bool one = n == 1;
            return T(one ? key + ".one" : key + ".other", one ? sourceOne : sourceOther, args);
        }
    }
}
