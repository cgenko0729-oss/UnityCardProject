using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core;

namespace Game.RunEffects
{
    /// <summary>
    /// 需要玩家做选择的后续步骤。RunEffect 是同步执行的，但「选一张牌删掉」必须等玩家点，
    /// 所以效果只负责**排一个请求**，由界面层去弹选牌面板，选完再回调。
    /// 这与战斗层「逻辑同步、表现异步」是同一个思路。
    /// </summary>
    public enum RunChoiceKind { RemoveCard, UpgradeCard, AddOneOfCards }

    public class RunChoiceRequest
    {
        public RunChoiceKind Kind;

        /// <summary>要选几张。</summary>
        public int Count = 1;

        /// <summary>AddOneOfCards 时的候选牌。其余情况从玩家牌库里选。</summary>
        public List<CardDefinition> Options;

        /// <summary>给界面显示的标题。</summary>
        public string Title;
    }

    /// <summary>
    /// 一次局外效果结算的上下文。对应战斗层的 <c>EffectContext</c>，但作用对象是 <see cref="RunContext"/>。
    /// </summary>
    public class RunEffectContext
    {
        public RunContext Run;

        public GameDatabase Db => Run != null ? Run.Database : null;
        public Rng Rng => Run != null ? Run.Rng : null;

        /// <summary>给玩家看的结果文本，例如「获得 50 金币」「失去 7 点生命」。</summary>
        public readonly List<string> Log = new List<string>(8);

        /// <summary>待处理的玩家选择。界面层在效果跑完后逐个弹面板。</summary>
        public readonly Queue<RunChoiceRequest> Choices = new Queue<RunChoiceRequest>();

        /// <summary>递归深度，防止组合子无限嵌套。</summary>
        public int Depth;

        public RunEffectContext(RunContext run)
        {
            Run = run;
        }

        public void Reset()
        {
            Log.Clear();
            Choices.Clear();
            Depth = 0;
        }

        public void AddLog(string line)
        {
            if (!string.IsNullOrEmpty(line)) Log.Add(line);
        }

        public void RequestChoice(RunChoiceRequest req)
        {
            if (req != null) Choices.Enqueue(req);
        }

        public RunEffectContext Child()
            => new RunEffectContext(Run) { Depth = Depth + 1 };

        /// <summary>把子上下文的产出并回父级。</summary>
        public void Absorb(RunEffectContext child)
        {
            if (child == null) return;
            for (int i = 0; i < child.Log.Count; i++) Log.Add(child.Log[i]);
            while (child.Choices.Count > 0) Choices.Enqueue(child.Choices.Dequeue());
        }
    }

    /// <summary>
    /// 局外效果的基类。用 <c>[SerializeReference]</c> 内嵌进事件 / 商店 / 宝箱配置。
    ///
    /// ★★★ 铁律与 <c>CardEffect</c> 完全一致：子类不得有任何在 Apply 中被写入的实例字段。
    /// 一个效果实例被该配置资产的所有使用者共享。可变数据放 RunEffectContext / RunContext。
    /// （Editor 下 ContentValidator 会扫这条规则。）
    /// </summary>
    [Serializable]
    public abstract class RunEffect
    {
        /// <summary>能否执行。返回 false 时，事件选项会变灰（例如金币不够）。</summary>
        public virtual bool CanApply(RunEffectContext ctx) => true;

        public abstract void Apply(RunEffectContext ctx);

        /// <summary>给界面显示的一行描述。</summary>
        public virtual string Describe(RunEffectContext ctx) => string.Empty;
    }

    /// <summary>局外效果的结算入口。事件、商店、宝箱、休息点全部走这里。</summary>
    public static class RunEffectResolver
    {
        public const int MaxDepth = 6;

        public static bool CanApplyAll(IReadOnlyList<RunEffect> effects, RunEffectContext ctx)
        {
            if (effects == null) return true;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] != null && !effects[i].CanApply(ctx)) return false;
            return true;
        }

        public static void ResolveAll(IReadOnlyList<RunEffect> effects, RunEffectContext ctx)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++) Resolve(effects[i], ctx);
        }

        public static void Resolve(RunEffect effect, RunEffectContext ctx)
        {
            if (effect == null || ctx == null || ctx.Run == null) return;

            if (ctx.Depth > MaxDepth)
            {
                UnityEngine.Debug.LogWarning(
                    $"[RunEffectResolver] 递归深度超过 {MaxDepth}，已中断：{effect.GetType().Name}");
                return;
            }

            if (!effect.CanApply(ctx)) return;
            effect.Apply(ctx);
        }

        public static string DescribeAll(IReadOnlyList<RunEffect> effects, RunEffectContext ctx, string separator = "，")
        {
            if (effects == null || effects.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder(64);
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] == null) continue;
                var s = effects[i].Describe(ctx);
                if (string.IsNullOrEmpty(s)) continue;
                if (sb.Length > 0) sb.Append(separator);
                sb.Append(s);
            }
            return sb.ToString();
        }
    }
}
