using System;
using Game.Units;
using UnityEngine;

namespace Game.Effects
{
    public enum ValueScale
    {
        /// <summary>固定值。</summary>
        None = 0,
        PerStatusStackOnSelf = 1,
        PerStatusStackOnTarget = 2,
        PerCardInHand = 3,
        PerCardPlayedThisTurn = 4,
        PerEnemyAlive = 5,
        PerBlockOnSelf = 6,
        PerMissingHpOnSelf = 7,
        /// <summary>X 费卡实际消耗掉的能量。</summary>
        XValue = 8,
        /// <summary>RepeatEffect / 多段攻击的当前次数（从 0 起）。</summary>
        PerRepeatIndex = 9,
        PerCardInDiscard = 10,
    }

    /// <summary>
    /// 可缩放数值。「根据状态动态计算数值」不需要新效果类，全部在这里解决。
    /// 灵感来自 Monster Train 的 useStatusEffectStackMultiplier，扩展成通用缩放。
    /// </summary>
    [Serializable]
    public struct EffectValue
    {
        public int Base;
        public ValueScale Scale;

        [Tooltip("Scale 需要 id 时使用（状态 id）")]
        public string ScaleId;

        [Tooltip("每一个缩放单位增加多少")]
        public int PerUnit;

        [Tooltip("结果下限。Min == Max == 0 表示不钳制")]
        public int Min;

        [Tooltip("结果上限。Min == Max == 0 表示不钳制")]
        public int Max;

        public static EffectValue Flat(int v) => new EffectValue { Base = v };

        public static EffectValue PerStack(string statusId, int perUnit, int baseValue = 0)
            => new EffectValue { Base = baseValue, Scale = ValueScale.PerStatusStackOnSelf, ScaleId = statusId, PerUnit = perUnit };

        public int Evaluate(EffectContext ctx, BattleUnit target)
        {
            int units = 0;
            var battle = ctx != null ? ctx.Battle : null;

            switch (Scale)
            {
                case ValueScale.None:
                    break;
                case ValueScale.PerStatusStackOnSelf:
                    units = ctx?.Source != null ? ctx.Source.GetStatusStacks(ScaleId) : 0;
                    break;
                case ValueScale.PerStatusStackOnTarget:
                    units = target != null ? target.GetStatusStacks(ScaleId) : 0;
                    break;
                case ValueScale.PerCardInHand:
                    units = battle?.Deck != null ? battle.Deck.Hand.Count : 0;
                    break;
                case ValueScale.PerCardInDiscard:
                    units = battle?.Deck != null ? battle.Deck.DiscardPile.Count : 0;
                    break;
                case ValueScale.PerCardPlayedThisTurn:
                    units = battle != null ? battle.CardsPlayedThisTurn : 0;
                    break;
                case ValueScale.PerEnemyAlive:
                    units = battle != null ? battle.AliveEnemyCount : 0;
                    break;
                case ValueScale.PerBlockOnSelf:
                    units = ctx?.Source != null ? ctx.Source.Block : 0;
                    break;
                case ValueScale.PerMissingHpOnSelf:
                    units = ctx?.Source != null ? ctx.Source.MaxHp - ctx.Source.Hp : 0;
                    break;
                case ValueScale.XValue:
                    units = ctx != null ? ctx.XValue : 0;
                    break;
                case ValueScale.PerRepeatIndex:
                    units = ctx != null ? ctx.RepeatIndex : 0;
                    break;
            }

            int v = Base + units * PerUnit;
            if (Min != 0 || Max != 0)
            {
                if (Min != 0 && v < Min) v = Min;
                if (Max != 0 && v > Max) v = Max;
            }
            return v;
        }
    }
}
