using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game.Battle;
using Game.Cards;
using Game.Effects;
using Game.Effects.Impl;
using Game.Statuses;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.CardTables
{
    /// <summary>
    /// 卡表序列化的自检。
    ///
    /// ★★ 这是**替代单元测试的东西**，不是可选的调试工具。
    ///   <c>Game.Tests.EditMode</c> 只引用 <c>Game.Runtime</c>，够不到 <c>Game.Editor</c>
    ///   （与 <c>Game.UI</c> 同一个覆盖盲区，铁律 52 提过），
    ///   而这一层恰好是「坏了不报错、只是产出错的卡」的地方。
    ///
    /// <para><b>核心断言是幂等</b>：<c>ToJson → FromJson → ToJson</c> 两次输出必须逐字节相同。
    ///   一句断言同时抓住四类 bug：转换器读写不对称、字段顺序不稳定、
    ///   「与默认值相同就省略」判断错误、以及未知键处理错误。
    ///   任何一条坏掉，表在反复导入导出中会自己改变形状，git diff 从此全是噪音。</para>
    ///
    /// <para><b>覆盖率是自动的</b>：待测效果实例由 <see cref="EffectKinds.All"/> 反射生成，
    ///   并用「与默认值不同的值」填满每一个字段。于是新增一个效果类
    ///   （或给已有效果类加一个字段）**自动**进入自检范围，不需要有人记得来这里补一行。
    ///   这是本工具反射驱动那条设计的必然结果——注册表会漏，反射不会。</para>
    /// </summary>
    public static class CardTableSelfCheck
    {
        [MenuItem("Tools/卡牌游戏/8. 卡表自检", priority = 8)]
        public static void Run()
        {
            var report = new StringBuilder();
            int failures = Check(report);

            if (failures == 0)
            {
                Debug.Log($"[CardTable] 自检通过。\n{report}");
                EditorUtility.DisplayDialog("卡表自检通过", report.ToString(), "好");
            }
            else
            {
                Debug.LogError($"[CardTable] 自检发现 {failures} 个问题：\n{report}");
                EditorUtility.DisplayDialog("卡表自检失败", $"{failures} 个问题，详情见 Console。", "好");
            }
        }

        public static void RunBatch()
        {
            var report = new StringBuilder();
            int failures = Check(report);
            Debug.Log($"[CardTable] 自检结束：{failures} 个问题。\n{report}");
            if (failures > 0) EditorApplication.Exit(1);
        }

        private static int Check(StringBuilder report)
        {
            int failures = 0;

            AssetIndex.Invalidate();

            failures += CheckEveryEffectRoundTrips(report);
            failures += CheckNestedTreesRoundTrip(report);
            failures += CheckKindKeyNeverCollidesWithAField(report);
            failures += CheckUnknownKindIsRejected(report);
            failures += CheckUnknownFieldIsRejected(report);
            failures += CheckOmittedTargetKeepsConstructorDefault(report);

            return failures;
        }

        // ============================================================ 1. 全效果往返

        /// <summary>
        /// 每个效果类各造一个「所有字段都非默认」的实例，逐个做往返幂等断言。
        /// 逐个而不是打包成一张卡：某一个类坏了要能立刻说出是哪一个。
        /// </summary>
        private static int CheckEveryEffectRoundTrips(StringBuilder report)
        {
            int failures = 0;
            int covered = 0;

            foreach (var spec in EffectKinds.All)
            {
                CardEffect effect;
                try
                {
                    effect = (CardEffect)Activator.CreateInstance(spec.Type);
                    Fill(effect, depth: 0);
                }
                catch (Exception e)
                {
                    report.AppendLine($"[失败] {spec.ShortName}: 构造测试实例时抛异常：{e.Message}");
                    failures++;
                    continue;
                }

                var table = new CardTable();
                table.Cards.Add(new CardRow
                {
                    Id = "selfcheck_" + spec.ShortName,
                    Name = "自检",
                    Desc = "{0}",
                    Effects = new List<CardEffect> { effect },
                });

                if (!RoundTripIsStable(table, spec.ShortName, report)) failures++;
                covered++;
            }

            report.AppendLine($"[通过] {covered} 个效果类各自往返幂等。");
            return failures;
        }

        // ============================================================ 2. 嵌套效果树

        /// <summary>
        /// 手写几棵真实形状的树。自动填充覆盖不到「组合子里装组合子」
        /// 和「同一棵树里混用多种 EffectValue / TargetSelector 形态」这两件事。
        /// </summary>
        private static int CheckNestedTreesRoundTrip(StringBuilder report)
        {
            var status = FirstStatus();

            var table = new CardTable();

            // 一层组合子 + 缩放数值 + 带附加字段的目标
            table.Cards.Add(new CardRow
            {
                Id = "selfcheck_nested_repeat",
                Name = "自检-重复",
                Cost = 2,
                Type = CardType.Attack,
                Rarity = CardRarity.Uncommon,
                Target = CardTargetKind.SingleEnemy,
                Keywords = new List<string> { "Exhaust", "Innate" },
                Desc = "重复 {0} 次。",
                Effects = new List<CardEffect>
                {
                    new RepeatEffect
                    {
                        Times = EffectValue.Flat(3),
                        Effects = new List<CardEffect>
                        {
                            new DamageEffect
                            {
                                Target = TargetSelector.Chosen,
                                Amount = new EffectValue
                                {
                                    Base = 3,
                                    Scale = ValueScale.PerStatusStackOnSelf,
                                    ScaleId = "strength",
                                    PerUnit = 2,
                                    Min = 1,
                                    Max = 30,
                                },
                            },
                            new BlockEffect
                            {
                                Target = TargetSelector.SelfOnly,
                                Amount = EffectValue.Flat(2),
                            },
                        },
                    },
                },
            });

            // 组合子套组合子（表能装，阶段 3 的窗口只画一层，但序列化层必须支持）
            table.Cards.Add(new CardRow
            {
                Id = "selfcheck_nested_deep",
                Name = "自检-深嵌套",
                Type = CardType.Skill,
                Target = CardTargetKind.None,
                Desc = "{0}",
                Effects = new List<CardEffect>
                {
                    new ConditionalEffect
                    {
                        Condition = new EffectCondition
                        {
                            Kind = ConditionKind.SelfHasStatus,
                            Id = "strength",
                            Value = 2,
                            Invert = true,
                        },
                        Then = new List<CardEffect>
                        {
                            new RepeatEffect
                            {
                                Times = EffectValue.Flat(2),
                                Effects = new List<CardEffect>
                                {
                                    new DrawEffect { Count = EffectValue.Flat(1) },
                                },
                            },
                        },
                        Else = new List<CardEffect>
                        {
                            new RandomPickEffect
                            {
                                PickCount = 2,
                                AllowDuplicates = true,
                                Options = new List<RandomPickEffect.Option>
                                {
                                    new RandomPickEffect.Option
                                    {
                                        Note = "甲",
                                        Weight = 30,
                                        Effect = new BlockEffect { Amount = EffectValue.Flat(4) },
                                    },
                                    new RandomPickEffect.Option
                                    {
                                        Note = "能量",
                                        Weight = 70,
                                        Effect = new EnergyEffect { Amount = EffectValue.Flat(1) },
                                    },
                                },
                            },
                        },
                    },
                },
            });

            // 目标选择器的对象形态 + 内容资产引用 + 内嵌升级版
            var upgradeCard = new CardRow
            {
                Id = "selfcheck_refs",
                Name = "自检-引用",
                Cost = 1,
                Type = CardType.Skill,
                Target = CardTargetKind.None,
                Desc = "{0}",
                Effects = new List<CardEffect>
                {
                    new DamageEffect
                    {
                        Target = new TargetSelector
                        {
                            Kind = TargetKind.RandomEnemy,
                            Count = 2,
                            AllowDuplicates = true,
                            ExcludeSelf = true,
                            RequireStatusId = status != null ? status.Id : null,
                        },
                        Amount = EffectValue.Flat(5),
                        Times = EffectValue.Flat(2),
                        IgnoreBlock = true,
                    },
                },
                Upgrade = new UpgradeRow
                {
                    Effects = new List<CardEffect>
                    {
                        new DamageEffect
                        {
                            Target = TargetSelector.AllEnemies,
                            Amount = EffectValue.Flat(8),
                        },
                    },
                },
            };

            if (status != null)
            {
                upgradeCard.Effects.Add(new ApplyStatusEffect
                {
                    Target = TargetSelector.SelfOnly,
                    Status = status,
                    Stacks = EffectValue.Flat(2),
                });
                upgradeCard.Desc = "{0}{1}";
            }
            else
            {
                report.AppendLine("[跳过] 工程里没有 StatusDefinition，" +
                                  "本次自检未覆盖「内容资产引用按 Id 往返」。");
            }

            table.Cards.Add(upgradeCard);

            int failures = RoundTripIsStable(table, "嵌套效果树", report) ? 0 : 1;
            if (failures == 0) report.AppendLine("[通过] 嵌套效果树 / 资产引用 / 内嵌升级版往返幂等。");
            return failures;
        }

        // ============================================================ 3-4. 负面用例

        /// <summary>未知判别符必须报错。这是「反射推导短名」这条设计的唯一失败模式，必须响。</summary>
        private static int CheckUnknownKindIsRejected(StringBuilder report)
        {
            string json = @"{ ""version"": 1, ""cards"": [ { ""id"": ""x"", ""name"": ""x"",
                ""effects"": [ { """ + EffectKinds.KindKey + @""": ""nosuchkind"" } ] } ] }";

            if (Throws(json, out string message))
            {
                report.AppendLine($"[通过] 未知 {EffectKinds.KindKey} 被拒绝：{message}");
                return 0;
            }

            report.AppendLine($"[失败] 未知 {EffectKinds.KindKey} 没有报错——" +
                              $"表里写错效果名会静默产出一张空卡。");
            return 1;
        }

        /// <summary>
        /// 判别符不得与任何字段的 JSON 名撞车 —— 这是一条**回归用例**。
        ///
        /// ★ 判别符原本叫 <c>kind</c>，而 <c>DamageEffect.Kind</c>（<c>DamageKind</c>，
        ///   正是铁律 20 点名的那个字段）camelCase 之后也是 <c>kind</c>。
        ///   于是「造成 X 点非攻击伤害」的卡被写成 <c>{"kind":"damage","kind":"Loss"}</c>，
        ///   读回来判别符变成 <c>Loss</c>。而 <c>DamageKind.Attack</c> 是默认值会被省略，
        ///   **所以当时 15 个效果类里只有这一个、且只在这一种取值下会坏**。
        ///   靠肉眼看 JSON 是发现不了的，只有往返断言会响。
        /// </summary>
        private static int CheckKindKeyNeverCollidesWithAField(StringBuilder report)
        {
            foreach (var spec in EffectKinds.All)
            {
                foreach (var f in spec.Fields)
                {
                    if (spec.JsonNameOf(f) != EffectKinds.KindKey) continue;

                    report.AppendLine(
                        $"[失败] {spec.Type.Name}.{f.Name} 的 JSON 名与判别符" +
                        $"「{EffectKinds.KindKey}」相同，该效果的类型会在读取时被这个字段覆盖。");
                    return 1;
                }
            }

            // 顺带做一次端到端断言：带非默认 DamageKind 的效果必须能原样往返。
            var table = new CardTable();
            table.Cards.Add(new CardRow
            {
                Id = "selfcheck_damagekind",
                Name = "自检",
                Target = CardTargetKind.None,
                Desc = "{0}",
                Effects = new List<CardEffect>
                {
                    new DamageEffect
                    {
                        Target = TargetSelector.SelfOnly,
                        Amount = EffectValue.Flat(3),
                        Kind = DamageKind.Loss,
                    },
                },
            });

            try
            {
                var back = CardTableJson.FromJson(CardTableJson.ToJson(table));
                var dmg = back.Cards[0].Effects[0] as DamageEffect;

                if (dmg == null || dmg.Kind != DamageKind.Loss)
                {
                    report.AppendLine($"[失败] 非攻击伤害往返后变成了 " +
                                      $"{(dmg == null ? "null" : dmg.Kind.ToString())}。");
                    return 1;
                }
            }
            catch (Exception e)
            {
                report.AppendLine($"[失败] 非攻击伤害往返时抛异常：{e.Message}");
                return 1;
            }

            report.AppendLine($"[通过] 判别符「{EffectKinds.KindKey}」与全部字段名无冲突，" +
                              $"非攻击伤害往返正确。");
            return 0;
        }

        /// <summary>
        /// 未知字段必须报错。这条对应 <c>MissingMemberHandling.Error</c>：
        /// 把 <c>rarity</c> 拼成 <c>rariry</c> 而不报错的话，
        /// 会得到一张稀有度悄悄变成 Common、照常进奖励池的卡。
        /// </summary>
        private static int CheckUnknownFieldIsRejected(StringBuilder report)
        {
            const string json = @"{ ""version"": 1, ""cards"": [
                { ""id"": ""x"", ""name"": ""x"", ""rariry"": ""Rare"" } ] }";

            if (Throws(json, out string message))
            {
                report.AppendLine($"[通过] 未知字段被拒绝：{message}");
                return 0;
            }

            report.AppendLine("[失败] 未知字段没有报错——拼错键名会静默丢掉那个设置。");
            return 1;
        }

        /// <summary>
        /// 省略 <c>target</c> 必须保持构造函数默认值。
        ///
        /// ★ 这条单独立一个用例，因为它错起来特别隐蔽：<c>BlockEffect()</c> 把 Target 设成
        ///   SelfOnly，若反序列化跳过了构造函数（比如有人改用 FormatterServices），
        ///   所有省略 target 的效果会变成 <c>Chosen</c>——
        ///   「自己获得护甲」的牌会变成「必须点一个敌人才能打出」。
        /// </summary>
        private static int CheckOmittedTargetKeepsConstructorDefault(StringBuilder report)
        {
            string json = @"{ ""version"": 1, ""cards"": [ { ""id"": ""x"", ""name"": ""x"",
                ""effects"": [ { """ + EffectKinds.KindKey + @""": ""block"", ""amount"": 5 } ] } ] }";

            try
            {
                var table = CardTableJson.FromJson(json);
                var effect = table.Cards[0].Effects[0];

                var expected = new BlockEffect().Target.Kind;
                var actual = effect.Target.Kind;

                if (actual != expected)
                {
                    report.AppendLine($"[失败] 省略 target 后 BlockEffect 的目标是 {actual}，" +
                                      $"应为构造函数默认值 {expected}。");
                    return 1;
                }

                report.AppendLine($"[通过] 省略的字段保持构造函数默认值（block.target = {actual}）。");
                return 0;
            }
            catch (Exception e)
            {
                report.AppendLine($"[失败] 读取最小效果时抛异常：{e.Message}");
                return 1;
            }
        }

        // ============================================================ 工具

        /// <summary>ToJson → FromJson → ToJson 必须逐字节相同。</summary>
        private static bool RoundTripIsStable(CardTable table, string label, StringBuilder report)
        {
            try
            {
                string first = CardTableJson.ToJson(table);
                string second = CardTableJson.ToJson(CardTableJson.FromJson(first));

                if (first == second) return true;

                report.AppendLine($"[失败] {label}: 往返不幂等。");
                report.AppendLine(FirstDifference(first, second));
                return false;
            }
            catch (Exception e)
            {
                report.AppendLine($"[失败] {label}: 往返时抛异常：{e.Message}");
                return false;
            }
        }

        private static string FirstDifference(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] == b[i]) continue;

                int from = Math.Max(0, i - 60);
                return $"  第一次：…{a.Substring(from, Math.Min(140, a.Length - from))}\n" +
                       $"  第二次：…{b.Substring(from, Math.Min(140, b.Length - from))}";
            }

            return $"  长度不同：{a.Length} vs {b.Length}";
        }

        private static bool Throws(string json, out string message)
        {
            try
            {
                CardTableJson.FromJson(json);
                message = null;
                return false;
            }
            catch (Exception e)
            {
                message = e.Message;
                return true;
            }
        }

        private static StatusDefinition FirstStatus()
        {
            foreach (var id in AssetIndex.KnownIds(typeof(StatusDefinition)))
                return AssetIndex.Find(typeof(StatusDefinition), id) as StatusDefinition;
            return null;
        }

        // ------------------------------------------------------------ 自动填充

        /// <summary>
        /// 把一个效果实例的每个字段都填成「与默认值不同」的值，让往返断言真的测到每个字段。
        /// 全默认的实例序列化出来只有 <c>{"kind":"x"}</c>，那等于什么都没测。
        /// </summary>
        private static void Fill(object target, int depth)
        {
            if (target == null || depth > 2) return;

            var spec = target is CardEffect ? EffectKinds.ForType(target.GetType()) : null;
            var fields = spec != null
                ? spec.Fields
                : target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var f in fields)
            {
                object v = MakeDistinctValue(f.FieldType, depth);
                if (v != null) f.SetValue(target, v);
            }
        }

        private static object MakeDistinctValue(Type t, int depth)
        {
            if (t == typeof(int)) return 7;
            if (t == typeof(bool)) return true;
            if (t == typeof(string)) return "selfcheck";

            if (t == typeof(EffectValue))
            {
                return new EffectValue
                {
                    Base = 3, Scale = ValueScale.PerCardInHand, PerUnit = 2, Min = 1, Max = 9,
                };
            }

            if (t == typeof(TargetSelector))
            {
                return new TargetSelector
                {
                    Kind = TargetKind.AllEnemies, Count = 2, AllowDuplicates = true, ExcludeSelf = true,
                };
            }

            if (t == typeof(EffectCondition))
            {
                return new EffectCondition
                {
                    Kind = ConditionKind.SelfHasStatus, Id = "strength", Value = 2, Invert = true,
                };
            }

            if (t.IsEnum)
            {
                // 取最后一个枚举值：它必然不是 0，于是一定会被写进 JSON。
                var values = Enum.GetValues(t);
                return values.Length > 0 ? values.GetValue(values.Length - 1) : null;
            }

            if (typeof(ScriptableObject).IsAssignableFrom(t))
            {
                foreach (var id in AssetIndex.KnownIds(t))
                    return AssetIndex.Find(t, id);
                return null;   // 工程里没有这类资产，跳过这个字段
            }

            if (typeof(CardEffect).IsAssignableFrom(t))
            {
                return depth >= 2 ? null : new DrawEffect { Count = EffectValue.Flat(2) };
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (depth >= 2) return null;

                var elem = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t);

                if (typeof(CardEffect).IsAssignableFrom(elem))
                {
                    list.Add(new BlockEffect { Amount = EffectValue.Flat(3) });
                }
                else if (elem == typeof(string))
                {
                    list.Add("selfcheck");
                }
                else if (!elem.IsAbstract && elem.GetConstructor(Type.EmptyTypes) != null)
                {
                    var item = Activator.CreateInstance(elem);
                    Fill(item, depth + 1);
                    list.Add(item);
                }

                return list;
            }

            return null;
        }
    }
}
