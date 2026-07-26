using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Relics;
using Game.Statuses;
using Game.Units;

namespace Game.Battle
{
    /// <summary>
    /// 战斗的全部可变状态。**没有任何 Unity 依赖**，可以在 EditMode 测试里完整跑完一场战斗。
    /// 承担三个职责：
    ///   1) 状态容器（单位、牌堆、能量、回合数）
    ///   2) Collect&lt;T&gt;()：拉取式 Hook 收集（借鉴 Chrono Ark 的 IReturn&lt;T&gt;，改为零 GC + 稳定排序）
    ///   3) Post()/Events：表现事件队列（借鉴 Monster Train 的 Signal，改为队列）
    /// </summary>
    public class BattleContext
    {
        // ================================================================= Hook 载体

        public readonly struct Hook<T> where T : class, IBattleHook
        {
            public readonly T Impl;
            public readonly HookSource Src;

            public Hook(T impl, HookSource src)
            {
                Impl = impl;
                Src = src;
            }
        }

        /// <summary>
        /// Collect 的返回值。用 <c>using var hooks = ctx.Collect&lt;IDamageHook&gt;();</c> 使用，
        /// 离开作用域时自动把内部 List 还回池子。这样既零 GC 又不怕重入。
        /// </summary>
        public readonly ref struct HookScope<T> where T : class, IBattleHook
        {
            private readonly BattleContext _ctx;
            private readonly List<Hook<T>> _list;

            internal HookScope(BattleContext ctx, List<Hook<T>> list)
            {
                _ctx = ctx;
                _list = list;
            }

            public int Count => _list.Count;
            public Hook<T> this[int i] => _list[i];
            public List<Hook<T>>.Enumerator GetEnumerator() => _list.GetEnumerator();
            public void Dispose() => _ctx.ReturnHookBuffer(_list);
        }

        // ================================================================= 状态

        public BattleUnit Player;
        public readonly List<BattleUnit> AllUnits = new List<BattleUnit>(8);
        public DeckController Deck;
        public Rng Rng;
        public RunContext Run;

        public BattlePhase Phase = BattlePhase.None;
        public int TurnNumber;
        public int Energy;

        /// <summary>
        /// 每回合的**基础**能量，来自 <see cref="RunContext.EnergyPerTurn"/>。
        /// ★ 遗物 / 状态的加成不写进这里——它每回合都要被重新拿去当计算的起点，
        ///   把加成累加进来会逐回合复利（3 → 4 → 5 …）。加成后的结果见 <see cref="EnergyThisTurn"/>。
        /// </summary>
        public int EnergyPerTurn = 3;

        /// <summary>
        /// 本回合实际恢复到的能量 = <see cref="EnergyPerTurn"/> + 所有 <see cref="IResourceHook"/> 的修正。
        ///
        /// ★ 存下来而不是让 UI 自己去算：算它必须跑一遍 <c>ModifyTurnEnergy</c>，
        ///   而那条路上的实现会 <see cref="PostRelicTriggered"/>（「黑星生效」）。
        ///   UI 每帧调一次的话，事件队列会被灌爆、<see cref="StateVersion"/> 每帧递增，
        ///   敌人意图数值陷入无休止的重算——正是铁律 4 说的那件事。
        ///   逻辑层在 <c>BeginTurn</c> 里本来就算过一次，记下来即可。
        ///
        /// ★ 它是能量球分母的唯一正确来源。用 <see cref="EnergyPerTurn"/> 当分母的话，
        ///   带「黑星」（每回合 +1 能量）的玩家每回合都看到「4/3」，
        ///   看起来像是自己拿到了超出上限的能量、系统出了 bug——而实际上一切正常。
        /// </summary>
        public int EnergyThisTurn = 3;
        public int CardsPlayedThisTurn;
        public CardType LastCardTypePlayed;
        public bool BattleEnded;
        public bool Victory;

        /// <summary>本场战斗内已打出的攻击牌数量（供「本回合第一张攻击牌」类遗物用）。</summary>
        public int AttacksPlayedThisTurn;

