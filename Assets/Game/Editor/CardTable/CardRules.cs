using System.Collections.Generic;
using Game.Cards;
using Game.Effects;

namespace Game.Editor.CardTables
{
    public enum CardIssueLevel
    {
        /// <summary>配错了，但游戏还能跑。对应 ContentValidator 的「警告」。</summary>
        Warning,

        /// <summary>必须修，导入会被拒绝。</summary>
        Error,
    }

    /// <summary>一条结构化的校验结果。带 <see cref="Field"/> 是为了（阶段 3）窗口能把红字画在对应控件旁边。</summary>
    public sealed class CardIssue
    {
        public CardIssueLevel Level;
        public string CardId;

        /// <summary>出问题的字段名（表里的 JSON 键），没有具体字段时为 null。</summary>
        public string Field;

        public string Message;

        public CardIssue(CardIssueLevel level, string cardId, string field, string message)
        {
            Level = level;
            CardId = cardId;
            Field = field;
            Message = message;
        }

        public override string ToString()
        {
            string tag = Level == CardIssueLevel.Error ? "错误" : "警告";
            string where = string.IsNullOrEmpty(Field) ? CardId : $"{CardId}.{Field}";
            return $"[{tag}] {where}: {Message}";
        }
    }

    /// <summary>
    /// 卡牌的内容规则，**全工程唯一一份**。
    ///
    /// ★★ 这些规则原本内联在 <c>ContentValidator.CheckCards</c> 里，形状是
    ///   「扫全库 + 往 StringBuilder 追加文本 + 返回一个计数」。提取到这里的唯一理由是
    ///   导入器和（阶段 3）编辑器窗口需要「校验一张卡 → 拿到结构化结果」。
    ///
    /// <para><b>刻意不在工具里另写一份校验。</b>两份规则一定会分叉，
    ///   而分叉的表现是「菜单 3 说没问题，窗口说有问题」——使用者从此不信任两者中的任何一个。
    ///   <c>ContentValidator.CheckCards</c> 现在只负责遍历资产和格式化输出，判断全部在这里。</para>
    ///
    /// <para>Editor 程序集没有测试覆盖（与 <c>Game.UI</c> 同一个盲区，铁律 52 提过），
    ///   所以判断逻辑一律收在这个纯函数类里，窗口只负责画。
    ///   这样没有测试的那一层不含任何判断。</para>
    /// </summary>
    public static class CardRules
    {
        /// <summary>
        /// 校验一张卡。返回空列表 = 没问题。
        ///
        /// <para>★ 这里的等级划分沿用工程既有口径（铁律 51）：本工程把「0 错误 0 警告」
        /// 当健康信号，所以只有真的配错才报。缺图、组合子内部数值没进描述这类
        /// 「正常的中间状态」一概不报——把它们记成警告等于当场废掉那个信号。</para>
        /// </summary>
        public static List<CardIssue> Validate(CardDefinition card)
        {
            var issues = new List<CardIssue>();
            if (card == null) return issues;

            string id = string.IsNullOrEmpty(card.Id) ? card.name : card.Id;

            if (string.IsNullOrEmpty(card.Id))
                issues.Add(new CardIssue(CardIssueLevel.Warning, id, "id", "Id 为空。"));

            // ---------------------------------------------------------- 效果
            if (card.Effects == null || card.Effects.Count == 0)
            {
                // ★ 状态牌 / 诅咒牌本来就该没有出牌效果——它们的作用就是堵手牌。
                //   同理，只有「留在手上的代价」的牌（灼烧）也不算配错。
                //   不放行这两类，校验器每次都会报一串假警告，真警告就没人看了。
                bool intentionallyEmpty = card.Type == CardType.Status
                                          || card.Type == CardType.Curse
                                          || card.HasInHandEndOfTurnEffects;

                if (!intentionallyEmpty)
                {
                    issues.Add(new CardIssue(CardIssueLevel.Warning, id, "effects",
                        "没有任何效果。"));
                }
            }
            else
            {
                for (int i = 0; i < card.Effects.Count; i++)
                {
                    if (card.Effects[i] == null)
                    {
                        issues.Add(new CardIssue(CardIssueLevel.Warning, id, "effects",
                            $"第 {i} 个效果为空（可能是类被重命名导致 [SerializeReference] 丢失引用）。"));
                    }
                }
            }

            // ---------------------------------------------------------- 描述模板
            if (!string.IsNullOrEmpty(card.DescriptionTemplate) && card.Effects != null)
            {
                for (int i = card.Effects.Count; i < 10; i++)
                {
                    if (card.DescriptionTemplate.Contains("{" + i + "}"))
                    {
                        issues.Add(new CardIssue(CardIssueLevel.Warning, id, "desc",
                            $"描述模板引用了 {{{i}}}，但只有 {card.Effects.Count} 个效果。"));
                        break;
                    }
                }
            }

            // ---------------------------------------------------------- 目标一致性
            //
            // ★★★ 这两条是**方向相反的两个错误**，严重度刻意不同。
            //
            //   运行时只有一处读卡牌级 TargetKind 的语义：
            //   `BattleController.NeedsTargetSelection` 里的 `== CardTargetKind.SingleEnemy`
            //   （CanPlayCard 的目标合法性检查也只在这个分支里）。
            //   也就是说 None / AllEnemies / Self / RandomEnemy 这四个取值**行为完全等价**，
            //   都只表示「不要让玩家点目标」。真正决定打谁的是每个效果自己的 target。
            //
            //   这个不对称让「卡牌级目标」这个字段看起来像在声明打击范围，实际不是——
            //   所以两个方向的配错都很自然，而后果完全不同。

            if (card.TargetKind == CardTargetKind.SingleEnemy && !UsesChosenTarget(card.Effects))
            {
                // 玩家被要求点一个敌人，然后那次点击被忽略。烦人，但卡的其它效果照常生效，
                // 所以是警告。
                issues.Add(new CardIssue(CardIssueLevel.Warning, id, "target",
                    "声明需要选择敌人，但没有任何效果使用 chosen——玩家会被要求点目标，" +
                    "而那个选择完全不会被用到。"));
            }

            if (card.TargetKind != CardTargetKind.SingleEnemy && UsesChosenTarget(card.Effects))
            {
                // ★ 这个方向是**错误**：出牌时 ChosenTarget 恒为 null，
                //   TargetResolver 解析 chosen 得到空集合，于是那些效果**静默命中 0 个目标**。
                //   表象是「卡打出去了、能量扣了、动画播了、伤害没有」，
                //   而卡上其它效果（护甲 / 抽牌）照常生效，所以看起来只有攻击那一半坏了——
                //   离根因（卡牌级目标不是 SingleEnemy）非常远。
                issues.Add(new CardIssue(CardIssueLevel.Error, id, "target",
                    $"有效果以 chosen 为目标，但卡牌级目标是 {card.TargetKind}。" +
                    $"运行时只有 SingleEnemy 会让玩家点目标，所以出牌时 chosen 恒为空，" +
                    $"那些效果会静默命中 0 个单位（卡打出去了、能量扣了、伤害没有）。" +
                    $"要么把卡牌级目标改成 SingleEnemy，" +
                    $"要么把效果目标改成 allEnemies / randomEnemy / self。"));
            }

            // 留在手上到回合末结算的效果**永远**没有 chosen——那个时机根本没有玩家点选。
            if (UsesChosenTarget(card.InHandEndOfTurnEffects))
            {
                issues.Add(new CardIssue(CardIssueLevel.Error, id, "inHandEndOfTurn",
                    "「留在手上到回合结束」的效果里有 chosen 目标。那个时机不存在玩家点选，" +
                    "所以它恒命中 0 个单位。改成 allEnemies / randomEnemy / self。"));
            }

            // ---------------------------------------------------------- 稀有度（铁律 14 / 21）
            //
            // ★ 这两条以前**没有任何地方检查**（CheckRewardPool 只数池子大小）。
            //   它们的共同后果是「升级版 / 诅咒牌混进战斗奖励三选一和商店」，
            //   而这件事在游戏里看起来完全正常——只是打三选一的时候偶尔出现「打击+」。
            if (!string.IsNullOrEmpty(card.Id)
                && card.Id.EndsWith("_plus", System.StringComparison.Ordinal)
                && card.Rarity != CardRarity.Special)
            {
                issues.Add(new CardIssue(CardIssueLevel.Error, id, "rarity",
                    $"Id 以 _plus 结尾（升级版）但稀有度是 {card.Rarity}，" +
                    $"它会和基础版一起出现在奖励三选一和商店里。必须是 Special（铁律 14）。"));
            }

            if ((card.Type == CardType.Status || card.Type == CardType.Curse)
                && card.Rarity != CardRarity.Special)
            {
                issues.Add(new CardIssue(CardIssueLevel.Error, id, "rarity",
                    $"{card.Type} 类型的牌稀有度是 {card.Rarity}，会被当成正常卡进入奖励池和商店。" +
                    $"必须是 Special（铁律 21）。"));
            }

            return issues;
        }

