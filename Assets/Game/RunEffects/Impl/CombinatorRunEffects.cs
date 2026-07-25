using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.RunEffects.Impl
{
    /// <summary>组合子：条件判断。与战斗层的 ConditionalEffect 一一对应。</summary>
    [Serializable]
    public class ConditionalRunEffect : RunEffect
    {
        public RunCondition Condition;

        [SerializeReference]
        public List<RunEffect> Then = new List<RunEffect>();

        [SerializeReference]
        public List<RunEffect> Else = new List<RunEffect>();

        /// <summary>★ 组合子永远「可施放」，否则条件不满足时会让整个事件选项变灰。</summary>
        public override bool CanApply(RunEffectContext ctx) => true;

        public override void Apply(RunEffectContext ctx)
        {
            var child = ctx.Child();
            RunEffectResolver.ResolveAll(Condition.Test(ctx) ? Then : Else, child);
            ctx.Absorb(child);
        }
    }

    /// <summary>组合子：按权重随机挑一个分支执行。用于「赌博」类事件。</summary>
    [Serializable]
    public class RandomPickRunEffect : RunEffect
    {
        [Serializable]
        public class Option
        {
            public string Note;
            public int Weight = 10;

            [SerializeReference]
            public List<RunEffect> Effects = new List<RunEffect>();
        }

        public List<Option> Options = new List<Option>();

        public override bool CanApply(RunEffectContext ctx) => true;

        public override void Apply(RunEffectContext ctx)
        {
            if (Options == null || Options.Count == 0 || ctx.Rng == null) return;

            int total = 0;
            for (int i = 0; i < Options.Count; i++) total += Mathf.Max(0, Options[i].Weight);
            if (total <= 0) return;

            int roll = ctx.Rng.Range(RngStream.Event, 0, total);
            int idx = 0;
            for (; idx < Options.Count; idx++)
            {
                roll -= Mathf.Max(0, Options[idx].Weight);
                if (roll < 0) break;
            }
            if (idx >= Options.Count) idx = Options.Count - 1;

            var child = ctx.Child();
            RunEffectResolver.ResolveAll(Options[idx].Effects, child);
            ctx.Absorb(child);
        }

        public override string Describe(RunEffectContext ctx) => "结果随机";
    }

    /// <summary>
    /// 直接开始一场战斗。给「你惊动了守卫」这类事件用。
    /// ★ 它本身不启动战斗，只是把 EncounterId 写进 RunContext，由 RunManager 读走。
    ///   RunEffect 是纯数据操作，不该持有流程控制权。
    /// </summary>
    [Serializable]
    public class StartBattleRunEffect : RunEffect
    {
        [Tooltip("EncounterDefinition.Id。留空则随机一场普通战斗")]
        public string EncounterId;

        [Tooltip("这场战斗胜利后是否给奖励")]
        public bool GiveReward = true;

        public override void Apply(RunEffectContext ctx)
        {
            var db = ctx.Db;
            string id = EncounterId;

            if (string.IsNullOrEmpty(id) && db != null)
            {
                var ids = new List<string>();
                db.GetEncounterIds(ids, elite: false, boss: false);
                if (ids.Count > 0) id = ids[ctx.Rng.Range(RngStream.Event, 0, ids.Count)];
            }

            if (string.IsNullOrEmpty(id)) return;

            ctx.Run.PendingBattleEncounterId = id;
            ctx.Run.PendingBattleGivesReward = GiveReward;
            ctx.AddLog("战斗开始了！");
        }

        public override string Describe(RunEffectContext ctx) => "进入战斗";
    }
}
