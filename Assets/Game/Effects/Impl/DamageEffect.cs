using System;
using Game.Battle;
using Game.Units;
using UnityEngine;

namespace Game.Effects.Impl
{
    /// <summary>造成伤害。多段攻击、多目标、动态数值全部由参数覆盖，不需要额外的效果类。</summary>
    [Serializable]
    public class DamageEffect : CardEffect
    {
        public EffectValue Amount = EffectValue.Flat(6);

        [Tooltip("攻击次数，用于多段攻击")]
        public EffectValue Times = EffectValue.Flat(1);

        public DamageKind Kind = DamageKind.Attack;

        [Tooltip("无视护甲")]
        public bool IgnoreBlock;

        public override void Apply(EffectContext ctx)
        {
            int times = Math.Max(1, Times.Evaluate(ctx, ctx.Source));
            int savedRepeat = ctx.RepeatIndex;

            for (int t = 0; t < times; t++)
            {
                ctx.RepeatIndex = t;
                for (int i = 0; i < ctx.Targets.Count; i++)
                {
                    var target = ctx.Targets[i];
                    if (target == null || !target.IsAlive) continue;

                    target.TakeDamage(ctx.Battle, new DamageInfo
                    {
                        Amount = Amount.Evaluate(ctx, target),
                        Kind = Kind,
                        IgnoreBlock = IgnoreBlock,
                        Card = ctx.Card,
                        Attacker = ctx.Source,
                    });
                }
            }

            ctx.RepeatIndex = savedRepeat;
        }

        /// <summary>给敌人意图 / 卡牌描述用：算出实际会造成的伤害，但不真的打。</summary>
        public int PreviewDamage(EffectContext ctx, BattleUnit target)
        {
            int raw = Amount.Evaluate(ctx, target);
            if (ctx.Battle == null) return raw;
            return BattleUnit.PreviewDamage(ctx.Battle, ctx.Source, target, raw);
        }

        public int PreviewTimes(EffectContext ctx) => Math.Max(1, Times.Evaluate(ctx, ctx.Source));

        public override string Describe(EffectContext ctx)
        {
            var target = ctx.ChosenTarget;
            if (target == null && ctx.Battle != null && Target.Kind == TargetKind.AllEnemies)
            {
                // 群体攻击没有单一目标，取第一个存活敌人做预览
                for (int i = 0; i < ctx.Battle.AllUnits.Count; i++)
                {
                    var u = ctx.Battle.AllUnits[i];
                    if (u.IsAlive && u.IsEnemyOf(ctx.Source)) { target = u; break; }
                }
            }
            return PreviewDamage(ctx, target).ToString();
        }
    }
}
