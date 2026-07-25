using System;
using System.Collections.Generic;
using Game.Core;
using Game.Units;
using UnityEngine;

namespace Game.Effects
{
    public enum TargetKind
    {
        /// <summary>不需要目标（抽牌、加能量）。</summary>
        None = 0,
        Self = 1,
        /// <summary>玩家点选 / AI 指定的那个目标。</summary>
        ChosenTarget = 2,
        AllEnemies = 3,
        AllAllies = 4,
        AllUnits = 5,
        RandomEnemy = 6,
        LowestHpEnemy = 7,
        HighestHpEnemy = 8,
        /// <summary>上一个效果打到的目标（借鉴 Monster Train 的 targets 传递）。</summary>
        PreviousTargets = 9,
    }

    [Serializable]
    public struct TargetSelector
    {
        public TargetKind Kind;

        [Tooltip("RandomEnemy 时取几个，0 视为 1")]
        public int Count;

        [Tooltip("RandomEnemy 时是否允许重复命中同一目标")]
        public bool AllowDuplicates;

        public bool ExcludeSelf;

        [Tooltip("只保留带某状态的目标，可留空")]
        public string RequireStatusId;

        public static TargetSelector Chosen => new TargetSelector { Kind = TargetKind.ChosenTarget };
        public static TargetSelector SelfOnly => new TargetSelector { Kind = TargetKind.Self };
        public static TargetSelector AllEnemies => new TargetSelector { Kind = TargetKind.AllEnemies };
        public static TargetSelector NoTarget => new TargetSelector { Kind = TargetKind.None };
        public static TargetSelector Previous => new TargetSelector { Kind = TargetKind.PreviousTargets };
    }

    public static class TargetResolver
    {
        /// <summary>把 selector 解析成具体单位，写进 buffer（内部会先 Clear）。</summary>
        public static void Resolve(in TargetSelector sel, EffectContext ctx, List<BattleUnit> buffer)
        {
            buffer.Clear();
            var b = ctx.Battle;
            if (b == null) return;

            switch (sel.Kind)
            {
                case TargetKind.None:
                    break;

                case TargetKind.Self:
                    if (ctx.Source != null && ctx.Source.IsAlive) buffer.Add(ctx.Source);
                    break;

                case TargetKind.ChosenTarget:
                    if (ctx.ChosenTarget != null && ctx.ChosenTarget.IsAlive) buffer.Add(ctx.ChosenTarget);
                    break;

                case TargetKind.AllEnemies:
                    for (int i = 0; i < b.AllUnits.Count; i++)
                    {
                        var u = b.AllUnits[i];
                        if (u.IsAlive && u.IsEnemyOf(ctx.Source)) buffer.Add(u);
                    }
                    break;

                case TargetKind.AllAllies:
                    for (int i = 0; i < b.AllUnits.Count; i++)
                    {
                        var u = b.AllUnits[i];
                        if (u.IsAlive && !u.IsEnemyOf(ctx.Source)) buffer.Add(u);
                    }
                    break;

                case TargetKind.AllUnits:
                    for (int i = 0; i < b.AllUnits.Count; i++)
                        if (b.AllUnits[i].IsAlive) buffer.Add(b.AllUnits[i]);
                    break;

                case TargetKind.RandomEnemy:
                {
                    var pool = b.RentUnitBuffer();
                    for (int i = 0; i < b.AllUnits.Count; i++)
                    {
                        var u = b.AllUnits[i];
                        if (u.IsAlive && u.IsEnemyOf(ctx.Source)) pool.Add(u);
                    }
                    int n = sel.Count <= 0 ? 1 : sel.Count;
                    for (int i = 0; i < n && pool.Count > 0; i++)
                    {
                        // ★ 预览模式下不得消耗随机流，否则 UI 每帧的可打性判断会让随机漂移
                        int idx = ctx.PreviewMode ? 0 : b.Rng.Range(RngStream.CardEffect, 0, pool.Count);
                        buffer.Add(pool[idx]);
                        if (!sel.AllowDuplicates) pool.RemoveAt(idx);
                    }
                    b.ReturnUnitBuffer(pool);
                    break;
                }

                case TargetKind.LowestHpEnemy:
                case TargetKind.HighestHpEnemy:
                {
                    BattleUnit best = null;
                    for (int i = 0; i < b.AllUnits.Count; i++)
                    {
                        var u = b.AllUnits[i];
                        if (!u.IsAlive || !u.IsEnemyOf(ctx.Source)) continue;
                        if (best == null) { best = u; continue; }
                        bool better = sel.Kind == TargetKind.LowestHpEnemy ? u.Hp < best.Hp : u.Hp > best.Hp;
                        if (better) best = u;
                    }
                    if (best != null) buffer.Add(best);
                    break;
                }

                case TargetKind.PreviousTargets:
                    for (int i = 0; i < ctx.LastTargets.Count; i++)
                        if (ctx.LastTargets[i].IsAlive) buffer.Add(ctx.LastTargets[i]);
                    break;
            }

            if (sel.ExcludeSelf && ctx.Source != null) buffer.Remove(ctx.Source);

            if (!string.IsNullOrEmpty(sel.RequireStatusId))
            {
                for (int i = buffer.Count - 1; i >= 0; i--)
                    if (buffer[i].GetStatusStacks(sel.RequireStatusId) <= 0) buffer.RemoveAt(i);
            }
        }
    }
}
