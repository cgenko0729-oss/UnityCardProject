using System.Collections.Generic;
using Game.Localization;
using Game.Statuses;
using UnityEngine;

namespace Game.Relics
{
    public enum RelicRarity
    {
        /// <summary>开局自带，不出现在奖励池里。</summary>
        Starter,
        Common,
        Uncommon,
        Rare,
        /// <summary>击败 Boss 后专属掉落。</summary>
        Boss,
        /// <summary>只在商店出售。</summary>
        Shop
    }

    /// <summary>
    /// 遗物的静态配置。★ 不存任何战斗中会变的数据。
    ///
    /// 关键设计：<see cref="Behaviours"/> 的元素类型就是 <see cref="StatusBehaviour"/>，
    /// 与状态**完全共用那套 Hook 接口**。遗物不是第二套机制，只是「不会消失、挂在玩家身上的状态」。
    /// 因此新增一个遗物 = 写一个行为类 + 建一个资产，`BattleController` 一行都不用改。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Relic", fileName = "Relic_")]
    public class RelicDefinition : ScriptableObject
    {
        [Header("标识")]
        public string Id;
        public string DisplayName;
        public Sprite Icon;
        public RelicRarity Rarity = RelicRarity.Common;

        [TextArea(2, 4)]
        public string Description;

        [Header("行为（与状态共用 Hook 接口）")]
        [SerializeReference]
        public List<StatusBehaviour> Behaviours = new List<StatusBehaviour>();

        [Header("商店")]
        [Tooltip("商店售价。0 表示用稀有度默认价。")]
        public int ShopPrice;

        public string LocalizedName => Loc.T($"relic.{Id}.name", DisplayName);
        public string LocalizedDescription => Loc.T($"relic.{Id}.desc", Description);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id))
                Id = name.StartsWith("Relic_") ? name.Substring(6).ToLowerInvariant() : name.ToLowerInvariant();
        }
#endif
    }
}
