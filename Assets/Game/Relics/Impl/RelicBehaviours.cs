using System;
using Game.Battle;
using Game.Cards;
using Game.Statuses;
using Game.Units;
using UnityEngine;

namespace Game.Relics.Impl
{
    // 遗物行为。★ 全部继承 StatusBehaviour、实现 Game.Battle 里的 Hook 接口——
    // 与状态共用同一套机制，BattleController 完全不知道「遗物」这个概念的存在。
    //
    // ★★ 铁律同样适用：这些类不得有可变实例字段。需要计数的遗物把数据写进
    //    HookSource.Relic.Counter（每局唯一）或挂一个状态用 StatusInstance.Stacks（每场战斗唯一）。

    // ================================================================= 战斗开始 / 结束

    /// <summary>战斗开始时给玩家挂若干层某状态。「开局 1 点力量」「开局 1 层神器」都用它。</summary>
    [Serializable]
    public class GrantStatusOnBattleStartBehaviour : StatusBehaviour, IBattleFlowHook
    {
        public StatusDefinition Status;
        public int Stacks = 1;

        public override int Order => HookOrder.Early;

        public void OnBattleStart(BattleContext ctx, in HookSource src)
        {
            if (Status == null || Stacks == 0) return;
            var player = ctx.Player;
            if (player == null) return;

            ctx.Post(BattleEventType.Message, 0, player.Uid, 0, src.Relic != null ? src.Relic.Id : "relic");
            player.AddStatus(ctx, Status, Stacks, player);
        }

        public void OnBattleEnd(BattleContext ctx, in HookSource src, bool victory) { }
    }

    /// <summary>战斗开始时回血 / 获得护甲。</summary>
    [Serializable]
    public class BattleStartResourceBehaviour : StatusBehaviour, IBattleFlowHook
    {
        [Tooltip("战斗开始回复的生命值")]
        public int Heal;

        [Tooltip("战斗开始获得的护甲")]
        public int Block;

        public override int Order => HookOrder.Early;

        public void OnBattleStart(BattleContext ctx, in HookSource src)
        {
            var player = ctx.Player;
            if (player == null) return;
            if (Heal > 0) player.Heal(ctx, Heal);
            if (Block > 0) player.AddBlock(ctx, Block);
        }

        public void OnBattleEnd(BattleContext ctx, in HookSource src, bool victory) { }
    }

    /// <summary>战斗胜利后回血 / 给金币。「燃烧之血」用这个。</summary>
    [Serializable]
    public class BattleRewardBehaviour : StatusBehaviour, IBattleFlowHook
    {
        [Tooltip("胜利后回复的生命值")]
        public int HealOnVictory = 6;

        [Tooltip("胜利后额外获得的金币")]
        public int GoldOnVictory;

        public override int Order => HookOrder.AddFlat;

        public void OnBattleStart(BattleContext ctx, in HookSource src) { }

        public void OnBattleEnd(BattleContext ctx, in HookSource src, bool victory)
        {
            if (!victory) return;

            // ★ 必须治疗 BattleUnit 而不是直接改 RunContext.Hp：
            //   EndBattle 会在所有 OnBattleEnd 跑完之后执行 Run.Hp = Player.Hp，
            //   直接改 Run.Hp 会被这一句原样覆盖掉。
            if (HealOnVictory > 0 && ctx.Player != null) ctx.Player.Heal(ctx, HealOnVictory);
            if (GoldOnVictory > 0 && ctx.Run != null) ctx.Run.Gold += GoldOnVictory;
        }
    }

    // ================================================================= 回合资源

    /// <summary>改变每回合的抽牌数 / 能量。可以限定只在第一回合生效。</summary>
    [Serializable]
    public class TurnResourceBehaviour : StatusBehaviour, IResourceHook
    {
        [Tooltip("每回合额外抽牌数")]
        public int ExtraDraw;

        [Tooltip("每回合额外能量")]
        public int ExtraEnergy;

        [Tooltip("只在第一回合生效（「备战包」「提灯」这类）")]
        public bool FirstTurnOnly;

        public override int Order => HookOrder.AddFlat;

        private bool Active(BattleContext ctx) => !FirstTurnOnly || ctx.TurnNumber <= 1;

        public void ModifyTurnDraw(BattleContext ctx, in HookSource src, ref int count)
        {
            if (ExtraDraw != 0 && Active(ctx)) count += ExtraDraw;
        }

        public void ModifyTurnEnergy(BattleContext ctx, in HookSource src, ref int amount)
        {
            if (ExtraEnergy != 0 && Active(ctx)) amount += ExtraEnergy;
        }
    }

    // ================================================================= 出牌规则

