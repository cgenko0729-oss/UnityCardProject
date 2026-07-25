using System.Collections.Generic;
using Game.Effects.Impl;
using Game.Statuses;

namespace Game.Effects
{
    /// <summary>
    /// 效果树的只读遍历工具。
    ///
    /// ★ 存在的理由是铁律 22：凡是「扫一遍这张卡都干了什么」的代码，
    ///   都必须递归进四个组合子（Repeat / Conditional / RandomPick / Delayed），
    ///   否则「重复 3 次施加易伤」这种正常卡会被漏掉。
    ///   把递归收在这一个地方，将来新增组合子只要改这里，
    ///   而不用去翻工程里所有扫效果树的调用点——那种改法必然会漏一处。
    ///
    /// ★ 递归以「单个效果」为单位（<see cref="CollectStatuses(CardEffect, List{StatusDefinition}, int)"/>），
    ///   不用共享的临时 List 中转。用静态 buffer 中转的写法在
    ///   「RandomPick 的选项又是一个 RandomPick」时会自己踩自己。
    ///
    /// 所有方法都是纯查询，不碰 <see cref="EffectContext"/>，因此 UI 可以随便调。
    /// </summary>
    public static class EffectTree
    {
        /// <summary>防御性的递归深度上限。效果树是资产配出来的，理论上可能被配成环。</summary>
        private const int MaxDepth = 12;

        /// <summary>
        /// 收集这棵效果树里所有 <see cref="ApplyStatusEffect"/> 引用到的状态（去重，保持出现顺序）。
        /// Tooltip 用它回答「打出这张牌会牵扯到哪些状态」。
        /// </summary>
        public static void CollectStatuses(List<CardEffect> effects, List<StatusDefinition> buffer)
        {
            if (effects == null || buffer == null) return;
            for (int i = 0; i < effects.Count; i++) CollectStatuses(effects[i], buffer, 0);
        }

        private static void CollectStatuses(CardEffect effect, List<StatusDefinition> buffer, int depth)
        {
            if (effect == null || depth > MaxDepth) return;

            switch (effect)
            {
                case ApplyStatusEffect apply:
                    if (apply.Status != null && !buffer.Contains(apply.Status)) buffer.Add(apply.Status);
                    return;

                // ---------------- 以下是四个组合子
                case RepeatEffect rep:
                    CollectStatuses(rep.Effects, buffer, depth + 1);
                    return;

                case ConditionalEffect cond:
                    CollectStatuses(cond.Then, buffer, depth + 1);
                    CollectStatuses(cond.Else, buffer, depth + 1);
                    return;

                case DelayedEffect del:
                    CollectStatuses(del.Effects, buffer, depth + 1);
                    return;

                case RandomPickEffect pick:
                    if (pick.Options == null) return;
                    for (int i = 0; i < pick.Options.Count; i++)
                    {
                        var opt = pick.Options[i];
                        if (opt != null) CollectStatuses(opt.Effect, buffer, depth + 1);
                    }
                    return;
            }
        }

        private static void CollectStatuses(List<CardEffect> effects, List<StatusDefinition> buffer, int depth)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++) CollectStatuses(effects[i], buffer, depth);
        }
    }
}