        /// <summary>
        /// 回合数硬上限。双方都无法造成有效伤害时（全防御牌 vs 只会加护甲的敌人）
        /// 战斗会永远打不完；手动游玩时玩家会自己退出，但自动模拟会挂死。
        /// 超过则判负结束。
        /// </summary>
        public int MaxTurns = 200;

        /// <summary>
        /// 本场战斗生效的遗物。由 <c>BattleController.StartBattle</c> 从 RunContext 复制进来。
        /// ★ 存的是 RelicInstance 而不是裸的行为列表，这样 Hook 拿得到 HookSource.Relic，
        ///   需要跨回合计数的遗物才有地方放可变数据（行为类本身必须无状态）。
        /// </summary>
        public readonly List<RelicInstance> Relics = new List<RelicInstance>();

        // ================================================================= Uid 分配
        //
        // ★ 刻意不用 static 计数器：static 会让存档读回来的 Uid 与新对象冲突，
        //   也让「同进程并行模拟多场战斗」不可能。计数器随 BattleContext 走。

        private int _nextUnitUid = 1;

        public int NextUnitUid() => _nextUnitUid++;

        // ================================================================= 效果结算栈

        /// <summary>
        /// 未跑完的效果。挂起等玩家选牌时，现场就保存在这里。
        /// 详见 <see cref="EffectResolutionStack"/>。
        /// </summary>
        public readonly EffectResolutionStack Resolution = new EffectResolutionStack();

        public BattleContext()
        {
            Resolution.ShouldSuspend = () => PendingSelection != null;
        }

        // ================================================================= 选牌

        /// <summary>
        /// 选牌策略。**非 null 表示「不需要问人」**——测试、自动模拟器、敌人回合都靠它
        /// 当场同步给出答案，于是结算根本不会挂起。
        /// UI 想接管时把它设成 null（见 <c>BattleScreen</c>）。
        /// </summary>
        public ICardSelector Selector = new RandomCardSelector();

        /// <summary>正在等玩家作答的选牌请求。非 null 时整场战斗暂停推进。</summary>
        public PendingCardSelection PendingSelection { get; private set; }

        public bool IsWaitingForSelection => PendingSelection != null;

        /// <summary>
        /// Selector 为 null 但又不能挂起时（敌人回合中途）的兜底。
        /// ★ 宁可随机选也绝不死锁：敌人回合是一个 for 循环，在里面挂起会把回合切成两半。
        /// </summary>
        private readonly RandomCardSelector _fallbackSelector = new RandomCardSelector();

        private readonly List<CardInstance> _selectionBuffer = new List<CardInstance>(16);

        /// <summary>
        /// 发起一次选牌。
        /// </summary>
        /// <returns>true 表示已挂起等玩家作答；false 表示已当场结算完毕。</returns>
        public bool RequestCardSelection(in CardSelectionRequest req, Action<List<CardInstance>> onResolved)
        {
            var pile = GetPile(req.Source);
            if (pile == null || pile.Count == 0)
            {
                onResolved?.Invoke(new List<CardInstance>());
                return false;
            }

            int pick = Math.Min(req.Count, pile.Count);
            if (pick < req.Count && !req.AllowFewer)
            {
                // 候选不够又不允许少选：整个请求作废，而不是「能选几张算几张」。
                onResolved?.Invoke(new List<CardInstance>());
                return false;
            }
            if (pick <= 0)
            {
                onResolved?.Invoke(new List<CardInstance>());
                return false;
            }

            // 只有「玩家回合 + UI 接管」才真的挂起去问人。
            bool interactive = Selector == null && Phase == BattlePhase.PlayerTurn && !BattleEnded;

            if (!interactive)
            {
                var selector = Selector ?? _fallbackSelector;
                _selectionBuffer.Clear();
                selector.Select(this, req, pile, _selectionBuffer);
                var result = new List<CardInstance>(_selectionBuffer);
                _selectionBuffer.Clear();
                onResolved?.Invoke(result);
                return false;
            }

            var pending = new PendingCardSelection
            {
                Request = req,
                PickCount = pick,
                OnResolved = onResolved,
            };
            pending.Candidates.AddRange(pile);
            PendingSelection = pending;

            Post(BattleEventType.CardSelectionRequested, 0, 0, pick);
            return true;
        }

