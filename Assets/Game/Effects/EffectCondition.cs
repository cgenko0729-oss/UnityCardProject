using System;
using Game.Units;
using UnityEngine;

namespace Game.Effects
{
    public enum ConditionKind
    {
        Always = 0,
        SelfHasStatus = 1,
        TargetHasStatus = 2,
        SelfHpBelowPercent = 3,
        TargetHpBelowPercent = 4,
        HandCountAtLeast = 5,
        EnergyAtLeast = 6,
        TurnNumberAtLeast = 7,
        EnemyCountAtLeast = 8,
        LastCardWasAttack = 9,
        IsFirstTurn = 10,
        SelfBlockAtLeast = 11,
    }

    /// <summary>可序列化的条件。给 ConditionalEffect 和 EnemyAction 共用。</summary>
    [Serializable]
    public struct EffectCondition
    {
        public ConditionKind Kind;

        [Tooltip("Kind 需要 id 时使用（状态 id）")]
        public string Id;

        public int Value;

        [Tooltip("勾选则结果取反")]
        public bool Invert;

        public bool Test(EffectContext ctx, BattleUnit target)
        {
            var battle = ctx != null ? ctx.Battle : null;
            bool r;

            switch (Kind)
            {
                case ConditionKind.SelfHasStatus:
                    r = ctx?.Source != null && ctx.Source.GetStatusStacks(Id) >= Math.Max(1, Value);
                    break;
                case ConditionKind.TargetHasStatus:
                    r = target != null && target.GetStatusStacks(Id) >= Math.Max(1, Value);
                    break;
                case ConditionKind.SelfHpBelowPercent:
                    r = ctx?.Source != null && ctx.Source.Hp * 100 < ctx.Source.MaxHp * Value;
                    break;
                case ConditionKind.TargetHpBelowPercent:
                    r = target != null && target.Hp * 100 < target.MaxHp * Value;
                    break;
                case ConditionKind.HandCountAtLeast:
                    r = battle?.Deck != null && battle.Deck.Hand.Count >= Value;
                    break;
                case ConditionKind.EnergyAtLeast:
                    r = battle != null && battle.Energy >= Value;
                    break;
                case ConditionKind.TurnNumberAtLeast:
                    r = battle != null && battle.TurnNumber >= Value;
                    break;
                case ConditionKind.EnemyCountAtLeast:
                    r = battle != null && battle.AliveEnemyCount >= Value;
                    break;
                case ConditionKind.LastCardWasAttack:
                    r = battle != null && battle.LastCardTypePlayed == Cards.CardType.Attack;
                    break;
                case ConditionKind.IsFirstTurn:
                    r = battle != null && battle.TurnNumber <= 1;
                    break;
                case ConditionKind.SelfBlockAtLeast:
                    r = ctx?.Source != null && ctx.Source.Block >= Value;
                    break;
                default:
                    r = true;
                    break;
            }

            return Invert ? !r : r;
        }
    }
}
