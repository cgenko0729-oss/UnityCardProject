using Game.Cards;
using Game.Relics;
using Game.Statuses;
using Game.Units;

namespace Game.Battle
{
    /// <summary>Hook 的来源信息：谁提供的、有几层。</summary>
    public readonly struct HookSource
    {
        /// <summary>提供者所在的单位（遗物则为玩家）。</summary>
        public readonly BattleUnit Owner;
        /// <summary>状态层数；非状态来源为 1。</summary>
        public readonly int Stacks;
        /// <summary>状态实例，可能为 null（遗物来源）。</summary>
        public readonly StatusInstance Status;
        /// <summary>遗物实例，可能为 null（状态来源）。</summary>
        public readonly RelicInstance Relic;

        public HookSource(BattleUnit owner, int stacks, StatusInstance status = null)
        {
            Owner = owner;
            Stacks = stacks;
            Status = status;
            Relic = null;
        }

        public HookSource(BattleUnit owner, RelicInstance relic)
        {
            Owner = owner;
            Stacks = 1;
            Status = null;
            Relic = relic;
        }
    }

    /// <summary>所有 Hook 接口的公共基。Order 决定同类 Hook 的执行顺序。</summary>
    public interface IBattleHook
    {
        int Order { get; }
    }

    public interface ITurnHook : IBattleHook
    {
        void OnTurnStart(BattleContext ctx, in HookSource src);
        void OnTurnEnd(BattleContext ctx, in HookSource src);
    }

    public interface IBattleFlowHook : IBattleHook
    {
        void OnBattleStart(BattleContext ctx, in HookSource src);
        void OnBattleEnd(BattleContext ctx, in HookSource src, bool victory);
    }

    public interface ICardPlayHook : IBattleHook
    {
        void OnCardPlayed(BattleContext ctx, in HookSource src, CardInstance card);
        void OnCardDrawn(BattleContext ctx, in HookSource src, CardInstance card);
        void OnCardDiscarded(BattleContext ctx, in HookSource src, CardInstance card);
        /// <summary>修改一张卡的费用。用于「本回合手牌费用 -1」等。</summary>
        void ModifyCardCost(BattleContext ctx, in HookSource src, CardInstance card, ref int cost);
    }

    public interface IDamageHook : IBattleHook
    {
        /// <summary>src.Owner 是攻击方时调用。</summary>
        void ModifyOutgoingDamage(BattleContext ctx, in HookSource src, BattleUnit target, ref DamageInfo dmg);
        /// <summary>src.Owner 是受击方时调用。</summary>
        void ModifyIncomingDamage(BattleContext ctx, in HookSource src, BattleUnit attacker, ref DamageInfo dmg);
        /// <summary>
        /// src.Owner 实际掉血之后调用（护甲已扣完）。
        /// dmg 是修正后的最终伤害信息——★ 必须带上它，否则实现者无法区分「被普攻打」和
        /// 「被中毒/荆棘打」，会导致荆棘反弹荆棘这类无限乒乓。
        /// </summary>
        void OnDamaged(BattleContext ctx, in HookSource src, in DamageInfo dmg, int hpLost);
    }

    public interface IBlockHook : IBattleHook
    {
        void ModifyBlockGain(BattleContext ctx, in HookSource src, ref int amount);
        /// <summary>回合开始清护甲时调用。把 clear 置 false 即为「壁垒」。</summary>
        void ModifyBlockDecay(BattleContext ctx, in HookSource src, ref bool clear);
    }

    public interface IHealHook : IBattleHook
    {
        void ModifyHeal(BattleContext ctx, in HookSource src, ref int amount);
    }

    public interface IEnergyHook : IBattleHook
    {
        void ModifyEnergyGain(BattleContext ctx, in HookSource src, ref int amount);
    }

    public interface IDeathHook : IBattleHook
    {
        void OnUnitDied(BattleContext ctx, in HookSource src, BattleUnit dead);
    }

