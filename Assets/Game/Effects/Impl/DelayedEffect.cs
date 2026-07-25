using System;
using System.Collections.Generic;
using Game.Battle;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>组合子：延迟到回合末 / 下回合开始再执行。</summary>
    [Serializable]
    public class DelayedEffect : CardEffect
    {
        public DelayTiming Timing = DelayTiming.EndOfThisTurn;

        [SerializeReference]
        public List<CardEffect> Effects = new List<CardEffect>();

        public DelayedEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override bool CanApply(EffectContext ctx) => true;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.Battle == null) return;

            // 快照当前上下文，到时候再跑
            var snapshot = ctx.Child();
            snapshot.Depth = 0;
            var effects = Effects;

            ctx.Battle.ScheduleDelayed(Timing, () => EffectResolver.ResolveAll(effects, snapshot));
        }
    }
}
