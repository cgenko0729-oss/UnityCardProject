using System;

namespace Game.Cards
{
    public enum CardType { Attack, Skill, Power, Status, Curse }

    public enum CardRarity { Basic, Common, Uncommon, Rare, Special }

    /// <summary>
    /// 牌堆。<see cref="None"/> 表示「哪个堆都不进」——能力牌打出后即消失，
    /// 以及 <see cref="Game.Battle.ICardFlowHook.ModifyCardDestination"/> 想让一张牌凭空消失时用。
    /// </summary>
    public enum CardPile { Draw, Hand, Discard, Exhaust, None }

    public enum CostMode
    {
        /// <summary>普通固定费用。</summary>
        Fixed,
        /// <summary>消耗全部能量，消耗量写入 EffectContext.XValue。</summary>
        X,
        /// <summary>不可打出（诅咒 / 状态牌）。</summary>
        Unplayable
    }

    /// <summary>玩家出这张牌时需要指定什么目标。</summary>
    public enum CardTargetKind { None, SingleEnemy, AllEnemies, Self, RandomEnemy }

    [Flags]
    public enum CardKeyword
    {
        None = 0,
        /// <summary>消耗：打出后进入消耗堆。</summary>
        Exhaust = 1 << 0,
        /// <summary>保留：回合结束不弃掉。</summary>
        Retain = 1 << 1,
        /// <summary>固有：开局必定在手牌。</summary>
        Innate = 1 << 2,
        /// <summary>虚无：回合结束若仍在手牌则消耗。</summary>
        Ethereal = 1 << 3,
        /// <summary>不可打出。</summary>
        Unplayable = 1 << 4,
    }
}
