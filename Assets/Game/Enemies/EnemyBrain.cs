using System.Collections.Generic;
using Game.Battle;
using Game.Core;
using Game.Effects;
using Game.Effects.Impl;
using Game.Units;

namespace Game.Enemies
{
    /// <summary>
    /// 敌人决策。默认实现覆盖：固定序列 / 权重随机 / 条件过滤 / 连续使用限制 / 多阶段。
    /// 需要特殊逻辑时继承并重写 ChooseAction（借鉴 Chrono Ark 的 AI.SkillSelect，但去掉了单例依赖）。
    /// </summary>
    public class EnemyBrain
    {
        protected BattleUnit Unit;
        protected EnemyDefinition Def;
        protected readonly List<int> History = new List<int>();

        /// <summary>当前阶段，从 0 起。</summary>
        public int Phase { get; protected set; }

        private readonly EffectContext _scratch = new EffectContext();

        public virtual void Init(BattleUnit unit, EnemyDefinition def)
        {
            Unit = unit;
            Def = def;
            Phase = 0;
            History.Clear();
        }

        /// <summary>回合开始时调用：决定这回合做什么并写入 Unit.CurrentIntent。</summary>
        public void DecideIntent(BattleContext ctx)
        {
            if (Def == null || Def.Actions == null || Def.Actions.Count == 0)
            {
                Unit.CurrentIntent = new Intent(IntentKind.Sleep, 0, 0, -1);
                return;
            }

            UpdatePhase(ctx);

            int idx = ChooseAction(ctx);
            if (idx < 0 || idx >= Def.Actions.Count) idx = 0;

            History.Add(idx);

            var action = Def.Actions[idx];
            Unit.CurrentIntent = BuildIntent(ctx, action, idx);
            ctx.Post(BattleEventType.IntentChanged, Unit.Uid, Unit.Uid, Unit.CurrentIntent.Value, action.Name);
        }

        protected virtual void UpdatePhase(BattleContext ctx)
        {
            if (Def.PhaseHpThresholds == null || Def.PhaseHpThresholds.Count == 0) return;

            int pct = Unit.MaxHp == 0 ? 100 : Unit.Hp * 100 / Unit.MaxHp;
            int p = 0;
            for (int i = 0; i < Def.PhaseHpThresholds.Count; i++)
                if (pct <= Def.PhaseHpThresholds[i]) p = i + 1;

            if (p != Phase)
            {
                Phase = p;
                OnPhaseChanged(ctx, p);
            }
        }

        protected virtual void OnPhaseChanged(BattleContext ctx, int newPhase)
            => ctx.Post(BattleEventType.Message, Unit.Uid, Unit.Uid, newPhase, "PhaseChange");

        /// <summary>返回要执行的 Action 在 Def.Actions 中的下标。</summary>
        protected virtual int ChooseAction(BattleContext ctx)
        {
            // 1) 固定序列优先
            if (Def.FixedSequence != null && Def.FixedSequence.Count > 0)
            {
                int step = History.Count;
                if (step < Def.FixedSequence.Count) return Def.FixedSequence[step];
                if (Def.LoopSequence) return Def.FixedSequence[step % Def.FixedSequence.Count];
                // 否则落到权重随机
            }

            // 2) 权重随机 + 条件 + 阶段 + 连续限制
            _scratch.Reset(ctx, Unit, ctx.Player, null);

            var candidates = ctx.RentIntBuffer();
            int total = 0;
            for (int i = 0; i < Def.Actions.Count; i++)
            {
                var a = Def.Actions[i];
                if (a.Weight <= 0) continue;
                if (a.PhaseMask != 0 && (a.PhaseMask & (1 << Phase)) == 0) continue;
                if (!a.Condition.Test(_scratch, ctx.Player)) continue;
                if (a.MaxConsecutive > 0 && CountTrailing(i) >= a.MaxConsecutive) continue;
                candidates.Add(i);
                total += a.Weight;
            }

            int result = FallbackAction();
            if (candidates.Count > 0 && total > 0)
            {
                int roll = ctx.Rng.Range(RngStream.EnemyAction, 0, total);
                for (int k = 0; k < candidates.Count; k++)
                {
                    roll -= Def.Actions[candidates[k]].Weight;
                    if (roll < 0) { result = candidates[k]; break; }
                }
            }
            ctx.ReturnIntBuffer(candidates);
            return result;
        }

        /// <summary>没有任何候选时用哪个行动。默认取当前阶段第一个可用的。</summary>
        protected int FallbackAction()
        {
            for (int i = 0; i < Def.Actions.Count; i++)
            {
                var a = Def.Actions[i];
                if (a.PhaseMask == 0 || (a.PhaseMask & (1 << Phase)) != 0) return i;
            }
            return 0;
        }

        protected int CountTrailing(int actionIndex)
        {
            int n = 0;
            for (int i = History.Count - 1; i >= 0 && History[i] == actionIndex; i--) n++;
            return n;
        }

        /// <summary>
        /// 生成意图快照。★ 数值必须是「预览值」（跑过伤害修正管线），
        /// 这样 UI 上显示的数字就是玩家实际会挨的数字。
        /// </summary>
        protected virtual Intent BuildIntent(BattleContext ctx, EnemyAction action, int idx)
        {
            int value = 0;
            int times = 1;

            _scratch.Reset(ctx, Unit, ctx.Player, null);
            _scratch.PreviewMode = true;   // 纯预览计算，绝不能消耗随机流

            for (int i = 0; i < action.Effects.Count; i++)
            {
                if (action.Effects[i] is DamageEffect d)
                {
                    value = d.PreviewDamage(_scratch, ctx.Player);
                    times = d.PreviewTimes(_scratch);
                    break;
                }
                if (action.Effects[i] is BlockEffect b)
                {
                    value = b.Amount.Evaluate(_scratch, Unit);
                    break;
                }
            }

            return new Intent(action.Intent, value, times, idx);
        }

        /// <summary>
        /// 只重算当前意图上显示的**数值**，不重新选择行动。
        ///
        /// ★ 为什么必须有这个：意图是在回合开始时算好、存进 <c>Unit.CurrentIntent</c> 的快照。
        ///   玩家随后给敌人上「虚弱」或给自己上「易伤」，快照不会变，
        ///   于是 UI 显示的伤害与玩家实际会挨的伤害对不上——这直接违背了
        ///   「意图上的数字 == 实际会挨的数字」这条承诺。
        ///   行动本身不能重选（那等于让玩家用刷新骗 AI），只重算数值。
        /// </summary>
        public void RefreshIntentValue(BattleContext ctx)
        {
            if (Def == null || Def.Actions == null) return;

            int idx = Unit.CurrentIntent.ActionIndex;
            if (idx < 0 || idx >= Def.Actions.Count) return;

            Unit.CurrentIntent = BuildIntent(ctx, Def.Actions[idx], idx);
        }

        /// <summary>执行当前意图。</summary>
        public virtual void ExecuteIntent(BattleContext ctx)
        {
            int idx = Unit.CurrentIntent.ActionIndex;
            if (Def == null || idx < 0 || idx >= Def.Actions.Count) return;

            var action = Def.Actions[idx];
            var effCtx = new EffectContext();
            effCtx.Reset(ctx, Unit, ctx.Player, null);
            EffectResolver.ResolveAll(action.Effects, effCtx);
        }
    }
}
