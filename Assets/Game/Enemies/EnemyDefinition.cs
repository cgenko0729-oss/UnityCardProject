using System;
using System.Collections.Generic;
using Game.Statuses;
using UnityEngine;

namespace Game.Enemies
{
    [Serializable]
    public struct StartingStatus
    {
        public StatusDefinition Status;
        public int Stacks;
    }

    /// <summary>敌人的静态配置。★ 不存血量、不存行动历史。</summary>
    [CreateAssetMenu(menuName = "Game/Enemy", fileName = "Enemy_")]
    public class EnemyDefinition : ScriptableObject
    {
        [Header("标识")]
        public string Id;
        public string DisplayName;
        public Sprite Art;

        [Header("数值")]
        public int MinHp = 10;
        public int MaxHp = 14;
        public bool IsElite;
        public bool IsBoss;

        public List<StartingStatus> StartingStatuses = new List<StartingStatus>();

        [Header("行动")]
        public List<EnemyAction> Actions = new List<EnemyAction>();

        [Tooltip("固定行动序列（存 Actions 的下标）。留空则完全用权重随机。")]
        public List<int> FixedSequence = new List<int>();

        [Tooltip("固定序列跑完后是否循环。false 表示之后转入权重随机。")]
        public bool LoopSequence = true;

        [Header("多阶段")]
        [Tooltip("血量百分比阈值，跌破即进入下一阶段。留空表示单阶段。例：[50] 表示血量 <=50% 进入阶段 1。")]
        public List<int> PhaseHpThresholds = new List<int>();

        [Header("自定义 AI")]
        [Tooltip("继承 EnemyBrain 的类的完整类名（含程序集），留空用默认 EnemyBrain。" +
                 "例：Game.Enemies.Impl.GuardianBrain, Game.Runtime")]
        public string CustomBrainType;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id))
                Id = name.StartsWith("Enemy_") ? name.Substring(6).ToLowerInvariant() : name.ToLowerInvariant();
            if (MaxHp < MinHp) MaxHp = MinHp;

            if (FixedSequence != null)
            {
                for (int i = 0; i < FixedSequence.Count; i++)
                {
                    if (FixedSequence[i] < 0 || FixedSequence[i] >= Actions.Count)
                        Debug.LogError($"[{name}] FixedSequence[{i}] = {FixedSequence[i]} 越界（Actions 只有 {Actions.Count} 个）。", this);
                }
            }
        }
#endif
    }
}
