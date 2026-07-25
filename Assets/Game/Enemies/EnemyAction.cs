using System;
using System.Collections.Generic;
using Game.Effects;
using UnityEngine;

namespace Game.Enemies
{
    /// <summary>敌人的一个行动。效果系统与卡牌完全共用。</summary>
    [Serializable]
    public class EnemyAction
    {
        public string Name = "Attack";
        public IntentKind Intent = IntentKind.Attack;

        [Tooltip("权重越大越容易被随机选中。0 表示不参与随机，只能被固定序列或自定义 Brain 选中。")]
        public int Weight = 10;

        [Tooltip("最多连续使用几次。0 表示不限制。")]
        public int MaxConsecutive = 0;

        [Tooltip("满足条件才可选。默认 Always。")]
        public EffectCondition Condition;

        [Tooltip("只在这些阶段可用（位掩码：第 0 阶段 = 1，第 1 阶段 = 2，第 2 阶段 = 4 …）。0 表示所有阶段。")]
        public int PhaseMask = 0;

        [SerializeReference]
        public List<CardEffect> Effects = new List<CardEffect>();
    }
}
