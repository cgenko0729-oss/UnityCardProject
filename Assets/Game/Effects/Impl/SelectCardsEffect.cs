using System;
using Game.Battle;
using Game.Cards;

namespace Game.Effects.Impl
{
    /// <summary>
    /// 从某个牌堆里选 N 张牌并处置它们。
    /// 「选择弃牌 / 消耗 / 保留 / 复制 / 回牌顶」全部是这一个效果的不同配置——
    /// 五种处置的「怎么选」完全相同，只有最后一步不同，拆成五个类等于把请求构造抄五遍。
    ///
    /// <para>★ 这是全项目**唯一会让结算挂起**的效果。挂起时后续效果会等玩家选完再跑，
    /// 因此「选 2 张弃掉，然后抽 2 张」的先后顺序是对的。
    /// 无 UI 的场合（EditMode 测试 / 自动模拟器 / 敌人回合）由
    /// <see cref="BattleContext.Selector"/> 当场随机作答，不挂起。</para>
    /// </summary>
    [Serializable]
    public class SelectCardsEffect : CardEffect
    {
        public CardPile Source = CardPile.Hand;

        public EffectValue Count = EffectValue.Flat(1);

        public CardSelectionAction Action = CardSelectionAction.Discard;

        /// <summary>候选不足时是否允许少选。false 表示不足就整个效果不生效。</summary>
        public bool AllowFewer = true;

        /// <summary>
        /// 是否允许玩家一张都不选。
        /// ★ 默认 false：代价通常已经在前面的效果里付掉了（能量、生命），
        ///   允许跳过等于白拿收益。只有「纯收益」的选牌才该开。
        /// </summary>
        public bool Cancellable;

        /// <summary>给玩家看的标题。留空则按 Action 自动生成。</summary>
        public string Prompt;

        public SelectCardsEffect()
        {
            Target = TargetSelector.NoTarget;
        }

        /// <summary>
        /// ★ 永远可施放。候选为空时让整张卡变灰是错的——
        /// 「弃一张牌，抽两张」在手牌只剩它自己的时候依然该能打。
        /// </summary>
        public override bool CanApply(EffectContext ctx) => true;

        public override void Apply(EffectContext ctx)
        {
            var battle = ctx.Battle;
            if (battle == null) return;

            // 预览路径（UI 每帧的可打性判断、卡牌描述）绝不能真的发起选牌：
            // 既会弹面板，也会消耗随机流。
            if (ctx.PreviewMode) return;

            int n = Count.Evaluate(ctx, ctx.Source);
            if (n <= 0) return;

            var req = new CardSelectionRequest
            {
                Source = Source,
                Count = n,
                AllowFewer = AllowFewer,
                Cancellable = Cancellable,
                Action = Action,
                Prompt = Prompt,
            };

            var action = Action;
            battle.RequestCardSelection(req, cards => CardSelectionOps.Apply(battle, action, cards));
        }

        public override string Describe(EffectContext ctx) => Count.Evaluate(ctx, ctx.Source).ToString();
    }
}
