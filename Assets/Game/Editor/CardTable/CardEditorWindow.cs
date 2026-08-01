using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Cards;
using Game.Effects;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.CardTables
{
    /// <summary>
    /// 卡牌编辑器窗口。
    ///
    /// ★★★ <b>这个窗口编辑的是 <c>CardTable.json</c>，不是 <c>.asset</c>。</b>
    ///   使用者拍板「表是唯一事实来源，单向导入」，而一个直接编辑资产的窗口会与那条规则打架——
    ///   你在窗口里改的东西下次导入就被表冲掉。所以窗口是**表的图形前端**：
    ///   点几下 → 写回 JSON → 你跑菜单 7 → 资产更新。
    ///   资产始终是 build 产物，唯一事实来源始终只有一个。
    ///
    /// <para><b>没有「未保存」状态。</b>任何改动立刻写回磁盘。
    ///   理由与铁律 56 相同：能不留状态就别留。一个「未保存」标志意味着要回答
    ///   「域重载时谁保存 / 关窗口时谁保存 / 崩了怎么办」三个问题，
    ///   而写一个几 KB 的文本文件是亚毫秒操作，那三个问题根本不必存在。
    ///   刻意不调 <c>AssetDatabase.Refresh()</c>——那才是会带来卡顿的东西。</para>
    ///
    /// <para><b>判断逻辑一行都不在这里。</b>校验全部走
    ///   <see cref="CardRules"/>，序列化全部走 <see cref="CardTableJson"/>。
    ///   Editor 程序集没有测试覆盖（铁律 52 那个盲区），所以没有测试的这一层只负责画。</para>
    /// </summary>
    public class CardEditorWindow : EditorWindow
    {
        private const string TablePath = "Assets/GameData/CardTable.json";
        private const float ListWidth = 260f;

        // ---------------------------------------------------------------- 状态
        private CardTable _table;
        private int _selected = -1;
        private string _search = "";
        private int _typeFilter;      // 0 = 全部
        private int _rarityFilter;    // 0 = 全部
        private Vector2 _listScroll, _detailScroll;
        private string _loadError;
        private string _saveError;

        /// <summary>
        /// 复用的临时 <see cref="CardDefinition"/>，只用来喂 <see cref="CardRules"/> 和描述预览。
        ///
        /// ★ 刻意只造**一个**并反复复用，而不是每次校验 new 一个：
        ///   <c>CardDefinition.OnValidate</c> 在描述模板下标越界时会打一条 <c>Debug.LogError</c>，
        ///   而这个窗口每帧都要校验。反复 CreateInstance 会把 Console 刷满红字。
        ///   （只从代码改字段不会触发 OnValidate，所以复用这一个是安全的。）
        /// </summary>
        private CardDefinition _scratch;

        private CardInstance _scratchInstance;

        /// <summary>校验结果按行缓存，只在表变动时重算。</summary>
        private readonly Dictionary<string, List<CardIssue>> _issues =
            new Dictionary<string, List<CardIssue>>(StringComparer.Ordinal);

        [MenuItem("Tools/卡牌游戏/9. 卡牌编辑器", priority = 9)]
        public static void Open()
        {
            var w = GetWindow<CardEditorWindow>("卡牌编辑器");
            w.minSize = new Vector2(880, 520);
            w.Show();
        }

        private void OnEnable()
        {
            _scratch = ScriptableObject.CreateInstance<CardDefinition>();
            _scratch.hideFlags = HideFlags.HideAndDontSave;
            _scratchInstance = new CardInstance(0, _scratch);
            Reload();
        }

        private void OnDisable()
        {
            if (_scratch != null) DestroyImmediate(_scratch);
            _scratch = null;
            _scratchInstance = null;
        }

        // ================================================================ 载入 / 保存

        private void Reload()
        {
            _loadError = null;
            _saveError = null;
            _issues.Clear();

            try
            {
                AssetIndex.Invalidate();

                string abs = Abs(TablePath);
                _table = File.Exists(abs)
                    ? CardTableJson.FromJson(File.ReadAllText(abs, Encoding.UTF8))
                    : new CardTable();
            }
            catch (Exception e)
            {
                _table = null;
                _loadError = e.Message;
                return;
            }

            _selected = Mathf.Clamp(_selected, -1, _table.Cards.Count - 1);
            Revalidate();
        }

        /// <summary>
        /// 写回磁盘。★ 保存失败必须把错误挂在界面上，绝不能只 <c>Debug.LogError</c>——
        /// 每帧一条错误会瞬间刷爆 Console，而使用者看到的只是「窗口好像没反应」。
        /// </summary>
        private void Save()
        {
            if (_table == null) return;

            try
            {
                File.WriteAllText(Abs(TablePath), CardTableJson.ToJson(_table), new UTF8Encoding(false));
                _saveError = null;
            }
            catch (Exception e)
            {
                _saveError = e.Message;
            }
        }

        /// <summary>任何改动后调这个：写盘 + 重算校验。</summary>
        private void Changed()
        {
            Save();
            Revalidate();
        }

        private void Revalidate()
        {
            _issues.Clear();
            if (_table == null) return;

            foreach (var row in _table.Cards)
            {
                if (row == null || string.IsNullOrEmpty(row.Id)) continue;
                _issues[row.Id] = ValidateRow(row);
            }
        }

        private List<CardIssue> ValidateRow(CardRow row)
        {
            ApplyToScratch(row);
            var list = CardRules.Validate(_scratch);

            // 表特有的检查（CardRules 只认识 CardDefinition，看不到 upgrade 块和表内重名）
            if (!string.IsNullOrWhiteSpace(row.Id))
            {
                int same = 0;
                foreach (var other in _table.Cards)
                    if (other != null && other.Id == row.Id) same++;

                if (same > 1)
                {
                    list.Add(new CardIssue(CardIssueLevel.Error, row.Id, "id",
                        "表里有两行用了同一个 id。"));
                }

                foreach (var other in _table.Cards)
                {
                    if (other == null || other == row || other.Upgrade == null) continue;
                    if (other.Id + "_plus" == row.Id)
                    {
                        list.Add(new CardIssue(CardIssueLevel.Error, row.Id, "id",
                            $"这个 id 会被「{other.Id}」的 upgrade 块自动占用，" +
                            $"不要再手写一张同名的卡。"));
                    }
                }
            }
            else
            {
                list.Add(new CardIssue(CardIssueLevel.Error, "(无 id)", "id", "id 不能为空。"));
            }

            if (row.Upgrade != null)
            {
                var upEffects = row.Upgrade.Effects ?? row.Effects;
                string upDesc = row.Upgrade.Desc ?? row.Desc;

                if (!string.IsNullOrEmpty(upDesc) && upEffects != null)
                {
                    for (int i = upEffects.Count; i < 10; i++)
                    {
                        if (upDesc.Contains("{" + i + "}"))
                        {
                            list.Add(new CardIssue(CardIssueLevel.Warning, row.Id, "upgrade.desc",
                                $"升级版描述引用了 {{{i}}}，但升级版只有 {upEffects.Count} 个效果。"));
                            break;
                        }
                    }
                }
            }

            return list;
        }

        private void ApplyToScratch(CardRow row)
        {
            _scratch.Id = row.Id;
            _scratch.DisplayName = row.Name;
            _scratch.Cost = row.Cost;
            _scratch.CostMode = row.CostMode;
            _scratch.Type = row.Type;
            _scratch.Rarity = row.Rarity;
            _scratch.TargetKind = row.Target;
            _scratch.Keywords = KeywordsOf(row.Keywords);
            _scratch.DescriptionTemplate = row.Desc;
            _scratch.Effects = row.Effects ?? new List<CardEffect>();
            _scratch.InHandEndOfTurnEffects = row.InHandEndOfTurn ?? new List<CardEffect>();
            _scratch.UpgradedVersion = null;
        }

        // ================================================================ 主绘制

        private void OnGUI()
        {
            DrawToolbar();

            if (_loadError != null)
            {
                EditorGUILayout.HelpBox(
                    $"读表失败，窗口里什么都不能编，以免覆盖掉一个还没修好的文件：\n\n{_loadError}",
                    MessageType.Error);

                if (GUILayout.Button("重新载入")) Reload();
                return;
            }

            if (_saveError != null)
            {
                EditorGUILayout.HelpBox($"写盘失败（改动没有保存）：\n{_saveError}", MessageType.Error);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawDetail();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("导入卡表（菜单 7）", EditorStyles.toolbarButton, GUILayout.Width(140)))
                {
                    Save();
                    CardTableImporter.Import();
                    Reload();
                }

                if (GUILayout.Button("卡表自检", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    CardTableSelfCheck.Run();

                if (GUILayout.Button("打开 JSON", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TablePath);
                    if (asset != null) AssetDatabase.OpenAsset(asset);
                    else EditorUtility.RevealInFinder(Abs(TablePath));
                }

                if (GUILayout.Button("从磁盘重载", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    Reload();

                GUILayout.FlexibleSpace();

                int errors = 0, warnings = 0;
                foreach (var kv in _issues)
                    foreach (var i in kv.Value)
                        if (i.Level == CardIssueLevel.Error) errors++; else warnings++;

                string summary = _table == null
                    ? ""
                    : $"{_table.Cards.Count} 行 · {errors} 错误 · {warnings} 警告";

                GUILayout.Label(summary, EditorStyles.miniLabel);
            }
        }

        // ---------------------------------------------------------------- 左栏

        private void DrawList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _typeFilter = EditorGUILayout.Popup(_typeFilter, WithAll(Enum.GetNames(typeof(CardType))),
                                                        EditorStyles.toolbarPopup);
                    _rarityFilter = EditorGUILayout.Popup(_rarityFilter, WithAll(Enum.GetNames(typeof(CardRarity))),
                                                          EditorStyles.toolbarPopup);
                }

                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, "box");

                for (int i = 0; i < _table.Cards.Count; i++)
                {
                    var row = _table.Cards[i];
                    if (row == null || !PassesFilter(row)) continue;
                    DrawListItem(i, row);
                }

                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ 新建")) NewCard();

                    using (new EditorGUI.DisabledScope(_selected < 0))
                    {
                        if (GUILayout.Button("复制选中")) DuplicateSelected();
                        if (GUILayout.Button("删除")) DeleteSelected();
                    }
                }
            }
        }

        private void DrawListItem(int index, CardRow row)
        {
            bool selected = index == _selected;
            var style = selected ? Styles.SelectedItem : Styles.Item;

            var rect = EditorGUILayout.BeginHorizontal(style);

            // 红点 / 黄点：不用展开就知道哪一行有问题
            var marker = MarkerFor(row);
            if (marker.HasValue)
            {
                var dot = GUILayoutUtility.GetRect(10, 16, GUILayout.Width(10));
                dot.y += 5; dot.height = 6; dot.width = 6;
                EditorGUI.DrawRect(dot, marker.Value);
            }
            else
            {
                GUILayout.Space(10);
            }

            GUILayout.Label(string.IsNullOrEmpty(row.Name) ? "(未命名)" : row.Name,
                            GUILayout.Width(96));
            GUILayout.Label(row.Id ?? "", EditorStyles.miniLabel, GUILayout.Width(96));
            GUILayout.FlexibleSpace();
            GUILayout.Label(row.CostMode == CostMode.X ? "X" : row.Cost.ToString(),
                            EditorStyles.miniLabel, GUILayout.Width(14));

            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selected = index;
                GUI.FocusControl(null);   // 否则上一张卡的输入框内容会跟着焦点跑过来
                Event.current.Use();
                Repaint();
            }
        }

        private Color? MarkerFor(CardRow row)
        {
            if (row.Id == null || !_issues.TryGetValue(row.Id, out var list) || list.Count == 0)
                return null;

            foreach (var i in list)
                if (i.Level == CardIssueLevel.Error) return new Color(0.9f, 0.25f, 0.2f);

            return new Color(0.95f, 0.75f, 0.2f);
        }

        private bool PassesFilter(CardRow row)
        {
            if (_typeFilter > 0 && (int)row.Type != _typeFilter - 1) return false;
            if (_rarityFilter > 0 && (int)row.Rarity != _rarityFilter - 1) return false;

            if (!string.IsNullOrEmpty(_search))
            {
                string q = _search.ToLowerInvariant();
                bool hit = (row.Id ?? "").ToLowerInvariant().Contains(q)
                           || (row.Name ?? "").ToLowerInvariant().Contains(q);
                if (!hit) return false;
            }

            return true;
        }

        // ---------------------------------------------------------------- 右栏

        private void DrawDetail()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_selected < 0 || _selected >= _table.Cards.Count)
                {
                    EditorGUILayout.HelpBox(
                        "左边选一张卡，或者点「+ 新建」。\n\n" +
                        "这个窗口编辑的是 CardTable.json。改完点工具栏的「导入卡表」" +
                        "才会产出 GameData/Cards/Authored/ 下的资产。",
                        MessageType.Info);
                    return;
                }

                var row = _table.Cards[_selected];

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                EditorGUI.BeginChangeCheck();

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawPreview(row);
                    using (new EditorGUILayout.VerticalScope()) DrawFieldsPane(row);
                }

                EditorGUILayout.Space(6);
                DrawEffectsSection(row);
                EditorGUILayout.Space(6);
                DrawUpgradeSection(row);

                bool changed = EditorGUI.EndChangeCheck();

                EditorGUILayout.Space(6);
                DrawIssues(row);

                EditorGUILayout.EndScrollView();

                // ★ 写盘放在 EndChangeCheck 之后、且在本帧所有绘制之后：
                //   在绘制途中写盘会让同一帧里后面的控件读到半新半旧的数据。
                if (changed) Changed();
            }
        }

        // ---- 卡面预览

        /// <summary>
        /// 近似卡面。**文字是真实算出来的**——走 <c>CardInstance.GetDescription(null)</c>，
        /// 也就是牌库界面用的那条路径，所以 <c>{N}</c> 的替换结果与游戏里逐字一致。
        ///
        /// <para>⚠ 战斗外没有 <c>BattleContext</c>，所以缩放类数值（每层力量 +N 之类）
        /// 显示的是它的**基础形态**。这是诚实的：编辑期本来就不存在「当前有几层力量」。</para>
        /// </summary>
        private void DrawPreview(CardRow row)
        {
            const float w = 210f, h = 290f;
            var rect = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.ExpandWidth(false));

            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.13f));
            var inner = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
            EditorGUI.DrawRect(inner, TypeColor(row.Type));

            // 费用球
            var cost = new Rect(inner.x + 6, inner.y + 6, 26, 26);
            EditorGUI.DrawRect(cost, new Color(0.1f, 0.1f, 0.15f, 0.85f));
            GUI.Label(cost, row.CostMode == CostMode.X ? "X"
                          : row.CostMode == CostMode.Unplayable ? "-"
                          : row.Cost.ToString(), Styles.CostLabel);

            // 卡名
            GUI.Label(new Rect(inner.x + 38, inner.y + 8, inner.width - 46, 22),
                      string.IsNullOrEmpty(row.Name) ? "(未命名)" : row.Name, Styles.CardName);

            // 类型 / 稀有度
            GUI.Label(new Rect(inner.x + 8, inner.y + 36, inner.width - 16, 16),
                      $"{row.Type} · {row.Rarity}", Styles.CardMeta);

            // 图位（导入器不写 Art，所以这里恒是占位——铁律 47）
            var artRect = new Rect(inner.x + 8, inner.y + 56, inner.width - 16, 96);
            EditorGUI.DrawRect(artRect, new Color(0, 0, 0, 0.25f));
            GUI.Label(artRect, "（美术不由卡表管理）", Styles.CardMeta);

            // 描述——真实计算
            var descRect = new Rect(inner.x + 10, inner.y + 162, inner.width - 20, 92);
            GUI.Label(descRect, DescriptionOf(row), Styles.CardDesc);

            // 关键字
            var kwRect = new Rect(inner.x + 8, inner.y + h - 34, inner.width - 16, 16);
            var kws = row.Keywords;
            if (kws != null && kws.Count > 0)
                GUI.Label(kwRect, "【" + string.Join(" ", kws) + "】", Styles.CardMeta);
        }

        private string DescriptionOf(CardRow row)
        {
            ApplyToScratch(row);
            try { return _scratchInstance.GetDescription(null); }
            catch (Exception e) { return "（描述算不出来：" + e.Message + "）"; }
        }

        // ---- 字段区

        private void DrawFieldsPane(CardRow row)
        {
            EditorGUILayout.LabelField("标识", EditorStyles.boldLabel);

            string newId = EditorGUILayout.TextField("id", row.Id);
            if (newId != row.Id)
            {
                // ★ 改 id 等于换一张新卡：本地化 key 由 Id 派生（card.<id>.name / .desc），
                //   所以旧译文会变成孤儿，而且没有任何东西会提醒你。
                if (!string.IsNullOrEmpty(row.Id) && !string.IsNullOrEmpty(newId)
                    && !EditorUtility.DisplayDialog("改 id？",
                        $"「{row.Id}」→「{newId}」\n\n" +
                        "本地化 key 由 id 派生，改 id 等于删掉旧卡再建一张新卡：\n" +
                        $"· card.{row.Id}.name / .desc 的现有译文会变成孤儿\n" +
                        "· 已经进过存档的卡，读档时会找不到它\n\n" +
                        "导入时旧资产也会被当成孤儿删掉。确定要改吗？",
                        "改", "取消"))
                {
                    newId = row.Id;
                }
                row.Id = newId;
            }

            row.Name = EditorGUILayout.TextField("名称", row.Name);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("规则", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(row.CostMode != CostMode.Fixed))
                    row.Cost = EditorGUILayout.IntField("费用", row.Cost);

                row.CostMode = (CostMode)EditorGUILayout.EnumPopup(row.CostMode, GUILayout.Width(90));
            }

            if (row.CostMode == CostMode.X)
            {
                EditorGUILayout.LabelField(" ", "X 费卡的 cost 恒为 0，实际消耗全部能量。",
                                           EditorStyles.miniLabel);
                row.Cost = 0;
            }

            row.Type = (CardType)EditorGUILayout.EnumPopup("类型", row.Type);
            row.Rarity = (CardRarity)EditorGUILayout.EnumPopup("稀有度", row.Rarity);
            row.Target = (CardTargetKind)EditorGUILayout.EnumPopup("目标", row.Target);
            DrawTargetKindExplanation(row);

            DrawKeywords(row);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("描述模板", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(PlaceholderHint(row), EditorStyles.miniLabel);
            row.Desc = EditorGUILayout.TextArea(row.Desc ?? "", GUILayout.Height(44));
        }

        /// <summary>
        /// 解释卡牌级「目标」到底控制什么，并把效果树实际会打谁列出来。
        ///
        /// ★★ 这个字段是本工程最容易误读的一处配置，而且误读之后**只有攻击那一半会坏**：
        ///   运行时唯一读它的地方是 <c>BattleController.NeedsTargetSelection</c> 里的
        ///   <c>== SingleEnemy</c>，所以 None / AllEnemies / Self / RandomEnemy 行为完全等价，
        ///   都只表示「不要让玩家点目标」。它**不声明打击范围**——打谁由每个效果自己的 target 决定。
        ///
        /// <para>字段名叫「目标」、取值里又有「All Enemies」，读起来就像「这张卡打全体」。
        ///   于是把它设成 AllEnemies、效果留在默认的 chosen，得到的是一张
        ///   「护甲和抽牌都生效、伤害静默消失」的卡。校验器现在会报这个错，
        ///   但错误信息是事后的；把因果关系写在字段旁边才能让它不发生。</para>
        /// </summary>
        private void DrawTargetKindExplanation(CardRow row)
        {
            bool asks = row.Target == CardTargetKind.SingleEnemy;
            bool usesChosen = CardRules.UsesChosenTarget(row.Effects);

            EditorGUILayout.LabelField(" ",
                asks ? "出牌时会让玩家点一个敌人 → 效果里的 chosen 命中他。"
                     : "出牌时**不会**让玩家点目标 → 效果里的 chosen 命中 0 个单位。\n" +
                       "（运行时只有 SingleEnemy 有行为意义；None / AllEnemies / Self / " +
                       "RandomEnemy 完全等价。打谁由下面每个效果自己的 target 决定。）",
                Styles.Hint);

            if (!asks && usesChosen)
            {
                EditorGUILayout.HelpBox(
                    "下面有效果用了 chosen，但这张卡不会让玩家点目标 —— 那些效果会静默无效。\n" +
                    "改「目标」为 Single Enemy，或把效果的 target 改成 All Enemies / Random Enemy。",
                    MessageType.Error);
            }

            // 从效果树推导「实际会打谁」——这是唯一能回答「这张卡到底打谁」的东西
            var kinds = new List<string>();
            CollectTargetKinds(row.Effects, kinds);

            EditorGUILayout.LabelField("实际命中",
                kinds.Count == 0 ? "（没有效果）" : string.Join(" / ", kinds),
                EditorStyles.miniLabel);
        }

        /// <summary>递归收集效果树里用到的目标类型，供「实际命中」显示。</summary>
        private static void CollectTargetKinds(List<CardEffect> effects, List<string> into)
        {
            if (effects == null) return;

            foreach (var e in effects)
            {
                if (e == null) continue;

                string name = e.Target.Kind.ToString();
                if (!into.Contains(name)) into.Add(name);

                // 与 CardRules.UsesChosenTarget 同一批组合子。
                // ⚠ 新增第五种组合子时这里也要加（铁律 22 的递归入口之一）。
                switch (e)
                {
                    case Game.Effects.Impl.RepeatEffect r: CollectTargetKinds(r.Effects, into); break;
                    case Game.Effects.Impl.DelayedEffect d: CollectTargetKinds(d.Effects, into); break;
                    case Game.Effects.Impl.ConditionalEffect c:
                        CollectTargetKinds(c.Then, into);
                        CollectTargetKinds(c.Else, into);
                        break;
                    case Game.Effects.Impl.RandomPickEffect p:
                        if (p.Options != null)
                            foreach (var o in p.Options)
                                if (o?.Effect != null)
                                    CollectTargetKinds(new List<CardEffect> { o.Effect }, into);
                        break;
                }
            }
        }

        /// <summary>把 {0} {1} 对应到哪个效果画出来——不然使用者要自己数下标。</summary>
        private string PlaceholderHint(CardRow row)
        {
            var effects = row.Effects;
            if (effects == null || effects.Count == 0) return "（没有效果，{N} 无处可指）";

            var sb = new StringBuilder();
            for (int i = 0; i < effects.Count; i++)
            {
                if (i > 0) sb.Append("   ");
                sb.Append("{").Append(i).Append("}=");
                sb.Append(effects[i] == null ? "空" : EffectKinds.ForType(effects[i].GetType()).ShortName);
            }
            return sb.ToString();
        }

        private void DrawKeywords(CardRow row)
        {
            EditorGUILayout.LabelField("关键字");

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);

                foreach (CardKeyword kw in Enum.GetValues(typeof(CardKeyword)))
                {
                    if (kw == CardKeyword.None) continue;

                    bool on = row.Keywords != null && row.Keywords.Contains(kw.ToString());
                    bool now = GUILayout.Toggle(on, kw.ToString(), EditorStyles.miniButton);

                    if (now == on) continue;

                    if (row.Keywords == null) row.Keywords = new List<string>();
                    if (now) row.Keywords.Add(kw.ToString());
                    else row.Keywords.Remove(kw.ToString());

                    // 空列表写成 null，JSON 里就不会留一个空数组
                    if (row.Keywords.Count == 0) row.Keywords = null;
                }
            }
        }

        // ---- 效果区

        private void DrawEffectsSection(CardRow row)
        {
            row.Effects = row.Effects ?? new List<CardEffect>();

            EditorGUILayout.LabelField("效果（按顺序结算）", EditorStyles.boldLabel);
            DrawEffectList(row.Effects, row, isUpgrade: false, depth: 0);

            EditorGUILayout.Space(4);

            bool hasInHand = row.InHandEndOfTurn != null && row.InHandEndOfTurn.Count > 0;
            bool wantInHand = EditorGUILayout.ToggleLeft(
                "留在手上到回合结束时结算的效果（灼烧 / 疑虑这类）", hasInHand);

            if (wantInHand && !hasInHand) row.InHandEndOfTurn = new List<CardEffect>();
            else if (!wantInHand && hasInHand) row.InHandEndOfTurn = null;

            if (row.InHandEndOfTurn != null)
            {
                using (new EditorGUI.IndentLevelScope())
                    DrawEffectList(row.InHandEndOfTurn, null, false, 0);
            }
        }

        /// <summary>
        /// 一个效果列表。<paramref name="descOwner"/> 非 null 时，增删移动会**自动重排它的 {N}**。
        /// </summary>
        private void DrawEffectList(List<CardEffect> list, CardRow descOwner, bool isUpgrade, int depth)
        {
            // 集合的增删一律推迟到遍历结束——在 for 里改 List 会让 IMGUI 的
            // 控件数量在同一帧内变化，Unity 会报 layout 组不匹配。
            int remove = -1, moveUp = -1, moveDown = -1;

            for (int i = 0; i < list.Count; i++)
            {
                var effect = list[i];

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"[{i}]", EditorStyles.miniLabel, GUILayout.Width(22));
                        GUILayout.Label(Summarize(effect), EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button("↑", EditorStyles.miniButton, GUILayout.Width(20))) moveUp = i;

                        using (new EditorGUI.DisabledScope(i == list.Count - 1))
                            if (GUILayout.Button("↓", EditorStyles.miniButton, GUILayout.Width(20))) moveDown = i;

                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) remove = i;
                    }

                    if (effect == null)
                    {
                        EditorGUILayout.HelpBox(
                            "这个效果是空的（多半是类被重命名后 [SerializeReference] 丢了引用）。删掉重加。",
                            MessageType.Error);
                        continue;
                    }

                    DrawEffectFields(effect, depth);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(depth * 12);
                if (GUILayout.Button("+ 添加效果", GUILayout.Width(110)))
                    ShowAddMenu(list, descOwner, depth);
                GUILayout.FlexibleSpace();
            }

            // ---- 应用推迟的操作，并同步重排描述里的占位符
            if (remove >= 0)
            {
                list.RemoveAt(remove);
                if (descOwner != null) RemapDesc(descOwner, isUpgrade, old => old == remove ? -1
                                                                            : old > remove ? old - 1 : old);
            }
            else if (moveUp > 0)
            {
                Swap(list, moveUp, moveUp - 1);
                int a = moveUp, b = moveUp - 1;
                if (descOwner != null) RemapDesc(descOwner, isUpgrade,
                    old => old == a ? b : old == b ? a : old);
            }
            else if (moveDown >= 0)
            {
                Swap(list, moveDown, moveDown + 1);
                int a = moveDown, b = moveDown + 1;
                if (descOwner != null) RemapDesc(descOwner, isUpgrade,
                    old => old == a ? b : old == b ? a : old);
            }
        }

        private void ShowAddMenu(List<CardEffect> list, CardRow descOwner, int depth)
        {
            var menu = new GenericMenu();

            foreach (var spec in EffectKinds.All)
            {
                var s = spec;

                // 一层组合子：子列表里不再允许放组合子（使用者拍板的深度）。
                bool blocked = depth > 0 && s.HasChildEffects;

                string label = $"{CategoryOf(s.ShortName)}/{s.ShortName}  —  {DescribeKind(s.ShortName)}";

                if (blocked)
                {
                    menu.AddDisabledItem(new GUIContent(label + "（组合子不能再套组合子，请手改 JSON）"));
                    continue;
                }

                menu.AddItem(new GUIContent(label), false, () =>
                {
                    list.Add((CardEffect)Activator.CreateInstance(s.Type));

                    // 新效果的下标是末尾，已有的 {N} 全部不受影响，所以不必重排；
                    // 但要把它接进描述，否则使用者加了效果却看不到数值。
                    if (descOwner != null) AppendPlaceholder(descOwner, list.Count - 1);

                    Changed();
                    Repaint();
                });
            }

            menu.ShowAsContext();
        }

        /// <summary>
        /// 反射画一个效果的全部字段。
        ///
        /// ★ 与 JSON 层同一条设计：控件由字段类型推导，**没有逐效果的 UI 代码**。
        ///   于是在 <c>Effects/Impl/</c> 新建一个效果类，它立刻在这里可编辑。
        ///   碰到不认识的字段类型会画一行灰字说明，而不是静默跳过——
        ///   静默跳过意味着那个字段永远无法在窗口里设置，且没人会发现。
        /// </summary>
        private void DrawEffectFields(CardEffect effect, int depth)
        {
            var spec = EffectKinds.ForType(effect.GetType());

            foreach (var f in spec.Fields)
            {
                string label = spec.JsonNameOf(f);
                object v = f.GetValue(effect);
                Type t = f.FieldType;

                if (t == typeof(int)) { f.SetValue(effect, EditorGUILayout.IntField(label, (int)v)); continue; }
                if (t == typeof(bool)) { f.SetValue(effect, EditorGUILayout.Toggle(label, (bool)v)); continue; }
                if (t == typeof(string)) { f.SetValue(effect, EditorGUILayout.TextField(label, (string)v)); continue; }

                if (t == typeof(EffectValue))
                {
                    f.SetValue(effect, DrawEffectValue(label, (EffectValue)v));
                    continue;
                }

                if (t == typeof(TargetSelector))
                {
                    f.SetValue(effect, DrawTargetSelector(label, (TargetSelector)v));
                    continue;
                }

                if (t == typeof(EffectCondition))
                {
                    f.SetValue(effect, DrawCondition(label, (EffectCondition)v));
                    continue;
                }

                if (t.IsEnum)
                {
                    f.SetValue(effect, EditorGUILayout.EnumPopup(label, (Enum)v));
                    continue;
                }

                if (typeof(ScriptableObject).IsAssignableFrom(t))
                {
                    var picked = EditorGUILayout.ObjectField(label, (ScriptableObject)v, t, false);
                    f.SetValue(effect, picked);

                    if (picked != null && string.IsNullOrEmpty(AssetIndex.IdOf(picked as ScriptableObject)))
                    {
                        EditorGUILayout.HelpBox(
                            "这个资产的 Id 是空的，导入时会报错（表里存的是 Id，不是引用）。",
                            MessageType.Error);
                    }
                    continue;
                }

                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                {
                    DrawListField(effect, f, label, depth);
                    continue;
                }

                EditorGUILayout.LabelField(label,
                    $"（{t.Name} 这个类型窗口还不会画，请在 JSON 里改）", EditorStyles.miniLabel);
            }
        }

        private void DrawListField(CardEffect owner, FieldInfo f, string label, int depth)
        {
            Type elem = f.FieldType.GetGenericArguments()[0];
            var raw = f.GetValue(owner) as IList;

            if (raw == null)
            {
                raw = (IList)Activator.CreateInstance(f.FieldType);
                f.SetValue(owner, raw);
            }

            // 子效果列表（组合子的 then / else / effects）
            if (typeof(CardEffect).IsAssignableFrom(elem))
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

                if (depth >= 1)
                {
                    EditorGUILayout.HelpBox(
                        $"这里已经是第二层了。窗口只画一层组合子（{raw.Count} 个子效果），" +
                        $"更深的嵌套请直接改 JSON——表本身支持任意深度。",
                        MessageType.Info);
                    return;
                }

                using (new EditorGUI.IndentLevelScope())
                    DrawEffectList((List<CardEffect>)raw, null, false, depth + 1);

                return;
            }

            // 其它对象列表（目前只有 RandomPickEffect.Option）
            EditorGUILayout.LabelField($"{label}（{raw.Count}）", EditorStyles.miniBoldLabel);

            int remove = -1;
            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < raw.Count; i++)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"[{i}]", EditorStyles.miniLabel, GUILayout.Width(22));
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) remove = i;
                        }

                        DrawPlainObject(raw[i], depth);
                    }
                }

                if (GUILayout.Button("+ 添加一项", GUILayout.Width(100)))
                    raw.Add(Activator.CreateInstance(elem));
            }

            if (remove >= 0) raw.RemoveAt(remove);
        }

        /// <summary>画一个不是 CardEffect 的普通可序列化对象（RandomPickEffect.Option）。</summary>
        private void DrawPlainObject(object obj, int depth)
        {
            if (obj == null) { EditorGUILayout.LabelField("（空）"); return; }

            foreach (var f in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(obj);
                Type t = f.FieldType;
                string label = Json.CamelCase(f.Name);

                if (t == typeof(int)) { f.SetValue(obj, EditorGUILayout.IntField(label, (int)v)); continue; }
                if (t == typeof(bool)) { f.SetValue(obj, EditorGUILayout.Toggle(label, (bool)v)); continue; }
                if (t == typeof(string)) { f.SetValue(obj, EditorGUILayout.TextField(label, (string)v)); continue; }
                if (t == typeof(EffectValue)) { f.SetValue(obj, DrawEffectValue(label, (EffectValue)v)); continue; }
                if (t.IsEnum) { f.SetValue(obj, EditorGUILayout.EnumPopup(label, (Enum)v)); continue; }

                if (typeof(CardEffect).IsAssignableFrom(t))
                {
                    var child = (CardEffect)v;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(label, GUILayout.Width(110));
                        GUILayout.Label(child == null ? "（空）" : Summarize(child), EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button(child == null ? "选择效果" : "换", EditorStyles.miniButton,
                                             GUILayout.Width(child == null ? 70 : 30)))
                        {
                            var menu = new GenericMenu();
                            foreach (var spec in EffectKinds.All)
                            {
                                var s = spec;
                                if (s.HasChildEffects) continue;   // 一层深度
                                menu.AddItem(new GUIContent($"{CategoryOf(s.ShortName)}/{s.ShortName}"), false,
                                    () => { f.SetValue(obj, Activator.CreateInstance(s.Type)); Changed(); Repaint(); });
                            }
                            menu.ShowAsContext();
                        }
                    }

                    if (child != null)
                    {
                        using (new EditorGUI.IndentLevelScope())
                            DrawEffectFields(child, depth + 1);
                    }
                    continue;
                }

                EditorGUILayout.LabelField(label, $"（{t.Name} 请在 JSON 里改）", EditorStyles.miniLabel);
            }
        }

        // ---- 三个紧凑抽屉

        /// <summary>
        /// <see cref="EffectValue"/>：默认只画一个整数框。这是把
        /// 「一个 DamageEffect 在裸 Inspector 里占 14 行」压下来的主力——
        /// 6 个子字段里平时只有 Base 有意义。
        /// </summary>
        private EffectValue DrawEffectValue(string label, EffectValue v)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                v.Base = EditorGUILayout.IntField(label, v.Base);

                bool scaled = v.Scale != ValueScale.None;
                bool want = GUILayout.Toggle(scaled, "缩放", EditorStyles.miniButton, GUILayout.Width(40));

                if (want != scaled)
                {
                    v.Scale = want ? ValueScale.PerStatusStackOnSelf : ValueScale.None;
                    if (!want) { v.ScaleId = null; v.PerUnit = 0; v.Min = 0; v.Max = 0; }
                    else if (v.PerUnit == 0) v.PerUnit = 1;
                }
            }

            if (v.Scale == ValueScale.None) return v;

            using (new EditorGUI.IndentLevelScope())
            {
                v.Scale = (ValueScale)EditorGUILayout.EnumPopup("缩放来源", v.Scale);

                bool needsId = v.Scale == ValueScale.PerStatusStackOnSelf
                               || v.Scale == ValueScale.PerStatusStackOnTarget;

                if (needsId)
                {
                    v.ScaleId = EditorGUILayout.TextField("状态 Id", v.ScaleId);

                    if (string.IsNullOrEmpty(v.ScaleId))
                    {
                        EditorGUILayout.HelpBox(
                            "这种缩放必须填状态 Id。不填的话它恒等于 0，而且运行时不会报错。",
                            MessageType.Error);
                    }
                }

                v.PerUnit = EditorGUILayout.IntField("每单位 +", v.PerUnit);

                using (new EditorGUILayout.HorizontalScope())
                {
                    v.Min = EditorGUILayout.IntField("下限", v.Min);
                    v.Max = EditorGUILayout.IntField("上限", v.Max);
                }

                EditorGUILayout.LabelField(" ",
                    $"最终值 = {v.Base} + 单位数 × {v.PerUnit}" +
                    (v.Min == 0 && v.Max == 0 ? "（不钳制）" : $"，钳到 [{v.Min}, {v.Max}]"),
                    EditorStyles.miniLabel);
            }

            return v;
        }

        /// <summary>
        /// <see cref="TargetSelector"/>：默认只画一个下拉。
        /// <c>count</c> / <c>allowDuplicates</c> 只在 <c>randomEnemy</c> 下才有意义，
        /// 平时画出来是纯噪音。
        /// </summary>
        private TargetSelector DrawTargetSelector(string label, TargetSelector v)
        {
            bool hasExtras = v.Count != 0 || v.AllowDuplicates || v.ExcludeSelf
                             || !string.IsNullOrEmpty(v.RequireStatusId);

            using (new EditorGUILayout.HorizontalScope())
            {
                v.Kind = (TargetKind)EditorGUILayout.EnumPopup(label, v.Kind);

                bool want = GUILayout.Toggle(hasExtras, "更多", EditorStyles.miniButton, GUILayout.Width(40));
                if (!want && hasExtras)
                {
                    v.Count = 0; v.AllowDuplicates = false; v.ExcludeSelf = false; v.RequireStatusId = null;
                    hasExtras = false;
                }
                else if (want) hasExtras = true;
            }

            if (!hasExtras) return v;

            using (new EditorGUI.IndentLevelScope())
            {
                if (v.Kind == TargetKind.RandomEnemy)
                {
                    v.Count = EditorGUILayout.IntField("取几个（0 视为 1）", v.Count);
                    v.AllowDuplicates = EditorGUILayout.Toggle("允许重复命中", v.AllowDuplicates);
                }

                v.ExcludeSelf = EditorGUILayout.Toggle("排除自己", v.ExcludeSelf);
                v.RequireStatusId = EditorGUILayout.TextField("只保留带此状态", v.RequireStatusId);
            }

            return v;
        }

        private EffectCondition DrawCondition(string label, EffectCondition c)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                c.Kind = (ConditionKind)EditorGUILayout.EnumPopup("条件", c.Kind);

                bool needsId = c.Kind == ConditionKind.SelfHasStatus || c.Kind == ConditionKind.TargetHasStatus;
                if (needsId) c.Id = EditorGUILayout.TextField("状态 Id", c.Id);

                if (c.Kind != ConditionKind.Always
                    && c.Kind != ConditionKind.LastCardWasAttack
                    && c.Kind != ConditionKind.IsFirstTurn)
                {
                    c.Value = EditorGUILayout.IntField("数值", c.Value);
                }

                c.Invert = EditorGUILayout.Toggle("取反", c.Invert);
            }

            return c;
        }

        // ---- 升级版

        private void DrawUpgradeSection(CardRow row)
        {
            bool has = row.Upgrade != null;
            bool want = EditorGUILayout.ToggleLeft("有升级版（自动产出 <id>_plus，稀有度强制 Special）", has,
                                                   EditorStyles.boldLabel);

            if (want && !has)
            {
                // 默认把基础版的效果深拷贝过来当起点——从空列表开始的话，
                // 使用者要把整棵树重搭一遍。
                row.Upgrade = new UpgradeRow { Effects = CardTableJson.CloneEffects(row.Effects) };
            }
            else if (!want && has)
            {
                if (EditorUtility.DisplayDialog("删掉升级版？",
                        $"「{row.Id}_plus」对应的资产会在下次导入时被删掉。", "删", "取消"))
                    row.Upgrade = null;
            }

            if (row.Upgrade == null) return;

            var up = row.Upgrade;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField($"id：{row.Id}_plus", EditorStyles.miniLabel);

                up.Name = OptionalString("名称", up.Name, (row.Name ?? "") + "+");
                up.Cost = OptionalInt("费用", up.Cost, row.Cost);
                up.Desc = OptionalString("描述模板", up.Desc, row.Desc);

                bool ownEffects = up.Effects != null;
                bool wantOwn = EditorGUILayout.ToggleLeft(
                    "使用自己的效果（不勾则整棵树继承基础版）", ownEffects);

                if (wantOwn && !ownEffects) up.Effects = CardTableJson.CloneEffects(row.Effects);
                else if (!wantOwn && ownEffects) up.Effects = null;

                if (up.Effects != null) DrawEffectList(up.Effects, row, isUpgrade: true, depth: 0);
            }
        }

        /// <summary>
        /// 「省略 = 继承」的字段。勾掉复选框写 null，界面上显示继承来的值当占位。
        /// 不这么画的话，使用者无法表达「我要继承」——空字符串和 null 在 UI 上长得一样。
        /// </summary>
        private string OptionalString(string label, string value, string inherited)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool over = GUILayout.Toggle(value != null, "", GUILayout.Width(16));

                if (!over)
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.TextField(label, inherited ?? "");
                    return null;
                }

                return EditorGUILayout.TextField(label, value ?? inherited ?? "");
            }
        }

        private int? OptionalInt(string label, int? value, int inherited)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool over = GUILayout.Toggle(value.HasValue, "", GUILayout.Width(16));

                if (!over)
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.IntField(label, inherited);
                    return null;
                }

                return EditorGUILayout.IntField(label, value ?? inherited);
            }
        }

        // ---- 校验面板

        private void DrawIssues(CardRow row)
        {
            if (row.Id == null || !_issues.TryGetValue(row.Id, out var list) || list.Count == 0)
            {
                EditorGUILayout.HelpBox("校验通过。", MessageType.Info);
                return;
            }

            foreach (var issue in list)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(issue.Field) ? issue.Message : $"{issue.Field}：{issue.Message}",
                    issue.Level == CardIssueLevel.Error ? MessageType.Error : MessageType.Warning);
            }
        }

        // ================================================================ 增删卡

        private void NewCard()
        {
            string id = UniqueId("new_card");

            _table.Cards.Add(new CardRow
            {
                Id = id,
                Name = "新卡",
                Cost = 1,
                Type = CardType.Attack,
                Rarity = CardRarity.Common,
                Target = CardTargetKind.SingleEnemy,
                Desc = "造成 {0} 点伤害。",
                Effects = new List<CardEffect>
                {
                    new Game.Effects.Impl.DamageEffect { Target = TargetSelector.Chosen },
                },
            });

            _selected = _table.Cards.Count - 1;
            Changed();
        }

        private void DuplicateSelected()
        {
            var src = _table.Cards[_selected];

            var copy = new CardRow
            {
                Id = UniqueId(src.Id + "_copy"),
                Name = (src.Name ?? "") + " 副本",
                Cost = src.Cost,
                CostMode = src.CostMode,
                Type = src.Type,
                Rarity = src.Rarity,
                Target = src.Target,
                Keywords = src.Keywords == null ? null : new List<string>(src.Keywords),
                Desc = src.Desc,

                // ★ 必须深拷贝：共用同一批 CardEffect 实例的话，改副本会同时改原件
                //   （与 CardTableJson.CloneEffects 注释里那条同一个理由）。
                Effects = CardTableJson.CloneEffects(src.Effects),
                InHandEndOfTurn = CardTableJson.CloneEffects(src.InHandEndOfTurn),
            };

            if (src.Upgrade != null)
            {
                copy.Upgrade = new UpgradeRow
                {
                    Name = src.Upgrade.Name,
                    Cost = src.Upgrade.Cost,
                    Desc = src.Upgrade.Desc,
                    Keywords = src.Upgrade.Keywords == null ? null : new List<string>(src.Upgrade.Keywords),
                    Effects = CardTableJson.CloneEffects(src.Upgrade.Effects),
                    InHandEndOfTurn = CardTableJson.CloneEffects(src.Upgrade.InHandEndOfTurn),
                };
            }

            _table.Cards.Insert(_selected + 1, copy);
            _selected++;
            Changed();
        }

        private void DeleteSelected()
        {
            var row = _table.Cards[_selected];

            if (!EditorUtility.DisplayDialog("删掉这张卡？",
                    $"「{row.Name}」({row.Id})\n\n" +
                    "对应的资产会在下次导入时从 Cards/Authored/ 删掉。", "删", "取消"))
                return;

            _table.Cards.RemoveAt(_selected);
            _selected = Mathf.Min(_selected, _table.Cards.Count - 1);
            Changed();
        }

        private string UniqueId(string basis)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in _table.Cards)
            {
                if (r?.Id == null) continue;
                taken.Add(r.Id);
                if (r.Upgrade != null) taken.Add(r.Id + "_plus");
            }

            if (!taken.Contains(basis)) return basis;

            for (int n = 2; n < 1000; n++)
                if (!taken.Contains($"{basis}_{n}")) return $"{basis}_{n}";

            return basis + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        // ================================================================ 占位符重排

        /// <summary>
        /// 效果列表变动后重排描述里的 <c>{N}</c>。
        ///
        /// ★★ 不做这件事的话，新窗口会原样重现旧痛点：往效果列表中间插一个效果，
        ///   后面所有 <c>{N}</c> 全部错位，而校验器只抓「下标越界」，抓不到「错位」。
        ///   这与铁律 33（两个列表靠下标对应）、以及事件选项本地化 key 用下标那条
        ///   是同一个形状的问题。
        ///
        /// <para><paramref name="map"/> 返回 −1 表示这个占位符要整个丢掉（对应效果被删了）。</para>
        /// </summary>
        private void RemapDesc(CardRow row, bool isUpgrade, Func<int, int> map)
        {
            if (isUpgrade)
            {
                if (row.Upgrade?.Desc != null) row.Upgrade.Desc = Remap(row.Upgrade.Desc, map);
            }
            else
            {
                row.Desc = Remap(row.Desc, map);
            }
        }

        /// <summary>
        /// 单次扫描完成重排。★ 绝不能用一串 Replace：
        /// <c>{0}→{1}</c> 之后再跑 <c>{1}→{2}</c>，第一步的产物会被第二步再搬一次。
        /// </summary>
        private static string Remap(string desc, Func<int, int> map)
        {
            if (string.IsNullOrEmpty(desc)) return desc;

            var sb = new StringBuilder(desc.Length + 8);

            for (int i = 0; i < desc.Length; i++)
            {
                if (desc[i] != '{') { sb.Append(desc[i]); continue; }

                int close = desc.IndexOf('}', i);
                if (close < 0) { sb.Append(desc[i]); continue; }

                string inner = desc.Substring(i + 1, close - i - 1);
                if (!int.TryParse(inner, out int idx)) { sb.Append(desc[i]); continue; }

                int mapped = map(idx);
                if (mapped >= 0) sb.Append('{').Append(mapped).Append('}');
                i = close;
            }

            return sb.ToString();
        }

        /// <summary>新增效果后把它接进描述末尾，否则加了效果却看不到数值。</summary>
        private static void AppendPlaceholder(CardRow row, int index)
        {
            string token = "{" + index + "}";
            if (row.Desc != null && row.Desc.Contains(token)) return;
            row.Desc = string.IsNullOrEmpty(row.Desc) ? token : row.Desc.TrimEnd() + " " + token;
        }

        // ================================================================ 杂项

        private static string Summarize(CardEffect e)
        {
            if (e == null) return "（空效果）";

            var spec = EffectKinds.ForType(e.GetType());
            var sb = new StringBuilder(spec.ShortName);

            // 摘要只挑最有信息量的两个字段，不然折叠头会比展开还长
            foreach (var f in spec.Fields)
            {
                string n = spec.JsonNameOf(f);
                if (n != "amount" && n != "count" && n != "times" && n != "stacks") continue;

                object v = f.GetValue(e);
                if (v is EffectValue ev)
                {
                    sb.Append("  ").Append(n).Append('=').Append(ev.Base);
                    if (ev.Scale != ValueScale.None) sb.Append("+缩放");
                }
            }

            if (e.Target.Kind != TargetKind.None) sb.Append("  → ").Append(e.Target.Kind);
            return sb.ToString();
        }

        private static string CategoryOf(string shortName)
        {
            switch (shortName)
            {
                case "damage": case "block": case "heal": return "伤害与护甲";
                case "energy": case "draw": return "资源";
                case "applyStatus": return "状态";
                case "discard": case "exhaust": case "addCard":
                case "modifyCardCost": case "selectCards": return "牌堆操作";
                case "repeat": case "conditional": case "randomPick": case "delayed": return "组合子";

                // ★ 兜底桶。分类是纯装饰，所以新写的效果类不会因为「忘了归类」
                //   而从菜单里消失——它只是落进「其他」。
                default: return "其他";
            }
        }

        private static string DescribeKind(string shortName)
        {
            switch (shortName)
            {
                case "damage": return "造成伤害，可多段（times）";
                case "block": return "获得护甲";
                case "heal": return "治疗";
                case "energy": return "正数获得 / 负数消耗能量";
                case "draw": return "抽牌";
                case "applyStatus": return "施加状态（力量 / 易伤 / 中毒…）";
                case "discard": return "弃牌（随机 / 全部 / 玩家选）";
                case "exhaust": return "消耗自身或手牌";
                case "addCard": return "生成一张卡到指定牌堆";
                case "modifyCardCost": return "修改手牌费用";
                case "selectCards": return "从牌堆选牌后处置";
                case "repeat": return "重复一组子效果 N 次";
                case "conditional": return "条件分支";
                case "randomPick": return "按权重随机挑子效果";
                case "delayed": return "延到回合末 / 下回合开始";
                default: return "";
            }
        }

        private static CardKeyword KeywordsOf(List<string> names)
        {
            var r = CardKeyword.None;
            if (names == null) return r;

            foreach (var n in names)
                if (Enum.TryParse<CardKeyword>(n, true, out var kw)) r |= kw;

            return r;
        }

        private static Color TypeColor(CardType t)
        {
            switch (t)
            {
                case CardType.Attack: return new Color(0.42f, 0.20f, 0.19f);
                case CardType.Skill: return new Color(0.18f, 0.31f, 0.38f);
                case CardType.Power: return new Color(0.32f, 0.20f, 0.40f);
                case CardType.Status: return new Color(0.26f, 0.26f, 0.28f);
                default: return new Color(0.16f, 0.14f, 0.18f);
            }
        }

        private static string[] WithAll(string[] names)
        {
            var r = new string[names.Length + 1];
            r[0] = "全部";
            Array.Copy(names, 0, r, 1, names.Length);
            return r;
        }

        private static void Swap(IList list, int a, int b)
        {
            object t = list[a]; list[a] = list[b]; list[b] = t;
        }

        /// <summary>见 <c>CardTableImporter.Abs</c>：System.IO 不能吃 "Assets/…" 相对路径。</summary>
        private static string Abs(string assetPath)
        {
            const string prefix = "Assets/";
            string rel = assetPath.StartsWith(prefix, StringComparison.Ordinal)
                ? assetPath.Substring(prefix.Length)
                : assetPath;

            return Path.Combine(Application.dataPath, rel).Replace('\\', '/');
        }

        // ---------------------------------------------------------------- 样式

        private static class Styles
        {
            public static readonly GUIStyle Item = new GUIStyle(EditorStyles.label)
            { padding = new RectOffset(2, 2, 2, 2) };

            public static readonly GUIStyle SelectedItem = new GUIStyle(Item)
            { normal = { background = Tex(new Color(0.24f, 0.37f, 0.58f)) } };

            public static readonly GUIStyle CostLabel = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };

            public static readonly GUIStyle CardName = new GUIStyle(EditorStyles.boldLabel)
            { normal = { textColor = Color.white }, fontSize = 13 };

            public static readonly GUIStyle CardMeta = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(1, 1, 1, 0.55f) }, alignment = TextAnchor.MiddleCenter };

            public static readonly GUIStyle CardDesc = new GUIStyle(EditorStyles.label)
            { wordWrap = true, normal = { textColor = new Color(1, 1, 1, 0.92f) }, fontSize = 11 };

            /// <summary>字段旁边的解释文字。必须 wordWrap，否则多行提示会被截掉后半句。</summary>
            public static readonly GUIStyle Hint = new GUIStyle(EditorStyles.miniLabel)
            { wordWrap = true, normal = { textColor = new Color(1, 1, 1, 0.5f) } };

            private static Texture2D Tex(Color c)
            {
                var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }
        }
    }
}