        /// <summary>
        /// 玩家（或 UI）作答。清掉挂起状态、执行回调，然后把挂起的结算继续跑完。
        /// </summary>
        public void ResolveSelection(IReadOnlyList<CardInstance> chosen)
        {
            var pending = PendingSelection;
            if (pending == null) return;

            PendingSelection = null;

            var list = new List<CardInstance>(pending.PickCount);
            if (chosen != null)
            {
                for (int i = 0; i < chosen.Count; i++)
                {
                    // ★ 只认候选列表里的牌：UI 传回来的东西不能直接信任，
                    //   面板弹出到玩家点确定之间，牌可能已经被别的效果挪走了。
                    if (chosen[i] != null && pending.Candidates.Contains(chosen[i]))
                        list.Add(chosen[i]);
                }
            }

            pending.OnResolved?.Invoke(list);

            RunTriggerQueue();
            Resolution.Pump();
        }

        /// <summary>丢弃当前挂起的选牌请求，**不执行**它的回调。战斗结束时用。</summary>
        public void CancelPendingSelection() => PendingSelection = null;

        /// <summary>取某个牌堆的实际列表。<see cref="CardPile.None"/> 返回 null。</summary>
        public List<CardInstance> GetPile(CardPile pile)
        {
            if (Deck == null) return null;
            return pile switch
            {
                CardPile.Hand => Deck.Hand,
                CardPile.Draw => Deck.DrawPile,
                CardPile.Discard => Deck.DiscardPile,
                CardPile.Exhaust => Deck.ExhaustPile,
                _ => null,
            };
        }

        // ================================================================= 表现事件队列

        public readonly Queue<BattleEvent> Events = new Queue<BattleEvent>(64);

        /// <summary>
        /// 状态版本号。任何会改变战斗状态的操作都会 Post 事件，因此这里递增一次就等于
        /// 「有东西变了」。UI 靠它决定要不要重算那些昂贵的派生显示值（例如敌人意图伤害），
        /// 不必每帧无脑重算，也不会漏掉变化。
        /// </summary>
        public int StateVersion { get; private set; }

        public void Post(in BattleEvent e)
        {
            StateVersion++;
            Events.Enqueue(e);
        }

        public void Post(BattleEventType type, int sourceUid = 0, int targetUid = 0, int value = 0, string id = null)
        {
            StateVersion++;
            Events.Enqueue(new BattleEvent(type, sourceUid, targetUid, value, id));
        }

        /// <summary>
        /// 报告「这个遗物刚刚生效了」。来源不是遗物时什么也不做。
        ///
        /// ★ 遗物的效果全部藏在 Hook 里，玩家在画面上**完全看不到它工作**——
        ///   金刚杵开局给了 1 点力量、回响护符让第一张攻击牌打了两次，
        ///   界面上只有结果，没有「是谁干的」。这条事件就是补上那半句话。
        ///
        /// ★ 为什么不在 <c>Collect</c> 或 Hook 的调用点统一发：
        ///   那里只知道「问过这个 Hook 了」，不知道它到底做没做事。
        ///   十二个拦截点里绝大多数每回合都会被问一遍，无差别发事件等于让整条遗物栏一直在闪。
        ///   只有行为类自己知道「我这次真的改了东西」，所以由它来发。
        ///
        /// ★ 绝对不要在 <see cref="ICardPlayHook.ModifyCardCost"/> 里发：
        ///   那条路是 UI **每帧**为每张手牌算费用时走的（铁律 4 的预览路径），
        ///   在那里发事件会把队列灌爆，而且 <see cref="StateVersion"/> 每帧递增
        ///   会让意图数值陷入无休止的重算。
        /// </summary>
        public void PostRelicTriggered(in HookSource src, int value = 0)
        {
            if (src.Relic == null) return;
            Post(BattleEventType.RelicTriggered, src.Owner != null ? src.Owner.Uid : 0,
                 src.Owner != null ? src.Owner.Uid : 0, value, src.Relic.Id);
        }

