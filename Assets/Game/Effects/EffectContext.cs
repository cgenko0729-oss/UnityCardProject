using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Units;

namespace Game.Effects
{
    /// <summary>
    /// 一次效果结算的上下文。
    /// 借鉴 Monster Train 的 CardEffectParams，但把它的 13 个 Manager 字段压成 1 个 BattleContext。
    /// </summary>
    public class EffectContext
    {
        public BattleContext Battle;

        /// <summary>施放者（玩家或某个敌人）。</summary>
        public BattleUnit Source;

        /// <summary>玩家点选 / AI 指定的目标，可为 null。</summary>
        public BattleUnit ChosenTarget;

        /// <summary>来源卡，可为 null（敌人行动 / 状态触发时）。</summary>
        public CardInstance Card;

        /// <summary>X 费卡实际消耗的能量。</summary>
        public int XValue;

        /// <summary>递归深度，防止组合子无限嵌套。</summary>
        public int Depth;

        /// <summary>RepeatEffect / 多段攻击当前是第几次（从 0 起）。</summary>
        public int RepeatIndex;

        /// <summary>
        /// 预览模式。UI 每帧调 CanPlayCard 做变灰判断时为 true。
        /// ★ 此模式下任何解析都不得消耗随机流，否则随机会随帧率漂移，破坏「同种子同结果」。
        /// （Monster Train 的 SaveManager.PreviewMode 是同样的用途。）
        /// </summary>
        public bool PreviewMode;

        /// <summary>本次效果解析出的目标。</summary>
        public readonly List<BattleUnit> Targets = new List<BattleUnit>(8);

        /// <summary>上一个效果的目标，供 TargetKind.PreviousTargets 使用。</summary>
        public readonly List<BattleUnit> LastTargets = new List<BattleUnit>(8);

        public Rng Rng => Battle != null ? Battle.Rng : null;

        public void Reset(BattleContext battle, BattleUnit source, BattleUnit chosenTarget, CardInstance card)
        {
            Battle = battle;
            Source = source;
            ChosenTarget = chosenTarget;
            Card = card;
            XValue = 0;
            Depth = 0;
            RepeatIndex = 0;
            PreviewMode = false;
            Targets.Clear();
            LastTargets.Clear();
        }

        /// <summary>把本次效果的目标传给下一个效果。空目标不覆盖（与 Monster Train 行为一致）。</summary>
        public void CarryTargetsForward()
        {
            if (Targets.Count == 0) return;
            LastTargets.Clear();
            LastTargets.AddRange(Targets);
        }

        /// <summary>派生一个子上下文（组合子用），深度 +1。</summary>
        public EffectContext Child()
        {
            var c = new EffectContext
            {
                Battle = Battle,
                Source = Source,
                ChosenTarget = ChosenTarget,
                Card = Card,
                XValue = XValue,
                Depth = Depth + 1,
                RepeatIndex = RepeatIndex,
                PreviewMode = PreviewMode,
            };
            c.LastTargets.AddRange(LastTargets);
            return c;
        }
    }
}
