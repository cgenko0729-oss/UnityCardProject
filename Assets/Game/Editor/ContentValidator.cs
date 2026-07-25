using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game.Cards;
using Game.Effects;
using Game.Enemies;
using Game.Statuses;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 架构规则的自动检查。对应设计文档第九部分 9.6 的「三重对策」之二。
    ///
    /// 最重要的一条：CardEffect / StatusBehaviour 的子类不得有「运行时缓存字段」。
    /// 因为这些对象被同一份配置的所有实例共享，写字段会造成跨实例串数据，
    /// 而且只在特定时序下出现，几乎无法排查（Monster Train 的 CardEffectDamage 就有这个问题）。
    ///
    /// 判定标准：private / protected 的非 readonly 实例字段，且没有 [SerializeField]。
    /// 这类字段唯一的用途就是缓存，正规参数一律是 public 序列化字段。
    /// </summary>
    public static class ContentValidator
    {
        // ============================================================ 资产枚举
        //
        // ★ 不能直接用 AssetDatabase.FindAssets("t:XXX")：那个 API 依赖搜索索引，
        //   在 -batchmode 下会稳定返回 0 个结果——校验器于是「全部通过」，
        //   而这种假通过比没有校验器更危险。这里做一层带回退的封装。

        private static List<T> LoadAll<T>() where T : ScriptableObject
        {
            var result = new List<T>();
            var seen = new HashSet<string>();

            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path)) continue;
                var so = AssetDatabase.LoadAssetAtPath<T>(path);
                if (so != null) result.Add(so);
            }

            if (result.Count > 0) return result;

            // 回退：直接扫目录。批处理模式下走这条路。
            // ★ 必须用 Application.dataPath 拼绝对路径——batchmode 下进程的工作目录
            //   不是工程根目录，写 "Assets/GameData" 会永远 Directory.Exists == false，
            //   校验器于是一个资产都扫不到却报告「全部通过」。
            string root = Application.dataPath;
            if (!System.IO.Directory.Exists(root)) return result;

            foreach (var file in System.IO.Directory.GetFiles(root, "*.asset",
                                                              System.IO.SearchOption.AllDirectories))
            {
                var full = file.Replace('\\', '/');
                int idx = full.LastIndexOf("/Assets/", StringComparison.Ordinal);
                string path = idx >= 0 ? full.Substring(idx + 1) : full;
                if (!seen.Add(path)) continue;

                var so = AssetDatabase.LoadAssetAtPath<T>(path);
                if (so != null) result.Add(so);
            }

            return result;
        }

        private static string PathOf(UnityEngine.Object o) => AssetDatabase.GetAssetPath(o);

        [MenuItem("Tools/卡牌游戏/3. 校验内容与架构规则", priority = 4)]
        public static void Validate()
        {
            var sb = new StringBuilder();
            int errors = Run(sb, out int warnings);

            if (errors == 0 && warnings == 0)
            {
                Debug.Log("[ContentValidator] 全部通过，没有发现问题。");
                EditorUtility.DisplayDialog("校验通过", "没有发现问题。", "好");
                return;
            }

            Debug.LogWarning($"[ContentValidator] {errors} 个错误，{warnings} 个警告：\n{sb}");
            EditorUtility.DisplayDialog("校验完成",
                $"{errors} 个错误，{warnings} 个警告。详情见 Console。", "好");
        }

        /// <summary>
        /// 命令行 / CI 用的入口：只写日志，不弹窗，有错误时退出码非 0。
        /// <c>Unity.exe -batchmode -quit -executeMethod Game.Editor.ContentValidator.ValidateBatch</c>
        /// </summary>
        public static void ValidateBatch()
        {
            var sb = new StringBuilder();
            int errors = Run(sb, out int warnings);

            Debug.Log($"[ContentValidator] 校验结束：{errors} 个错误，{warnings} 个警告。\n{sb}");

            if (errors > 0) EditorApplication.Exit(1);
        }

        /// <summary>跑完所有检查，返回错误数，警告数由 out 参数带出。</summary>
        private static int Run(StringBuilder sb, out int warnings)
        {
            int errors = 0;
            warnings = 0;

            errors += CheckStatelessTypes(typeof(CardEffect), sb);
            errors += CheckStatelessTypes(typeof(StatusBehaviour), sb);
            errors += CheckStatelessTypes(typeof(Game.RunEffects.RunEffect), sb);

            warnings += CheckCards(sb);
            warnings += CheckEnemies(sb);
            warnings += CheckRelics(sb);
            warnings += CheckPotions(sb);
            warnings += CheckEvents(sb);
            warnings += CheckRewardPool(sb);
            warnings += CheckDuplicateIds(sb);

            return errors;
        }

        // ============================================================ 无状态检查

        private static int CheckStatelessTypes(Type baseType, StringBuilder sb)
        {
            int errors = 0;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.IsAbstract || !baseType.IsAssignableFrom(t)) continue;

                    var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    foreach (var f in fields)
                    {
                        if (f.IsInitOnly) continue;                                   // readonly 安全
                        if (f.GetCustomAttribute<SerializeField>() != null) continue; // 序列化参数，安全
                        if (f.Name.Contains("k__BackingField")) continue;             // 自动属性

                        errors++;
                        sb.AppendLine($"[错误] {t.FullName} 有可变私有字段「{f.Name}」。" +
                                      $"这类对象被所有实例共享，禁止缓存运行时数据。" +
                                      $"请改成 readonly，或把数据放进 EffectContext / BattleContext。");
                    }
                }
            }

            return errors;
        }

        // ============================================================ 内容检查

        private static int CheckCards(StringBuilder sb)
        {
            int warnings = 0;

            foreach (var card in LoadAll<CardDefinition>())
            {
                var path = PathOf(card);

                if (string.IsNullOrEmpty(card.Id))
                {
                    warnings++; sb.AppendLine($"[警告] {path}: Id 为空。");
                }

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
                        warnings++; sb.AppendLine($"[警告] {card.Id}: 没有任何效果。");
                    }
                }
                else
                {
                    for (int i = 0; i < card.Effects.Count; i++)
                    {
                        if (card.Effects[i] == null)
                        {
                            warnings++;
                            sb.AppendLine($"[警告] {card.Id}: 第 {i} 个效果为空（可能是类被重命名导致 " +
                                          $"[SerializeReference] 丢失引用）。");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(card.DescriptionTemplate) && card.Effects != null)
                {
                    for (int i = card.Effects.Count; i < 10; i++)
                    {
                        if (card.DescriptionTemplate.Contains("{" + i + "}"))
                        {
                            warnings++;
                            sb.AppendLine($"[警告] {card.Id}: 描述模板引用了 {{{i}}}，但只有 {card.Effects.Count} 个效果。");
                            break;
                        }
                    }
                }

                if (card.TargetKind == CardTargetKind.SingleEnemy && HasNoChosenTarget(card))
                {
                    warnings++;
                    sb.AppendLine($"[警告] {card.Id}: 声明需要选择敌人，但没有任何效果使用 ChosenTarget。");
                }
            }

            return warnings;
        }

        /// <summary>
        /// ★ 必须递归进组合子：「重复 3 次造成 4 点伤害」的 ChosenTarget 藏在 RepeatEffect 里，
        ///   只看顶层会把这种完全正常的卡误报成配错。
        /// </summary>
        private static bool HasNoChosenTarget(CardDefinition card)
            => !UsesChosenTarget(card.Effects);

        private static int CheckEnemies(StringBuilder sb)
        {
            int warnings = 0;

            foreach (var e in LoadAll<EnemyDefinition>())
            {

                if (e.Actions == null || e.Actions.Count == 0)
                {
                    warnings++; sb.AppendLine($"[警告] {e.Id}: 没有任何行动。");
                    continue;
                }

                if (e.FixedSequence != null)
                {
                    for (int i = 0; i < e.FixedSequence.Count; i++)
                    {
                        if (e.FixedSequence[i] < 0 || e.FixedSequence[i] >= e.Actions.Count)
                        {
                            warnings++;
                            sb.AppendLine($"[警告] {e.Id}: FixedSequence[{i}] = {e.FixedSequence[i]} 越界。");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(e.CustomBrainType) && Type.GetType(e.CustomBrainType) == null)
                {
                    warnings++;
                    sb.AppendLine($"[警告] {e.Id}: 找不到自定义 Brain 类型「{e.CustomBrainType}」。");
                }

                bool anyWeight = false;
                for (int i = 0; i < e.Actions.Count; i++) if (e.Actions[i].Weight > 0) anyWeight = true;
                bool hasSequence = e.FixedSequence != null && e.FixedSequence.Count > 0 && e.LoopSequence;
                if (!anyWeight && !hasSequence)
                {
                    warnings++;
                    sb.AppendLine($"[警告] {e.Id}: 所有行动权重为 0 且没有循环序列，AI 只会一直用第一个行动。");
                }
            }

            return warnings;
        }

        // ============================================================ 阶段 4 新增的校验

        private static int CheckRelics(StringBuilder sb)
        {
            int warnings = 0;

            foreach (var r in LoadAll<Game.Relics.RelicDefinition>())
            {

                if (r.Behaviours == null || r.Behaviours.Count == 0)
                {
                    warnings++;
                    sb.AppendLine($"[警告] 遗物 {r.Id}: 没有任何行为，拿到手不会有任何效果。");
                    continue;
                }

                for (int i = 0; i < r.Behaviours.Count; i++)
                {
                    if (r.Behaviours[i] == null)
                    {
                        warnings++;
                        sb.AppendLine($"[警告] 遗物 {r.Id}: 第 {i} 个行为为空" +
                                      $"（多半是行为类被改名导致 [SerializeReference] 丢引用）。");
                    }
                }

                if (string.IsNullOrEmpty(r.Description))
                {
                    warnings++;
                    sb.AppendLine($"[警告] 遗物 {r.Id}: 没有描述，玩家无从知道它做了什么。");
                }
            }

            return warnings;
        }

        private static int CheckPotions(StringBuilder sb)
        {
            int warnings = 0;

            foreach (var p in LoadAll<Game.Potions.PotionDefinition>())
            {
                if (string.IsNullOrEmpty(p.Id))
                {
                    warnings++;
                    sb.AppendLine($"[警告] 药水 {p.name}: 没有 Id。");
                    continue;
                }

                if (p.Effects == null || p.Effects.Count == 0)
                {
                    warnings++;
                    sb.AppendLine($"[警告] 药水 {p.Id}: 没有任何效果，喝了不会发生任何事。");
                    continue;
                }

                for (int i = 0; i < p.Effects.Count; i++)
                {
                    if (p.Effects[i] == null)
                    {
                        warnings++;
                        sb.AppendLine($"[警告] 药水 {p.Id}: 第 {i} 个效果为空" +
                                      $"（多半是效果类被改名导致 [SerializeReference] 丢引用）。");
                    }
                }

                if (string.IsNullOrEmpty(p.DescriptionTemplate))
                {
                    warnings++;
                    sb.AppendLine($"[警告] 药水 {p.Id}: 没有描述模板，玩家无从知道它做了什么。");
                }

                // ★ 与卡牌同一条规则：声明要选目标却没有任何效果打到 Chosen，
                //   玩家会被要求点一个敌人然后发现点了没用。
                if (p.TargetKind == CardTargetKind.SingleEnemy && !UsesChosenTarget(p.Effects))
                {
                    warnings++;
                    sb.AppendLine($"[警告] 药水 {p.Id}: 声明了 SingleEnemy，" +
                                  $"但没有任何效果以 Chosen 为目标——玩家的点选会被忽略。");
                }

                if (p.TargetKind != CardTargetKind.SingleEnemy && p.TargetKind != CardTargetKind.None)
                {
                    warnings++;
                    sb.AppendLine($"[警告] 药水 {p.Id}: TargetKind 只支持 None 与 SingleEnemy，" +
                                  $"当前为 {p.TargetKind}。");
                }
            }

            return warnings;
        }

        /// <summary>效果树里是否存在以 Chosen 为目标的效果（递归进组合子）。</summary>
        private static bool UsesChosenTarget(System.Collections.Generic.IReadOnlyList<CardEffect> effects)
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

        private static int CheckEvents(StringBuilder sb)
        {
            int warnings = 0;

            foreach (var e in LoadAll<Game.Events.EventDefinition>())
            {

                if (e.Options == null || e.Options.Count == 0)
                {
                    warnings++;
                    sb.AppendLine($"[警告] 事件 {e.Id}: 没有任何选项，玩家会卡在这个界面出不去。");
                    continue;
                }

                // ★ 至少要有一个「无条件可选」的出口，否则条件全不满足时玩家会被永久卡住
                bool hasUnconditionalExit = false;
                for (int i = 0; i < e.Options.Count; i++)
                {
                    var o = e.Options[i];
                    if (o.Condition.Kind == Game.RunEffects.RunConditionKind.Always && o.EndsEvent)
                        hasUnconditionalExit = true;

                    if (o.Effects == null) continue;
                    for (int k = 0; k < o.Effects.Count; k++)
                        if (o.Effects[k] == null)
                        {
                            warnings++;
                            sb.AppendLine($"[警告] 事件 {e.Id}: 选项 {i} 的第 {k} 个效果为空。");
                        }
                }

                if (!hasUnconditionalExit)
                {
                    warnings++;
                    sb.AppendLine($"[警告] 事件 {e.Id}: 没有任何「无条件且会结束事件」的选项，" +
                                  $"条件都不满足时玩家会被卡住。");
                }
            }

            return warnings;
        }

        /// <summary>
        /// 奖励池够不够。三选一要求奖励池里至少有 3 张不同的牌，
        /// 否则玩家看到的「三选一」实际上是「一选一」。
        /// </summary>
        private static int CheckRewardPool(StringBuilder sb)
        {
            int warnings = 0;

            int rewardCards = 0;
            foreach (var c in LoadAll<CardDefinition>())
                if (c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Special) rewardCards++;

            if (rewardCards < 3)
            {
                warnings++;
                sb.AppendLine($"[警告] 奖励池只有 {rewardCards} 张卡（需要 >= 3），三选一会出现重复或空位。");
            }

            int normal = 0, elite = 0, boss = 0;
            foreach (var e in LoadAll<Game.Core.EncounterDefinition>())
            {
                if (e.IsBoss) boss++;
                else if (e.IsElite) elite++;
                else normal++;
            }

            if (normal == 0) { warnings++; sb.AppendLine("[警告] 没有普通战斗，地图的战斗节点会是空的。"); }
            if (boss == 0) { warnings++; sb.AppendLine("[警告] 没有 Boss 战斗，地图最后一层无法通关。"); }
            if (elite == 0) { warnings++; sb.AppendLine("[警告] 没有精英战斗，精英节点会降级成普通战斗。"); }

            return warnings;
        }

        private static int CheckDuplicateIds(StringBuilder sb)
        {
            int warnings = 0;
            warnings += CheckDuplicate<CardDefinition>(sb, c => c.Id, "卡牌");
            warnings += CheckDuplicate<StatusDefinition>(sb, s => s.Id, "状态");
            warnings += CheckDuplicate<EnemyDefinition>(sb, e => e.Id, "敌人");
            warnings += CheckDuplicate<Game.Relics.RelicDefinition>(sb, r => r.Id, "遗物");
            warnings += CheckDuplicate<Game.Events.EventDefinition>(sb, e => e.Id, "事件");
            return warnings;
        }

        private static int CheckDuplicate<T>(StringBuilder sb, Func<T, string> idGetter, string label)
            where T : ScriptableObject
        {
            var seen = new Dictionary<string, string>();
            int warnings = 0;

            foreach (var so in LoadAll<T>())
            {
                var path = PathOf(so);
                var id = idGetter(so);
                if (string.IsNullOrEmpty(id)) continue;

                if (seen.TryGetValue(id, out var other))
                {
                    warnings++;
                    sb.AppendLine($"[警告] {label} id 重复「{id}」：{other} 与 {path}");
                }
                else seen[id] = path;
            }

            return warnings;
        }
    }
}
