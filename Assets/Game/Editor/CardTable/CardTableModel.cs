using System.Collections.Generic;

namespace Game.Editor.CardTables
{
    /// <summary>
    /// <c>CardTable.json</c> 的顶层结构。
    ///
    /// ★ 为什么卡牌层用手写 DTO、而效果树用运行时类型直接反射序列化：
    ///   卡牌层只有十来个字段且极少变动，手写 DTO 的成本几乎为零，换来的是**紧凑的 schema**
    ///   （<see cref="CardRow.Upgrade"/> 内嵌块、<c>keywords</c> 写成数组），
    ///   这些都不是 <see cref="Game.Cards.CardDefinition"/> 的字段布局能表达的。
    ///   而效果树有 15 个类、每类若干字段，且**新增效果类是我们想要鼓励的操作**——
    ///   给它写 DTO 意味着每加一个效果类要同步改三处（DTO / 映射 / 窗口 UI），
    ///   正好把这个工具想解决的痛点变得更疼。所以那一层走反射，零注册。
    ///
    /// <para>这与存档的铁律 46①（DTO 不复用运行时类型）**不冲突，因为风险等级不同**：
    ///   存档在玩家磁盘上、不可控、不可重新生成，所以要用编译错误来拦；
    ///   而卡表在 git 里、可随时全量重导、且有 <see cref="CardRules"/> + 201 个用例兜底。
    ///   代价是改字段名会让老表里的那个键变成「未知字段」——
    ///   所以 <see cref="CardTableJson"/> 读取时遇到未知键一律**报错而不是静默忽略**。</para>
    /// </summary>
    public class CardTable
    {
        /// <summary>格式版本。将来改 schema 时用它决定要不要迁移。</summary>
        public int Version = 1;

        public List<CardRow> Cards = new List<CardRow>();
    }

    /// <summary>表里的一张卡。字段名与 JSON 键一一对应（camelCase 由序列化器统一处理）。</summary>
    public class CardRow
    {
        // ---------------------------------------------------------------- 标识
        public string Id;
        public string Name;

        // ---------------------------------------------------------------- 规则
        public int Cost = 1;

        /// <summary>
        /// Fixed / X / Unplayable。省略即 Fixed。
        ///
        /// ★ 只有这一个字段配了 <c>DefaultValueHandling.Ignore</c>：它在 95% 的卡上都是 Fixed，
        ///   每行都写一遍纯属噪音。<c>Type</c> / <c>Rarity</c> / <c>Target</c> 刻意**不**这么做——
        ///   它们是一张卡的身份，值得每行都看得见。<c>Cost</c> 更不能省：X 费卡的 cost 就是 0，
        ///   而 0 恰好是 int 的默认值，省掉它等于让「X 费」和「忘了填费用」长得一模一样。
        /// </summary>
        [Newtonsoft.Json.JsonProperty(
            DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public Game.Cards.CostMode CostMode = Game.Cards.CostMode.Fixed;

        public Game.Cards.CardType Type = Game.Cards.CardType.Attack;
        public Game.Cards.CardRarity Rarity = Game.Cards.CardRarity.Common;
        public Game.Cards.CardTargetKind Target = Game.Cards.CardTargetKind.SingleEnemy;

        /// <summary>
        /// 关键字写成字符串数组（<c>["Exhaust","Retain"]</c>）而不是位标记枚举。
        /// <see cref="Game.Cards.CardKeyword"/> 是 <c>[Flags]</c>，序列化成
        /// <c>"Exhaust, Retain"</c> 那种逗号串在 git diff 里读不出改了哪一位。
        /// </summary>
        public List<string> Keywords;

        // ---------------------------------------------------------------- 描述
        /// <summary>用 {0} {1} 引用第 N 个效果的数值。</summary>
        public string Desc;

        // ---------------------------------------------------------------- 效果
        public List<Game.Effects.CardEffect> Effects = new List<Game.Effects.CardEffect>();

        /// <summary>回合结束时这张牌若仍在手牌就结算的效果（灼烧 / 疑虑这类）。</summary>
        public List<Game.Effects.CardEffect> InHandEndOfTurn;

        // ---------------------------------------------------------------- 升级
        /// <summary>
        /// 升级版。**内嵌块而不是第二张卡**——导入器会自动产出 <c>&lt;id&gt;_plus</c>、
        /// 自动设 <see cref="Game.Cards.CardRarity.Special"/>（铁律 14）、自动接
        /// <c>UpgradedVersion</c>。留空 = 不可升级。
        /// </summary>
        public UpgradeRow Upgrade;

        /// <summary>
        /// 展开升级版时由 <c>CardTableImporter</c> 填的中间结果：这一行的 <c>UpgradedVersion</c>
        /// 该指向哪个 Id。
        ///
        /// <para>★ <c>[JsonIgnore]</c> 是必需的，不是洁癖：设置里开了
        /// <c>MissingMemberHandling.Error</c>，所以任何出现在 JSON 里的键都必须能对上一个字段——
        /// 反过来，任何**不该出现在 JSON 里**的字段都必须显式排除，
        /// 否则导出一次表就会多出一个 <c>upgradeTargetId</c> 键，
        /// 而它是派生数据，手改它不会有任何效果。</para>
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string UpgradeTargetId;
    }

    /// <summary>
    /// 升级版的差量。
    ///
    /// ★ 标量字段（Name / Cost / Desc / Keywords…）省略即继承基础版，
    ///   但 <see cref="Effects"/> 只要出现就是**整体替换，不做按下标 patch**。
    ///   按下标 patch 会重现「两个列表靠下标对应」那一类错位隐患
    ///   （铁律 33、以及事件选项本地化 key 用下标那条已经踩过的坑）：
    ///   往基础版效果列表中间插一个效果，升级版的 patch 会静默打到错误的效果上。
    ///   多写几行 JSON 换掉一整类不报错的故障，划算。
    /// </summary>
    public class UpgradeRow
    {
        public string Name;

        /// <summary>null = 继承基础版。</summary>
        public int? Cost;

        public List<string> Keywords;
        public string Desc;

        public List<Game.Effects.CardEffect> Effects;
        public List<Game.Effects.CardEffect> InHandEndOfTurn;
    }
}
