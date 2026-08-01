using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.Effects;

namespace Game.Editor.CardTables
{
    /// <summary>
    /// 效果类的短名注册表——**由反射从类名和字段推导，没有任何手工维护的映射表**。
    ///
    /// ★★★ 这是整个卡表方案的关键决定。
    ///   使用者提出的痛点里最硬的一条是「难以给新卡加效果」。压缩 schema 只是让编卡变舒服；
    ///   真正解决那一条的是这里：<c>kind</c> 判别符、JSON 键名、以及（阶段 3）窗口里的字段控件，
    ///   三者全部反射推导。于是在 <c>Effects/Impl/</c> 新建一个效果类之后，
    ///   它**立刻**能在表里写、能在窗口里编，不需要改导入器、不需要改窗口、不需要注册任何东西。
    ///
    /// <para>这与铁律 40 批评的「UI key 注册表」正好相反：注册表漏一条**不报错**
    /// （表现只是某个语言下冒出一句中文）。而这里连注册表都不存在，
    /// 唯一的失败模式是表里写了不存在的 <c>kind</c>——那会在导入时**当场报错并列出所有合法短名**。</para>
    ///
    /// <para>短名规则：类名去掉结尾的 <c>Effect</c>，首字母小写。
    /// <c>DamageEffect</c> → <c>damage</c>，<c>ApplyStatusEffect</c> → <c>applyStatus</c>。
    /// 万一两个类撞了短名，<see cref="Build"/> 会**抛异常并指出双方类名**，
    /// 而不是让后来的那个静默覆盖前一个。</para>
    /// </summary>
    internal static class EffectKinds
    {
        /// <summary>
        /// 类型判别符的键名。
        ///
        /// ★★★ 必须以 <c>$</c> 开头，这不是风格选择而是正确性要求。
        ///   判别符和效果自己的字段共处同一个 JSON 对象，所以它的键名**必须落在
        ///   C# 标识符取不到的空间里**——<c>$</c> 开头的名字永远不可能由某个字段
        ///   camelCase 而来，于是「反射推导字段名」这条设计不会在将来某个新字段上撞车。
        ///
        /// <para>这个坑是实测出来的，代价很具体：判别符原本叫 <c>kind</c>，
        ///   而 <see cref="Game.Effects.Impl.DamageEffect"/> 有一个 <c>DamageKind Kind</c> 字段，
        ///   camelCase 之后也是 <c>kind</c>。于是「造成 X 点非攻击伤害」的卡会被写成
        ///   <c>{"kind":"damage","kind":"Loss"}</c>，读回来判别符变成 <c>Loss</c>。
        ///   而 <c>DamageKind.Attack</c> 是默认值、会被省略，
        ///   **所以 95% 的卡完全正常，只有设了非攻击伤害的卡会坏，且坏在读取阶段**。
        ///   顺带一提，那正是本工程铁律 20 特别点出来的那个字段。</para>
        ///
        /// <para><see cref="EffectSpec"/> 的构造函数还会额外断言「没有字段的 JSON 名等于它」，
        /// 万一将来有人把它改回一个可撞的名字，会当场抛异常而不是静默产出坏表。</para>
        /// </summary>
        public const string KindKey = "$kind";

        private static Dictionary<string, EffectSpec> _byShortName;
        private static Dictionary<Type, EffectSpec> _byType;

        /// <summary>全部短名，按字母序。用于错误信息和（阶段 3）窗口的添加菜单。</summary>
        public static IEnumerable<string> AllShortNames
        {
            get { EnsureBuilt(); return _byShortName.Keys.OrderBy(k => k, StringComparer.Ordinal); }
        }

        public static IEnumerable<EffectSpec> All
        {
            get { EnsureBuilt(); return _byType.Values.OrderBy(s => s.ShortName, StringComparer.Ordinal); }
        }

