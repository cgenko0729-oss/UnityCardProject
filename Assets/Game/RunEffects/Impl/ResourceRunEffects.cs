using System;
using Game.Core;
using Game.Localization;
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
            ctx.AddLog(Amount >= 0
                ? Loc.T("run.gold.gain", "获得 {0} 金币", Amount)
                : Loc.T("run.gold.lose", "失去 {0} 金币", -Amount));
        }

        public override string Describe(RunEffectContext ctx)
            => Amount >= 0
                ? Loc.T("run.gold.gain", "获得 {0} 金币", Amount)
                : Loc.T("run.gold.pay", "支付 {0} 金币", -Amount);
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

            ctx.AddLog(actual > 0
                ? Loc.T("run.hp.heal", "回复 {0} 点生命", actual)
                : Loc.T("run.hp.lose", "失去 {0} 点生命", -actual));
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
                    ? (Amount >= 0 ? Loc.T("run.hp.heal_percent", "回复 {0}% 生命", Amount)
                                   : Loc.T("run.hp.lose_percent", "失去 {0}% 生命", -Amount))
                    : (Amount >= 0 ? Loc.T("run.hp.heal", "回复 {0} 点生命", Amount)
                                   : Loc.T("run.hp.lose", "失去 {0} 点生命", -Amount));

            int d = Resolve(ctx);
            return d >= 0 ? Loc.T("run.hp.heal", "回复 {0} 点生命", d)
                          : Loc.T("run.hp.lose", "失去 {0} 点生命", -d);
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
            ctx.AddLog(DescribeMaxHp());
        }

        public override string Describe(RunEffectContext ctx) => DescribeMaxHp();

        private string DescribeMaxHp()
            => Amount >= 0
                ? Loc.T("run.maxhp.gain", "最大生命 +{0}", Amount)
                : Loc.T("run.maxhp.lose", "最大生命 -{0}", -Amount);
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
            ctx.AddLog(Loc.T("run.hp.heal", "回复 {0} 点生命", actual));
        }

        public override string Describe(RunEffectContext ctx)
        {
            if (ctx?.Run == null) return Loc.T("run.hp.heal_percent_max", "回复 {0}% 最大生命", PercentOfMax);
            return Loc.T("run.hp.heal", "回复 {0} 点生命", Mathf.Max(1, ctx.Run.MaxHp * PercentOfMax / 100));
        }
    }
}
