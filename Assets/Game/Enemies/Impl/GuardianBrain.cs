using Game.Battle;

namespace Game.Enemies.Impl
{
    /// <summary>
    /// 多阶段 Boss「守卫者」的自定义 AI。
    /// 演示：进入二阶段时清负面 + 加力量；二阶段玩家护甲高时必用大招。
    ///
    /// ★ 约定：自定义 Brain 里引用 Actions 下标时必须定义常量并注明对应的 Action 名字，
    ///   避免 Chrono Ark 里 BChar.Skills[3] 那种改顺序就全乱的魔数问题。
    /// </summary>
    public class GuardianBrain : EnemyBrain
    {
        private const int ACTION_DESTROY = 4;   // 对应 Actions[4] "毁灭"

        protected override void OnPhaseChanged(BattleContext ctx, int newPhase)
        {
            base.OnPhaseChanged(ctx, newPhase);
            if (newPhase != 1) return;

            Unit.RemoveStatus(ctx, "weak");
            Unit.RemoveStatus(ctx, "vulnerable");

            var db = ctx.Run != null ? ctx.Run.Database : null;
            var strength = db != null ? db.GetStatus("strength") : null;
            if (strength != null) Unit.AddStatus(ctx, strength, 5, Unit);

            ctx.Post(BattleEventType.Message, Unit.Uid, Unit.Uid, 0, "GuardianEnrage");
        }

        protected override int ChooseAction(BattleContext ctx)
        {
            // 二阶段：玩家护甲 >= 20 时必定使用「毁灭」
            if (Phase == 1 && ctx.Player != null && ctx.Player.Block >= 20 && ACTION_DESTROY < Def.Actions.Count)
                return ACTION_DESTROY;

            return base.ChooseAction(ctx);
        }
    }
}
