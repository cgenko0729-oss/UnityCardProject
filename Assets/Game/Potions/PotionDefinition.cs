using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Effects;
using UnityEngine;

namespace Game.Potions
{
    public enum PotionRarity { Common, Uncommon, Rare }

    /// <summary>
    /// 药水的静态配置。★ 不存任何战斗中会变的数据。
    ///
    /// 关键设计：<see cref="Effects"/> 的元素类型就是 <see cref="CardEffect"/>，
    /// 与卡牌**完全共用那套效果类和 EffectResolver**。
    /// 药水不是第二套机制，只是「一次性的、不进牌库的卡」——
    /// 因此新增一个药水 = 建一个资产，零代码；
    /// 而且卡牌能做的一切（多段、条件、组合子、甚至选牌）药水立刻全都能做。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Potion", fileName = "Potion_")]
    public class PotionDefinition : ScriptableObject
    {
        [Header("标识")]
        public string Id;
        public string DisplayName;
        public Sprite Icon;
        public PotionRarity Rarity = PotionRarity.Common;

        [Header("描述")]
        [Tooltip("模板里的 {N} 会被第 N 个效果的 Describe 结果替换，与卡牌完全一致。")]
        [TextArea(2, 4)]
        public string DescriptionTemplate;

        [Header("使用")]
        [Tooltip("需要玩家点一个敌人时设为 SingleEnemy，其余一律 None。")]
        public CardTargetKind TargetKind = CardTargetKind.None;

        [SerializeReference]
        public List<CardEffect> Effects = new List<CardEffect>();

        [Header("商店")]
        [Tooltip("商店售价。0 表示用稀有度默认价。")]
        public int ShopPrice;

        public bool NeedsTarget => TargetKind == CardTargetKind.SingleEnemy;

        /// <summary>动态描述。ctx 可为 null（战斗外的商店 / 奖励界面），此时用静态数值。</summary>
        public string GetDescription(BattleContext ctx)
        {
            var effCtx = new EffectContext
            {
                Battle = ctx,
                Source = ctx?.Player,
                PreviewMode = true,   // 描述每帧都会算，绝不能消耗随机流
            };
            return EffectDescription.Format(DescriptionTemplate, Effects, effCtx);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id))
                Id = name.StartsWith("Potion_") ? name.Substring(7).ToLowerInvariant() : name.ToLowerInvariant();
        }
#endif
    }

    /// <summary>
    /// 玩家背包里的一瓶药水。
    /// 目前除了 Uid 没有可变数据，但仍然做成 Instance——
    /// 与 Card / Relic 保持同一套 Definition/Instance 分工，
    /// 将来要加「已充能次数」「被诅咒」之类的状态时不必再改一遍所有调用点。
    /// </summary>
    public class PotionInstance
    {
        /// <summary>一局游戏内唯一编号，由 <c>RunContext.NextPotionUid</c> 分配。</summary>
        public readonly int Uid;
        public readonly PotionDefinition Def;

        public PotionInstance(int uid, PotionDefinition def)
        {
            Uid = uid;
            Def = def;
        }

        public string Id => Def != null ? Def.Id : null;
        public string DisplayName => Def != null ? Def.DisplayName : "<null>";

        public override string ToString() => $"{DisplayName}#{Uid}";
    }
}
