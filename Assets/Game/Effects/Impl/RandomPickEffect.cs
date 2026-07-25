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

            // ★ 先把 PickCount 次抽取一次性抽完，再统一执行。
            //   原来是「抽一个、跑一个、再抽下一个」，但子效果里若有选牌会挂起，
            //   循环就断在半路。抽取本身只依赖权重表、不依赖执行结果，
            //   所以提前抽完不改变抽取结果本身。
            //   （副作用：PickCount > 1 且子效果会消耗随机流时，随机流的先后次序与旧版不同。
            //     目前全工程无任何内容或测试使用本组合子，不存在回归面。）
            var picked = new List<CardEffect>(PickCount);
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

                if (pool[idx].Effect != null) picked.Add(pool[idx].Effect);
                if (!AllowDuplicates) pool.RemoveAt(idx);
            }

            if (picked.Count == 0) return;

            var stack = ctx.Battle.Resolution;
            if (stack == null)
            {
                for (int i = 0; i < picked.Count; i++) EffectResolver.Resolve(picked[i], child);
                return;
            }

            // 倒序压栈，保证抽到的第一个最先执行
            for (int i = picked.Count - 1; i >= 0; i--)
                stack.Push(new[] { picked[i] }, child);
        }
    }
}
