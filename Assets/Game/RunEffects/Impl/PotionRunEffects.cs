using System;
using Game.Core;
using Game.Localization;
using Game.Potions;
using UnityEngine;

namespace Game.RunEffects.Impl
{
    /// <summary>
    /// 获得药水。事件 / 宝箱 / 休息点用。
    /// 留空 <see cref="PotionId"/> 则随机抽一瓶。
    /// </summary>
    [Serializable]
    public class GainPotionRunEffect : RunEffect
    {
        [Tooltip("指定药水 id。留空表示随机")]
        public string PotionId;

        [Tooltip("给几瓶")]
        public int Count = 1;

        public override bool CanApply(RunEffectContext ctx) => ctx?.Run != null;

        public override void Apply(RunEffectContext ctx)
        {
            var run = ctx.Run;
            if (run == null) return;

            for (int i = 0; i < Count; i++)
            {
                var def = Resolve(ctx);
                if (def == null) return;

                // ★ 槽位满时如实上报，不要静默吞掉：
                //   玩家看到「获得药水」却发现背包没变，会以为是 bug。
                if (!run.HasPotionSpace)
                {
                    ctx.AddLog(Loc.T("run.gainpotion.full", "药水槽已满，无法携带更多"));
                    return;
                }

                run.AddPotion(def);
                ctx.AddLog(Loc.T("run.gainpotion.done", "获得药水「{0}」", def.LocalizedName));
            }
        }

        private PotionDefinition Resolve(RunEffectContext ctx)
        {
            var db = ctx.Run.Database;
            if (db == null) return null;

            if (!string.IsNullOrEmpty(PotionId))
            {
                var def = db.GetPotion(PotionId);
                if (def == null)
                    Debug.LogWarning($"[GainPotionRunEffect] 找不到药水「{PotionId}」。");
                return def;
            }

            return ContentPicker.PickPotion(ctx.Run.Rng, db, RngStream.Potion);
        }

        public override string Describe(RunEffectContext ctx)
        {
            if (!string.IsNullOrEmpty(PotionId) && ctx?.Run?.Database != null)
            {
                var def = ctx.Run.Database.GetPotion(PotionId);
                if (def != null)
                    return Count > 1
                        ? Loc.T("run.gainpotion.many", "获得 {0} 瓶「{1}」", Count, def.LocalizedName)
                        : Loc.T("run.gainpotion.one", "获得「{0}」", def.LocalizedName);
            }
            return Count > 1
                ? Loc.T("run.gainpotion.random_many", "获得 {0} 瓶随机药水", Count)
                : Loc.T("run.gainpotion.random_one", "获得一瓶随机药水");
        }
    }

    /// <summary>增加药水槽位。遗物 / 事件用。</summary>
    [Serializable]
    public class PotionSlotsRunEffect : RunEffect
    {
        public int Amount = 1;

        public override void Apply(RunEffectContext ctx)
        {
            if (ctx?.Run == null) return;
            ctx.Run.PotionSlots = Mathf.Max(0, ctx.Run.PotionSlots + Amount);

            // 槽位缩小时把超出的药水丢掉，否则背包会一直超载
            while (ctx.Run.Potions.Count > ctx.Run.PotionSlots)
                ctx.Run.Potions.RemoveAt(ctx.Run.Potions.Count - 1);

            ctx.AddLog(DescribeSlots());
        }

        public override string Describe(RunEffectContext ctx) => DescribeSlots();

        private string DescribeSlots()
            => Amount >= 0
                ? Loc.T("run.potionslots.gain", "药水槽 +{0}", Amount)
                : Loc.T("run.potionslots.lose", "药水槽 -{0}", -Amount);
    }
}