        /// <summary>
        /// 效果树里是否存在以 chosen 为目标的效果。
        ///
        /// ★ 必须递归进组合子：「重复 3 次造成 4 点伤害」的 ChosenTarget 藏在 RepeatEffect 里，
        ///   只看顶层会把这种完全正常的卡误报成配错（铁律 22）。
        ///
        /// <para>⚠ 新增第五种组合子时**必须**在这里加一个 case。
        /// 这是铁律 22 点名的几个「效果树递归入口」之一。</para>
        /// </summary>
        public static bool UsesChosenTarget(IReadOnlyList<CardEffect> effects)
        {
            if (effects == null) return false;

            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null) continue;
                if (e.Target.Kind == TargetKind.ChosenTarget) return true;

                switch (e)
                {
                    case Game.Effects.Impl.RepeatEffect rep when UsesChosenTarget(rep.Effects):
                        return true;
                    case Game.Effects.Impl.ConditionalEffect cond
                        when UsesChosenTarget(cond.Then) || UsesChosenTarget(cond.Else):
                        return true;
                    case Game.Effects.Impl.DelayedEffect del when UsesChosenTarget(del.Effects):
                        return true;
                    case Game.Effects.Impl.RandomPickEffect pick when PickUsesChosen(pick):
                        return true;
                }
            }
            return false;
        }

        private static bool PickUsesChosen(Game.Effects.Impl.RandomPickEffect pick)
        {
            if (pick.Options == null) return false;

            for (int i = 0; i < pick.Options.Count; i++)
            {
                var opt = pick.Options[i];
                if (opt?.Effect == null) continue;
                if (UsesChosenTarget(new[] { opt.Effect })) return true;
            }
            return false;
        }
    }
}