        public static EffectSpec ForType(Type t)
        {
            EnsureBuilt();
            if (_byType.TryGetValue(t, out var spec)) return spec;
            throw new InvalidOperationException(
                $"[CardTable] 类型 {t.FullName} 不是可序列化的 CardEffect 子类" +
                $"（是抽象类，或不在 {typeof(CardEffect).Assembly.GetName().Name} 程序集里）。");
        }

        /// <summary>按短名查。查不到时抛出**带全部合法短名**的异常——错误信息本身就是文档。</summary>
        public static EffectSpec ForShortName(string shortName)
        {
            EnsureBuilt();
            if (shortName != null && _byShortName.TryGetValue(shortName, out var spec)) return spec;

            throw new CardTableFormatException(
                $"未知的效果 {KindKey}「{shortName}」。合法取值：{string.Join(", ", AllShortNames)}");
        }

        public static bool TryGet(string shortName, out EffectSpec spec)
        {
            EnsureBuilt();
            spec = null;
            return shortName != null && _byShortName.TryGetValue(shortName, out spec);
        }

        private static void EnsureBuilt()
        {
            if (_byShortName == null) Build();
        }

        private static void Build()
        {
            var byShort = new Dictionary<string, EffectSpec>(StringComparer.Ordinal);
            var byType = new Dictionary<Type, EffectSpec>();

            // 只扫 CardEffect 所在的程序集（Game.Runtime）。全域扫描既慢，
            // 又会把测试程序集里的临时子类当成正式内容收进来。
            var types = typeof(CardEffect).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CardEffect).IsAssignableFrom(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal);

            foreach (var t in types)
            {
                string shortName = ShortNameFor(t);

                if (byShort.TryGetValue(shortName, out var clash))
                {
                    throw new InvalidOperationException(
                        $"[CardTable] 效果短名冲突：{t.FullName} 与 {clash.Type.FullName} " +
                        $"都推导出「{shortName}」。请重命名其中一个类。");
                }

                var spec = new EffectSpec(t, shortName);
                byShort.Add(shortName, spec);
                byType.Add(t, spec);
            }

            _byShortName = byShort;
            _byType = byType;
        }

