using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Localization
{
    /// <summary>
    /// 一种语言的翻译表。
    ///
    /// ★ 简体中文<b>没有</b>表，也不该有。源语言的文案就写在代码与 SO 里，
    ///   由 <see cref="Loc.T"/> 当 fallback 用。理由见 <see cref="Loc"/> 的类注释。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Locale Table", fileName = "Locale_")]
    public class LocaleTable : ScriptableObject
    {
        /// <summary>BCP-47 语言标签：en / zh-Hant / ja。</summary>
        public string LanguageCode;

        /// <summary>语言自己的名字（English / 繁體中文 / 日本語）。选语言的界面上要显示它自己写的名字。</summary>
        public string DisplayName;

        [Tooltip("这个语言优先用哪些系统字体。留空则用 UIFactory 里按语言写死的候选链。")]
        public string[] FontFamilies;

        /// <summary>
        /// 一条翻译。
        ///
        /// ★ 刻意用「一个条目类的列表」而不是 Keys / Values 两条平行列表：
        ///   平行列表一旦长度或顺序错开，「A 的译文」会静默写到「B」上而不报任何错——
        ///   铁律 33 记的就是这个坑（UnitView 的状态小牌子按下标对齐而构建时会 continue）。
        /// </summary>
        [Serializable]
        public struct Entry
        {
            public string Key;

            [TextArea(1, 4)]
            public string Value;
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<string, string> _index;

        public void BuildIndex()
        {
            if (_index == null) _index = new Dictionary<string, string>(Entries.Count);
            else _index.Clear();

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (string.IsNullOrEmpty(e.Key)) continue;

                // 重复 key 取第一条，并且要吵出来——静默取最后一条会让
                // 「我明明改了译文怎么没生效」变成一个查不出的谜
                if (_index.ContainsKey(e.Key))
                {
                    Debug.LogWarning($"[Loc] 语言表 {name} 里 key「{e.Key}」重复，取第一条。", this);
                    continue;
                }
                _index[e.Key] = e.Value;
            }
        }

        /// <summary>查一条翻译。查不到或译文为空都返回 false——空串译文等于没翻。</summary>
        public bool TryGet(string key, out string value)
        {
            if (_index == null) BuildIndex();
            return _index.TryGetValue(key, out value) && !string.IsNullOrEmpty(value);
        }

        /// <summary>导入工具用：整表重建。</summary>
        public void SetEntries(List<Entry> entries)
        {
            Entries = entries ?? new List<Entry>();
            _index = null;
        }

        public void Invalidate() => _index = null;
    }
}