        // ================================================================= 触发队列（防递归）

        private readonly Queue<Action> _triggerQueue = new Queue<Action>(16);
        private bool _runningTriggers;

        public void EnqueueTrigger(Action a)
        {
            if (a != null) _triggerQueue.Enqueue(a);
        }

        /// <summary>
        /// 消费触发队列。重入安全：内层调用直接返回，由最外层统一跑完。
        /// 借鉴 Monster Train 的 RunTriggerQueue，避免连锁触发递归爆栈。
        /// </summary>
        public void RunTriggerQueue()
        {
            if (_runningTriggers) return;
            _runningTriggers = true;
            try
            {
                int guard = 0;
                while (_triggerQueue.Count > 0)
                {
                    if (++guard > 512)
                    {
                        UnityEngine.Debug.LogError("[BattleContext] 触发队列超过 512 次，疑似死循环，已中断。");
                        _triggerQueue.Clear();
                        break;
                    }
                    _triggerQueue.Dequeue().Invoke();
                }
            }
            finally
            {
                _runningTriggers = false;
            }
        }

        // ================================================================= 延迟效果

        private readonly List<(DelayTiming when, Action act)> _delayed = new List<(DelayTiming, Action)>();
        private readonly List<Action> _delayedRunBuffer = new List<Action>(8);

        public void ScheduleDelayed(DelayTiming when, Action act)
        {
            if (act != null) _delayed.Add((when, act));
        }

        /// <summary>
        /// 触发某个时机的全部延迟效果。
        /// ★ 先把本次要跑的项摘出来再执行：否则一个「回合结束时再排一个回合结束效果」的卡
        ///   会在同一次循环里被无限执行下去（原实现的 for 条件每轮重新读 Count）。
        ///   新排进来的项留到下一次 FireDelayed 再跑。
        /// </summary>
        public void FireDelayed(DelayTiming when)
        {
            _delayedRunBuffer.Clear();
            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                if (_delayed[i].when != when) continue;
                _delayedRunBuffer.Add(_delayed[i].act);
                _delayed.RemoveAt(i);
            }

            // 上面是倒序摘的，这里倒回来跑，保证与排入顺序一致
            for (int i = _delayedRunBuffer.Count - 1; i >= 0; i--)
                _delayedRunBuffer[i].Invoke();

            _delayedRunBuffer.Clear();
            RunTriggerQueue();
        }

        // ================================================================= Hook 收集

        private readonly Dictionary<Type, object> _hookPools = new Dictionary<Type, object>();

        /// <summary>
        /// 收集所有实现了 T 的行为。来源：所有存活单位身上的状态 → 遗物。
        /// 无需订阅/退订：状态消失，Hook 自然消失。
        /// 用法：<c>using var hooks = ctx.Collect&lt;IDamageHook&gt;();</c>
        ///
        /// <para><paramref name="includeDead"/>：是否也收集已死亡单位身上的状态。
        /// 默认 false——死人不该继续影响战斗。唯一的例外是「亡语」类效果：
        /// <c>ResolveDeath</c> 里单位的 Hp 已经是 0，不放开这个开关它就收不到自己的死亡触发器。</para>
        /// </summary>
        public HookScope<T> Collect<T>(bool includeDead = false) where T : class, IBattleHook
        {
            var buffer = RentHookBuffer<T>();

            for (int u = 0; u < AllUnits.Count; u++)
            {
                var unit = AllUnits[u];
                if (!unit.IsAlive && !includeDead) continue;
                var statuses = unit.Statuses;
                for (int s = 0; s < statuses.Count; s++)
                {
                    var inst = statuses[s];
                    if (inst.Def == null || inst.Def.Behaviours == null) continue;
                    var behaviours = inst.Def.Behaviours;
                    for (int b = 0; b < behaviours.Count; b++)
                    {
                        if (behaviours[b] is T hook)
                            buffer.Add(new Hook<T>(hook, new HookSource(unit, inst.Stacks, inst)));
                    }
                }
            }

            for (int r = 0; r < Relics.Count; r++)
            {
                var relic = Relics[r];
                var behaviours = relic.Def != null ? relic.Def.Behaviours : null;
                if (behaviours == null) continue;
                for (int b = 0; b < behaviours.Count; b++)
                    if (behaviours[b] is T hook)
                        buffer.Add(new Hook<T>(hook, new HookSource(Player, relic)));
            }

            StableSortByOrder(buffer);
            return new HookScope<T>(this, buffer);
        }

