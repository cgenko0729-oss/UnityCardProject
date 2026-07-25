using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>组合子：按权重随机挑 N 个子效果执行。</summary>
    [Serializable]
    public class RandomPickEffect : CardEffect
    {
        [Serializable]
        public class Option
        {
            public string Note;
            public int Weight = 10;

            [SerializeReference]
            public CardEffect Effect;
        }

        public List<Option> Options = new List<Option>();

        [Tooltip("抽几个")]
        public int PickCount = 1;

        [Tooltip("允许重复抽到同一个选项")]
        public bool AllowDuplicates;

        public RandomPickEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        public override bool CanApply(EffectContext ctx) => true;

        public override void Apply(EffectContext ctx)
        {
            if (Options == null || Options.Count == 0 || ctx.Battle == null) return;

            var pool = new List<Option>(Options);
            var child = ctx.Child();

            for (int k = 0; k < PickCount && pool.Count > 0; k++)
            {
                int total = 0;
                for (int i = 0; i < pool.Count; i++) total += Mathf.Max(0, pool[i].Weight);
                if (total <= 0) break;

                int roll = ctx.Battle.Rng.Range(RngStream.CardEffect, 0, total);
                int idx = 0;
                for (; idx < pool.Count; idx++)
                {
                    roll -= Mathf.Max(0, pool[idx].Weight);
                    if (roll < 0) break;
                }
                if (idx >= pool.Count) idx = pool.Count - 1;

                EffectResolver.Resolve(pool[idx].Effect, child);
                if (!AllowDuplicates) pool.RemoveAt(idx);
            }
        }
    }
}
