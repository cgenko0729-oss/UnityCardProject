using System;

namespace Game.Effects
{
    /// <summary>
    /// 所有效果的基类。用 [SerializeReference] 内嵌进 CardDefinition / EnemyAction。
    ///
    /// ★★★ 铁律：子类不得有任何在 Apply 中被写入的实例字段。
    /// 一个效果实例被该卡的所有 CardInstance 共享，写字段会造成跨实例串数据，
    /// 而且只在特定时序下出现，极难排查。可变数据一律放 EffectContext / BattleContext。
    /// （Editor 下有 ContentValidator 自动扫描这条规则。）
    /// </summary>
    [Serializable]
    public abstract class CardEffect
    {
        public TargetSelector Target = TargetSelector.Chosen;

        /// <summary>
        /// 能否施放。返回 false 会让整张卡不可打出（借鉴 Monster Train 的 TestEffect）。
        /// 注意：组合子（Conditional / RandomPick）应当永远返回 true，否则会误锁整张卡。
        /// </summary>
        public virtual bool CanApply(EffectContext ctx) => true;

        /// <summary>执行。调用前 ctx.Targets 已由 EffectResolver 解析完毕。</summary>
        public abstract void Apply(EffectContext ctx);

        /// <summary>生成描述用的数值文本，填进 CardDefinition.DescriptionTemplate 的 {N}。</summary>
        public virtual string Describe(EffectContext ctx) => string.Empty;
    }
}
