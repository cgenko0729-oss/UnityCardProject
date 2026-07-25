using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Enemies;
using Game.Potions;
using Game.Relics;
using Game.Statuses;
using Game.Units;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>
    /// 战斗状态机。UI 只允许调用本类的 TryPlayCard / EndTurn / CanPlayCard。
    ///
    /// ★ 全类不含 IEnumerator、不含 Update、不引用任何 UI 类型。
    /// ★ 阶段 4 起本类是**纯 C# 类**，不再继承 MonoBehaviour——它本来就没用过任何 Unity 生命周期，
    ///   继承 MonoBehaviour 的唯一后果是「测试必须建 GameObject」和「无法在一个进程里并行跑多场战斗」。
    ///   持有者（RunManager / BattleBootstrap）直接 new 一个就行，不需要挂在任何 GameObject 上。
    /// </summary>
    public class BattleController
    {
        public BattleContext Ctx { get; private set; }
        public BattlePhase Phase => Ctx != null ? Ctx.Phase : BattlePhase.None;
        public bool IsRunning => Ctx != null && !Ctx.BattleEnded;

        /// <summary>战斗结束回调（RunManager 订阅）。参数为是否胜利。</summary>
        public event Action<bool> BattleFinished;

        /// <summary>
        /// 预览专用上下文。★ 与结算用的上下文分开：CanPlayCard 每帧被 UI 调用，
        /// 若与结算共用一个实例，一旦某个效果在 Apply 途中间接触发了 CanPlayCard，
        /// 正在结算的 Targets / XValue 会被 Reset 掉，产生只在特定卡组合下出现的诡异 bug。
        ///
        /// <para>结算侧则**每次出牌新建**一个上下文（见 <see cref="ResolvePlayStep"/>）：
        /// 结算可能挂起等玩家选牌，挂起期间这个上下文必须一直活着，
        /// 复用单例会让「回响」的第二次结算把第一次还没跑完的现场 Reset 掉。
        /// 一次出牌最多分配 9 个对象，不在每帧路径上，不值得为它做池化。</para>
        /// </summary>
        private readonly EffectContext _previewCtx = new EffectContext();

        // ================================================================= 开始

        public void StartBattle(RunContext run, EncounterDefinition encounter)
        {
            if (run == null || encounter == null)
            {
                Debug.LogError("[BattleController] StartBattle 参数为空。");
                return;
            }

            Ctx = new BattleContext
            {
                Rng = run.Rng,
                Run = run,
                EnergyPerTurn = run.EnergyPerTurn,
                Deck = new DeckController(),
            };

            // 遗物的 Hook 与状态的 Hook 走同一套 Collect，这里只是把持有的遗物复制进来。
            // ★ 验收标准之一：新增一个遗物不需要改本类任何一行结算代码。
            Ctx.Relics.AddRange(run.Relics);

            Ctx.Player = new BattleUnit(Ctx.NextUnitUid(), "Player", run.MaxHp, true) { Hp = run.Hp };
            Ctx.AllUnits.Add(Ctx.Player);

            for (int i = 0; i < encounter.Enemies.Count; i++)
            {
                var def = encounter.Enemies[i];
                if (def == null) continue;

                int hp = Ctx.Rng.Range(RngStream.Encounter, def.MinHp, def.MaxHp + 1);
                var unit = new BattleUnit(Ctx.NextUnitUid(), def.DisplayName, hp, false) { EnemyDef = def };
                unit.Brain = CreateBrain(def);
                unit.Brain.Init(unit, def);
                Ctx.AllUnits.Add(unit);
            }

            // 起始状态要在所有单位都进 AllUnits 之后再挂，否则 Collect 收不到
            for (int i = 1; i < Ctx.AllUnits.Count; i++)
            {
                var unit = Ctx.AllUnits[i];
                var def = unit.EnemyDef;
                if (def?.StartingStatuses == null) continue;
                for (int s = 0; s < def.StartingStatuses.Count; s++)
                    unit.AddStatus(Ctx, def.StartingStatuses[s].Status, def.StartingStatuses[s].Stacks, null);
            }

            Ctx.Deck.Init(Ctx, run.Deck);

            Ctx.Phase = BattlePhase.BattleStart;
            Ctx.Post(BattleEventType.BattleStarted);

            using (var flow = Ctx.Collect<IBattleFlowHook>())
            {
                for (int i = 0; i < flow.Count; i++) flow[i].Impl.OnBattleStart(Ctx, flow[i].Src);
            }
            Ctx.RunTriggerQueue();

            BeginTurn();
        }

        private static EnemyBrain CreateBrain(EnemyDefinition def)
        {
            if (string.IsNullOrEmpty(def.CustomBrainType)) return new EnemyBrain();

            var t = Type.GetType(def.CustomBrainType);
            if (t == null || !typeof(EnemyBrain).IsAssignableFrom(t))
            {
                Debug.LogWarning($"[BattleController] 找不到 Brain 类型「{def.CustomBrainType}」({def.name})，改用默认 EnemyBrain。");
                return new EnemyBrain();
            }
            return (EnemyBrain)Activator.CreateInstance(t);
        }

        // ================================================================= 回合

        private void BeginTurn()
        {
            if (Ctx.BattleEnded) return;

            Ctx.TurnNumber++;

            // 双方都打不动时战斗会永远进行下去。手玩时玩家会自己退出，但自动模拟会挂死。
            if (Ctx.TurnNumber > Ctx.MaxTurns)
            {
                Debug.LogWarning($"[BattleController] 超过 {Ctx.MaxTurns} 回合上限，判负结束。");
                EndBattle(false);
                return;
            }

            Ctx.Phase = BattlePhase.TurnStart;
            Ctx.CardsPlayedThisTurn = 0;
            Ctx.AttacksPlayedThisTurn = 0;

            Ctx.Player.DecayBlock(Ctx);

            int energy = Ctx.EnergyPerTurn;
            using (var res = Ctx.Collect<IResourceHook>())
            {
                for (int i = 0; i < res.Count; i++) res[i].Impl.ModifyTurnEnergy(Ctx, res[i].Src, ref energy);
            }
            Ctx.Energy = 0;
            Ctx.GainEnergy(energy);

            using (var hooks = Ctx.Collect<ITurnHook>())
            {
                for (int i = 0; i < hooks.Count; i++) hooks[i].Impl.OnTurnStart(Ctx, hooks[i].Src);
            }
            Ctx.RunTriggerQueue();
            Ctx.FireDelayed(DelayTiming.StartOfNextTurn);

            TickStatusDecay(atTurnStart: true);

            int draw = Ctx.Run.CardsPerTurn;
            using (var res = Ctx.Collect<IResourceHook>())
            {
                for (int i = 0; i < res.Count; i++) res[i].Impl.ModifyTurnDraw(Ctx, res[i].Src, ref draw);
            }
            if (draw > 0) Ctx.Deck.Draw(draw);

            for (int i = 0; i < Ctx.AllUnits.Count; i++)
            {
                var u = Ctx.AllUnits[i];
                if (u.IsAlive && !u.IsPlayer && u.Brain != null) u.Brain.DecideIntent(Ctx);
            }

            Ctx.Phase = BattlePhase.PlayerTurn;
            Ctx.Post(BattleEventType.TurnStarted, 0, 0, Ctx.TurnNumber);

            CheckBattleEnd();
        }

        public void EndTurn()
        {
            if (Ctx == null || Ctx.Phase != BattlePhase.PlayerTurn) return;
            if (Ctx.IsWaitingForSelection) return;

            Ctx.Phase = BattlePhase.TurnEnd;

            using (var hooks = Ctx.Collect<ITurnHook>())
            {
                for (int i = 0; i < hooks.Count; i++) hooks[i].Impl.OnTurnEnd(Ctx, hooks[i].Src);
            }
            Ctx.RunTriggerQueue();
            Ctx.FireDelayed(DelayTiming.EndOfThisTurn);

            Ctx.Post(BattleEventType.TurnEnded, 0, 0, Ctx.TurnNumber);

            // ★ 状态衰减必须在 OnTurnEnd 之后：中毒要先掉血再减层
            TickStatusDecay(atTurnStart: false);

            // ★ 「留在手上的代价」（灼烧 / 疑虑）夹在衰减之后、清理手牌之前。
            //   放衰减之前的话，疑虑施加的 1 层虚弱会被同一次衰减立刻扣掉；
            //   放清理之后的话，牌已经离手，判定永远为假。
            Ctx.Deck.FireInHandEndOfTurnEffects();
            if (CheckBattleEnd()) return;

            Ctx.Deck.EndTurnDiscard();

            if (CheckBattleEnd()) return;

            EnemyTurn();
        }

        private void EnemyTurn()
        {
            Ctx.Phase = BattlePhase.EnemyTurn;
            Ctx.Post(BattleEventType.EnemyTurnStarted);

            for (int i = 0; i < Ctx.AllUnits.Count; i++)
            {
                var u = Ctx.AllUnits[i];
                if (!u.IsAlive || u.IsPlayer || u.Brain == null) continue;

                u.DecayBlock(Ctx);
                u.Brain.ExecuteIntent(Ctx);
                Ctx.RunTriggerQueue();

                if (CheckBattleEnd()) return;
            }

            BeginTurn();
        }

        /// <summary>状态层数的自然衰减。统一在这里做，保证结算顺序可控。</summary>
        private void TickStatusDecay(bool atTurnStart)
        {
            for (int u = 0; u < Ctx.AllUnits.Count; u++)
            {
                var unit = Ctx.AllUnits[u];
                if (!unit.IsAlive) continue;   // 死者的状态不再衰减，与 Collect 的语义保持一致

                for (int s = unit.Statuses.Count - 1; s >= 0; s--)
                {
                    var inst = unit.Statuses[s];
                    if (inst.Def == null) { unit.Statuses.RemoveAt(s); continue; }

                    switch (inst.Def.Decay)
                    {
                        case StatusDecay.LoseOneAtTurnEnd:
                            if (!atTurnStart) inst.Stacks--;
                            break;
                        case StatusDecay.RemoveAtTurnEnd:
                            if (!atTurnStart) inst.Stacks = 0;
                            break;
                        case StatusDecay.LoseAllAtTurnStart:
                            if (atTurnStart) inst.Stacks = 0;
                            break;
                    }

                    if (inst.Stacks <= 0)
                    {
                        unit.Statuses.RemoveAt(s);
                        Ctx.Post(BattleEventType.StatusRemoved, unit.Uid, unit.Uid, 0, inst.Def.Id);
                    }
                }
            }
        }

        // ================================================================= 出牌

        /// <summary>纯查询，无副作用，UI 可以每帧调用。</summary>
        public bool CanPlayCard(CardInstance card, BattleUnit target, out PlayFailReason reason)
        {
            reason = PlayFailReason.None;

            if (Ctx == null || Ctx.BattleEnded) { reason = PlayFailReason.BattleEnded; return false; }
            if (Ctx.Phase != BattlePhase.PlayerTurn) { reason = PlayFailReason.NotPlayerTurn; return false; }
            // 上一张牌还在等玩家选牌，整场战斗都不该继续推进
            if (Ctx.IsWaitingForSelection) { reason = PlayFailReason.WaitingForSelection; return false; }
            if (card == null || !Ctx.Deck.Hand.Contains(card)) { reason = PlayFailReason.NotInHand; return false; }

            if (card.HasKeyword(CardKeyword.Unplayable) || card.Def.CostMode == CostMode.Unplayable)
            {
                reason = PlayFailReason.Unplayable;
                return false;
            }

            int cost = card.GetCost(Ctx);
            if (cost >= 0 && cost > Ctx.Energy) { reason = PlayFailReason.NotEnoughEnergy; return false; }

            if (card.Def.TargetKind == CardTargetKind.SingleEnemy)
            {
                if (target == null) { reason = PlayFailReason.NeedTarget; return false; }
                if (!target.IsAlive || target.IsPlayer) { reason = PlayFailReason.InvalidTarget; return false; }
            }

            PreparePreviewContext(card, target);
            bool ok = EffectResolver.CanApplyAll(card.Def.Effects, _previewCtx);
            _previewCtx.Targets.Clear();

            if (!ok) { reason = PlayFailReason.EffectCannotApply; return false; }
            return true;
        }

        private void PreparePreviewContext(CardInstance card, BattleUnit target)
        {
            _previewCtx.Reset(Ctx, Ctx.Player, target, card);
            _previewCtx.PreviewMode = true;
            _previewCtx.XValue = card.Def.CostMode == CostMode.X ? Ctx.Energy : 0;
        }

        public bool TryPlayCard(CardInstance card, BattleUnit target) => TryPlayCard(card, target, out _);

        public bool TryPlayCard(CardInstance card, BattleUnit target, out PlayFailReason reason)
        {
            if (!CanPlayCard(card, target, out reason)) return false;

            // 出牌前的拦截：取消出牌 / 额外结算几次（回响）
            bool cancel = false;
            int extraPlays = 0;
            using (var flow = Ctx.Collect<ICardFlowHook>())
            {
                for (int i = 0; i < flow.Count; i++)
                    flow[i].Impl.PreCardPlay(Ctx, flow[i].Src, card, ref cancel, ref extraPlays);
            }
            if (cancel) { reason = PlayFailReason.Cancelled; return false; }
            if (extraPlays < 0) extraPlays = 0;
            if (extraPlays > 8) extraPlays = 8;   // 防止配错导致卡死

            int cost = card.GetCost(Ctx);
            int spend = card.Def.CostMode == CostMode.X ? Ctx.Energy : cost;

            Ctx.SpendEnergy(spend);

            // ★ 先离手，防止「弃掉所有手牌」之类的效果把自己也算进去
            Ctx.Deck.Hand.Remove(card);
            Ctx.CardsPlayedThisTurn++;
            if (card.Type == CardType.Attack) Ctx.AttacksPlayedThisTurn++;
            Ctx.LastCardTypePlayed = card.Type;

            Ctx.Post(BattleEventType.CardPlayed, Ctx.Player.Uid, card.Uid, spend, card.Id);

            using (var hooks = Ctx.Collect<ICardPlayHook>())
            {
                for (int i = 0; i < hooks.Count; i++) hooks[i].Impl.OnCardPlayed(Ctx, hooks[i].Src, card);
            }

            ResolvePlayStep(card, target, spend, extraPlays);
            return true;
        }

        /// <summary>
        /// 结算这张牌的效果一次；跑完再排下一次（回响），全部跑完才收尾。
        ///
        /// ★ 为什么不用 for 循环：效果里可能有选牌，结算会挂起等玩家作答。
        ///   循环变量与「循环之后的收尾代码」都活在 C# 调用栈上，挂起时栈会展开，
        ///   收尾代码就会在效果真正跑完之前抢先执行——牌会在玩家还没选完时就进弃牌堆。
        ///   写成回调链之后，「下一次」和「收尾」都由结算栈在正确的时刻驱动。
        /// </summary>
        private void ResolvePlayStep(CardInstance card, BattleUnit target, int spend, int repsLeft)
        {
            if (Ctx.BattleEnded) { FinishPlay(card); return; }

            var ectx = new EffectContext();
            ectx.Reset(Ctx, Ctx.Player, target, card);
            ectx.PreviewMode = false;
            ectx.XValue = card.Def.CostMode == CostMode.X ? spend : 0;

            EffectResolver.ResolveAll(card.Def.Effects, ectx, () =>
            {
                Ctx.RunTriggerQueue();

                if (repsLeft > 0 && !Ctx.BattleEnded)
                    ResolvePlayStep(card, target, spend, repsLeft - 1);
                else
                    FinishPlay(card);
            });
        }

        private void FinishPlay(CardInstance card)
        {
            SendCardToDestination(card);
            CheckBattleEnd();
        }

        /// <summary>决定一张打完的牌进哪个堆。默认规则可被 ICardFlowHook 改写。</summary>
        private void SendCardToDestination(CardInstance card)
        {
            CardPile dest;
            if (card.Type == CardType.Power) dest = CardPile.None;                     // 能力牌打出后消失
            else if (card.HasKeyword(CardKeyword.Exhaust) || card.IsTemporary) dest = CardPile.Exhaust;
            else dest = CardPile.Discard;

            using (var flow = Ctx.Collect<ICardFlowHook>())
            {
                for (int i = 0; i < flow.Count; i++)
                    flow[i].Impl.ModifyCardDestination(Ctx, flow[i].Src, card, ref dest);
            }

            switch (dest)
            {
                case CardPile.None: break;
                case CardPile.Exhaust: Ctx.Deck.Exhaust(card); break;
                case CardPile.Discard: Ctx.Deck.Discard(card, fromHand: false); break;
                default: Ctx.Deck.AddCard(card, dest); break;
            }
        }

        // ================================================================= 结束

        public bool CheckBattleEnd()
        {
            if (Ctx == null) return false;
            if (Ctx.BattleEnded) return true;

            if (!Ctx.Player.IsAlive) { EndBattle(false); return true; }
            if (Ctx.AliveEnemyCount == 0) { EndBattle(true); return true; }
            return false;
        }

        private void EndBattle(bool victory)
        {
            Ctx.BattleEnded = true;
            Ctx.Victory = victory;
            Ctx.Phase = victory ? BattlePhase.Victory : BattlePhase.Defeat;

            // ★ 战斗结束时丢掉一切没跑完的效果与没答完的选牌请求。
            //   不清的话：玩家在选牌面板还开着的时候被中毒打死，
            //   面板会继续挂在结算界面上，而它的回调会往一个已经结束的战斗里写数据。
            Ctx.CancelPendingSelection();
            Ctx.Resolution.Clear();

            using (var flow = Ctx.Collect<IBattleFlowHook>())
            {
                for (int i = 0; i < flow.Count; i++) flow[i].Impl.OnBattleEnd(Ctx, flow[i].Src, victory);
            }

            for (int i = 0; i < Ctx.Run.Deck.Count; i++) Ctx.Run.Deck[i].OnBattleEnd();
            Ctx.Run.Hp = Ctx.Player.Hp;
            Ctx.Run.LastBattleVictory = victory;
            if (victory) Ctx.Run.BattlesWon++;

            Ctx.Post(BattleEventType.BattleEnded, 0, 0, victory ? 1 : 0);
            BattleFinished?.Invoke(victory);
        }

        // ================================================================= 辅助查询（给 UI 用）

        public void GetAliveEnemies(List<BattleUnit> buffer) => Ctx?.GetAliveEnemies(buffer);

        /// <summary>
        /// 重算所有敌人意图上显示的数值（不重新选择行动）。
        /// 玩家上了「虚弱」「易伤」之后必须调一次，否则 UI 上是过期数字。
        /// 纯预览计算，无副作用，UI 可以放心调。
        /// </summary>
        public void RefreshIntents()
        {
            if (Ctx == null || Ctx.BattleEnded) return;

            for (int i = 0; i < Ctx.AllUnits.Count; i++)
            {
                var u = Ctx.AllUnits[i];
                if (u.IsAlive && !u.IsPlayer && u.Brain != null) u.Brain.RefreshIntentValue(Ctx);
            }
        }

        public bool NeedsTargetSelection(CardInstance card)
            => card != null && card.Def != null && card.Def.TargetKind == CardTargetKind.SingleEnemy;

        // ================================================================= 药水

        /// <summary>纯查询，无副作用，UI 可以每帧调用。</summary>
        public bool CanUsePotion(PotionInstance potion, BattleUnit target, out PotionFailReason reason)
        {
            reason = PotionFailReason.None;

            if (Ctx == null || Ctx.BattleEnded) { reason = PotionFailReason.BattleEnded; return false; }
            if (Ctx.Phase != BattlePhase.PlayerTurn) { reason = PotionFailReason.NotPlayerTurn; return false; }
            if (Ctx.IsWaitingForSelection) { reason = PotionFailReason.WaitingForSelection; return false; }

            if (potion?.Def == null || Ctx.Run == null || !Ctx.Run.Potions.Contains(potion))
            {
                reason = PotionFailReason.NotHeld;
                return false;
            }

            if (potion.Def.NeedsTarget)
            {
                if (target == null) { reason = PotionFailReason.NeedTarget; return false; }
                if (!target.IsAlive || target.IsPlayer) { reason = PotionFailReason.InvalidTarget; return false; }
            }

            return true;
        }

        public bool TryUsePotion(PotionInstance potion, BattleUnit target)
            => TryUsePotion(potion, target, out _);

        /// <summary>
        /// 喝一瓶药水。效果走的是与卡牌完全相同的 <see cref="EffectResolver"/>，
        /// 因此药水里也可以放选牌效果，结算同样会挂起等玩家作答。
        /// </summary>
        public bool TryUsePotion(PotionInstance potion, BattleUnit target, out PotionFailReason reason)
        {
            if (!CanUsePotion(potion, target, out reason)) return false;

            // ★ 先从背包移除再结算：效果里若有「再获得一瓶药水」，
            //   不先移除会算错槽位，甚至让这瓶药水复制自己。
            Ctx.Run.RemovePotion(potion);

            Ctx.Post(BattleEventType.PotionUsed, Ctx.Player.Uid, 0, 0, potion.Id);

            var ectx = new EffectContext();
            ectx.Reset(Ctx, Ctx.Player, target, null);
            ectx.PreviewMode = false;

            EffectResolver.ResolveAll(potion.Def.Effects, ectx, () =>
            {
                Ctx.RunTriggerQueue();
                CheckBattleEnd();
            });

            return true;
        }

        /// <summary>倒掉一瓶药水腾出槽位。不产生任何效果。</summary>
        public bool DiscardPotion(PotionInstance potion)
        {
            if (Ctx?.Run == null || potion == null) return false;
            if (!Ctx.Run.RemovePotion(potion)) return false;
            Ctx.Post(BattleEventType.PotionDiscarded, 0, 0, 0, potion.Id);
            return true;
        }

        // ================================================================= 选牌（给 UI 用）

        /// <summary>非 null 表示结算已挂起，UI 该弹选牌面板了。</summary>
        public PendingCardSelection PendingSelection => Ctx?.PendingSelection;

        /// <summary>
        /// 玩家选完了。传空列表表示「跳过」（仅当请求允许跳过时才该这么调）。
        /// 调用之后挂起的结算会自动继续跑完。
        /// </summary>
        public void ResolveSelection(IReadOnlyList<CardInstance> chosen)
        {
            if (Ctx == null || Ctx.PendingSelection == null) return;
            Ctx.ResolveSelection(chosen);
            CheckBattleEnd();
        }
    }
}
