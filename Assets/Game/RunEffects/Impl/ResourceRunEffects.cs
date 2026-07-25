using System;
using Game.Core;
using UnityEngine;

namespace Game.RunEffects.Impl
{
    /// <summary>金币增减。负数为花费；花费时若不够会让整个选项变灰。</summary>
    [Serializable]
    public class GoldRunEffect : RunEffect
    {
        [Tooltip("正数为获得，负数为花费")]
        public int Amount = 10;

        public override bool CanApply(RunEffectContext ctx)
            => Amount >= 0 || (ctx.Run != null && ctx.Run.Gold >= -Amount);

        public override void Apply(RunEffectContext ctx)
        {
            ctx.Run.Gold += Amount;
            if (ctx.Run.Gold < 0) ctx.Run.Gold = 0;
            ctx.AddLog(Amount >= 0 ? $"获得 {Amount} 金币" : $"失去 {-Amount} 金币");
        }

        public override string Describe(RunEffectContext ctx)
            => Amount >= 0 ? $"获得 {Amount} 金币" : $"支付 {-Amount} 金币";
    }

    /// <summary>
    /// 生命值增减。可以按最大生命的百分比计算——事件里「失去 10% 生命」这种写法很常见，
    /// 写死数值会让前期和后期的代价严重失衡。
    /// </summary>
    [Serializable]
    public class HpRunEffect : RunEffect
    {
        [Tooltip("正数为回复，负数为受伤")]
        public int Amount = -5;

        [Tooltip("勾选后 Amount 视为「最大生命的百分比」")]
        public bool PercentOfMax;

        [Tooltip("按百分比计算时的最小绝对值，避免前期算出 0")]
        public int MinMagnitude = 1;

        public override bool CanApply(RunEffectContext ctx) => ctx.Run != null;

        public override void Apply(RunEffectContext ctx)
        {
            int delta = Resolve(ctx);
            if (delta == 0) return;

            int actual = ctx.Run.ModifyHp(delta);
            if (actual == 0) return;

            ctx.AddLog(actual > 0 ? $"回复 {actual} 点生命" : $"失去 {-actual} 点生命");
        }

        private int Resolve(RunEffectContext ctx)
        {
            if (!PercentOfMax) return Amount;

            int magnitude = Mathf.Abs(Amount) * ctx.Run.MaxHp / 100;
            if (magnitude < MinMagnitude) magnitude = MinMagnitude;
            return Amount >= 0 ? magnitude : -magnitude;
        }

        public override string Describe(RunEffectContext ctx)
        {
            if (ctx?.Run == null)
                return PercentOfMax
                    ? (Amount >= 0 ? $"回复 {Amount}% 生命" : $"失去 {-Amount}% 生命")
                    : (Amount >= 0 ? $"回复 {Amount} 点生命" : $"失去 {-Amount} 点生命");

            int d = Resolve(ctx);
            return d >= 0 ? $"回复 {d} 点生命" : $"失去 {-d} 点生命";
        }
    }

    /// <summary>最大生命增减。</summary>
    [Serializable]
    public class MaxHpRunEffect : RunEffect
    {
        public int Amount = 5;

        [Tooltip("增加最大生命时是否同时回满这部分")]
        public bool AlsoHeal = true;

        public override void Apply(RunEffectContext ctx)
        {
            ctx.Run.ModifyMaxHp(Amount, AlsoHeal);
            ctx.AddLog(Amount >= 0 ? $"最大生命 +{Amount}" : $"最大生命 {Amount}");
        }

        public override string Describe(RunEffectContext ctx)
            => Amount >= 0 ? $"最大生命 +{Amount}" : $"最大生命 {Amount}";
    }

    /// <summary>按缺失生命的比例回血。休息点的「休整」用它。</summary>
    [Serializable]
    public class RestHealRunEffect : RunEffect
    {
        [Tooltip("回复最大生命的百分之多少")]
        public int PercentOfMax = 30;

        public override void Apply(RunEffectContext ctx)
        {
            int amount = Mathf.Max(1, ctx.Run.MaxHp * PercentOfMax / 100);
            int actual = ctx.Run.ModifyHp(amount);
            ctx.AddLog($"回复 {actual} 点生命");
        }

        public override string Describe(RunEffectContext ctx)
        {
            if (ctx?.Run == null) return $"回复 {PercentOfMax}% 最大生命";
            return $"回复 {Mathf.Max(1, ctx.Run.MaxHp * PercentOfMax / 100)} 点生命";
        }
    }
}
