using System.Collections.Generic;
using Game.Cards;
using Game.Effects;
using Game.Effects.Impl;
using Game.Statuses;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>
    /// 诅咒牌与状态牌。
    ///
    /// ★ 这一批只用既有机制：<see cref="CostMode.Unplayable"/>、<see cref="CardKeyword.Ethereal"/>、
    ///   <see cref="CardKeyword.Exhaust"/>、<c>CardDefinition.InHandEndOfTurnEffects</c>。
    ///   `CardType.Status` / `Curse` 与 `CardView` 的诅咒配色在阶段 2 就写好了，
    ///   一直没有任何内容用上——这批卡把它们真正接通。
    ///
    /// <para>两者的区别只在归属：**状态牌是临时的**（战斗结束消失，由敌人塞给你），
    /// **诅咒牌是永久的**（进牌库，只能靠商店删卡或事件清除）。
    /// 因此状态牌由 <c>AddCardEffect.Temporary = true</c> 生成，诅咒牌则由局外效果加进牌库。</para>
    /// </summary>
    internal static class SampleContentCurses
    {
        internal static void CreateCurseAndStatusCards(Dictionary<string, StatusDefinition> statuses,
                                                       Dictionary<string, CardDefinition> cards)
        {
            // ================================================================ 状态牌（临时）

            // 最朴素的堵手牌：没有任何效果，就是占一个格子。
            Make(cards, "wound", "伤口", CardType.Status,
                 "不可打出。");

            // ★ 零代码showcase：Ethereal 在 DeckController 里早就实现了，之前没有任何内容用它。
            //   眩晕留在手上会自己烧掉，所以它的代价是「这一回合少一张可用牌」而不是永久堵塞。
            var dazed = Make(cards, "dazed", "眩晕", CardType.Status,
                 "不可打出。虚无（回合结束时若仍在手牌则消耗）。");
            dazed.Keywords = CardKeyword.Ethereal;
            EditorUtility.SetDirty(dazed);

            // 可以打出，但要花 1 费才能清掉——用资源换手牌空间的选择题。
            // ★ 类型仍是 Status（它确实是敌人塞的状态牌），只是 CostMode 改成可打出。
            //   标成 Skill 会让它在牌库界面里混进正经技能牌里。
            var slimed = Make(cards, "slimed", "粘液", CardType.Status,
                 "消耗。");
            slimed.CostMode = CostMode.Fixed;
            slimed.Cost = 1;
            slimed.Rarity = CardRarity.Special;
            slimed.Keywords = CardKeyword.Exhaust;
            EditorUtility.SetDirty(slimed);

            // 留在手上会持续掉血：迫使玩家要么快点打完，要么想办法弃掉。
            var burn = Make(cards, "burn", "灼烧", CardType.Status,
                 "不可打出。回合结束时若仍在手牌，受到 2 点伤害。");
            burn.InHandEndOfTurnEffects = new List<CardEffect>
            {
                new DamageEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Amount = EffectValue.Flat(2),
                    // Kind 只决定「算不算攻击」（荆棘据此判断要不要反弹）；
                    // 想真正穿透护甲必须另外勾 IgnoreBlock——两者是独立的开关。
                    Kind = Battle.DamageKind.Loss,
                    IgnoreBlock = true,
                }
            };
            EditorUtility.SetDirty(burn);

            // ================================================================ 诅咒牌（永久）

            Make(cards, "injury", "伤势", CardType.Curse,
                 "不可打出。");

            var doubt = Make(cards, "doubt", "疑虑", CardType.Curse,
                 "不可打出。回合结束时若仍在手牌，获得 1 层虚弱。");
            doubt.InHandEndOfTurnEffects = new List<CardEffect>
            {
                new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = statuses["weak"],
                    Stacks = EffectValue.Flat(1),
                }
            };
            EditorUtility.SetDirty(doubt);
        }

        /// <summary>
        /// 建一张不可打出的状态 / 诅咒牌。
        /// ★ 稀有度一律 Special：与升级版同理，否则它们会混进奖励三选一和商店，
        ///   玩家会在战斗奖励里看到「伤口」可选。
        /// </summary>
        private static CardDefinition Make(Dictionary<string, CardDefinition> cards,
                                           string id, string name, CardType type, string description)
        {
            var so = SampleContentGenerator.LoadOrCreateAsset<CardDefinition>(
                $"Assets/GameData/Cards/Card_{SampleContentGenerator.Capitalize(id)}.asset");

            so.Id = id;
            so.DisplayName = name;
            so.Type = type;
            so.Cost = 0;
            so.CostMode = CostMode.Unplayable;
            so.TargetKind = CardTargetKind.None;
            so.Keywords = CardKeyword.None;
            so.Rarity = CardRarity.Special;
            so.DescriptionTemplate = description;
            so.Effects = new List<CardEffect>();
            so.InHandEndOfTurnEffects = new List<CardEffect>();
            so.UpgradedVersion = null;

            EditorUtility.SetDirty(so);
            cards[id] = so;
            return so;
        }
    }
}