    /// <summary>
    /// 每回合第一张指定类型的牌费用 -N。「笔尖」用这个。
    /// ★ 判断依据是 <c>BattleContext.CardsPlayedThisTurn / AttacksPlayedThisTurn</c>，
    ///   行为类自身不记任何状态。
    /// </summary>
    [Serializable]
    public class FirstCardCostReductionBehaviour : StatusBehaviour, ICardPlayHook
    {
        public CardType CardType = CardType.Attack;

        [Tooltip("减少的费用")]
        public int Reduction = 1;

        public override int Order => HookOrder.AddFlat;

        public void ModifyCardCost(BattleContext ctx, in HookSource src, CardInstance card, ref int cost)
        {
            if (card == null || card.Type != CardType) return;
            if (!IsFirstOfType(ctx)) return;
            cost -= Reduction;
        }

        private bool IsFirstOfType(BattleContext ctx)
            => CardType == CardType.Attack ? ctx.AttacksPlayedThisTurn == 0 : ctx.CardsPlayedThisTurn == 0;

        public void OnCardPlayed(BattleContext ctx, in HookSource src, CardInstance card) { }
        public void OnCardDrawn(BattleContext ctx, in HookSource src, CardInstance card) { }
        public void OnCardDiscarded(BattleContext ctx, in HookSource src, CardInstance card) { }
    }

    /// <summary>每回合第一张指定类型的牌额外结算一次（回响）。演示 ICardFlowHook.PreCardPlay。</summary>
    [Serializable]
    public class EchoFirstCardBehaviour : StatusBehaviour, ICardFlowHook
    {
        public CardType CardType = CardType.Attack;

        public override int Order => HookOrder.AddFlat;

        public void PreCardPlay(BattleContext ctx, in HookSource src, CardInstance card,
                                ref bool cancel, ref int extraPlays)
        {
            if (card == null || card.Type != CardType) return;

            bool first = CardType == CardType.Attack
                ? ctx.AttacksPlayedThisTurn == 0
                : ctx.CardsPlayedThisTurn == 0;

            if (first) extraPlays += 1;
        }

        public void ModifyCardDestination(BattleContext ctx, in HookSource src, CardInstance card, ref CardPile pile) { }
    }

    /// <summary>
    /// 改变某类牌打出后的归宿。「回收器：技能牌洗回抽牌堆」用这个。
    /// 演示 ICardFlowHook.ModifyCardDestination——这是原本硬编码在 BattleController 里的 if/else 链。
    /// </summary>
    [Serializable]
    public class CardDestinationBehaviour : StatusBehaviour, ICardFlowHook
    {
        public CardType CardType = CardType.Skill;
        public CardPile Destination = CardPile.Draw;

        public override int Order => HookOrder.AddFlat;

        public void PreCardPlay(BattleContext ctx, in HookSource src, CardInstance card,
                                ref bool cancel, ref int extraPlays) { }

        public void ModifyCardDestination(BattleContext ctx, in HookSource src, CardInstance card, ref CardPile pile)
        {
            if (card == null || card.Type != CardType) return;

            // 已经被判定为消耗（虚无 / 临时卡 / 带消耗关键字）的牌不改写，否则临时卡会永远留在牌库里
            if (pile == CardPile.Exhaust) return;

            pile = Destination;
        }
    }

    // ================================================================= 计数型

    /// <summary>
    /// 每打出 N 张指定类型的牌，触发一次回血。演示 <see cref="RelicInstance.Counter"/> 的用法：
    /// 需要跨回合累计、又不适合做成状态的数据放在这里。
    /// </summary>
    [Serializable]
    public class EveryNCardsHealBehaviour : StatusBehaviour, ICardPlayHook, IBattleFlowHook
    {
        public CardType CardType = CardType.Skill;
        public int Threshold = 3;
        public int HealAmount = 2;

        public override int Order => HookOrder.AddFlat;

        public void OnCardPlayed(BattleContext ctx, in HookSource src, CardInstance card)
        {
            var relic = src.Relic;
            if (relic == null || card == null || card.Type != CardType || Threshold <= 0) return;

            relic.Counter++;
            if (relic.Counter < Threshold) return;

            relic.Counter = 0;
            ctx.Post(BattleEventType.Message, 0, ctx.Player.Uid, 0, relic.Id);
            ctx.Player.Heal(ctx, HealAmount);
        }

        // 每场战斗重新计数，避免上一场剩下的进度带到下一场
        public void OnBattleStart(BattleContext ctx, in HookSource src)
        {
            if (src.Relic != null) src.Relic.Counter = 0;
        }

        public void OnBattleEnd(BattleContext ctx, in HookSource src, bool victory) { }

        public void OnCardDrawn(BattleContext ctx, in HookSource src, CardInstance card) { }
        public void OnCardDiscarded(BattleContext ctx, in HookSource src, CardInstance card) { }
        public void ModifyCardCost(BattleContext ctx, in HookSource src, CardInstance card, ref int cost) { }
    }
}
