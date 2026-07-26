using System;
using System.Collections.Generic;
using Game.Battle;
using Game.Enemies;
using Game.Statuses;

namespace Game.Units
{
    /// <summary>
    /// 玩家和敌人共用一个类。区别只在 IsPlayer 以及是否有 EnemyDef / Brain。
    /// 刻意不分子类：避免 PlayerUnit / EnemyUnit 分叉后到处判断类型。
    /// </summary>
    public class BattleUnit
    {
        /// <summary>
        /// 战斗内唯一编号。★ 由 <see cref="BattleContext.NextUnitUid"/> 分配，不再用 static 计数器——
        /// static 会让存档读回来的 Uid 与新对象冲突，也让同进程并行模拟多场战斗不可能。
        /// </summary>
        public readonly int Uid;
        public string Name;
        public readonly bool IsPlayer;

        public int Hp;
        public int MaxHp;
        public int Block;

        public readonly List<StatusInstance> Statuses = new List<StatusInstance>(8);

        // 仅敌人使用
        public EnemyDefinition EnemyDef;
        public EnemyBrain Brain;
        public Intent CurrentIntent;

        private bool _deathResolved;

        public bool IsAlive => Hp > 0;

        /// <summary>
        /// 显示用的名字。
        /// ★ 敌人一律现查 <see cref="EnemyDefinition.LocalizedName"/> 而不是用构造时存下的
        ///   <see cref="Name"/>：战斗中途切语言时，构造时那一份是旧语言的，
        ///   而且没有任何东西会去更新它。玩家没有 EnemyDef，退回 Name。
        /// </summary>
        public string DisplayName => EnemyDef != null ? EnemyDef.LocalizedName : Name;

        public BattleUnit(int uid, string name, int maxHp, bool isPlayer)
        {
            Uid = uid;
            Name = name;
            MaxHp = maxHp;
            Hp = maxHp;
            IsPlayer = isPlayer;
        }

        public bool IsEnemyOf(BattleUnit other) => other != null && other.IsPlayer != IsPlayer;

        // ===================================================== 状态

        public int GetStatusStacks(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < Statuses.Count; i++)
                if (Statuses[i].Def != null && Statuses[i].Def.Id == id) return Statuses[i].Stacks;
            return 0;
        }

