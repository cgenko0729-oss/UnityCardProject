using System.Collections.Generic;
using Game.Effects;
using UnityEngine;

namespace Game.Cards
{
    /// <summary>
    /// 一张卡的静态配置。
    /// ★ 铁律：这里绝不允许出现战斗中会被写入的字段。所有可变数据在 CardInstance。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Card", fileName = "Card_")]
    public class CardDefinition : ScriptableObject
    {
        [Header("标识")]
        public string Id;
        public string DisplayName;
        public Sprite Art;

        [Header("规则")]
        public int Cost = 1;
        public CostMode CostMode = CostMode.Fixed;
        public CardType Type = CardType.Attack;
        public CardRarity Rarity = CardRarity.Common;
        public CardTargetKind TargetKind = CardTargetKind.SingleEnemy;
        public CardKeyword Keywords = CardKeyword.None;

        [Header("描述")]
        [Tooltip("用 {0} {1} 引用第 N 个效果的数值。例：造成 {0} 点伤害，获得 {1} 点护甲。")]
        [TextArea(2, 4)]
        public string DescriptionTemplate;

        [Header("效果（按顺序结算）")]
        [SerializeReference]
        public List<CardEffect> Effects = new List<CardEffect>();

        [Header("留在手上的代价")]
        [Tooltip("回合结束时这张牌若仍在手牌，就结算这些效果。「灼烧」「疑虑」这类状态/诅咒牌靠它。")]
        [SerializeReference]
        public List<CardEffect> InHandEndOfTurnEffects = new List<CardEffect>();

        public bool HasInHandEndOfTurnEffects
            => InHandEndOfTurnEffects != null && InHandEndOfTurnEffects.Count > 0;

        [Header("升级")]
        [Tooltip("升级后变成哪张卡。留空表示不可升级。")]
        public CardDefinition UpgradedVersion;

        public bool HasKeyword(CardKeyword k) => (Keywords & k) != 0;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id)) Id = name.StartsWith("Card_") ? name.Substring(5).ToLowerInvariant() : name.ToLowerInvariant();

            // 校验描述模板里的 {N} 不超出效果数量
            if (!string.IsNullOrEmpty(DescriptionTemplate) && Effects != null)
            {
                for (int i = Effects.Count; i < 10; i++)
                {
                    if (DescriptionTemplate.Contains("{" + i + "}"))
                    {
                        Debug.LogError($"[{name}] 描述模板引用了 {{{i}}}，但只有 {Effects.Count} 个效果。", this);
                        break;
                    }
                }
            }
        }
#endif
    }
}
