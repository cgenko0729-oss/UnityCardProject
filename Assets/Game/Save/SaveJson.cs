using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Game.Save
{
    /// <summary>
    /// 存档 DTO ↔ JSON 字符串。★ 纯函数，不碰文件、不碰 Unity API——
    /// 测试要能在不写磁盘的前提下做完整的往返断言。
    /// </summary>
    public static class SaveJson
    {
        /// <summary>
        /// ★ 三个设置各自解决一个具体问题，别随手改：
        ///
        /// <list type="bullet">
        /// <item><b>StringEnumConverter</b>：枚举写成字符串。默认写成整数，于是往
        ///   <c>MapNodeType</c> 中间插一个新类型，所有老存档的节点类型会**整体偏移一位**
        ///   ——商店变成事件，而且不报任何错。写成字符串后，插值不影响老存档，
        ///   删值会在读取时抛异常（能被当场发现），顺便让明文存档真的可读。</item>
        /// <item><b>Formatting.Indented</b>：使用者拍板「不加密、明文可读」，
        ///   目的就是能拿记事本改存档来复现 bug。压成一行等于放弃这个价值。</item>
        /// <item><b>NullValueHandling.Ignore</b>：null 字段不写。存档里绝大多数
        ///   可选字段（PendingReward / 各种 Id）平时都是 null。</item>
        /// </list>
        /// </summary>
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter() },
        };

        public static string ToJson(RunSave save) => JsonConvert.SerializeObject(save, Settings);
        public static string ToJson(MetaSave save) => JsonConvert.SerializeObject(save, Settings);

        /// <summary>
        /// 解析。★ 内容非法时**返回 null 而不是抛异常**——
        /// 调用方（读档、测试）关心的永远是「能不能用」，
        /// 而 <c>JsonReaderException</c> / <c>JsonSerializationException</c> 是两个不同的类型，
        /// 让每个调用点各自 catch 一遍迟早会漏。
        /// </summary>
        public static RunSave RunFromJson(string json) => From<RunSave>(json);

        public static MetaSave MetaFromJson(string json) => From<MetaSave>(json);

        private static T From<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<T>(json, Settings);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
