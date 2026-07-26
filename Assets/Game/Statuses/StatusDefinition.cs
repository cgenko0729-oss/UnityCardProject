using System.Collections.Generic;
using Game.Localization;
using UnityEngine;

namespace Game.Statuses
{
    public enum StatusPolarity { Buff, Debuff, Neutral }

    /// <summary>层数如何自然变化。★ 衰减由 BattleController.TickStatusDecay 统一处理，不写在行为里。</summary>
    public enum StatusDecay
    {
        /// <summary>永不衰减（力量、壁垒）。</summary>
        None,
        /// <summary>回合结束 -1 层（易伤、虚弱、中毒）。</summary>
        LoseOneAtTurnEnd,
        /// <summary>回合结束整个移除。</summary>
        RemoveAtTurnEnd,
        /// <summary>回合开始整个移除。</summary>
        LoseAllAtTurnStart
    }

    /// <summary>状态的静态配置。★ 不存层数。</summary>
    [CreateAssetMenu(menuName = "Game/Status", fileName = "Status_")]
    public class StatusDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public Sprite Icon;
        public StatusPolarity Polarity = StatusPolarity.Buff;
        public StatusDecay Decay = StatusDecay.None;
        public int MaxStacks = 999;

        [Tooltip("支持 {stacks} 占位符")]
        [TextArea(2, 3)]
        public string Description;

        [SerializeReference]
        public List<StatusBehaviour> Behaviours = new List<StatusBehaviour>();

        public string LocalizedName => Loc.T($"status.{Id}.name", DisplayName);

        private string LocalizedDescription => Loc.T($"status.{Id}.desc", Description);

        public string Describe(int stacks)
        {
            var desc = LocalizedDescription;
            return string.IsNullOrEmpty(desc) ? LocalizedName : desc.Replace("{stacks}", stacks.ToString());
        }

        /// <summary>
        /// 不带具体层数的解释，<c>{stacks}</c> 渲染成 <c>X</c>。
        ///
        /// ★ 给「卡牌 tooltip」用：那时候玩家还没把状态挂上去，没有真实层数可填。
        ///   退而用 <c>Describe(1)</c> 会写出「回合结束回复 1 点生命」这种
        ///   看起来很确定、其实是编出来的数字——比写 X 更容易误导人。
        /// </summary>
        public string DescribeGeneric()
        {
            var desc = LocalizedDescription;
            return string.IsNullOrEmpty(desc) ? LocalizedName : desc.Replace("{stacks}", "X");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id))
                Id = name.StartsWith("Status_") ? name.Substring(7).ToLowerInvariant() : name.ToLowerInvariant();
        }
#endif
    }
}