        /// <summary>插入排序：稳定，保证同 Order 的 Hook 顺序确定（可重现）。n 很小，性能无忧。</summary>
        private static void StableSortByOrder<T>(List<Hook<T>> list) where T : class, IBattleHook
        {
            for (int i = 1; i < list.Count; i++)
            {
                var key = list[i];
                int order = key.Impl.Order;
                int j = i - 1;
                while (j >= 0 && list[j].Impl.Order > order)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
        }

        private List<Hook<T>> RentHookBuffer<T>() where T : class, IBattleHook
        {
            if (!_hookPools.TryGetValue(typeof(T), out var boxed))
            {
                boxed = new Stack<List<Hook<T>>>();
                _hookPools[typeof(T)] = boxed;
            }
            var stack = (Stack<List<Hook<T>>>)boxed;
            return stack.Count > 0 ? stack.Pop() : new List<Hook<T>>(8);
        }

        internal void ReturnHookBuffer<T>(List<Hook<T>> list) where T : class, IBattleHook
        {
            if (list == null) return;
            list.Clear();
            if (_hookPools.TryGetValue(typeof(T), out var boxed))
                ((Stack<List<Hook<T>>>)boxed).Push(list);
        }

        // ================================================================= 查询

        public int AliveEnemyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < AllUnits.Count; i++)
                    if (AllUnits[i].IsAlive && !AllUnits[i].IsPlayer) n++;
                return n;
            }
        }

        public BattleUnit FindUnit(int uid)
        {
            for (int i = 0; i < AllUnits.Count; i++)
                if (AllUnits[i].Uid == uid) return AllUnits[i];
            return null;
        }

        public void GetAliveEnemies(List<BattleUnit> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < AllUnits.Count; i++)
                if (AllUnits[i].IsAlive && !AllUnits[i].IsPlayer) buffer.Add(AllUnits[i]);
        }

        // ================================================================= 能量

        public void GainEnergy(int amount)
        {
            if (amount != 0)
            {
                using (var hooks = Collect<IEnergyHook>())
                {
                    for (int i = 0; i < hooks.Count; i++)
                        hooks[i].Impl.ModifyEnergyGain(this, hooks[i].Src, ref amount);
                }
            }
            Energy += amount;
            if (Energy < 0) Energy = 0;
            Post(BattleEventType.EnergyChanged, 0, 0, Energy);
        }

        public void SpendEnergy(int amount)
        {
            Energy -= amount;
            if (Energy < 0) Energy = 0;
            Post(BattleEventType.EnergyChanged, 0, 0, Energy);
        }

        // ================================================================= 临时缓冲池

        private readonly Stack<List<BattleUnit>> _unitPool = new Stack<List<BattleUnit>>();

        public List<BattleUnit> RentUnitBuffer() => _unitPool.Count > 0 ? _unitPool.Pop() : new List<BattleUnit>(8);

        public void ReturnUnitBuffer(List<BattleUnit> l)
        {
            if (l == null) return;
            l.Clear();
            _unitPool.Push(l);
        }

        private readonly Stack<List<int>> _intPool = new Stack<List<int>>();

        public List<int> RentIntBuffer() => _intPool.Count > 0 ? _intPool.Pop() : new List<int>(8);

        public void ReturnIntBuffer(List<int> l)
        {
            if (l == null) return;
            l.Clear();
            _intPool.Push(l);
        }
    }
}
