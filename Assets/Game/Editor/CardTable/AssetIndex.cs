using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Game.Editor.CardTables
{
    /// <summary>
    /// Id → ScriptableObject 的反查表。表里所有对内容资产的引用（<c>applyStatus.status</c>、
    /// <c>addCard.card</c>）都写成 Id 字符串，导入时靠这里解析成真实引用。
    ///
    /// ★ 必须走 <see cref="ContentValidator.LoadAllPublic{T}"/>，不能自己写
    ///   <c>AssetDatabase.FindAssets("t:XXX")</c>——那个 API 在 <c>-batchmode</c> 下恒返回 0
    ///   （铁律 15）。自己写一份的话，命令行导入会「成功」但所有状态引用都解析成 null，
    ///   产出一批看起来正常、打出来什么都不发生的卡。
    /// </summary>
    internal static class AssetIndex
    {
        private static readonly Dictionary<Type, Dictionary<string, ScriptableObject>> Cache =
            new Dictionary<Type, Dictionary<string, ScriptableObject>>();

        /// <summary>
        /// 丢弃缓存。导入器在**每次导入开始时**必须调一次：
        /// 一次导入会创建新资产，而后续行可能引用它们（B 卡的 addCard 指向本次新建的 A 卡）。
        /// 缓存跨导入存活的话，第二次导入才会解析成功——表现是「第一次导入某个引用是空的」。
        /// </summary>
        public static void Invalidate() => Cache.Clear();

        /// <summary>按 Id 查一个内容资产。查不到返回 null，由调用方决定是报错还是放行。</summary>
        public static ScriptableObject Find(Type soType, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (!Cache.TryGetValue(soType, out var byId))
            {
                byId = BuildIndex(soType);
                Cache[soType] = byId;
            }

            return byId.TryGetValue(id, out var so) ? so : null;
        }

        /// <summary>这个类型下已知的全部 Id，按字母序。只用来生成错误信息里的「你是不是想写……」。</summary>
        public static IEnumerable<string> KnownIds(Type soType)
        {
            if (!Cache.TryGetValue(soType, out var byId))
            {
                byId = BuildIndex(soType);
                Cache[soType] = byId;
            }

            var ids = new List<string>(byId.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private static Dictionary<string, ScriptableObject> BuildIndex(Type soType)
        {
            var byId = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);

            // ContentValidator.LoadAllPublic<T>() 是泛型方法，这里只有 Type，所以反射调用。
            // 刻意不在 ContentValidator 上加一个非泛型重载——那会为了一个内部工具
            // 去改一个被 CI 入口依赖的类。
            var method = typeof(ContentValidator)
                .GetMethod(nameof(ContentValidator.LoadAllPublic),
                           BindingFlags.Public | BindingFlags.Static)
                ?.MakeGenericMethod(soType);

            if (method == null)
            {
                Debug.LogError("[CardTable] 找不到 ContentValidator.LoadAllPublic<T>，" +
                               "内容引用将全部无法解析。");
                return byId;
            }

            var all = method.Invoke(null, null) as System.Collections.IEnumerable;
            if (all == null) return byId;

            foreach (var obj in all)
            {
                var so = obj as ScriptableObject;
                if (so == null) continue;

                string id = IdOf(so);
                if (string.IsNullOrEmpty(id)) continue;

                // 同 Id 冲突不在这里报——ContentValidator.CheckDuplicateIds 已经管这件事，
                // 在两个地方各报一次只会让 Console 更吵。这里保留第一个，行为与数据库一致。
                if (!byId.ContainsKey(id)) byId.Add(id, so);
            }

            return byId;
        }

        /// <summary>读一个内容资产的 Id。字段和属性都试，因为不是所有 Definition 都用字段。</summary>
        public static string IdOf(ScriptableObject so)
        {
            if (so == null) return null;
            var t = so.GetType();

            var field = t.GetField("Id", BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
                return field.GetValue(so) as string;

            var prop = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead)
                return prop.GetValue(so) as string;

            return null;
        }
    }
}
