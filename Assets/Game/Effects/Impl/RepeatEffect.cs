using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>组合子：把一组效果重复执行 N 次。用于「连续效果」「重复执行」。</summary>
    [Serializable]
    public class RepeatEffect : CardEffect
    {
        public EffectValue Times = EffectValue.Flat(2);

        [SerializeReference]
        public List<CardEffect> Effects = new List<CardEffect>();

        public RepeatEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override bool CanApply(EffectContext ctx) => true;

        public override void Apply(EffectContext ctx)
        {
            int n = Times.Evaluate(ctx, ctx.Source);
            if (n <= 0) return;

            var child = ctx.Child();
            var stack = ctx.Battle?.Resolution;

            if (stack == null)
            {
                for (int i = 0; i < n; i++)
                {
                    child.RepeatIndex = i;
                    EffectResolver.ResolveAll(Effects, child);
                }
                return;
            }

            // ★ 不能用 for 循环内联跑：子效果里若有选牌，结算会挂起，
            //   而循环变量 i 活在 C# 调用栈上，挂起后就永远回不来了（只会跑第 0 次）。
            //   改成把 n 次全部压进帧栈——倒序压，因为栈是后进先出，倒着压第 0 次才在最上面。
            //   n 个帧共用同一个 child 上下文，RepeatIndex 由帧在每步之前写入，
            //   与原来「共用 child、循环里改 RepeatIndex」的行为完全一致。
            for (int i = n - 1; i >= 0; i--)
                stack.Push(Effects, child, null, i);
        }

        public override string Describe(EffectContext ctx) => Times.Evaluate(ctx, ctx.Source).ToString();
    }
}
