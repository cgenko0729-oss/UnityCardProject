using System.Collections.Generic;
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

        public string Describe(int stacks)
            => string.IsNullOrEmpty(Description) ? DisplayName : Description.Replace("{stacks}", stacks.ToString());

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id))
                Id = name.StartsWith("Status_") ? name.Substring(7).ToLowerInvariant() : name.ToLowerInvariant();
        }
#endif
    }
}