        /// <summary>DamageEffect → damage；ApplyStatusEffect → applyStatus。</summary>
        private static string ShortNameFor(Type t)
        {
            string n = t.Name;
            const string suffix = "Effect";
            if (n.Length > suffix.Length && n.EndsWith(suffix, StringComparison.Ordinal))
                n = n.Substring(0, n.Length - suffix.Length);
            return Json.CamelCase(n);
        }
    }

    /// <summary>一个效果类的序列化规格：短名 + 稳定的字段顺序 + 一份用来比对默认值的模板实例。</summary>
    internal sealed class EffectSpec
    {
        public readonly Type Type;
        public readonly string ShortName;

        /// <summary>
        /// 字段列表，**基类字段在前**（于是 <c>CardEffect.Target</c> 恒排第一），
        /// 同一层内按声明顺序。顺序必须稳定，否则同一张卡两次导出的 JSON 键序不同，
        /// git diff 会满屏都是噪音，而且往返自检永远过不了。
        /// </summary>
        public readonly FieldInfo[] Fields;

        /// <summary>
        /// 一份全新实例，只用来读「构造函数设定的默认值」。
        /// 写 JSON 时与它相同的字段直接省略——于是 <c>BlockEffect</c> 只写
        /// <c>{"kind":"block","amount":5}</c>，而不是把 Target/Amount 六个子字段全铺出来。
        /// 读回来时省略的键自然保持构造函数默认值（<c>BlockEffect()</c> 会把 Target 设成 SelfOnly），
        /// 所以「省略 = 默认」这条语义两个方向都成立。
        /// </summary>
        public readonly object Template;

        /// <summary>
        /// 这个效果是不是「组合子」——即它的字段里装得下别的效果。
        ///
        /// ★ 由**反射判断**，不是一张 { repeat, conditional, randomPick, delayed } 的硬编码名单。
        ///   名单会漏：写第五个组合子的人不会想到要来这里登记，
        ///   而漏登记的后果是编辑器窗口把它当叶子效果画，它的子效果字段直接不显示，
        ///   于是那个新组合子在窗口里**永远配不出内容**，且不报任何错。
        ///   （铁律 22 已经点过「效果树的递归入口有好几处」这件事，这里是又一处。）
        ///
        /// <para>判据：任何字段的类型是 <c>CardEffect</c>、<c>List&lt;CardEffect&gt;</c>，
        /// 或者是一个「自己带 CardEffect 字段」的列表元素类型
        /// （<c>RandomPickEffect.Options</c> 就是这一种）。</para>
        /// </summary>
        public readonly bool HasChildEffects;

        private readonly Dictionary<string, FieldInfo> _byJsonName;
        private readonly Dictionary<FieldInfo, string> _jsonNames;

        public EffectSpec(Type type, string shortName)
        {
            Type = type;
            ShortName = shortName;
            Fields = CollectFields(type);
            Template = Activator.CreateInstance(type);
            HasChildEffects = DetectChildEffects(Fields);

            _byJsonName = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            _jsonNames = new Dictionary<FieldInfo, string>();

            foreach (var f in Fields)
            {
                string name = Json.CamelCase(f.Name);

                // ★ 见 EffectKinds.KindKey 的注释：判别符与字段同处一个 JSON 对象，
                //   撞名的后果是「读回来的效果类型变成那个字段的值」，且只在该字段
                //   非默认值时才发作。宁可在这里炸掉编辑器，也不要产出坏表。
                if (name == EffectKinds.KindKey)
                {
                    throw new InvalidOperationException(
                        $"[CardTable] {type.FullName} 的字段 {f.Name} 的 JSON 名与类型判别符" +
                        $"「{EffectKinds.KindKey}」相同。改判别符或改字段名。");
                }

                _byJsonName[name] = f;
                _jsonNames[f] = name;
            }
        }

        public string JsonNameOf(FieldInfo f) => _jsonNames[f];

        public FieldInfo FieldByJsonName(string jsonName)
            => jsonName != null && _byJsonName.TryGetValue(jsonName, out var f) ? f : null;

        /// <summary>给错误信息用：这个效果有哪些可写的键。</summary>
        public string FieldListForError()
        {
            var sb = new StringBuilder();
            foreach (var f in Fields)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(JsonNameOf(f));
            }
            return sb.ToString();
        }

        private static bool DetectChildEffects(FieldInfo[] fields)
        {
            foreach (var f in fields)
            {
                if (CarriesEffect(f.FieldType)) return true;
            }
            return false;
        }

        private static bool CarriesEffect(Type t)
        {
            if (typeof(CardEffect).IsAssignableFrom(t)) return true;

            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(List<>)) return false;

            var elem = t.GetGenericArguments()[0];
            if (typeof(CardEffect).IsAssignableFrom(elem)) return true;

            // RandomPickEffect.Option 这种「包一层的选项类」。只看一层就够——
            // 再往下递归会在自引用类型上死循环，而本工程没有那种形状。
            foreach (var inner in elem.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (typeof(CardEffect).IsAssignableFrom(inner.FieldType)) return true;

            return false;
        }

        private static FieldInfo[] CollectFields(Type t)
        {
            // 从 CardEffect 往下走，基类的字段排在派生类之前。
            var chain = new List<Type>();
            for (var c = t; c != null && c != typeof(object); c = c.BaseType) chain.Add(c);
            chain.Reverse();

            var list = new List<FieldInfo>();
            foreach (var c in chain)
            {
                var declared = c.GetFields(BindingFlags.Public | BindingFlags.Instance |
                                           BindingFlags.DeclaredOnly);
                foreach (var f in declared)
                {
                    if (f.IsNotSerialized) continue;
                    list.Add(f);
                }
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// 卡表格式错误。刻意是独立类型：导入器要把它与「Newtonsoft 自己的语法错误」
    /// 分开报给使用者——前者能指出是哪张卡的哪个字段，后者只能指出行号。
    /// </summary>
    public class CardTableFormatException : Exception
    {
        public CardTableFormatException(string message) : base(message) { }
    }
}
