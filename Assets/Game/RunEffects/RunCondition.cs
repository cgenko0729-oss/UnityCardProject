using System;
using UnityEngine;

namespace Game.RunEffects
{
    public enum RunConditionKind
    {
        Always = 0,
        GoldAtLeast = 1,
        HpBelowPercent = 2,
        HpAtLeast = 3,
        HasRelic = 4,
        HasCard = 5,
        DeckSizeAtLeast = 6,
        HasUpgradableCard = 7,
        BattlesWonAtLeast = 8,
    }

    /// <summary>可序列化的局外条件。事件选项的可用性与 ConditionalRunEffect 共用。</summary>
    [Serializable]
    public struct RunCondition
    {
        public RunConditionKind Kind;

        [Tooltip("Kind 需要 id 时使用（遗物 id / 卡牌 id）")]
        public string Id;

        public int Value;

        [Tooltip("勾选则结果取反")]
        public bool Invert;

        public bool Test(RunEffectContext ctx)
        {
            var run = ctx != null ? ctx.Run : null;
            bool r;

            switch (Kind)
            {
                case RunConditionKind.GoldAtLeast:
                    r = run != null && run.Gold >= Value;
                    break;
                case RunConditionKind.HpBelowPercent:
                    r = run != null && run.Hp * 100 < run.MaxHp * Value;
                    break;
                case RunConditionKind.HpAtLeast:
                    r = run != null && run.Hp >= Value;
                    break;
                case RunConditionKind.HasRelic:
                    r = run != null && run.HasRelic(Id);
                    break;
                case RunConditionKind.HasCard:
                    r = run != null && HasCard(ctx, Id);
                    break;
                case RunConditionKind.DeckSizeAtLeast:
                    r = run != null && run.Deck.Count >= Value;
                    break;
                case RunConditionKind.HasUpgradableCard:
                    r = run != null && HasUpgradable(ctx);
                    break;
                case RunConditionKind.BattlesWonAtLeast:
                    r = run != null && run.BattlesWon >= Value;
                    break;
                default:
                    r = true;
                    break;
            }

            return Invert ? !r : r;
        }

        private static bool HasCard(RunEffectContext ctx, string id)
        {
            var deck = ctx.Run.Deck;
            for (int i = 0; i < deck.Count; i++)
                if (deck[i].Id == id) return true;
            return false;
        }

        private static bool HasUpgradable(RunEffectContext ctx)
        {
            var deck = ctx.Run.Deck;
            for (int i = 0; i < deck.Count; i++)
                if (deck[i].CanUpgrade) return true;
            return false;
        }
    }
}
