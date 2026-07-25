using System;
using System.Collections.Generic;

namespace Game.Effects
{
    /// <summary>
    /// 效果结算的帧栈。**整套「结算可以中途挂起等玩家选牌」的地基就是这个类。**
    ///
    /// 为什么需要它：战斗逻辑是同步的，而玩家选牌是异步的。
    /// 如果直接在效果里等玩家，C# 调用栈会一路展开，组合子（Repeat / Conditional）
    /// 里剩下的循环和后处理就永远回不来了。
    /// 把「还没跑完的效果列表 + 跑到第几个」显式存进栈，挂起就只是「停止 Pump」，
    /// 恢复就只是「继续 Pump」，调用栈长什么样完全无关。
    ///
    /// ★ 实例挂在 BattleContext 上，不是 static——static 会让「一个进程里并行跑多场战斗」
    ///   （自动模拟器的前提）直接失效，这与去掉 static Uid 计数器是同一个理由。
    /// </summary>
    public sealed class EffectResolutionStack
    {
        /// <summary>一层未跑完的效果列表。</summary>
        private sealed class Frame
        {
            public IReadOnlyList<CardEffect> Effects;
            public EffectContext Ctx;
            public int Index;
            public Action OnComplete;

            /// <summary>≥0 时，每跑一个效果前把它写进 Ctx.RepeatIndex（供 RepeatEffect 用）。</summary>
            public int RepeatIndex;
        }

        private readonly List<Frame> _frames = new List<Frame>(8);
        private readonly Stack<Frame> _pool = new Stack<Frame>(8);
        private bool _pumping;

        /// <summary>栈里还有没跑完的效果。</summary>
        public bool IsActive => _frames.Count > 0;

        /// <summary>挂起判定回调。返回 true 时 Pump 立刻停下并保留现场。</summary>
        public Func<bool> ShouldSuspend;

        /// <summary>
        /// 压入一层待结算的效果列表。
        /// ★ 只入栈、不执行——执行统一交给 <see cref="Pump"/>。
        ///   组合子在 Apply 里调用它时，Pump 正在外层跑，于是子效果会在
        ///   「组合子 Apply 返回之后、父列表下一个效果之前」执行，顺序与原来的内联写法一致。
        /// </summary>
        public void Push(IReadOnlyList<CardEffect> effects, EffectContext ctx,
                         Action onComplete = null, int repeatIndex = -1)
        {
            if (effects == null || effects.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var f = _pool.Count > 0 ? _pool.Pop() : new Frame();
            f.Effects = effects;
            f.Ctx = ctx;
            f.Index = 0;
            f.OnComplete = onComplete;
            f.RepeatIndex = repeatIndex;
            _frames.Add(f);
        }

        /// <summary>
        /// 把栈跑到空或跑到挂起为止。
        /// 重入安全：内层调用直接返回，由最外层那次统一跑完
        /// （与 <c>BattleContext.RunTriggerQueue</c> 完全相同的手法）。
        /// </summary>
        public void Pump()
        {
            if (_pumping) return;
            _pumping = true;
            try
            {
                int guard = 0;
                while (_frames.Count > 0)
                {
                    if (ShouldSuspend != null && ShouldSuspend()) return;

                    if (++guard > 4096)
                    {
                        UnityEngine.Debug.LogError("[EffectResolutionStack] 单次结算超过 4096 步，疑似死循环，已中断。");
                        Clear();
                        return;
                    }

                    var frame = _frames[_frames.Count - 1];

                    if (frame.Index >= frame.Effects.Count)
                    {
                        _frames.RemoveAt(_frames.Count - 1);
                        var done = frame.OnComplete;
                        Recycle(frame);
                        // ★ OnComplete 里常会再 Push 新的一层（例如卡牌的「回响」重复结算），
                        //   所以必须在出栈之后再调用，否则新层会被当成旧层的一部分。
                        done?.Invoke();
                        continue;
                    }

                    var effect = frame.Effects[frame.Index++];
                    if (frame.RepeatIndex >= 0) frame.Ctx.RepeatIndex = frame.RepeatIndex;

                    EffectResolver.Step(effect, frame.Ctx);
                }
            }
            finally
            {
                _pumping = false;
            }
        }

        /// <summary>战斗结束 / 出错时清空现场，避免残留的帧在下一次 Pump 时诈尸。</summary>
        public void Clear()
        {
            for (int i = 0; i < _frames.Count; i++) Recycle(_frames[i]);
            _frames.Clear();
        }

        private void Recycle(Frame f)
        {
            f.Effects = null;
            f.Ctx = null;
            f.OnComplete = null;
            f.Index = 0;
            f.RepeatIndex = -1;
            _pool.Push(f);
        }
    }
}
