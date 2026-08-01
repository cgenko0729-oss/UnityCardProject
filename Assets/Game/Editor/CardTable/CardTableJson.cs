using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Game.Editor.CardTables
{
    /// <summary>
    /// <see cref="CardTable"/> ⇄ JSON 字符串。★ 纯函数，不碰文件、不碰 AssetDatabase
    /// （SO 引用的解析被隔离在 <see cref="AssetIndex"/> 里），
    /// 这样 <see cref="CardTableSelfCheck"/> 才能在不写磁盘的前提下做完整的往返断言。
    /// 形状刻意与 <c>Game.Save.SaveJson</c> 保持一致。
    /// </summary>
    public static class CardTableJson
    {
        /// <summary>
        /// ★ 五个设置各自解决一个具体问题，别随手改：
        ///
        /// <list type="bullet">
        /// <item><b>StringEnumConverter</b>：枚举写成字符串。理由与存档相同（铁律 46②）——
        ///   写成整数的话，往 <c>CardType</c> 中间插一个新类型，整张表的类型会**整体偏移一位**，
        ///   攻击牌变成技能牌且不报任何错。</item>
        /// <item><b>Formatting.Indented</b>：这张表是给人和 AI 直接读写的源文件，
        ///   而且要在 git diff 里逐行 review。压成一行等于放弃它存在的全部理由。</item>
        /// <item><b>NullValueHandling.Ignore</b>：null 字段不写。绝大多数卡没有 upgrade、
        ///   没有 keywords、没有 inHandEndOfTurn，写出来只是噪音。</item>
        /// <item><b>MissingMemberHandling.Error</b>：★ JSON 里有表模型不认识的键就**报错**。
        ///   Newtonsoft 默认是静默忽略——那意味着把 <c>"rarity"</c> 拼成 <c>"rariry"</c>
        ///   会得到一张稀有度悄悄变成 Common 的卡，它会照常进奖励池，没有任何提示。
        ///   这是本工程反复吃过的那种「不报错的错」，必须在入口就拦掉。</item>
        /// <item><b>CamelCasePropertyNamesContractResolver</b>：C# 字段是 PascalCase，
        ///   JSON 键统一 camelCase。规则只有这一条，不做任何逐字段的名字映射，
        ///   于是给 <c>CardRow</c> 加字段不需要同时改任何映射表。</item>
        /// </list>
        /// </summary>
        private static JsonSerializerSettings BuildSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Error,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters =
            {
                new StringEnumConverter(),
                new EffectValueConverter(),
                new TargetSelectorConverter(),
                new ScriptableObjectIdConverter(),
                new CardEffectConverter(),
            },
        };

        public static string ToJson(CardTable table)
            => JsonConvert.SerializeObject(table, BuildSettings());

        /// <summary>
        /// 解析。★ 与 <c>SaveJson</c> 相反，这里**失败就抛异常**而不是返回 null：
        /// 读档的调用方只关心「能不能用」，而编卡的人需要知道**是哪张卡的哪个字段错了**。
        /// 一个 null 返回值会让导入器只能说「表读不了」，那等于把工具变成猜谜游戏。
        /// </summary>
        public static CardTable FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new CardTable();

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new CardTableFormatException($"JSON 语法错误：{e.Message}");
            }

            var serializer = JsonSerializer.Create(BuildSettings());
            var table = new CardTable { Version = root.Value<int?>("version") ?? 1 };

            // 空表是合法状态（工具刚装上、还没写卡），不是错误。
            if (!(root["cards"] is JArray cards)) return table;

            for (int i = 0; i < cards.Count; i++)
            {
                var tok = cards[i];

                // ★ 逐行反序列化而不是整表一次 ToObject：这样任何一行出错都能带上
                //   「第几张卡 / 哪个 id」的上下文。整表一次转换只会给出一个 JSON 路径
                //   （cards[7].effects[1].amount），使用者得自己数到第 8 张卡。
                string id = tok is JObject jo ? jo.Value<string>("id") : null;

                try
                {
                    table.Cards.Add(tok.ToObject<CardRow>(serializer));
                }
                catch (Exception e)
                {
                    string where = string.IsNullOrEmpty(id) ? $"第 {i + 1} 张卡" : $"第 {i + 1} 张卡「{id}」";
                    throw new CardTableFormatException($"{where}：{Innermost(e).Message}");
                }
            }

            return table;
        }

        /// <summary>
        /// 深拷贝一棵效果树，走一次 JSON 往返。
        ///
        /// ★ 为什么需要它：升级版省略 <c>effects</c> 时要继承基础版的效果。
        ///   直接把同一个 <c>List</c>（和里面同一批 <see cref="Game.Effects.CardEffect"/> 实例）
        ///   赋给两个 ScriptableObject，会让两个资产的 <c>[SerializeReference]</c> 指向同一批对象。
        ///   Unity 落盘时各写一份拷贝，所以**运行时看不出问题**——
        ///   但在导入过程中改基础版的效果会同时改掉升级版，
        ///   而且（阶段 3）编辑器窗口里编基础版会让升级版跟着变。
        ///   这正是铁律 1 那类「共享实例」故障的形状，用一次往返换掉它很划算。
        /// </summary>
        public static System.Collections.Generic.List<Game.Effects.CardEffect> CloneEffects(
            System.Collections.Generic.List<Game.Effects.CardEffect> src)
        {
            if (src == null) return null;

            var serializer = JsonSerializer.Create(BuildSettings());
            var token = JToken.FromObject(src, serializer);
            return token.ToObject<System.Collections.Generic.List<Game.Effects.CardEffect>>(serializer);
        }

        /// <summary>
        /// 剥到最内层异常。Newtonsoft 会把转换器抛出的异常包进
        /// <see cref="JsonSerializationException"/>，而我们自己那些
        /// <see cref="CardTableFormatException"/> 的信息才是有用的那条。
        /// </summary>
        private static Exception Innermost(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e;
        }
    }
}