    // ================================================================= 阶段 4 新增的四个拦截点
    //
    // 刻意做成四个独立的小接口，而不是往已有的 8 个接口上加方法：
    // 加方法会逼所有既有实现类补一堆空方法，而这四类拦截点的实现者（主要是遗物）
    // 通常只关心其中一个。接口小 → 实现类干净 → Collect 收集到的条目也更少。

    /// <summary>
    /// 状态施加的拦截点。「神器（抵消下 N 次减益）」「免疫中毒」这类效果靠它实现。
    /// ★ 只有 src.Owner == target 的 Hook 会被调用（即：只有自己身上的东西能挡自己的 debuff）。
    /// </summary>
    public interface IStatusHook : IBattleHook
    {
        /// <summary>在层数写入之前调用。把 stacks 改成 0 即为「完全免疫」。</summary>
        void ModifyStatusApply(BattleContext ctx, in HookSource src,
                               StatusDefinition def, BattleUnit target, BattleUnit applier, ref int stacks);

        /// <summary>层数写入之后调用。stacks 是本次实际变化量。</summary>
        void OnStatusApplied(BattleContext ctx, in HookSource src,
                             StatusDefinition def, BattleUnit target, int stacks);
    }

    /// <summary>
    /// 致死伤害的拦截点。「不死图腾」「濒死回血」「死亡时爆炸」靠它实现。
    /// ★ 在扣血之前调用，此时单位仍然存活。
    /// </summary>
    public interface IFatalHook : IBattleHook
    {
        /// <summary>
        /// src.Owner 即将被这次伤害打死时调用。
        /// 把 prevent 置 true 表示阻止死亡（伤害会被钳到剩 1 血）。
        /// 也可以直接改小 lethalDamage。
        /// </summary>
        void OnLethalDamage(BattleContext ctx, in HookSource src, in DamageInfo dmg,
                            ref int lethalDamage, ref bool prevent);
    }

    /// <summary>
    /// 出牌流程的拦截点。「回响（攻击牌打出两次）」「本回合第一张攻击牌免费」
    /// 「打出后洗回抽牌堆」这类改变规则的效果靠它实现。
    /// </summary>
    public interface ICardFlowHook : IBattleHook
    {
        /// <summary>
        /// 卡牌离手之前调用。
        /// cancel 置 true 会中止这次出牌（能量已退还）；
        /// extraPlays 加 1 表示额外再结算一次效果（回响）。
        /// </summary>
        void PreCardPlay(BattleContext ctx, in HookSource src, CardInstance card,
                         ref bool cancel, ref int extraPlays);

        /// <summary>
        /// 决定卡牌打出后的归宿。默认由 BattleController 算出一个 pile 传进来，
        /// 改写它即可（例如「洗回抽牌堆」）。
        /// </summary>
        void ModifyCardDestination(BattleContext ctx, in HookSource src, CardInstance card, ref CardPile pile);
    }

    /// <summary>
    /// 回合资源（抽牌数 / 能量数）的拦截点。
    /// 这两个数原本写死在 RunContext 里，遗物无法介入，因此单独开一个接口。
    /// </summary>
    public interface IResourceHook : IBattleHook
    {
        /// <summary>回合开始抽几张牌。</summary>
        void ModifyTurnDraw(BattleContext ctx, in HookSource src, ref int count);

        /// <summary>回合开始恢复多少能量。</summary>
        void ModifyTurnEnergy(BattleContext ctx, in HookSource src, ref int amount);
    }

    /// <summary>一次伤害的全部信息。在 Hook 管线里被逐层修改。</summary>
    public struct DamageInfo
    {
        public int Amount;
        public DamageKind Kind;
        public bool IgnoreBlock;
        /// <summary>来源卡，可能为 null。</summary>
        public CardInstance Card;
        /// <summary>攻击者，可能为 null（中毒等无来源伤害）。</summary>
        public BattleUnit Attacker;
    }
}