        public StatusInstance FindStatus(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Statuses.Count; i++)
                if (Statuses[i].Def != null && Statuses[i].Def.Id == id) return Statuses[i];
            return null;
        }

        public void AddStatus(BattleContext ctx, StatusDefinition def, int stacks, BattleUnit source)
        {
            if (def == null || stacks == 0) return;

            // 施加前的拦截：神器抵消、免疫某类减益、层数加倍……
            // ★ 只有自己身上的 Hook 有权干预自己的状态。
            using (var hooks = ctx.Collect<IStatusHook>())
            {
                for (int i = 0; i < hooks.Count; i++)
                    if (hooks[i].Src.Owner == this)
                        hooks[i].Impl.ModifyStatusApply(ctx, hooks[i].Src, def, this, source, ref stacks);
            }
            if (stacks == 0) return;

            var inst = FindStatus(def.Id);
            if (inst == null)
            {
                if (stacks < 0) return;   // 不存在的负面层数，忽略
                inst = new StatusInstance(def, 0, source != null ? source.Uid : 0);
                Statuses.Add(inst);
            }

            inst.AddStacks(stacks);

            if (inst.Stacks <= 0)
            {
                Statuses.Remove(inst);
                ctx.Post(BattleEventType.StatusRemoved, Uid, Uid, 0, def.Id);
                return;
            }

            ctx.Post(BattleEventType.StatusApplied, source != null ? source.Uid : 0, Uid, inst.Stacks, def.Id);

            using (var hooks = ctx.Collect<IStatusHook>())
            {
                for (int i = 0; i < hooks.Count; i++)
                    hooks[i].Impl.OnStatusApplied(ctx, hooks[i].Src, def, this, stacks);
            }
        }

        /// <summary>
        /// 消耗指定状态的层数（神器抵消一次减益、不死图腾用掉一次复活）。
        /// ★ 这与「自然衰减」是两回事——自然衰减只允许发生在 BattleController.TickStatusDecay 里，
        ///   而主动消耗是效果的一部分，必须即时生效，否则同一回合内会被重复触发。
        /// </summary>
        public void ConsumeStatusStack(BattleContext ctx, StatusInstance inst, int count = 1)
        {
            if (inst == null || count <= 0) return;

            inst.Stacks -= count;
            if (inst.Stacks > 0)
            {
                ctx.Post(BattleEventType.StatusApplied, Uid, Uid, inst.Stacks, inst.Id);
                return;
            }

            Statuses.Remove(inst);
            ctx.Post(BattleEventType.StatusRemoved, Uid, Uid, 0, inst.Id);
        }

        public void RemoveStatus(BattleContext ctx, string id)
        {
            var inst = FindStatus(id);
            if (inst == null) return;
            Statuses.Remove(inst);
            ctx.Post(BattleEventType.StatusRemoved, Uid, Uid, 0, id);
        }

        // ===================================================== 护甲

        public void AddBlock(BattleContext ctx, int amount)
        {
            if (amount <= 0) return;

            using (var hooks = ctx.Collect<IBlockHook>())
            {
                for (int i = 0; i < hooks.Count; i++)
                    if (hooks[i].Src.Owner == this)
                        hooks[i].Impl.ModifyBlockGain(ctx, hooks[i].Src, ref amount);
            }

            if (amount <= 0) return;
            Block += amount;
            ctx.Post(BattleEventType.BlockGained, Uid, Uid, amount);
        }

        /// <summary>回合开始时清护甲。「壁垒」通过 IBlockHook.ModifyBlockDecay 把 clear 置 false。</summary>
        public void DecayBlock(BattleContext ctx)
        {
            bool clear = true;
            using (var hooks = ctx.Collect<IBlockHook>())
            {
                for (int i = 0; i < hooks.Count; i++)
                    if (hooks[i].Src.Owner == this)
                        hooks[i].Impl.ModifyBlockDecay(ctx, hooks[i].Src, ref clear);
            }

            if (clear && Block > 0)
            {
                ctx.Post(BattleEventType.BlockLost, Uid, Uid, Block);
                Block = 0;
            }
        }

        // ===================================================== 伤害

        /// <summary>
        /// 唯一的伤害入口。管线：攻方修正 → 受方修正 → 扣护甲 → 扣血 → OnDamaged → 死亡排队。
        /// 返回实际掉的血量（不含被护甲挡掉的部分）。
        /// </summary>
        public int TakeDamage(BattleContext ctx, DamageInfo dmg)
        {
            if (!IsAlive) return 0;

            using (var hooks = ctx.Collect<IDamageHook>())
            {
                // 1) 攻击方修正（力量、虚弱……）
                if (dmg.Attacker != null)
                {
                    for (int i = 0; i < hooks.Count; i++)
                        if (hooks[i].Src.Owner == dmg.Attacker)
                            hooks[i].Impl.ModifyOutgoingDamage(ctx, hooks[i].Src, this, ref dmg);
                }

                // 2) 受击方修正（易伤……）
                for (int i = 0; i < hooks.Count; i++)
                    if (hooks[i].Src.Owner == this)
                        hooks[i].Impl.ModifyIncomingDamage(ctx, hooks[i].Src, dmg.Attacker, ref dmg);

                if (dmg.Amount < 0) dmg.Amount = 0;

                // 3) 护甲
                int remaining = dmg.Amount;
                if (!dmg.IgnoreBlock && Block > 0 && remaining > 0)
                {
                    int blocked = Math.Min(Block, remaining);
                    Block -= blocked;
                    remaining -= blocked;
                    ctx.Post(BattleEventType.DamageBlocked, dmg.Attacker != null ? dmg.Attacker.Uid : 0, Uid, blocked);
                }

                // 3.5) 致死拦截。★ 必须在扣血之前——此时单位仍然存活，Hook 才收得到。
                if (remaining > 0 && Hp - remaining <= 0)
                {
                    bool prevent = false;
                    using (var fatal = ctx.Collect<IFatalHook>())
                    {
                        for (int i = 0; i < fatal.Count; i++)
                            if (fatal[i].Src.Owner == this)
                                fatal[i].Impl.OnLethalDamage(ctx, fatal[i].Src, dmg, ref remaining, ref prevent);
                    }
                    if (prevent) remaining = Math.Max(0, Hp - 1);
                    if (remaining < 0) remaining = 0;
                }

                // 4) 扣血
                if (remaining > 0)
                {
                    Hp -= remaining;
                    if (Hp < 0) Hp = 0;
                    ctx.Post(BattleEventType.DamageDealt, dmg.Attacker != null ? dmg.Attacker.Uid : 0, Uid, remaining);

                    for (int i = 0; i < hooks.Count; i++)
                        if (hooks[i].Src.Owner == this)
                            hooks[i].Impl.OnDamaged(ctx, hooks[i].Src, dmg, remaining);
                }

                // 5) 死亡：排队而非递归
                if (Hp <= 0 && !_deathResolved)
                {
                    var self = this;
                    ctx.EnqueueTrigger(() => self.ResolveDeath(ctx));
                }

                return remaining;
            }
        }

        /// <summary>
        /// 只跑修正管线，不真的造成伤害。给敌人意图预览用，保证 UI 显示的数字 == 实际会挨的伤害。
        /// </summary>
        public static int PreviewDamage(BattleContext ctx, BattleUnit attacker, BattleUnit target, int raw)
        {
            var dmg = new DamageInfo { Amount = raw, Kind = DamageKind.Attack, Attacker = attacker };
            using (var hooks = ctx.Collect<IDamageHook>())
            {
                if (attacker != null)
                {
                    for (int i = 0; i < hooks.Count; i++)
                        if (hooks[i].Src.Owner == attacker)
                            hooks[i].Impl.ModifyOutgoingDamage(ctx, hooks[i].Src, target, ref dmg);
                }
                if (target != null)
                {
                    for (int i = 0; i < hooks.Count; i++)
                        if (hooks[i].Src.Owner == target)
                            hooks[i].Impl.ModifyIncomingDamage(ctx, hooks[i].Src, attacker, ref dmg);
                }
            }
            return dmg.Amount < 0 ? 0 : dmg.Amount;
        }

        public void Heal(BattleContext ctx, int amount)
        {
            if (amount <= 0 || !IsAlive) return;

            using (var hooks = ctx.Collect<IHealHook>())
            {
                for (int i = 0; i < hooks.Count; i++)
                    if (hooks[i].Src.Owner == this)
                        hooks[i].Impl.ModifyHeal(ctx, hooks[i].Src, ref amount);
            }

            if (amount <= 0) return;
            Hp = Math.Min(MaxHp, Hp + amount);
            ctx.Post(BattleEventType.Healed, Uid, Uid, amount);
        }

        /// <summary>由触发队列调用。★ 死亡单位不从 AllUnits 移除，只是 Hp 为 0，用 IsAlive 过滤。</summary>
        public void ResolveDeath(BattleContext ctx)
        {
            if (_deathResolved || Hp > 0) return;
            _deathResolved = true;

            ctx.Post(BattleEventType.UnitDied, Uid, Uid);

            // ★ includeDead: true —— 此刻本单位 Hp 已是 0，不放开这个开关，
            //   它身上的「亡语」状态（死亡时爆炸 / 死亡时给玩家一张诅咒）永远收不到自己的死亡。
            using (var hooks = ctx.Collect<IDeathHook>(includeDead: true))
            {
                for (int i = 0; i < hooks.Count; i++)
                    hooks[i].Impl.OnUnitDied(ctx, hooks[i].Src, this);
            }
        }

        public override string ToString() => $"{Name}#{Uid}({Hp}/{MaxHp}+{Block})";
    }
}
