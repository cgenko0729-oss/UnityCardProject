using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Enemies;
using Game.Localization;
using Game.Potions;
using Game.Statuses;
using Game.Units;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 把游戏数据翻译成 tooltip 词条。
    ///
    /// ★ 「这张牌牵扯到哪些状态」一律靠 <see cref="EffectTree.CollectStatuses"/> 扫效果树得到，
    ///   不去描述文本里做子串匹配。文案随时会改措辞（「令目标变得脆弱」里没有「易伤」二字），
    ///   而效果树是这张牌真正要做的事，改文案不会让词条静默消失。
    /// </summary>
    public static class TooltipContent
    {
        public static readonly Color KeywordAccent = new Color(0.95f, 0.85f, 0.45f);
        public static readonly Color BuffAccent = new Color(0.55f, 0.90f, 0.60f);
        public static readonly Color DebuffAccent = new Color(1.00f, 0.55f, 0.50f);
        public static readonly Color NeutralAccent = new Color(0.80f, 0.86f, 0.96f);
        public static readonly Color IntentAccent = new Color(1.00f, 0.85f, 0.40f);

        /// <summary>关键字在 <see cref="CardKeyword"/> 里的位顺序。遍历时按这个顺序输出，保证稳定。</summary>
        private static readonly CardKeyword[] AllKeywords =
        {
            CardKeyword.Exhaust, CardKeyword.Retain, CardKeyword.Innate,
            CardKeyword.Ethereal, CardKeyword.Unplayable
        };

        public static Color AccentOf(StatusPolarity polarity) => polarity switch
        {
            StatusPolarity.Buff => BuffAccent,
            StatusPolarity.Debuff => DebuffAccent,
            _ => NeutralAccent
        };

        // ============================================================ 单条词条

        /// <summary>带真实层数的状态词条。用于单位面板上挂着的状态。</summary>
        public static TooltipEntry ForStatus(StatusDefinition def, int stacks)
            => new TooltipEntry(Loc.T("tooltip.status_with_stacks", "{0} {1}", def.LocalizedName, stacks), def.Describe(stacks), AccentOf(def.Polarity));

        /// <summary>不带层数的状态词条（<c>{stacks}</c> 渲染成 X）。用于卡牌 / 药水 / 意图。</summary>
        public static TooltipEntry ForStatusGeneric(StatusDefinition def)
            => new TooltipEntry(def.LocalizedName, def.DescribeGeneric(), AccentOf(def.Polarity));

        // ============================================================ 组装

        /// <summary>
        /// 卡牌：关键字 + 这张牌会牵扯到的状态。
        /// </summary>
        public static bool BuildForCard(CardInstance card, GameDatabase db, List<TooltipEntry> buffer)
        {
            if (card == null || card.Def == null) return false;

            int before = buffer.Count;

            // ExtraKeywords 也要算进去：有些效果会给某张牌临时挂上「消耗」
            AppendKeywords(card.Def.Keywords | card.ExtraKeywords, db, buffer);

            var statuses = new List<StatusDefinition>(4);
            EffectTree.CollectStatuses(card.Def.Effects, statuses);
            EffectTree.CollectStatuses(card.Def.InHandEndOfTurnEffects, statuses);
            AppendStatusesGeneric(statuses, buffer);

            return buffer.Count > before;
        }

        /// <summary>药水：说明 + 它会牵扯到的状态。</summary>
        public static bool BuildForPotion(PotionDefinition def, BattleContext ctx, List<TooltipEntry> buffer)
        {
            if (def == null) return false;

            buffer.Add(new TooltipEntry(def.LocalizedName, def.GetDescription(ctx), NeutralAccent));

            var statuses = new List<StatusDefinition>(4);
            EffectTree.CollectStatuses(def.Effects, statuses);
            AppendStatusesGeneric(statuses, buffer);
            return true;
        }

        /// <summary>
        /// 敌人意图：这次行动大致要做什么 + 它会施加的状态。
        ///
        /// ★ 状态那部分才是真正有价值的信息——数值意图图标上已经写着了，
        ///   而「这一击还会给你上两层易伤」是玩家看不见、又必须知道的。
        /// </summary>
        public static bool BuildForIntent(BattleUnit unit, List<TooltipEntry> buffer)
        {
            if (unit == null || unit.IsPlayer || !unit.IsAlive) return false;

            var intent = unit.CurrentIntent;
            if (intent.Kind == IntentKind.Unknown) return false;

            buffer.Add(new TooltipEntry(IntentTitle(intent), IntentBody(intent), IntentAccent));

            var action = ActionOf(unit, intent);
            if (action != null)
            {
                var statuses = new List<StatusDefinition>(4);
                EffectTree.CollectStatuses(action.Effects, statuses);
                AppendStatusesGeneric(statuses, buffer);
            }
            return true;
        }

        // ============================================================ 内部

        public static void AppendKeywords(CardKeyword keywords, GameDatabase db, List<TooltipEntry> buffer)
        {
            if (keywords == CardKeyword.None || db == null) return;

            for (int i = 0; i < AllKeywords.Length; i++)
            {
                var bit = AllKeywords[i];
                if ((keywords & bit) == 0) continue;

                var def = db.GetKeyword(bit);
                // 没有配对应资产就跳过，而不是打一条「Exhaust」的英文占位。
                // 缺资产由 ContentValidator 报出来，这里保持安静。
                if (def == null) continue;

                buffer.Add(new TooltipEntry(def.LocalizedName, def.LocalizedDescription, KeywordAccent));
            }
        }

        private static void AppendStatusesGeneric(List<StatusDefinition> statuses, List<TooltipEntry> buffer)
        {
            for (int i = 0; i < statuses.Count; i++)
                if (statuses[i] != null) buffer.Add(ForStatusGeneric(statuses[i]));
        }

        private static EnemyAction ActionOf(BattleUnit unit, Intent intent)
        {
            var def = unit.EnemyDef;
            if (def == null || def.Actions == null) return null;
            if (intent.ActionIndex < 0 || intent.ActionIndex >= def.Actions.Count) return null;
            return def.Actions[intent.ActionIndex];
        }

        private static string IntentTitle(Intent intent) => intent.Kind switch
        {
            IntentKind.Attack => Loc.T("intent.title.attack", "攻击"),
            IntentKind.AttackDefend => Loc.T("intent.title.attack_defend", "攻击 + 防御"),
            IntentKind.AttackDebuff => Loc.T("intent.title.attack_debuff", "攻击 + 减益"),
            IntentKind.Defend => Loc.T("intent.title.defend", "防御"),
            IntentKind.Buff => Loc.T("intent.title.buff", "强化自身"),
            IntentKind.Debuff => Loc.T("intent.title.debuff", "施加减益"),
            IntentKind.Sleep => Loc.T("intent.title.sleep", "休眠"),
            IntentKind.Special => Loc.T("intent.title.special", "特殊行动"),
            _ => Loc.T("intent.title.unknown", "意图")
        };

        private static string IntentBody(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Attack:
                case IntentKind.AttackDefend:
                case IntentKind.AttackDebuff:
                    string hit = intent.Times > 1
                        ? Loc.T("intent.body.attack_multi", "下回合造成 {0} 点伤害，共 {1} 次（合计 {2}）。", intent.Value, intent.Times, intent.Value * intent.Times)
                        : Loc.T("intent.body.attack", "下回合造成 {0} 点伤害。", intent.Value);
                    if (intent.Kind == IntentKind.AttackDefend) hit += "\n" + Loc.T("intent.body.also_block", "同时给自己加护甲。");
                    if (intent.Kind == IntentKind.AttackDebuff) hit += "\n" + Loc.T("intent.body.also_debuff", "同时对你施加减益。");
                    return hit + "\n\n" + Loc.T("intent.body.note", "※ 数值已计入当前的力量 / 虚弱 / 易伤。");

                case IntentKind.Defend:
                    return Loc.T("intent.body.defend", "下回合给自己获得 {0} 点护甲。", intent.Value);
                case IntentKind.Buff:
                    return Loc.T("intent.body.buff", "下回合强化自己。");
                case IntentKind.Debuff:
                    return Loc.T("intent.body.debuff", "下回合对你施加减益。");
                case IntentKind.Sleep:
                    return Loc.T("intent.body.sleep", "这回合不行动。");
                default:
                    return Loc.T("intent.body.unknown", "意图不明。");
            }
        }
    }
}
