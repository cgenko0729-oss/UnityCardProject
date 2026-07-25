using Game.Battle;
using Game.Effects;

namespace Game.Cards
{
    /// <summary>
    /// 运行时卡牌。可克隆、可升级、可临时改费。
    /// 借鉴 Chrono Ark 的 Skill：卡是普通 C# 对象，不是资产也不是 MonoBehaviour。
    /// </summary>
    public class CardInstance
    {
        /// <summary>
        /// 一局游戏内唯一编号。★ 由 <see cref="Core.RunContext.NextCardUid"/> 分配，不用 static 计数器：
        /// static 计数器会让存档读回来的卡与新生成的卡撞号（阶段 5 的存档必然撞上）。
        /// </summary>
        public readonly int Uid;
        public CardDefinition Def { get; private set; }
        public int UpgradeLevel { get; private set; }

        /// <summary>本场战斗内的费用增量（负数为减费）。战斗结束清零。</summary>
        public int BattleCostDelta;

        /// <summary>本回合内的费用增量。回合结束清零。</summary>
        public int TurnCostDelta;

        /// <summary>运行时追加的关键字（例如临时获得「消耗」）。</summary>
        public CardKeyword ExtraKeywords;

        /// <summary>true 表示战斗中临时生成的卡，战斗结束丢弃，不进牌库。</summary>
        public bool IsTemporary;

        public CardInstance(int uid, CardDefinition def, bool temporary = false)
        {
            Uid = uid;
            Def = def;
            IsTemporary = temporary;
        }

        public string Id => Def != null ? Def.Id : null;
        public string DisplayName => Def != null ? Def.DisplayName : "<null>";
        public CardType Type => Def != null ? Def.Type : CardType.Skill;

        public bool HasKeyword(CardKeyword k)
            => (Def != null && Def.HasKeyword(k)) || (ExtraKeywords & k) != 0;

        public bool CanUpgrade => Def != null && Def.UpgradedVersion != null;

        public void Upgrade()
        {
            if (!CanUpgrade) return;
            Def = Def.UpgradedVersion;
            UpgradeLevel++;
        }

        /// <summary>复制一张牌。默认复制出来的是临时卡。uid 由调用方从 RunContext 取。</summary>
        public CardInstance Clone(int uid, bool temporary = true)
        {
            return new CardInstance(uid, Def, temporary)
            {
                UpgradeLevel = UpgradeLevel,
                BattleCostDelta = BattleCostDelta,
                TurnCostDelta = TurnCostDelta,
                ExtraKeywords = ExtraKeywords,
            };
        }

        /// <summary>实际费用，含状态/遗物 Hook 修正。X 费卡返回 -1，不可打出返回 int.MaxValue。</summary>
        public int GetCost(BattleContext ctx)
        {
            if (Def == null) return int.MaxValue;
            if (Def.CostMode == CostMode.X) return -1;
            if (Def.CostMode == CostMode.Unplayable) return int.MaxValue;

            int cost = Def.Cost + BattleCostDelta + TurnCostDelta;
            if (ctx != null)
            {
                using (var hooks = ctx.Collect<ICardPlayHook>())
                {
                    for (int i = 0; i < hooks.Count; i++)
                        hooks[i].Impl.ModifyCardCost(ctx, hooks[i].Src, this, ref cost);
                }
            }
            return cost < 0 ? 0 : cost;
        }

        /// <summary>用于 UI 显示的费用文本。</summary>
        public string GetCostText(BattleContext ctx)
        {
            if (Def == null) return "?";
            if (Def.CostMode == CostMode.X) return "X";
            if (Def.CostMode == CostMode.Unplayable) return "-";
            return GetCost(ctx).ToString();
        }

        /// <summary>
        /// 生成动态描述。把 DescriptionTemplate 里的 {N} 替换成第 N 个效果的 Describe 结果。
        /// ctx 可为 null（战斗外，例如牌库界面），此时用静态数值。
        /// </summary>
        public string GetDescription(BattleContext ctx, Units.BattleUnit source = null, Units.BattleUnit target = null)
        {
            if (Def == null) return string.Empty;
            string template = Def.DescriptionTemplate;
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (Def.Effects == null || Def.Effects.Count == 0) return template;

            var effCtx = new EffectContext
            {
                Battle = ctx,
                Source = source ?? ctx?.Player,
                ChosenTarget = target,
                Card = this,
                PreviewMode = true,   // 描述每帧都会算，绝不能消耗随机流
            };

            return EffectDescription.Format(template, Def.Effects, effCtx);
        }

        public void OnBattleEnd()
        {
            BattleCostDelta = 0;
            TurnCostDelta = 0;
            ExtraKeywords = CardKeyword.None;
        }

        public void OnTurnEnd() => TurnCostDelta = 0;

        public override string ToString() => $"{DisplayName}#{Uid}";
    }
}
