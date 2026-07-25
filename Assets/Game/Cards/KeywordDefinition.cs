using UnityEngine;

namespace Game.Cards
{
    /// <summary>
    /// 一个卡牌关键字（消耗 / 保留 / 固有 / 虚无 / 不可打出）的显示名与解释文案。
    ///
    /// ★ 为什么关键字也做成资产，而不是在 UI 层写一张静态表：
    ///   显示名和解释是**内容**，不是代码。做成资产后，改文案不用碰代码、不用重新编译，
    ///   将来接本地化时它和卡牌 / 状态 / 遗物走同一条管线，不会剩下一处特例。
    ///
    /// ★ <see cref="Keyword"/> 必须只带一个位。<see cref="CardKeyword"/> 是 [Flags]，
    ///   写成 <c>Exhaust | Retain</c> 这种组合值会让「按位反查定义」拿到错的东西，
    ///   `Tools/卡牌游戏/3. 校验内容与架构规则` 会扫出来。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Keyword", fileName = "Keyword_")]
    public class KeywordDefinition : ScriptableObject
    {
        [Tooltip("必须只勾一个位。")]
        public CardKeyword Keyword = CardKeyword.Exhaust;

        public string DisplayName;

        [TextArea(2, 4)]
        public string Description;

        /// <summary>是否只带一个位（0 和多位组合都算非法）。</summary>
        public bool IsSingleBit
        {
            get
            {
                int v = (int)Keyword;
                return v != 0 && (v & (v - 1)) == 0;
            }
        }
    }
}
