# WhatAiDo — AI 开发日志

> 这个文件记录 AI 在本工程做过的所有事、当前进度、以及下一步该做什么。
> **每次新开会话，先读这个文件，再读 `Docs/Architecture/README.md`。** 这两个是跨会话交接的唯一凭据。

---

## 一、工程基本信息

| 项 | 值 |
|---|---|
| Unity 版本 | 6000.0.62f1 (Unity 6) |
| C# / 运行时 | C# 9，.NET Standard 2.1 |
| 渲染管线 | URP 17.0.4 |
| UI 方案 | uGUI 2.0.0，**运行时程序化构建，全工程零 prefab** |
| 输入 | `activeInputHandler = 1`（**只启用新输入系统**），已通过 `Game.UI.InputCompat` 兼容 |
| 测试 | com.unity.test-framework 1.6.0，EditMode |
| 根命名空间 | `Game.*` |
| 程序集 | `Game.Runtime` → `Game.UI` → `Game.Editor`；`Game.Tests.EditMode` 只依赖 `Game.Runtime` |

**架构一句话**：Definition(SO 只读) / Instance(运行时) 分离；效果用 `[SerializeReference]` 多态类；
逻辑同步执行 + 表现事件队列，战斗系统可脱离 UI 在 EditMode 里跑完整场。

---

## 二、怎么跑起来（新会话必读）

1. 打开 Unity，等编译完成。
2. 菜单 `Tools/卡牌游戏/1. 生成示例内容` → 在 `Assets/GameData/` 下生成全部 SO 资产 + `GameDatabase.asset`。
3. 菜单 `Tools/卡牌游戏/4. 创建完整流程场景` → 生成并打开 `Assets/Scenes/Main.unity`。
4. 按 Play → 主菜单 → 开始新游戏 → 地图 → 一路打到 Boss。
   - 地图：点亮的节点可以进入；⚔ 战斗 / ☠ 精英 / ♨ 休息 / ◆ 商店 / ？事件 / ▣ 宝箱 / 王 首领
   - 战斗出牌有两条路，都能用：
     - **拖拽（推荐）**：需要目标的牌拖起来会举到半空并拉出一条箭头，把箭头指到敌人身上松手；
       不需要目标的牌跟着鼠标走，拖过屏幕下方那条「松手出牌」的白线松手即出。
     - **点击**：点卡牌 → 需要目标的卡进入选择态 → 点敌人出牌。
   - 右键 / Esc 取消（牌、药水、正在进行的拖拽一起取消）；空格 或 E 结束回合
   - 战斗结束后要点「继续」才推进流程（等表现事件播完，否则看不到致命一击）
   - 想复现某一局：选中 `GameApp`，把 `Fixed Seed` 改成非 0
5. 只调试单场战斗：菜单 `Tools/卡牌游戏/2. 创建战斗测试场景` → `Battle.unity`，
   选中 `BattleBootstrap` 改 `Encounter Id`。
6. 跑测试：`Window → General → Test Runner → EditMode → Run All`（应为 **198/198 通过**）。
7. 校验内容与架构规则：菜单 `Tools/卡牌游戏/3. 校验内容与架构规则`（应为 0 错误 0 警告）。
   CI / 命令行用 `-executeMethod Game.Editor.ContentValidator.ValidateBatch`，有错误时退出码为 1。

**命令行跑测试**（需先关闭 Unity 编辑器，否则会因工程锁失败）：
```
"C:\Program Files\Unity\Hub\Editor\6000.0.62f1\Editor\Unity.exe" -batchmode -nographics ^
  -projectPath "D:\UnityAiProject\TryCardProject" -runTests -testPlatform EditMode ^
  -testResults "%TEMP%\testresults.xml" -logFile "%TEMP%\unity_test.log"
```

---

## 三、进度总览

| 阶段 | 状态 | 说明 |
|---|---|---|
| 阶段 1 最小可玩战斗 | ✅ 完成并通过测试 | 能量 / 抽牌 / 手牌 / 弃牌 / 洗牌 / 伤害 / 护甲 / 敌人行动 / 胜负判定 |
| 阶段 2 卡牌与效果扩展 | ✅ 完成并通过测试 | 14 个效果类、5 个关键字、X 费、升级、动态描述、4 个组合子 |
| 阶段 3 状态与敌人 AI | ✅ 完成并通过测试 | 8 个 Hook 接口、6 个状态、权重/条件/连续限制/多阶段 AI、意图预览 |
| 阶段 4 地图与奖励 | ✅ 完成并通过测试 | 12 个 Hook、地图、RunManager、RunEffect、遗物、奖励/商店/事件/休息、8 个界面 |
| 阶段 5 存档与内容 | 🔶 部分完成 | ✅ SaveSystem / MetaSave；⬜ 自动对战模拟器 / 内容量产 / Fuzz |
| 阶段 6 动画音效打磨 | 🔶 部分完成 | ✅ 本地化 / ✅ TextMeshPro / ✅ 打击反馈 / ✅ 逐张发牌；⬜ 音效 |

**当前代码规模**：`Assets/Game/` 下 125 个 .cs 文件；测试 12 个文件 198 个用例。
**内容规模**：10 个状态、57 张卡、6 个敌人、11 场战斗、16 个遗物、7 个事件、10 瓶药水、5 个关键字。
**本地化规模**：`Locale_en.asset` 里 514 条译文，简体中文（源语言）+ 英文。
（第七次会话写的「495 条」之后没人同步过——第九 / 十 / 十二次会话各加了一批。
以 `grep -c "^  - Key:"` 的实测为准，别再手抄这个数字。）

> ⚠️ **新会话第一件事**：如果 `Assets/GameData/Potions/` 或 `Assets/GameData/Keywords/` 不存在、
> 或卡池不足 57 张，说明内容资产还没生成，先跑菜单 `Tools/卡牌游戏/1. 生成示例内容`。
>
> `GameData/Keywords/` 是第六次会话新增的（5 个 `KeywordDefinition`）。**缺了它不会报错**，
> 只是「消耗 / 保留 / 固有 / 虚无 / 不可打出」的悬停解释静默消失——
> `Tools/卡牌游戏/3. 校验内容与架构规则` 会把这种情况报成警告。

---

## 四、目录与文件清单

```
Assets/
├── Game/
│   ├── Game.Runtime.asmdef
│   ├── Core/        Rng, GameDatabase, EncounterDefinition, RunContext,
│   │                RunManager, RewardGenerator, ShopStock, ContentPicker
│   ├── Cards/       CardEnums, CardDefinition, CardInstance, DeckController,
│   │                KeywordDefinition                                    ← 第六次会话
│   ├── Effects/     CardEffect, EffectContext, EffectValue, EffectCondition,
│   │                TargetSelector(+TargetResolver), EffectResolver,
│   │                EffectTree（效果树的唯一递归遍历入口，见铁律 22）
│   │   └── Impl/    Damage, Block, Draw, Energy, ApplyStatus, Heal, Discard, Exhaust,
│   │                AddCard, ModifyCardCost ｜ 组合子: Repeat, Conditional, RandomPick, Delayed
│   ├── Units/       BattleUnit
│   ├── Statuses/    StatusDefinition, StatusInstance, StatusBehaviour
│   │   └── Impl/    CombatBehaviours（力量/易伤/虚弱/中毒/壁垒/荆棘）
│   │                ProtectiveBehaviours（神器/回光/再生/恶魔形态）
│   ├── Enemies/     Intent, EnemyAction, EnemyDefinition, EnemyBrain
│   │   └── Impl/    GuardianBrain
│   ├── Battle/      BattleEnums, BattleEvent, BattleHooks, BattleContext, BattleController
│   ├── Save/        SaveConstants, RunSave(+8 个 DTO), MetaSave,          ← 阶段 5
│   │                RunSaveWriter, RunSaveReader, SaveJson, SaveMigration
│   ├── Map/         MapNode, GameMap, MapGenerator                        ← 阶段 4
│   ├── Relics/      RelicDefinition, RelicInstance                        ← 阶段 4
│   │   └── Impl/    RelicBehaviours（8 类行为，覆盖 4 个新 Hook）
│   ├── RunEffects/  RunEffect(+Resolver+Context), RunCondition            ← 阶段 4
│   │   └── Impl/    Resource / Deck / Combinator 三组共 11 个局外效果
│   ├── Events/      EventDefinition                                       ← 阶段 4
│   ├── UI/          Game.UI.asmdef, UIFactory, InputCompat, CardView, UnitView,
│   │                HandFanLayout, TargetArrowView,                    ← 阶段 6 预支
│   │                Tooltip(+TooltipView/TooltipContent),                ← 阶段 6 预支
│   │                FloatingText, BattlePresenter, BattleScreen, BattleBootstrap,
│   │                GameApp, ScreenBase, TopBarView, MapScreen(+MapNodeView),
│   │                BattleHostScreen, RewardScreen, ShopScreen, EventScreen,
│   │                RestScreen, MainMenuScreen, GameOverScreen, CardPickerScreen,
│   │                CardListView（只读牌堆 / 卡组浏览）           ← 第十二次会话
│   │                PileFlyFx（洗牌时一叠卡背飞回抽牌堆）        ← 第十三次会话
│   └── Editor/      Game.Editor.asmdef, SampleContentGenerator(+Relics/+Events),
│                    BattleSceneBuilder, MainSceneBuilder, ContentValidator
├── GameData/        （由菜单生成，不手写）
└── Tests/EditMode/  Game.Tests.EditMode.asmdef, TestContent, BattleTestFixture,
                     BattleCoreTests(13), EffectAndStatusTests(25),
                     MapGeneratorTests(11), RelicTests(20), RunFlowTests(20)
```

**必须读懂的主要类**：
- 战斗：`BattleController`、`BattleContext`、`EffectResolver`、`DeckController`、`BattleUnit`、`CardInstance`、`EnemyBrain`
- 局外：`RunManager`、`RunContext`、`MapGenerator`、`RunEffectResolver`、`GameApp`

读完这 12 个就能改 90% 的需求。

---

## 五、必须遵守的铁律（改代码前先看）

1. **`CardEffect` / `StatusBehaviour` 子类不得有可变私有字段。** 这些对象被同一份配置的所有实例共享。
   `Tools/卡牌游戏/3. 校验内容与架构规则` 会用反射扫出违规。
2. **ScriptableObject 里不得有战斗中被写入的字段。** 可变数据一律在 `*Instance` / `BattleContext`。
3. **`Assets/Game/Battle/` 下不得出现 `IEnumerator`、`Update()`、`UnityEngine.UI`。** 靠 asmdef 由编译器兜底。
4. **随机一律走 `ctx.Rng.Range(RngStream.X, ...)`**，禁止 `UnityEngine.Random`。
   预览路径（UI 每帧的可打性判断、卡牌描述）必须设 `EffectContext.PreviewMode = true`，否则随机流会随帧率漂移。
5. **Hook 里若要造成新的伤害/触发，用 `ctx.EnqueueTrigger(...)` 排队**，不要直接递归调用。
6. **`Collect<T>()` 必须写成 `using var hooks = ctx.Collect<T>();`**，离开作用域才会归还 buffer。
7. **死亡单位不从 `AllUnits` 移除**，只是 `Hp = 0`，一律用 `IsAlive` 过滤。
8. **状态层数衰减只在 `BattleController.TickStatusDecay` 里做**，不要写进 `StatusBehaviour`，
   否则「中毒先掉血再减层」的顺序会失控。
9. UI 只能通过 `BattleController.TryPlayCard / EndTurn / CanPlayCard` 写入战斗，其余一律只读。
   局外同理：界面只能通过 `RunManager` 的公开方法推进流程，不得自己改 `RunContext.Phase`。
10. **`RunEffect` 子类同样不得有可变私有字段**，规则与 `CardEffect` 完全一致，校验器已覆盖。
11. **`RunEffect` 不得直接切换流程**。需要开战就写 `RunContext.PendingBattleEncounterId`，
    由 `RunManager.ReturnToMap` 统一消费——流程跳转只能有一个出口。
12. **遗物行为必须复用那 12 个 Hook 接口**，不许另起一套。需要每场战斗独立的计数时，
    让遗物在 `OnBattleStart` 挂一个状态，用 `StatusInstance.Stacks` 存；
    需要跨战斗计数用 `HookSource.Relic.Counter`。
13. **商店库存必须存进 `RunContext.ShopStocks`**，不能在 `ShopScreen` 里现生成，
    否则玩家反复进出就能刷商品。
14. **卡牌升级版（`*_plus`）的稀有度必须是 `Special`**，否则会混进奖励池和商店。
15. `AssetDatabase.FindAssets("t:XXX")` 在 `-batchmode` 下恒返回 0。
    Editor 工具里扫资产一律走 `ContentValidator.LoadAll<T>()` 那种带目录回退的写法。

---

## 六、下一步（阶段 5 待办）

按依赖顺序：

1. ~~`Game/Save/`：存档系统~~ ✅ **第十次会话已完成**，见下面的会话记录与铁律 41–45。
2. `Editor/AutoBattleSimulator.cs`：无 UI 跑 1000 场，输出胜率 / 平均回合数 / 每张卡的贡献。
   `BattleController` 已是纯 C# 类且 Uid 不再是 static，可以在一个进程里并行跑多场。
3. 内容量产：卡池扩到 60+，敌人 15+，遗物 30+。
   数值/文案考虑走表导入，效果结构继续用 `[SerializeReference]` 在 Inspector 里搭。
4. Fuzz 测试：随机种子 + 随机策略跑满 1000 场，断言不崩溃、不死循环、牌数守恒。

**不要在阶段 5 做的事**：多角色、多难度、成就、本地化、动画打磨、云存档、Mod。

> 阶段 5 之外的**全部可选项**（机制 / 内容 / 表现 / 工具 / 已发现的待收问题）见
> [`Docs/Ideas-Backlog.md`](Ideas-Backlog.md)。那份是创意池，本节是既定路线，两者互补。

---

## 六之二、阶段 4 已知遗留（不影响验收，但下次要收）

| # | 问题 | 位置 | 说明 |
|---|---|---|---|
| ~~1~~ | ~~**手牌 UI 全量重建**~~ | `BattleScreen.RefreshHandViews` | ✅ 第五次会话已修：按 Uid 增量复用 + `CardView` 自己插值。 |
| 2 | **每帧描述重算的 GC** | `CardInstance.GetDescription` | 每帧每张手牌 new 一个 `EffectContext` + 若干字符串。**本地化后又多了一次查表 + `string.Format` 分配**，更该按「依赖指纹」缓存了。 |
| ~~3~~ | ~~文案硬编码~~ | ~~UI 各处~~ | ✅ 第七次会话已修：全部走 `Loc.T`。 |
| 4 | 无召唤机制 | `BattleController.EnemyTurn` | 索引遍历 `AllUnits`，战斗中加入新单位会出问题；`BattleScreen` 也只在 Bind 时建 UnitView。 |

---

## 六之三、2026-07-25 第四次会话新增的铁律

16. **选牌效果不能把「跑完之后要做的事」写在 `ResolveAll` 的下一行。**
    结算可能挂起等玩家作答，挂起时 C# 调用栈会展开，下一行会抢在效果真正跑完之前执行。
    一律用 `ResolveAll(effects, ctx, onComplete: () => ...)` 的回调形式。
17. **组合子里不能用 for 循环内联跑子效果。** 循环变量活在调用栈上，挂起后回不来。
    改为把每次迭代压进 `ctx.Battle.Resolution`（倒序压，栈是后进先出）。
18. **`BattleContext.Selector` 非 null = 「不用问人」。** EditMode 测试、自动模拟器、
    敌人回合全靠它当场同步作答，所以它们永远不会挂起。只有 UI 把它置 null 才进入交互模式。
19. **药水的效果就是 `List<CardEffect>`，不许为药水另写效果类。**
    需要新效果类时，先问「这个效果卡牌是不是也该有」——答案通常是「是」。
20. **`DamageKind` 与 `IgnoreBlock` 是两个独立开关。**
    `Kind = Loss` 只是告诉荆棘「这不是攻击，别反弹」，**它不穿透护甲**。
    想无视护甲必须另外勾 `IgnoreBlock`。
21. **状态牌 / 诅咒牌的稀有度必须是 `Special`**，规则与升级版完全一致，
    否则会出现在战斗奖励三选一和商店里。
22. **`ContentValidator` 里凡是扫效果树的检查都必须递归进四个组合子**，
    否则「重复 3 次造成伤害」这种正常卡会被误报。

---

## 六之四、2026-07-25 第五次会话新增的铁律（手牌交互）

23. **一张牌的位姿只能有一个出口。**
    位置 / 角度 / 缩放一律由 `BattleScreen.LayoutHand` 写进 `CardView.SetLayoutTarget`，
    再由 `CardView.Update` 插值。`CardView.Refresh` 只许改颜色和文字——
    它每帧被调用，一旦在那里也写 `localScale`，就会和插值每帧对着打，缩放永远到不了目标值。

24. **`_handArea` 必须建在能量球 / 药水栏 / 日志 / 结束回合按钮之后**（uGUI 的遮挡顺序就是兄弟顺序），
    否则拖起来的牌会钻到 HUD 底下。代价是遮挡关系反过来了：
    **牌一旦压到「结束回合」按钮上就会吃掉它的点击**，所以 `HandWidth` 与按钮位置是耦合的，
    改任一边都要重算另一边（推导写在 `HandWidth` 的注释里）。

25. **拖拽状态必须能自己收，不能只依赖 `OnEndDrag`。**
    被拖的 `CardView` 可能在拖拽途中被销毁（战斗结束清手牌、某个效果把这张牌移出手牌），
    对象一死 EventSystem 就再也不会发 `OnEndDrag`，界面会永久卡在拖拽态。
    `UpdateDragVisuals` 每帧检查 `_dragCard` 是否还活着，是唯一的兜底。

26. **`OnCardEndDrag` 里必须先复位拖拽状态、再调 `TryPlayCard`。**
    出牌可能同步挂起并弹出选牌面板（铁律 16），那一刻界面必须已经不在拖拽态，
    否则箭头会浮在面板上，而且拖拽状态再没有机会复位。

27. **悬停判定区要比卡面往下多伸一块。**
    pivot 在底边、悬停会抬牌，若判定区就是卡面本身，光标停在卡面下缘时会
    「抬起 → 牌底离开光标 → 判定移出 → 落回 → 再进入」，每帧抖动。
    `CardView` 的 `HoverPad` 就是为这个存在的，别当成多余节点删掉。

28. **「当前悬停的是哪张牌」必须每帧扫，不能靠 `OnPointerEnter/Exit` 通知维护一个字段。**
    进 / 出事件的先后顺序不由我们控制，`Enter(B)` 先于 `Exit(A)` 时字段会被清成 null。

---

## 六之五、2026-07-26 第六次会话新增的铁律（Tooltip）

29. **Tooltip 的词条一律从数据推导，不做文案子串匹配。**
    「这张牌牵扯到哪些状态」走 `EffectTree.CollectStatuses` 扫效果树；
    「有哪些关键字」走 `CardKeyword` 的位。
    拿描述文本去和状态名做子串匹配看起来更省事，但文案换个措辞
    （「令目标变得脆弱」里没有「易伤」二字）词条就静默消失，而且没人会发现。

30. **凡是「悬停才出现」的 UI，都必须在 `OnDisable` 里取消。**
    光标停在上面时对象被销毁（打出这张牌、状态掉光、切界面），
    `OnPointerExit` **永远不会来**，提示框会一直挂在屏幕上指着一个不存在的东西。
    `TooltipTarget.OnDisable` 是唯一可靠的挂钩点。

31. **`TooltipView.Suppressed` 是全局静态开关，谁打开谁负责在 `OnDisable` 里放开。**
    `BattleScreen` 在拖拽时打开它；战斗界面若在拖拽途中被销毁而没放开，
    **整个游戏的 tooltip 会永久哑掉，并且不报任何错**。

32. **需要悬停的 `Text` 必须显式打开 `raycastTarget`。**
    `UIFactory.CreateText` 默认把它关掉（文字不该吃点击），
    忘了打开的话挂上去的 `TooltipTarget` 永远收不到 `OnPointerEnter`，
    表现是「这个位置就是没反应」，查起来毫无线索。

33. **两个列表「按下标一一对应」的前提是构建时没有 `continue`。**
    `UnitView` 的状态小牌子会跳过 `Def == null` 的状态，一跳过下标就整体错位，
    「易伤 2」会被写到「虚弱」的牌子上。一律另存一份 Id 列表按 Id 反查。

---

## 六之六、2026-07-26 第七次会话新增的铁律（本地化）

34. **凡是玩家看得见的文案，一律走 `Loc.T(key, 中文原文)`，中文原文留在代码 / SO 里当 fallback。**
    **不要给简体中文也建一张表。** 中文是唯一每天都在看的语言——一旦它也变成查表结果，
    key 写错时中文会显示成 key 本身或空串，而这类错误在别的语言里根本没人会发现。
    让源语言走一条**不可能坏**的路径，比形式上的对称值钱得多。
    附带好处：`Loc` 没加载表时恒等于「返回 fallback」，
    所以 166 个 EditMode 用例、自动模拟器完全不需要知道它存在。

35. **各语言的占位符集合必须与原文完全一致**，由 `ContentValidator.CheckLocalization` 报**错误**（不是警告）。
    译者把 `{0}` 翻没了、写成全角 `｛0｝`、或改成 `{2}`，`string.Format` 会在那个语言下抛
    `FormatException`——而中文下一切正常，不靠校验器就只能等玩家来报。

36. **Definition 只加 `Localized*` 访问器，绝不改既有字段。**
    字段继续存简中原文并充当 fallback，于是 `.asset` 一个字节没变、
    `SampleContentGenerator` 一行没改、`ContentValidator` 既有检查全部继续有效。
    key 由已有的 `Id` 派生，不必再发明一套编号。

37. **动词 / 名词必须当参数传进 `Loc.T`，不能在外面用字符串拼。**
    中文「选择 N 张牌 + 弃掉」，英文「Choose N card(s) to + discard」——
    成分在句子里的位置不一样，拼接表达不了这件事。
    同理，**分隔符与标点也是文案**（中文全角「，」/ 英文「, 」），
    这类「看起来是标点」的最容易漏。

38. **切语言 = 重建整棵界面**，不要让每个 `TMP_Text` 自己订阅 `Loc.LanguageChanged`。
    一个界面上百个文字节点，逐个订阅意味着每个都要记住自己的 key 和参数、
    还要在销毁时退订——忘一个就是一条指向已销毁对象的悬空委托。
    因此语言选择**只放主菜单**：在战斗里重建还要处理拖拽状态、挂起中的选牌面板、
    正在播的表现事件，不值那个复杂度。

39. **`BattleUnit.DisplayName` 现查 `EnemyDef.LocalizedName`，不用构造时存下的 `Name`。**
    凡是「构造时抄一份文案存起来」的地方，切语言后那一份都是旧的，而且没有任何东西会去更新它。

40. **`Loc.T` 的 key 与源文必须是字面量。**
    待翻译清单由 `LocalizationKeys` **扫源码**得到（不维护「UI key 注册表」——
    注册表漏一条不会报错，表现只是某个语言下突然冒出一句中文）。
    源文写成变量拼接的话，那条就永远进不了清单。
    扫描前会先剥注释，否则文档注释里的示例会被当成真调用收进去。

---

## 六之七、2026-07-26 第十次会话新增的铁律（存档）

41. **存档 = 安全时刻的完整快照，不是增量。**
    一个节点内部的所有修改，**要么整体落盘，要么整体丢弃**。
    这条不是洁癖，是唯一能吃掉「半完成态」的规则：`EventScreen.Choose` 跑完效果时
    金币和生命已经扣了，而选牌请求还挂在 UI 层的 `_pendingChoices` 队列里——
    那个队列不在 `RunContext` 里，存不下来。
    如果那一刻存过盘，读档回到事件界面就能**再选一次选项**，代价扣两次或好处拿两次。
    按快照语义，磁盘上留的是进事件之前那一份，代价与好处一起回滚，玩家重选一次，不多不少。

42. **三个存档点的刀口位置都是有讲究的，别挪。**

    | 存档点 | 位置 | 挪早了 | 挪晚了 |
    |---|---|---|---|
    | 节点 | `EnterNode` 里，锁定节点之后、`ExecuteNode` 之前 | 读档能改选别的节点 | 商店库存 / 宝箱奖励已掷骰，读档重来结果不同 = 刷商品 |
    | 战斗 | `StartBattle` 里，`Battle.StartBattle` **之前** | — | 起手牌已抽完、`RngStream.CardDraw` 已推进，读档重打就是**刷起手牌** |
    | 奖励 | `AcknowledgeBattleEnd` 里，奖励生成**之后** | 读档重掷三选一 | — |

    配套：`EnterNode` 必须在存盘前把 `Run.Phase` 写成目标阶段（**只赋值不广播**）。
    还留着 `Map` 的话，`CurrentNodeId` 已指向新节点却读档回到地图，玩家白嫖跳过一个节点。

43. **`Game.Runtime` 不认识文件系统。**
    `RunManager` 只发 `AutosaveRequested` 事件，写盘的是 `Game.UI.SaveService`。
    直接在 `RunManager` 里调 `SaveSystem.SaveRun` 的话，198 个 EditMode 用例和将来的
    自动对战模拟器每跑一局就往 `persistentDataPath` 砸一次文件。
    这是第四次会话 `InteractivePlayer` 那条教训的同一形状：**开关挂在数据/创建者身上，不挂在流程里**。

44. **往 `RunContext` 加字段，必须同步 `RunSave` / `RunSaveWriter` / `RunSaveReader`。**
    这是存档系统的头号死因：没有任何东西会提醒你，表现是「读档后某个东西回到了初始值」，
    往往几周后才被发现。`SaveSystemTests.EveryRunContextFieldIsAccountedForBySave`
    用反射把字段集合钉死了——加字段会当场变红，逼你在「存它」与
    「明确决定不存它（写进 `notSaved` 并说明理由）」之间选一个。
    另一条 `LoadedRun_ContinuesExactlyLikeAnUninterruptedRun` 从行为上兜同一件事，两者互补。

45. **`CardSave.DefId` 存的是**当前**的 `Def.Id`，不是基础版的 Id。**
    `CardInstance.Upgrade()` 是把 `Def` 换成 `Def.UpgradedVersion`，所以升过级的「打击」
    它的 `Def.Id` 已经是 `strike_plus`。架构文档 03 那份两年前的草稿写的是
    「存基础 Id + 读档时 `Upgrade()` N 次」，照抄会**双重升级**。
    还原走 `CardInstance.Restore(uid, def, upgradeLevel)`，只补计数不再升级。

46. **存档格式有三条硬约束**：
    ① DTO **不复用运行时类型**（运行时类的集合全是 `readonly`，装的又是 SO 引用；
    有独立 DTO，改运行时类会在映射处**编译不过**，而不是默默产出读不回来的存档）；
    ② 枚举**写成字符串**（写成整数的话，往 `MapNodeType` 中间插一个新类型，
    所有老存档的节点类型会整体偏移一位——商店变成事件，且不报任何错）；
    ③ 写盘必须**原子**（`.tmp` → `File.Replace` → `.bak`）：直接覆写正档时写到一半断电，
    玩家整局游戏就没了。

---

## 六之八、2026-07-26 第十一次会话新增的铁律（美术接线）

47. **所有美术都是「有图才换，没图走原路」。**
    六个 `Sprite` 字段（`CardDefinition.Art` / `EnemyDefinition.Art` / `EventDefinition.Art` /
    `RelicDefinition.Icon` / `PotionDefinition.Icon` / `StatusDefinition.Icon`）两年前就存在，
    这次只是接线。**没配图的资产必须与接美术之前逐像素相同**——
    57 张卡现在一张图都没有，如果接线顺手改了无图时的排版，
    一次可选的美化就变成了一次全卡池的排版回归。

48. **图标不许铺满它的底板。**
    遗物栏的触发闪光、药水栏的选中态、地图节点的可走/已走/锁定，
    全都是靠 tween **底板颜色**表达的。图标铺满 = 把唯一的可见反馈盖掉，
    而且不报任何错。一律留一圈边（遗物 3px、地图 8px），闪光就变成发亮的外框。

49. **`UnitView` 的「提亮」类效果必须走提亮层，不能写 `_bg.color`。**
    受击白闪、护甲蓝闪、可选中高亮原本都是 `Color.Lerp(c, 亮色, t)` 写进 Body 底色的，
    而敌人立绘一铺上去就把底色整个盖住——**所有配了立绘的敌人，打击反馈会全部静默消失**。
    现在拆成两半：乘算的那一半（死亡变暗 / 被指向染黄 / 残血脉动）继续写颜色，
    叠加的那一半写 `_tintImage.color` 的 alpha。
    两者数学上等价（不透明底色上叠 alpha t 的亮色 = `Lerp(c, 亮色, t)`），
    所以没有立绘的单位观感一点没变。

50. **`Image.preserveAspect` 是 letterbox，不是 cover。**
    一张 512×768 的立绘用 `preserveAspect` 塞进 170×96 的横窗，会缩成中间一条细竖图
    两边大片空白，看起来就像图根本没挂上。要裁切填满只能自己按宽高比算贴宽还是贴高，
    另一边溢出交给 `RectMask2D` 裁掉——这就是 `UIFactory.CreateArtWindow`。
    纵向溢出默认**保头不保脚**：人物图裁中间往往正好把脸切掉。

51. **缺图既不算错误也不算警告。**
    本工程把「0 错误 0 警告」当健康信号。把 57 张没画的卡记成 57 条警告，
    等于当场废掉那个信号——真正该被看见的几条（占位符不匹配、关键字没定义）会淹死在里面。
    `ContentValidator.ReportArtCoverage` 单独打一条 Log 报覆盖率。

---

## 六之九、2026-07-26 第十二次会话新增的铁律（牌堆浏览）

52. **`DeckController` 的四个牌堆是 `public readonly List`——`readonly` 只锁引用不锁内容。**
    UI 要排序 / 筛选 / 反转，一律先 `new List<>(pile)` 复制一份。
    就地 `pile.Sort(...)` 会**真的改掉玩家的抽牌顺序**：编译通过、不抛异常、
    `Game.UI` 也没有测试程序集能覆盖它，而表象只是「这局的运气有点怪」。
    这是铁律 9（UI 只读）唯一一处可以被静默违反的地方——别的越权写入都会撞上
    `BattleController` 那三个入口，只有集合内容是敞开的。
    收口在 `CardListView.Arrange`，那里是唯一允许碰顺序的地方。

53. **抽牌堆是隐藏信息，展示时必须重排。**
    弃牌堆 / 消耗堆按真实顺序（它们本来就是公开信息），抽牌堆一律排序后展示：
    只回答「里面有什么」，不回答「什么时候来」。
    这条是**玩法约束**而不是审美——按真实顺序显示等于把抽牌随机性从决策层删掉，
    「下一张抽什么」从一个要承担的风险变成一条免费情报。
    排序键最后必须垫一个 `Uid`：`List.Sort` 不稳定，牌组里三张一样的「打击」
    两次打开可能换位，看起来像面板在自己闪。

54. **凡是每帧无条件赋值的全局开关，别处压下的值都会被它冲掉。**
    `BattleScreen.LateUpdate` 里那句 `TooltipView.Suppressed = 正在拖牌` 是无条件赋值，
    于是放大卡面压下的 `Suppressed` 活不过一帧，表现是「大卡开着，
    底下网格的 tooltip 照样从大卡背后冒出来」。
    这是铁律 31 的另一面：全局静态开关不止「忘了放开」会坏，
    **「被别人每帧覆盖」也会坏，而且更难看出来**——因为两边的代码单独看都是对的。
    新增压制方必须把自己的诉求 OR 进那一处赋值（见 `CardListView.SuppressesTooltip`）。

---

## 六之十、2026-07-26 第十三次会话新增的铁律（逐张发牌）

55. **手牌视图曾经是全工程唯一一处「表现不跟事件队列走」的地方。**
    血条、护甲、飘字、震屏全都由 `BattlePresenter` 从 `BattleContext.Events` 里一条条取出来播
    （见 `UnitView._shownHp`），唯独 `BattleScreen.RefreshHandViews` 直接读 `Deck.Hand`。
    而战斗逻辑是同步的——`BeginTurn` 里 `Deck.Draw(5)` 是个纯 for 循环，返回时 5 张牌已经全在手上。
    **凡是「表现直接读逻辑状态」的地方，都会在逻辑同步完成的那一帧把整批变化一次性吐出来**，
    再精致的单体动画也救不回来。要分批就必须接回队列。

56. **「这条事件播过了没有」用扫队列回答，不要维护一个「已经播过」的集合。**
    集合是跨帧状态，于是必须自己回答三个问题：换战斗时谁清、读档时谁清、战斗结束时谁清；
    而且任何一条没发出来的事件都会让那张牌**永远不出现，且不报任何错**。
    扫队列没有状态：队列播空 → 集合天然为空 → 手上所有牌必然可见，兜底是白送的。
    `Ctx.Events` 是具体的 `Queue<BattleEvent>`，`foreach` 用 struct 枚举器，零 GC。
    这条是铁律 31 / 54 的正面版本：**能不留状态就别留**。

57. **打出的牌同时命中「飞向目标」和「飞向弃牌堆」两条路径，必须让前者赢。**
    `FinishPlay` → `SendCardToDestination` → `Deck.Discard`，所以一张打出去的牌
    **既发 `CardPlayed` 也发 `CardDiscarded`**。被弃牌动画抢走的话，敌人闪白、被击退、
    飘出数字，而画面上没有任何东西指向它——`CardFlyOut` 整个类就是为了补上这句因果。

58. **一张牌的归宿从「它现在真的在哪一堆」读，不从事件类型猜。**
    `CardExhausted` 与 `CardDiscarded` 在队列里长得一样（都只带 Uid），
    而一张牌任何时刻只属于一个牌堆，直接问 `Deck.ExhaustPile.Contains(card)` 是唯一不会答错的问法。

59. **「快进」必须是加速，不能是跳过。**
    这条是 `MaxBacklogRate` 那段注释的延伸：五段攻击、全场中毒结算这些**最该有打击感**的时刻，
    恰恰是队列最长的时刻，一「跳过」就等于把它们全部删掉。
    `RequestFastForward` 只是把 `PlaybackRate` 临时乘 8——5 张牌 0.45 秒变 0.056 秒 ≈ 3 帧，
    观感上就是「立刻发完」，但每一条仍然播过。
    ⚠️ 复位必须写在 `Update` 里推进之前的「队列空了」分支：忘了的话
    **点过一次之后整场战斗都在 8 倍速**，而且不报任何错。

60. **`Image.DOFade` / `DOAnchorPos` 这些扩展在 `DOTweenModuleUI.cs` 里，`Game.UI` 引用不到。**
    那些模块脚本没有 asmdef、编进 `Assembly-CSharp`，而 `Game.UI` 是 asmdef 程序集。
    核心 `DOTween.dll` 里的 `DOTween.To` / `DOScale` / `DOPunchScale` / `DOLocalRotate` / `DOVirtual` 才够得着。
    `UnitView.PlayHit` 两年前就踩过一次，这次 `PileFlyFx` 又踩了一次——**写 tween 前先看一眼它在哪个文件里**。

61. **被 `SetPileButton` 每帧无条件重写的东西，不要在别处 tween 它。**
    牌堆按钮的「收到一张牌」反馈只能动 `scale`，不能动颜色：底色被
    `RefreshPileButtons` → `SetPileButton` 每帧写一次（表现播放期间要置灰），
    在别处 tween 颜色活不过一帧。这是铁律 54 的同一形状，只是换了个开关。

---

## 七、会话记录

### 2026-07-24 — 架构设计（第一次会话）

- 读取并分析了两个本地反编译工程：
  - `D:\_A QuickStart\Unity & Game\Unity Source Code Sample\ChronoArk`（2246 个 .cs）
  - `D:\_A QuickStart\Unity & Game\Unity Source Code Sample\Monster Train`（2432 个 .cs）
- 产出 `Docs/Architecture/` 下 7 份文档（分析 / 总览 / 类清单 / 代码 / 流程 / 示例 / 路线图）。
- 关键设计决策：
  - 效果系统选 **`[SerializeReference]` 多态类 + 组合子**（否决了「一效果一类」333 类方案、
    SO 资产方案、Command 模式、外部表格方案）。代价是不支持 Mod 热加载。
  - 事件机制借鉴 Chrono Ark 的 `IReturn<T>` 拉取式派发，把 55 个单方法接口压成 **8 个多方法接口**。
  - 逻辑同步 + 表现事件队列（两个参考工程都是全链路 `IEnumerator`，无法测试）。
  - 主动否决了需求里点名的 `EffectResult` 类型（会变成没人读的空壳），理由写在文档 06 的 9.11。
  - 明确不做战斗中途存档，只在节点级安全点存。

### 2026-07-24 — 阶段 1~3 实施（第二次会话）

**做了什么**

1. 建立四个 asmdef，用编译器强制「逻辑不依赖 UI」。
2. 按文档实现 `Assets/Game/` 下 54 个 .cs。
3. 表现层全部程序化构建，**不需要任何 prefab / 手工连线**：
   `BattleBootstrap` → 建 `RunContext` → `BattleController.StartBattle` → `BattleScreen.Bind`。
4. Editor 三个工具：示例内容生成器、战斗场景生成器、架构规则校验器。
5. 35 个 EditMode 测试，覆盖伤害管线、护甲、状态顺序、关键字、X 费、组合子、递归保护、
   敌人 AI（固定序列 / 连续限制 / 阶段掩码）、意图预览一致性、随机确定性。

**验证方式**

- 用 Unity 托管 DLL 建临时 csproj 做编译检查：Runtime / UI / Editor / Tests 全部 0 error 0 warning。
- 因为本机 Unity 编辑器占用工程锁，把 `Assets/Packages/ProjectSettings` 复制到临时目录，
  用 `-batchmode -runTests` 跑 EditMode 测试：**35/35 通过，退出码 0**。

**实施中发现并修正的 4 个真实问题**（文档已同步）

| # | 问题 | 修正 |
|---|---|---|
| 1 | `Collect<T>()` 返回共享 List，遍历中重入同类型 Collect 会被清空 | 改为返回 `HookScope<T>`（ref struct + Dispose 归还池），调用点写 `using var` |
| 2 | `IDamageHook.OnDamaged` 拿不到伤害类型 → 荆棘反弹荆棘伤害，两个带荆棘的单位互打乒乓到 512 次守卫上限 | 参数改为 `in DamageInfo dmg`；`ThornsBehaviour` 只反弹 `DamageKind.Attack`。**由测试 `Thorns_DoesNotRecurseInfinitely` 抓出** |
| 3 | UI 每帧调 `CanPlayCard` 会解析 `RandomEnemy` 目标并消耗随机流 → 随机结果随帧率漂移 | 新增 `EffectContext.PreviewMode`，预览路径禁止消耗随机流 |
| 4 | 工程 `activeInputHandler = 1`，旧 `UnityEngine.Input` 与 `StandaloneInputModule` 运行时会抛异常 | 新增 `Game.UI.InputCompat`，三种输入设置下都能跑 |

另外用 `List.Sort` 排 Hook 顺序会因为不稳定排序破坏确定性，已改成稳定的插入排序。

**已知限制 / 尚未验证**

- 运行时 UI 只在编译层面验证过，**尚未在 Play 模式下实际点过**（本机 Unity 被占用）。
  首次运行若报错，优先看：字体（`UIFactory.Font` 找不到中文字体时会退回内置字体，中文可能显示成方块）、
  `EventSystem` 是否正确挂上 `InputSystemUIInputModule`。
- `DiscardEffect.ChooseByPlayer` 与 `ExhaustEffect` 的玩家选牌模式目前按随机处理，
  等阶段 4 接入手牌选择 UI 后再补。
- 遗物系统只预留了 `BattleContext.RelicBehaviours` 字段，尚无 `RelicDefinition`。

### 2026-07-25 — 阶段 4 实施（第三次会话）

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 前置重构 | 补齐关键前置项（新 Hook / 去 MonoBehaviour / 去 static Uid） |
| 场景结构 | 单场景 + Screen 切换 |
| 地图规模 | 单层完整地图（15 行） |
| 局外效果 | 新建 `RunEffect` 多态体系 |

**做了什么**

1. **补齐 4 个 Hook 拦截点**（刻意做成 4 个独立小接口，而不是往已有 8 个接口上加方法——
   加方法会逼所有既有实现类补空方法）：
   - `IStatusHook`：状态施加的拦截 → 神器、免疫减益
   - `IFatalHook`：致死伤害的拦截 → 不死图腾、濒死回血
   - `ICardFlowHook`：`PreCardPlay`（取消 / 回响）+ `ModifyCardDestination`（改归宿）
   - `IResourceHook`：每回合抽牌数 / 能量数
   - `Collect<T>(includeDead)`：让濒死单位收得到自己的死亡触发器（「亡语」原本做不出来）
2. **`BattleController` 改纯 C# 类**，`CardInstance` / `BattleUnit` 的 static Uid 计数器
   分别挪进 `RunContext` / `BattleContext`。解锁了并行模拟与阶段 5 的存档。
3. **地图**：`MapGenerator` 拉 6 条路径生成 15 行分层图，天然保证「每个节点都在通路上」。
   全部走 `RngStream.Map`。
4. **`RunEffect` 局外效果体系**：11 个效果类 + `RunCondition` + 组合子，
   事件 / 商店 / 休息 / 宝箱共用。加一个新事件 = 建一个资产，零代码。
5. **遗物**：`RelicDefinition.Behaviours` 的元素类型就是 `StatusBehaviour`，与状态**完全共用**
   那套 Hook。16 个遗物，`BattleController` 一行没改。
6. **8 个局外界面** + 通用选牌面板，全部程序化 uGUI，单场景切换。
7. Editor：内容生成器拆成三个文件；新增 `MainSceneBuilder`；`ContentValidator` 加了
   遗物 / 事件 / 奖励池三组检查与 CI 入口。

**验证方式**

- 用 Unity 托管 DLL 建临时 csproj：Runtime / UI / Editor / Tests 四个程序集全部 0 error 0 warning。
- 本机 Unity 编辑器占用工程锁，复制 `Assets/Packages/ProjectSettings` 到临时目录跑 batchmode：
  - **EditMode 测试 86/86 通过**（原 35 + 新增 51），退出码 0。
  - 内容生成 + 校验：**0 错误 0 警告**。

**实施中发现并修正的 5 个真实问题**

| # | 问题 | 修正 |
|---|---|---|
| 1 | `FireDelayed` 的 for 每轮重读 `Count`，「回合结束时再排一个回合结束效果」会当场无限执行 | 先把本次要跑的项摘出来再执行，新排入的留到下一次 |
| 2 | `BattleController` 的 `_playCtx` 被 `CanPlayCard`（UI 每帧调）与结算共用，效果结算途中若间接触发查询会把 Targets/XValue 冲掉 | 拆出独立的 `_previewCtx` |
| 3 | 升级版卡牌（`*_plus`）默认稀有度 Common，会和原版一起出现在奖励三选一和商店里 | 一律标 `Special`，`GetCardsByRarity(null)` 排除 |
| 4 | `ContentValidator` 在 batchmode 下 `FindAssets("t:XXX")` 恒返回 0，于是「全部通过」——假通过比没有校验更危险 | 加目录扫描回退 + `ValidateBatch` CI 入口（有错误退出码 1） |
| 5 | `bladedance` 声明 `SingleEnemy` 但效果打的是 `RandomEnemy`，玩家被要求选目标然后选择被忽略 | 改成 `TargetKind.None`（由新加的校验规则抓出） |

另外顺手修了三处旧问题：`TickStatusDecay` 不再给死者减层（与 `Collect` 语义对齐）、
手牌满时加牌会如实上报最终落点（原本谎报进了手牌）、
`BattleContext.MaxTurns` 兜底防止双方都打不动时战斗永不结束（自动模拟会挂死）。

**已知限制 / 尚未验证**

- 局外 UI 与战斗 UI 一样，**只在编译层面与 EditMode 逻辑层面验证过，尚未在 Play 模式下实际点过**
  （本机 Unity 被占用）。首次运行若报错，优先看字体与 `EventSystem`。
- 阶段 4 遗留的 6 项见上面「六之二」，其中第 1、2 条是玩家能直接感知到的。

### 2026-07-25 — 阶段 4 首次 Play 模式试玩修复（第三次会话续）

使用者在 Play 模式下试玩，报告：进入战斗后**看不到敌人，选了牌也没有目标可点**（手牌、能量、结束回合按钮都正常）。

**根因**：`RunManager.StartBattle` 里 `SetPhase(RunPhase.Battle)` 写在 `Battle.StartBattle(...)` **之前**。
`SetPhase` 是同步的——它立刻触发 `PhaseChanged` → `GameApp` 建出 `BattleHostScreen` →
`BattleScreen.Bind()` 执行，而此刻 `Battle.Ctx` 还是 null，`BuildUnitViews()` 直接 return。
手牌因为在 `LateUpdate` 里每帧刷新所以照常出现，单位面板却只在 `Bind` 时建一次，于是永远缺席。
故障表象（「敌人不见了」）离根因（「界面绑早了」）很远。

**修复**

1. `RunManager.StartBattle`：先 `Battle.StartBattle(...)`，再 `SetPhase(...)`。
2. `BattleScreen`：新增 `_boundCtx`，在 `LateUpdate` 里检测到 `Ctx` 变化就补建单位面板并重新
   `Init` 表现层。这样即使将来又有人把界面绑到一个尚未开始的战斗上，也只是晚一帧而不是永久残废。
3. 新增回归测试 `RunFlowTests.PhaseChanged_FiresOnlyAfterItsDataIsReady`：
   订阅 `PhaseChanged`，在**广播的那一刻**断言各阶段界面需要的数据都已就位
   （Battle → `Ctx`/`Player`/敌人；Reward/Treasure → `PendingReward`；Shop → 库存；Event → 事件配置），
   并把地图上每一类节点都走一遍。
   **已验证：把顺序改回旧写法，这条测试会失败；改回来则 86/86 通过。**

**教训**：原有的 19 个局外流程用例全部只在 `EnterNode` 返回**之后**检查状态，
因此对「广播时数据还没准备好」这类时序问题完全免疫。
凡是同步广播的事件，测试必须在回调里断言，而不是在调用返回后断言。

### 2026-07-25 — 第二轮 Play 模式修复（第三次会话续）

使用者报告：从事件里选完选项后**事件界面没有关闭**，回到地图还能看到事件的文字和按钮。
截图显示的实际情况更严重：**主菜单、结算界面、战斗界面三个同时活着并互相叠字**。

**根因（界面泄漏）**：`ScreenBase.Initialize` 把 UI 根建在 `_screenLayer` 下，
而 `ScreenBase` 组件挂在 `GameApp` 用 `NewScreen<T>()` 另建的 GameObject 上。
`GameApp.OnPhaseChanged` 里 `Destroy(_current.gameObject)` 销毁的是那个空壳组件，
**整棵 UI 树原封不动地留在 `_screenLayer` 里**——每切一次界面就叠一层，永不回收。
「事件 UI 没关」只是这个 bug 最先被看见的表现。

**修复**

| # | 改动 | 说明 |
|---|---|---|
| 1 | `ScreenBase.Initialize(app)`：`Root` 改为组件自己的 `RectTransform` | 组件与它的 UI 根合并成同一个 GameObject，销毁必然连带 |
| 2 | `GameApp.NewScreen<T>()`：在 `_screenLayer` 下建根，再 `AddComponent<T>` 到根上 | 同上 |
| 3 | `CardPickerScreen` 同样合并；`GameApp` 记住 `_activePicker`，切界面时一并销毁 | 否则选牌面板会浮在新界面上 |
| 4 | `CardPickerScreen.Close`：先清空回调再销毁 | 回调里常会立刻再开一个面板（事件的连续选择），顺序会互相踩 |
| 5 | `BattleHostScreen`：战斗界面挂在自己的子 `RectTransform` 上 | 原本在 Canvas 里塞了个纯 `Transform` 子节点 |

**同时修掉了原「已知遗留」的第 1、2 条**

- **意图数值实时更新**：`BattleContext.StateVersion`（`Post` 时递增）+
  `EnemyBrain.RefreshIntentValue`（只重算数值、**不重选行动**，否则玩家能靠刷新骗 AI 换招）+
  `BattleController.RefreshIntents`。`BattleScreen` 在 `LateUpdate` 里发现版本变化才重算，
  不是每帧无脑算。`BuildIntent` 补上 `PreviewMode = true`（铁律 4）。
  新增 3 个用例：数值随虚弱变化且等于实际掉血、重算不改行动、重算不消耗随机流。
- **表现播放期间锁输入**：`BattleScreen.InputLocked`（事件队列非空且 presenter 活着）。
  出牌 / 选目标 / 结束回合 / 键盘全部受控，手牌同时变灰、结束回合按钮置灰。
  取消选择不锁（玩家随时该能反悔）；正在选目标时按空格改为先取消选择而不是结束回合。

**顺带修的公平性问题**：事件里的删卡 / 升级卡面板改成**不可取消**——
代价（金币）在效果里已经扣掉了，允许取消等于白扣玩家的钱。
休息点那种「还没付代价」的地方仍然可以取消。

**验证**：四个程序集 0 error 0 warning，EditMode **89/89 通过**。
界面泄漏本身是 MonoBehaviour 层的问题，EditMode 测不到，只能靠 Play 模式确认。

### 2026-07-25 — 第四次会话：选牌 / 药水 / 三批内容

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 选牌的阻塞模型 | **A. 续延挂起**（否决了「出牌前一次问完」与「只支持卡牌级选牌」） |
| 药水范围 | 完整版（槽位 / 目标 / 掉落 / 商店 / 事件全通） |
| 内容量 | 中量，卡池 24 → 57 |
| Git 流程 | 每个 feature 一个分支，`--no-ff` 合回 main，分支全部保留并推送 |

**六个分支（全部已合并进 main，均未删除）**

| 分支 | 内容 |
|---|---|
| `feature/in-battle-card-selection` | 可挂起的结算栈 + `SelectCardsEffect` |
| `feature/potion-system` | 药水系统 |
| `feature/curse-status-cards` | 诅咒牌 / 状态牌 |
| `feature/keyword-cards` | Retain / Innate / Ethereal |
| `feature/combinator-cards` | 四个组合子的实战卡 |
| `feature/selection-cards` | 选牌机制的实际卡牌 |

**架构上唯一的大改：`EffectResolutionStack`**

原来「跑到第几个效果」这个状态活在 C# 调用栈上。为了等玩家选牌而返回时，
栈会展开——组合子里剩下的循环回不来，写在 `ResolveAll` 下一行的收尾代码还会抢跑。
现在把它显式存进帧栈：挂起 = 停止 Pump，恢复 = 继续 Pump，调用栈长什么样完全无关。

代价是三个组合子必须改写成「压栈」而不是「内联循环」，
`BattleController` 的出牌收尾也改成了回调链。
**`BattleContext.Selector` 是让这次改造零回归的关键**：非 null 表示「不用问人」，
测试 / 模拟器 / 敌人回合全部当场同步作答，因此既有 89 个用例一行未改。

**实施中被测试抓出的 4 个真实问题**

| # | 问题 | 修正 |
|---|---|---|
| 1 | `LastCardTypePlayed` 在结算**之前**就被设成当前卡类型，于是「若上一张是攻击牌」在任何攻击牌上恒为真，条件形同虚设 | 赋值挪到 `FinishPlay`（结算完成后） |
| 2 | `DamageKind.Loss` 并不穿透护甲——它只影响荆棘判定 | 灼烧类效果另勾 `IgnoreBlock` |
| 3 | 「留在手上的代价」放在状态衰减之前，疑虑施加的 1 层虚弱会被同一次衰减当场扣掉 | `EndTurn` 重排：衰减 → 留手代价 → 清理手牌 |
| 4 | `ContentValidator` 的 ChosenTarget 检查不递归进组合子，把「重复 3 次造成伤害」误报成配错 | 改为递归覆盖四个组合子 |

**验证**

- 四个程序集 0 error 0 warning（Unity 编辑器占锁，用托管 DLL 建临时 csproj 编译）
- EditMode **164/164 通过**（原 89 + 新增 75），在工程副本里 batchmode 跑
- 内容生成 57 张卡 / 10 瓶药水 / 7 个事件，`ContentValidator` **0 错误 0 警告**

**已知限制 / 尚未验证**

- ⚠️ **`Assets/GameData/` 下的新资产尚未在真实工程里生成**——本机 Unity 编辑器占着工程锁，
  生成与校验都是在工程副本 `D:\UnityAiProject\_TestCopy` 里跑的。
  使用者需要在自己的 Unity 里跑一次菜单 `Tools/卡牌游戏/1. 生成示例内容`。
- 药水栏、选牌面板、奖励/商店的药水行**只在编译与 EditMode 逻辑层验证过，尚未在 Play 模式下点过**。
- `RunContext` 新增了 `Potions` / `PotionSlots` / `_nextPotionUid`，阶段 5 的存档要一并序列化。

### 2026-07-25 — 第四次会话续：Play 模式修复（交互模式接线 + 药水栏可用性）

使用者试玩后报告：**拿到药水但不知道在哪用、点不动；「映写」「弃牌」这类选牌卡打出后系统没让选**。

**根因（一个，两个症状）：交互模式的开关从未被打开。**
`BattleScreen.Bind` 结尾有 `_boundCtx = Ctx;`，而 `Ctx.Selector = null`
（「不要替玩家选，挂起等他点」）只写在 `LateUpdate` 的「Ctx 变化了」分支里。
第三次会话修过 `RunManager.StartBattle` 的顺序，现在 Bind 时 `Ctx` 已经非 null，
于是 `_boundCtx == Ctx` 从一开始就成立，**那个分支永不执行**——
选择器一直是默认的 `RandomCardSelector`，所有选牌被系统静默地随机决定。
界面上毫无异常，只有玩家会觉得「怎么不问我」。

**修法：把开关从界面的每帧循环挪到数据源头。**
新增 `RunContext.InteractivePlayer`，由**创建这一局的人**设置
（`GameApp.StartNewRun` / `BattleBootstrap` 置 true，测试与模拟器保持 false），
`BattleController.StartBattle` 据此一次性决定 `Ctx.Selector`。
界面的绑定时机会变，但「谁开的这一局」不会变。

顺带修掉同一处的结构性隐患：`BattleScreen` 的上下文接管拆在 `Bind` 与 `LateUpdate`
两半，而后者永不执行——**任何加在那里的新逻辑都会被静默跳过**。
现在合并成单一的 `AdoptContext()`。

**药水栏**（原本能渲染也能点，但基本不可发现）：加「药水」标题、空槽显示「空槽」、
改成「先点选看说明、再确认使用」两步、选中的药水高亮且提示常驻
（原提示 2 秒消失，玩家看不出自己正拿着一瓶药水在找目标）。

**教训**：这个 bug 逃过了 17 个选牌用例，因为它们直接设 `Ctx.Selector = null`，
**绕开了「谁来设」这一步**。EditMode 测不到 MonoBehaviour，
所以凡是「UI 负责打开某个逻辑层开关」的设计都是脆的——开关要放在数据上，才钉得住。
已补 2 个用例：`InteractiveRun_SuspendsWithoutAnyUiInvolvement` / `NonInteractiveRun_KeepsTheAutomaticSelector`。

**验证**：EditMode **166/166 通过**。生成器产出的 57 张卡 / 10 瓶药水 / 7 个事件资产已提交。

### 2026-07-25 — 第五次会话：手牌扇形排列 + 拖拽出牌

对应 `Docs/Ideas-Backlog.md` 的 **C5**（顺带做掉 **C3** / 原「已知遗留 #1」）。

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 拖拽的目标指定方式 | **A. 尖塔式箭头**（需目标的牌举起来拉贝塞尔箭头；不需目标的牌跟手、拖过出牌线松手） |
| 点击出牌 | 保留，与拖拽双轨并存 |
| 手牌视图 | 改成增量复用 + 插值归位（= 顺手修掉遗留 #1） |
| EditMode 测试 | 不写（纯手感代码，靠 Play 模式验证；166 个既有用例保持不变） |

尖塔式方案与 `Docs/Architecture/04-完整执行流程.md` 第 5 节两年前写下的规划一致
（`CardView.OnBeginDrag` → SingleEnemy 画箭头 / 其余拖到出牌区），本次算是把它兑现。

**做了什么**

1. **`HandFanLayout`（新）**：扇形排布的纯计算。
   刻意**没用**圆周公式——纯圆周的横向间距由半径决定，手牌数一变间距就跟着变，
   很难同时满足「2 张牌不要离太远」和「12 张牌不要溢出屏幕」。
   改成「横向按间距线性排 + 纵向抛物线下沉 + 倾角按归一化位置线性插值」，
   看起来和圆弧没区别，但三条曲线各自独立可调。牌多时自动压缩间距。
2. **`TargetArrowView`（新）**：`MaskableGraphic` 子类，在 `OnPopulateMesh` 里自己生成
   「由细到粗的二次贝塞尔带 + 三角箭头」。不用 `LineRenderer`（世界空间的，和 Overlay Canvas 对不上），
   也不用「沿曲线摆一串小圆点 Image」（几十个 Graphic 每帧改位置，重建开销远大于一次 mesh）。
   指到合法目标时从黄色变红色。
3. **`CardView`**：加 `IBeginDrag/IDrag/IEndDragHandler`；位姿改成「BattleScreen 写目标、自己指数插值」；
   pivot 移到底边中点（扇形要绕握牌那端转）；加 `HoverPad` 悬停判定垫。
4. **`BattleScreen`**：
   - `RefreshHandViews` 改增量复用（`Uid → CardView` 字典），新牌从抽牌堆位置飞入；
   - `LayoutHand` 用 `HandFanLayout` 算目标位姿，并处理悬停 / 选中 / 拖拽三种抬牌；
   - 兄弟顺序 = 遮挡顺序，悬停/拖拽那张提到最前，只在「谁在最前」变化时才重排；
   - 新增拖拽状态机（`DragMode.Aim` / `Free`）、出牌线、箭头、目标锁定；
   - 右键 / Esc / 空格统一走 `CancelTargeting()`（牌、药水、拖拽一起取消）。

**实施中发现并修正的 3 个真实问题**

| # | 问题 | 修正 |
|---|---|---|
| 1 | `_handArea` 为了不被 HUD 盖住而移到能量球/日志/按钮**之后**建，遮挡关系随之反转——手牌 12 张时最右那张会压在「结束回合」按钮上并**吃掉它的点击** | `HandWidth` 从 1600 收到 1360，让最外侧牌的右边缘停在按钮左边缘前 20px；推导过程写进常量注释，并立为铁律 24 |
| 2 | `TargetArrowView` 的公开字段名 `Start` 与 `UIBehaviour.Start()` 撞名（编译器 CS0108 警告） | 改名 `From` / `To`。工程要求 0 警告，这条不能留 |
| 3 | 「当前悬停的是哪张牌」原本靠 `OnPointerEnter/Exit` 通知维护字段，而进/出事件的先后顺序不受我们控制 | 改成 `LayoutHand` 每帧扫 `CardView.Hovered`，与事件顺序无关（铁律 28） |

另外两处是写的时候就避开的坑，记在铁律 25 / 26 / 27：拖拽中卡牌被销毁时 `OnEndDrag` 不会来、
出牌前必须先复位拖拽状态（否则会和挂起的选牌面板打架）、悬停判定要留垫子防抖。

**验证**

- 四个程序集 **0 error 0 warning**（Unity 编辑器占锁，用 VS2022 的 MSBuild 编译 Unity 生成的 csproj）
- `Game.Runtime` **一行未改**，`Assets/Tests/` 未改 → 166 个 EditMode 用例不受影响
- ⚠️ **手感本身只能在 Play 模式下确认**：扇形弧度 / 倾角 / 插值速度 / 举牌位高度 / 出牌线高度
  全部是纯表现参数，EditMode 测不到。要调的话集中在两处：
  `HandFanLayout` 顶部的 5 个常量、`BattleScreen` 的 `HoverLift` / `SelectedLift` / `PlayLineY` / `AimSlot`，
  以及 `CardView.FollowSpeed`（越大越「硬」）。

### 2026-07-26 — 第六次会话：关键字 / 状态 Tooltip

对应 `Docs/Ideas-Backlog.md` 的 **C2**。

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 覆盖范围 | **全都要**：手牌 / 单位面板上的状态 / 敌人意图 / 遗物栏 / 药水栏 |
| 「这张牌涉及哪些词条」怎么定位 | **扫效果树 + 整牌悬停**（否决了文案子串匹配与逐词热区） |
| 触发与定位 | 悬停 0.25 秒后弹，面板贴着目标出现并自动避开屏幕边缘 |
| 关键字文案存哪 | **新建 `KeywordDefinition` SO**（否决了 UI 层静态表） |

> ⚠️ **必须手动跑一次 `Tools/卡牌游戏/1. 生成示例内容`**，
> 否则 `Assets/GameData/Keywords/` 下的 5 个资产不存在，关键字的悬停解释会静默消失。
> 校验器会把这种情况报成警告。

**做了什么**

1. **`EffectTree`（新，Runtime）**：效果树的唯一递归遍历入口。
   铁律 22 原本只是一句「记得递归进四个组合子」，靠人记；现在递归收在一个类里，
   将来加组合子只要改这一处，而不用去翻所有扫效果树的调用点。
   目前提供 `CollectStatuses`（Tooltip 用它回答「这张牌牵扯到哪些状态」）。
2. **`KeywordDefinition`（新，Runtime）** + `GameDatabase.Keywords` / `GetKeyword(CardKeyword)`。
   索引用**枚举位**当键而不是字符串 Id，且只收单一位的定义
   （`CardKeyword` 是 `[Flags]`，组合值按位反查永远匹配不到，收进来只会制造一个查不出的谜）。
3. **`StatusDefinition.DescribeGeneric()`**：`{stacks}` 渲染成 `X`。
   卡牌 tooltip 用它——那时玩家还没把状态挂上去，退而用 `Describe(1)` 会写出
   「回合结束回复 1 点生命」这种看起来很确定、其实是编的数字。
4. **通用 tooltip（新，UI）**：`TooltipEntry` / `ITooltipSource` / `TooltipTarget` / `TooltipView` / `TooltipContent`。
   `TooltipView` 自建一个 `sortingOrder = 5000` 的独立 Canvas 并**关掉 GraphicRaycaster**：
   - 独立 Canvas → 战斗界面、局外界面、选牌模态框、单场战斗调试场景（那里没有 GameApp）
     全都不用再回答一次「该插在哪一层之后」；
   - 关掉射线 → 提示框弹出的位置紧挨着玩家正要点的东西，能吃射线就一定会挡到点击。
5. **接线**：`CardView` 实现 `ITooltipSource`；`UnitView` 把原来那一整段状态文字
   **拆成逐条可悬停的小牌子**，并给意图加悬停；`TopBarView` 的遗物从自带的 `RelicHover`
   + 位置写死的 Text **迁到同一套**（全局只剩一种提示框样式）；药水按钮加悬停。
6. **生成器 / 校验器**：产出 5 个 `KeywordDefinition`；新增 `CheckKeywords`——
   检查单一位、无重复、有文案，以及**卡池里用到的每个关键字位都配了定义**。

**实施中发现并修正的 4 个真实问题**

| # | 问题 | 修正 |
|---|---|---|
| 1 | `EffectTree` 最初用一个共享的静态 `List<CardEffect>` 中转单个子效果，「RandomPick 的选项又是一个 RandomPick」时会自己清掉自己正在遍历的列表 | 递归改成以**单个效果**为单位，彻底不需要中转 buffer |
| 2 | `UnitView` 刷新状态牌子时按下标与 `Unit.Statuses` 对齐，但构建时会 `continue` 跳过 `Def == null` 的状态——一跳过下标就整体错位，「易伤 2」会写到「虚弱」的牌子上 | 另存一份 `_chipIds`，按 Id 反查（立为铁律 33） |
| 3 | `TooltipView.Place` 用 `corners[0]`/`corners[2]` 当左下/右上，而扇形手牌是**带旋转**的，旋转后「左下角」未必还是 x 最小的点 | 取四个角的 min/max 算真正的包围盒 |
| 4 | `TooltipContent` 缺 `using Game.Units;`（编译器抓出） | 补上 |

另外三处是写的时候就避开的坑，记在铁律 30 / 31 / 32：
悬停对象被销毁时 `OnPointerExit` 不会来、
全局 `Suppressed` 开关必须由打开者在 `OnDisable` 里放开、
`UIFactory.CreateText` 默认关闭 `raycastTarget`（意图文字必须手动打开）。

**验证**

- 四个程序集 **0 error 0 warning**（Unity 编辑器占锁，用 VS2022 的 MSBuild 编译 Unity 生成的 csproj）
- `Assets/Tests/` 未改；Runtime 侧的改动都是**新增**（`EffectTree` / `KeywordDefinition` /
  `GameDatabase.Keywords` / `StatusDefinition.DescribeGeneric`），没有改动既有行为，
  166 个 EditMode 用例的断言对象一个未动
- ⚠️ **尚未在 Play 模式下点过**。首次试玩重点看四处：
  手牌悬停 0.25 秒后是否弹、单位面板的状态小牌子排版是否溢出得难看、
  敌人意图能否悬停（`raycastTarget` 那条）、遗物提示是否还在

### 2026-07-26 — 第七次会话：本地化（zh-Hans + en）与 TextMeshPro 迁移

对应「阶段 4 已知遗留 #3」与阶段 6 的本地化项。

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 方案选型 | **自建轻量 `Loc` 层**（否决了 `com.unity.localization`） |
| 本次落地语言 | **zh-Hans（源语言）+ en**；架构 / 工具 / 校验按多语言做好，繁中 / 日文以后只是多一个资产 |
| 字体 | 按语言切系统字体候选链 |
| 文字渲染 | 顺便迁到 **TextMeshPro** |
| 英文译文 | 由 AI 直接写 |

**为什么否决官方的 `com.unity.localization`**

1. 它**强制拉入 Addressables**。本工程零 Addressables、零 prefab，所有资产挂在
   `GameDatabase` 一个 SO 根上；引入后打包管线要整个重验，收益是零。
2. 它的核心价值是 `LocalizeStringEvent` 组件——在 Inspector 里把 Text 连到表条目上。
   本工程 UI **全是运行时代码搭的**，落到实处只剩一次字典查询。
3. 表加载是 `AsyncOperationHandle`，与「逻辑同步执行」冲突。
4. 官方做法要把 `public string DisplayName` 换成 `LocalizedString`，
   会同时打穿 57 张卡的 `.asset`、生成器的 290 处赋值、校验器的全部文案检查——**且不可逆**。
5. `Game.Tests.EditMode` 只依赖 `Game.Runtime`；Runtime 一旦引用 `LocalizationSettings`，
   166 个已经绿的用例就要为本地化冒风险。

**做了什么（三笔提交，分支 `feature/localization-tmp`）**

1. **`refactor(ui)` TMP 迁移**：`UIFactory.FontAsset` 按语言取系统字体，
   用 `TMP_FontAsset.CreateFontAsset(familyName, styleName)` 建——它走
   `AtlasPopulationMode.DynamicOS`，字形在用到那一刻才从字体文件光栅化。
   中文两万多个常用字，预烘静态图集要么巨大要么缺字。
   `CreateText` 的参数仍收 `TextAnchor` 并在内部映射，几十个调用点一行未动。
2. **`feat(loc)` 核心 + Runtime**：`Loc` / `LocaleTable` / `GameDatabase.Locales`；
   各 Definition 加 `Localized*` 访问器；`RunEffect` / `CardSelection` 的句子全部走 `Loc.T`。
3. **`feat(loc)` UI + 工具链**：24 个界面文件的硬编码全换；主菜单加语言切换；
   `LocalizationKeys`（扫 SO + 扫源码）、`LocalizationTool`（菜单 5/6 导出导入 CSV）、
   `ContentValidator.CheckLocalization`。

**实施中发现并修正的 4 个真实问题**

| # | 问题 | 修正 |
|---|---|---|
| 1 | `RewardScreen.SetRow` 的 `disabledSuffix` 默认值原本是中文字面量，改成 `Loc.T(...)` 后编译不过——可选参数默认值必须是编译期常量 | 默认值改 `null`，方法内再取译文；`null` = 用默认后缀，空串 = 不要后缀 |
| 2 | `LocalizationKeys` 扫源码时把**文档注释里的示例** `Loc.T("key", "原文")` 当成真调用，凭空多出一条谁也不知道是什么的待翻译条目，并让「还有几条没翻」永远差一 | 扫描前先剥注释 |
| 3 | 卡面角标的关键字名原本是另一份硬编码，与 `KeywordDefinition` 是两份文案，改一处不会同步另一处 | 卡面与 `CardPicker` 都改用 `keyword.<位>.name`，与 tooltip 共用同一份译文 |
| 4 | 生成译文资产时，脚本按 key 与源文交叉核对，查出 17 条 `enemy.*.name` / `encounter.*.name` 根本没被收进清单 | 抽取脚本的单元素数组被 PowerShell 展平了。**这条值得记：如果只核对「译文有没有缺」而不核对「源文有没有漏收」，这 17 条会安静地永远不被翻译** |

**验证**

- 四个程序集 **0 error 0 warning**（Unity 编辑器占锁，用 VS2022 的 MSBuild 编译 Unity 生成的 csproj）
- `Assets/Tests/` 一行未改；Definition 层只加访问器不动字段，
  `Assets/GameData/` 下既有资产**一个字节没变**（只新增 `Locales/Locale_en.asset`）
  → 166 个 EditMode 用例的断言对象一个未动
- 译文 **495 条，与源文一一对应，占位符零差异**（脚本交叉核对）

**已知限制 / 尚未验证**

- ⚠️ **尚未在 Play 模式下点过。** 首次试玩重点看三处：
  ① 主菜单是否出现「简体中文 / English」两颗按钮，点 English 后整个界面是否换语言且**字体正常**；
  ② 英文下**排版是否溢出**——中文换英文文本平均膨胀 1.6–2 倍，而卡面 / 按钮尺寸是程序化写死的。
  `UIFactory.EnableAutoSize` 已经写好但**还没有接到任何调用点**，
  哪里溢出就在哪里调它（这是本次唯一确定还有活要干的地方）；
  ③ 切语言后顶栏 / 弹窗的**遮挡顺序**是否还对（`GameApp.OnLanguageChanged` 里重排了 `OverlayLayer`）。
- `Locale_en.asset` 是**脚本直接生成的 YAML**，没经过 Unity 的序列化器。
  Unity 首次导入若报错，跑一次菜单 `Tools/卡牌游戏/6. 导入本地化 CSV` 重建即可。
- 语言选择目前存 `PlayerPrefs`（key = `game.language`），阶段 5 做 `MetaSave` 时要迁进去。
- 繁体中文 / 日文：`UIFactory` 的字体候选链、`LocalizationTool` 的导出列都已经准备好，
  补一个 `LocaleTable` 资产即可，**零代码改动**。

> ⚠️ **本文件缺一次会话**：git 上「打击反馈第 0/1/2 层」三个分支与「符号字形兜底链修复」
> （`feature/feedback-foundation` / `feature/hit-feedback` / `feature/feedback-polish` /
> `fix/font-symbol-fallback`，即第九次会话）没有补进下面的记录。
> 那次的产出可以从 `git log` 与 `Game/UI/FeedbackSettings.cs`、`BattlePresenter` 读出来。

### 2026-07-26 — 第十一次会话：美术接线（六个 Sprite 字段终于有人读了）

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 卡面布局 | **尖塔式插画窗**（名字一条、插画一条、描述一条，互不重叠） |
| 范围 | **全都要**：卡牌 / 遗物 / 药水 / 状态 / 敌人立绘 / 地图节点 |
| 图片指派 | 纯手动，在 Inspector 里拖（否决了批量指派窗口与按文件名自动匹配） |

**动手前查清的三件事**

1. `CardDefinition.Art` 等**六个 `Sprite` 字段两年前就存在**，但没有任何一处 UI 读过它们。
   所以这次是接线，不是加数据。
2. **重跑生成器不会冲掉手动挂的图**：`LoadOrCreate` 有资产就复用，
   而 `MakeCard` / `MakeRelic` 等从头到尾没碰过 `Art` / `Icon`。这点本来最可能出事，结果天生是对的。
3. 使用者已经放进 `GameData/Cards/` 的那张 PNG，meta 是 `textureType: 8`（Sprite 2D and UI），
   **本来就能直接拖**。

**做了什么（分支 `feature/card-art`，三笔提交）**

1. `UIFactory.CreateArtWindow`：裁切填满的插画窗（铁律 50）。
2. `CardView` / `CardMiniView` 各加一个插画窗。有图时描述区被压掉约一半高度，
   于是给它开 `UIFactory.EnableAutoSize`——那是第七次会话为本地化写好、
   但一直**没接到任何调用点**的开关，这是它第一个真正的用处。
3. 遗物栏 / 药水栏 / 状态小牌子 / 敌人立绘 / 地图节点，六处全部「有图才换」。
4. `GameDatabase.MapIcons`：地图节点是唯一需要新增数据的一处——
   节点类型是枚举，没有对应的 SO，为 7 个固定类型各造一个 SO 只会多出 7 个几乎空白的资产。
5. `ContentValidator.ReportArtCoverage`：报覆盖率，但不计入错误 / 警告（铁律 51）。

**实施中避开的两个坑**（都会静默坏掉，不报任何错）

| # | 坑 | 处理 |
|---|---|---|
| 1 | 敌人立绘会把 Body 底色盖住，而受击白闪 / 护甲蓝闪写的正是那个底色 → 配了立绘的敌人打击反馈全部消失 | 拆出提亮层（铁律 49） |
| 2 | 遗物触发闪光、药水选中态、地图节点状态都靠 tween 底板颜色，图标铺满就全看不见 | 图标一律留边（铁律 48） |

**验证**

- 四个程序集 **0 error 0 warning**（Unity 编辑器占锁，用 VS2022 MSBuild 编译）
- ⚠️ **本次没有重跑 EditMode 198 条**：`Game.Runtime` 的改动只有
  `GameDatabase.MapIcons` 字段 + `GetMapIcon` + `MapNodeIcon` 结构体，**纯新增**；
  而 EditMode 测试只依赖 `Game.Runtime`、完全不覆盖 `Game.UI`，
  这次的改动几乎全在 UI 层，跑一遍在结构上抓不到任何东西。测试程序集编译通过。
- ⚠️ **尚未在 Play 模式下点过。** 首次试玩重点看四处：
  ① 给一张卡挂上图后，手牌与奖励三选一里是否都出现插画，描述有没有被挤到看不清；
  ② 给一个敌人挂上立绘后，**打它一下是否还会闪白**（这是铁律 49 那条唯一的验证方式）；
  ③ 给遗物挂图标后，触发时那圈边是否还会发亮；
  ④ 没挂图的卡 / 敌人 / 遗物是不是和以前一模一样。

### 2026-07-26 — 第十次会话：存档系统（阶段 5 第 1 项）

对应「六、下一步」的第 1 条。

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 战斗中途退出 | **回到该场战斗开始前，重打一次**（否决了「回地图改选节点」与「直接判定本局结束」） |
| 地图 | **整图存进存档**（先选了「只存种子」，看过故障场景后改的，见下） |
| 槽位 | 单槽自动存档 + 主菜单「继续游戏」「放弃本局」 |
| MetaSave | 只装语言，从 `PlayerPrefs` 一次性迁移 |
| 序列化 | **Newtonsoft.Json**（否决了 JsonUtility——`Dictionary` 与嵌套泛型都不支持） |
| 内容缺失 | 跳过该项 + 警告，继续读档 |
| 商店 | 也走快照，整个商店可以反悔 |
| 防篡改 | 不加，明文 JSON（开发期能拿记事本改存档复现 bug，这个价值比防作弊大） |
| 本次范围 | 只做存档，不含自动模拟器 / Fuzz / 内容量产 |

**「只存种子」被推翻的那个故障场景**

使用者一开始选的是「只存种子，读档时重新生成地图」。但
`MapGenerator.Generate(rng, cfg)` 的 `cfg` 装的是**当前数据库里有哪些 encounter / event 的 Id
及其顺序**（`RunManager.GenerateMap`），而阶段 5 的路线图第 3 条就是「内容量产：敌人 15+」。
于是**加一个新敌人 → 老存档读出来是另一张地图**，而 `CurrentNodeId` / `VisitedNodeIds`
是按下标索引的，玩家会被瞬移到一个类型完全不同的节点上——**全程不报任何错**。
省下的只有约 5 KB 和一个 40 行的 DTO。摆出这个场景后改成了存整图。

**做了什么（分支 `feature/save-system`，五笔提交）**

1. `Game/Save/`：8 个 DTO + `RunSaveWriter` / `RunSaveReader` / `SaveJson` / `SaveMigration`，
   **全是纯函数**，不碰文件、不碰 Unity API。存档的全部风险都在「哪些字段被存了、读回来对不对」，
   拆开之后这一部分 100% 可以在 EditMode 里断言。
2. `Game/Core/SaveSystem.cs`：只剩路径、原子写（`.tmp` → `File.Replace` → `.bak`）、
   读不出来时退到 `.bak`、出错只打日志不抛异常（存档失败绝不该打断玩家正在玩的这一局）。
3. `RunManager`：`EnterNode` 拆成「锁定节点 + 快照」与 `ExecuteNode`（准备数据 + 广播），
   新增 `Resume` 重放后者；新增 `AutosaveRequested` 事件；
   新增 `RunContext.ActiveBattleEncounterId`（读档重开战斗不能拿 `CurrentNode.ContentId` 推——
   事件里开的战斗，当前节点是那个事件）。
4. `Game/UI/SaveService.cs`：唯一决定何时写盘的地方；`GameOver` / `Victory` 时删档。
   语言从 `PlayerPrefs` 迁进 `MetaSave`，带一次性导入。
5. 主菜单加「继续游戏」（无存档置灰）与「放弃本局」，两颗都用**同一颗按钮点两次**做确认——
   本工程只有选牌面板一种模态框，为一句是非题再造一套（遮罩 / 层级 / Esc / 切界面时销毁）
   每一件做漏了都是一个真 bug，第三次会话的界面泄漏就是这么来的。

**实施中发现并修正的 3 个真实问题**

| # | 问题 | 修正 |
|---|---|---|
| 1 | 架构文档 03 的草稿是「存基础 Id + 读档时 Upgrade N 次」，而 `Upgrade()` 会把 `Def` 换成 `UpgradedVersion`，升过级的牌 `Def.Id` 已经是 `*_plus`——照抄会双重升级 | 存当前 `Def.Id`，新增 `CardInstance.Restore` 只补计数不再升级（铁律 45） |
| 2 | 同一份草稿里的 `Map = run.Map` 根本跑不通：`GameMap` / `MapNode` 的集合全是 `readonly`（Unity 序列化器不碰），`Rows` 是嵌套泛型，`ShopStocks` 是 `Dictionary` | 独立 DTO；顺带成为「改运行时类不会无声改掉存档格式」的保证（铁律 46） |
| 3 | `RunSave.Version` 若默认成 `CurrentVersion`，一份根本没有 Version 键的坏文件（被截断的、别的程序写的）会**冒充成一份合法的当前存档**走进 Reader | 默认 0，只由 Writer 显式赋值；`SaveMigration` 见 0 直接拒绝 |

**验证**

- 四个程序集 **0 error 0 warning**（Unity 编辑器占锁，用 VS2022 MSBuild 编译 Unity 生成的 csproj）
- EditMode **198 通过**（原 175 + 新增 23），在工程副本里 batchmode 跑。
  注：原有数字是 175 而不是本文件别处写的 166——第九次会话的 `FeedbackEventTests` 加了 9 条，
  当时没同步进本文件，这次一并订正
- 其中两条是本次最值钱的：
  - `LoadedRun_ContinuesExactlyLikeAnUninterruptedRun`——存档后继续走 3 步，
    与从未中断的那一局逐字段比对。**它不枚举字段**，所以「加了字段忘了存」会被它抓到，
    而所有逐字段断言的用例都对这种漏洞免疫；
  - `EveryRunContextFieldIsAccountedForBySave`——反射把 `RunContext` 的字段集合钉死，
    从结构上兜同一件事，且给得出人话的错误信息。

**已知限制 / 尚未验证**

- ⚠️ **尚未在 Play 模式下点过。** 首次试玩重点看四处：
  ① 开局 → 打两场 → 关掉 → 重开点「继续游戏」，牌库 / 金币 / 遗物 / 地图进度是否原样；
  ② 战斗打到一半关掉，重开是否回到**该场战斗开头**且起手牌与第一次相同；
  ③ 「放弃本局」→「继续游戏」是否变灰；「开始新游戏」在有存档时是否要点两次；
  ④ 英文下主菜单两颗新按钮的文字是否溢出（`ui.menu.abandon_confirm` 那条最长）。
- **Newtonsoft + IL2CPP**：纯 POCO + `List<T>` 一般安全，但泛型反序列化在 AOT 下有踩雷史。
  工程目前只在 Editor 跑，**出包前要实测一次**，必要时加 `link.xml`。
- `FeedbackSettings` 的 5 个键仍在 `PlayerPrefs`，没跟着语言一起迁进 `MetaSave`。
  要迁的话照 `SaveService.LoadLanguage` 那个写法办（meta 里没有值时去 PlayerPrefs 找一次再搬过去），
  否则老玩家调好的震屏强度会在更新后被静默重置。
- 存档路径是 `Application.persistentDataPath`；测试用 `SaveSystem.OverrideDirectory` 引到临时目录。

### 2026-07-26 — 第八次会话：项目使用指南 + 手工资产安全合并

**使用者确认的范围**

| 议题 | 选择 |
|---|---|
| 内容制作方式 | 生成器代码与 Inspector 手工资产两套流程都要 |
| 手工资产登记 | 标准 `Assets/GameData/<Type>/` 目录自动发现 |
| Id 冲突 | 生成资产优先；冲突手工资产跳过并输出双方路径 |
| 删除语义 | 从生成器删代码不自动删资产；磁盘上的 `.asset` 仍会被发现 |
| 指南读者 | 程序员；包含代码示例、调用链和影响范围 |

**做了什么**

1. 新增 `Assets/Docs/ProjectUseGuide.md`：
   - 生成器与 Inspector 两套流程；
   - 卡牌、效果、动态数值、状态、敌人、敌人 AI、Encounter、药水、遗物、事件、
     RunEffect、条件、关键字、奖励/商店、初始牌组和本地化；
   - 随机、存档、UI、美术和地图节点的影响范围；
   - 常见错误与实际源码中的现有限制。
2. `SampleContentGenerator.CreateDatabase()` 改为：
   - 先收录生成器资产；
   - 再递归扫描标准内容目录；
   - 按 Id/键合并；
   - 生成内容在冲突时优先；
   - 手工内容不再因重新生成而从 `GameDatabase` 列表中消失。
3. 语言表也会从 `Assets/GameData/Locales/` 补充登记；已经在数据库中的表保持优先。
4. `Assets/Docs/Architecture/README.md` 的文档索引加入使用指南。

**验证**

- `Game.Runtime`、`Game.UI`、`Game.Editor` 使用 VS2022 MSBuild 编译：**0 error 0 warning**。
- 文档本地链接目标全部存在，Markdown 代码围栏全部配对。
- Unity 编辑器当前占用工程，未在本次会话中实际点击“生成示例内容”；首次使用时应关注
  Console 是否出现手工资产 Id 冲突，并确认 `GameDatabase` 列表包含手工资产。

### 2026-07-26 — 第十二次会话：牌堆浏览 + 卡组浏览

对应 `Docs/Ideas-Backlog.md` 的 **C12**，以及 `ImprovementAnaylze.md` 4.3 里标了 ★★★ 的
「查看抽牌堆 / 弃牌堆 / 消耗堆内容」。

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 抽牌堆顺序 | **排序后显示**（费用→类型→名字→Uid）；弃牌堆 / 消耗堆按真实入堆顺序 |
| 面板实现 | **新建只读 `CardListView`**（否决了「给 `CardPickerScreen` 加只读模式」） |
| 卡组按钮范围 | 所有局外界面（地图/商店/事件/休息/奖励），**战斗中隐藏** |
| 面板功能 | 基线浏览 + 小卡挂 Tooltip + 点小卡放大成完整卡面；**不做**筛选 / 排序控件 |
| 表现事件还在播时（`InputLocked`） | **禁止打开，按钮置灰** |
| 小卡描述数值 | 一律静态基础值，与既有的奖励 / 删卡面板保持一致 |
| 放大卡面 | 双轨：悬停出 Tooltip，点击出大卡；大卡弹出时主动压住 Tooltip |
| 快捷键 | 不加 |

**为什么不复用 `CardPickerScreen`**

那个类的语义是「必须选够 N 张才能确定，回调返回下标」，已经在服务 6 个调用点，
其中战斗内选牌还牵着可挂起的结算栈（铁律 16/17）。
加一套「不选、不回调、随时可关」的生命周期，就多出一个
「面板关掉时该不该 `ResolveSelection`」的分支——**漏掉它的表现是战斗永久卡在挂起态**，
而且不报任何错。共享的是 `CardMiniView` 和网格代码，不是那套生命周期。

**做了什么（分支 `feature/pile-browse`）**

1. **`CardListView`（新，UI）**：只读浏览面板。遮罩 + 标题（带张数）+ 网格滚动 + 关闭按钮，
   空堆显示一行提示。`Arrange` 是唯一允许碰顺序的地方（铁律 52）。
   放大卡面用**等比放大的 `CardMiniView`**，不用 `CardView`——后者的 `Create` 要一个
   `BattleScreen`，还带拖拽 / 悬停抬牌 / 位姿插值（铁律 23），局外根本没有 BattleScreen。
2. **`CardMiniView` 补上 Tooltip**：实现 `ITooltipSource`，转发给现成的
   `TooltipContent.BuildForCard`。`Create` 新增的 `db` 参数**默认 null = 不挂**，
   所以既有 5 个调用点一行未改、行为一个像素未变。
   顺带补掉「手牌上的大卡有提示、小卡没有」这处没人注意过的不一致。
3. **战斗内**：左下那行纯文字 `_pileText` 换成三颗按钮（位置与兄弟顺序都没动——
   它夹在能量球与 `_handArea` 之间，而 `HandWidth` 是按遮挡关系推算的，见铁律 24）。
   面板建在 `BattleScreen._modalLayer`，**不是** `GameApp.OverlayLayer`。
4. **局外**：`ScreenBase.ShowDeckButton`（`BattleHostScreen` 覆写 false）→
   `GameApp` → `TopBarView` 右上角的「卡组 N」按钮 → `GameApp.ShowDeckView()`。
   顶栏右侧三样东西的几何是互相咬住的，注释里写清了。
5. **本地化**：新增 10 条 key，作废 `ui.battle.piles`；英文译文同步进 `Locale_en.asset`，
   占位符与 `SourceSnapshot` 都用脚本交叉核对过（这条是第七次会话的教训：
   只核对「译文有没有缺」而不核对「源文有没有漏收」，漏的那些会安静地永远不被翻译）。

**实施中避开 / 修掉的 4 个坑（每一条都不报错，只是静默错）**

| # | 坑 | 处理 |
|---|---|---|
| 1 | 就地 `DrawPile.Sort()` 会**真的改掉抽牌顺序**——`readonly` 只锁引用不锁内容，编译器不会拦 | 收口到 `CardListView.Arrange`，一律先复制（立为铁律 52） |
| 2 | `BattleScreen.LateUpdate` 每帧**无条件**写 `TooltipView.Suppressed`，把放大卡面压下的值一帧就冲掉 | 在那一处 OR 进 `_cardList.SuppressesTooltip`（立为铁律 54） |
| 3 | 面板自己在 `Update` 里轮询 Esc 会踩时序：两个 MonoBehaviour 的 Update 先后不定，`CancelTargeting` 可能同帧先吃掉 Esc，于是面板「有时候关得掉、有时候关不掉」 | 改成拉取式 `ConsumeCancelInput()`，由持有者在自己的取消分支最前面问，优先级写死 |
| 4 | 遮罩只吃**射线**不吃键盘：面板开着时按空格照样会结束回合 | `Update` 里面板开着就 return |

另外两处是顺手兜住的：`GameApp.OnPhaseChanged` 里把卡组面板与选牌面板一起销毁
（否则「商店里点开卡组 → 买牌弹出选牌面板」会叠在一起，正是第三次会话界面泄漏的同一形状）；
`RefreshPileButtons` 在战斗结束时收掉还开着的面板。

**验证**

- 四个程序集 **0 error 0 warning**（Unity 编辑器占锁，用 VS2022 MSBuild 编译 Unity 生成的 csproj）
- **`Game.Runtime` 一行未改，`Assets/Tests/` 一行未改** → EditMode 198 条的断言对象一个未动。
  本次改动全在 `Game.UI`，而测试只依赖 `Game.Runtime`，跑一遍在结构上抓不到任何东西
  （同第十一次会话的判断）。测试程序集编译通过。
- `Assets/GameData/` 只改 `Locales/Locale_en.asset`（−1 条 key、+10 条），卡牌 / 敌人 / 遗物资产一个字节没变。
- 本地化用脚本离线复现了 `ContentValidator.CheckLocalization` 的三条判定
  （key 在不在表里 / 占位符集合是否一致 / `SourceSnapshot` 是否等于当前源文）：10 条全过，作废的那条两边都已清掉。

**已知限制 / 尚未验证**

- ⚠️ **尚未在 Play 模式下点过。** 首次试玩重点看六处：
  ① 战斗中三颗按钮都能开能关、Esc 能关、关掉后手牌**还能拖**（铁律 25 那类状态残留）；
  ② 面板开着时按空格**不会**结束回合；
  ③ 点「结束回合」后动画在播时三颗按钮**是灰的**；
  ④ 弃一张牌 → 打开弃牌堆，数量与内容对得上；抽牌堆里**看不出**下一张是什么；
  ⑤ 地图右上角按钮在商店 / 事件 / 休息里都在，**战斗里不在**；
     从商店点开卡组再买牌，两个面板不叠；
  ⑥ 英文下标题 / 「关闭」/ 顶栏「Deck N」不溢出——顶栏右侧现在挤了三样东西，最可能出问题。
- **滚动时误点**：小卡实现 `IPointerClickHandler`，按住拖动滚动条后在同一张卡上松手会弹出大卡。
  既有的 `CardPickerScreen` 选择行为完全一样，所以这不是新问题，但卡多了会更容易碰到。
  真要治就是加拖动阈值判定。
- **筛选 / 排序控件没做**（使用者明确不要）。卡组超过 30 张后再考虑，见 C12。
- 放大卡面是 `localScale = 2`，**已配插画的卡会有轻微模糊**（取决于源图分辨率）。
  57 张卡目前都没配图，所以现在看不见；将来配图后可以把 `ArtHeight` 也按倍数放大来缓解。

### 2026-07-26 — 第十三次会话：逐张发牌（抽 / 弃 / 洗的表现分批）

对应 `Docs/Ideas-Backlog.md` 的 **C13**（原 C 表末尾「抽牌动画 / 洗牌动画」两条）。

**决策（由使用者拍板）**

| 议题 | 选择 |
|---|---|
| 驱动方式 | **A1 扫事件队列**（否决了「presenter 播到时通知我一声」的有状态写法，以及纯 UI 错峰） |
| 发牌期间输入 | **锁住，但点一下立刻发完**（否决了「不计入 InputLocked」与「锁完不给跳过」） |
| `TurnStarted` 的位置 | **不挪，`Game.Runtime` 一行不碰**（代价见下面的已知取舍） |
| 范围 | L0 逐张发牌 + 出生点校准 + 落位 punch + **弃牌 / 洗牌对称动画**；不做「抽牌堆计数延迟跳」 |
| 弃牌 / 消耗的飞行终点 | 三颗牌堆按钮的真实位置（否决了把弃牌堆按钮挪到右下——那要重算 `HandWidth`，铁律 24） |

**诊断：缺的不是动画，是节拍器**

飞入动画两年前就写好了——`CardView.Create` 之后立刻 `SnapTo(SpawnSlot)`，再由
`CardView.Update` 指数插值飞到扇形位。问题是 5 张牌在**同一帧**诞生（铁律 55）。
而节拍器也早就有：`DeckController.DrawOne` 每抽一张就 Post 一条 `CardDrawn`（带 `card.Uid`），
只是 `BattlePresenter.Play` 没有对应的 case、`DurationOf` 也返回 0，这条事件一直被静默丢弃。

**做了什么（分支 `feature/draw-animation`）**

1. **`BattlePresenter`**：`CardDrawn` / `CardDiscarded` / `CardExhausted` / `DeckShuffled`
   四条事件从 0 时长改成有时长（0.09 / 0.05 / 0.05 / 0.35）；
   新增 `RequestFastForward`（临时 8 倍速，**加速不是跳过**，铁律 59）。
   刻意**不**给 `CardDrawn` 写日志——每回合 5 行「抽到 XX」会把只有 12 行的日志窗整个冲掉。
2. **`BattleScreen`**：`ScanPendingCardEvents` 每帧扫队列得出「还没播到的进 / 出」（铁律 56）；
   `RefreshHandViews` 改用**可见手牌**；离手的牌先钉在原地进 `_leaving`，
   等自己那条事件被播到才 `CardFlyOut` 飞向对应牌堆。
3. **出生点校准**：`SpawnSlot` 从写死的 `(-720, -20)` 改成抽牌堆按钮的真实位置
   （换算过来约 x = −860，**差了 140 像素**）。牌少时没人看得出，一旦一张张发，
   玩家的视线会跟着每张牌从头看到尾，起点对不对就很显眼。
4. **落位 punch**：走 `CardView` 自己的附加系数，不用 DOTween（铁律 23）；
   由 `CardView` 自己判定「飞到了没有」——飞行时长取决于 `FollowSpeed` 与距离，外面估不准。
5. **`PileFlyFx`（新）**：洗牌时 5 块卡背色块走二次贝塞尔从弃牌堆飞回抽牌堆。
   不用真的 `CardInstance`：洗 30 张牌就建 30 个完整卡面纯属浪费，而那个尺寸下玩家什么也读不到。
6. **牌堆按钮反馈**：收 / 发牌时鼓一下，补偿三颗按钮并排挤在左下角（彼此只隔 128px）、
   飞行终点在画面上几乎是同一个角落这件事。

**实施中发现并处理的 4 个真实问题**

| # | 问题 | 处理 |
|---|---|---|
| 1 | 打出的牌**同时**发 `CardPlayed` 和 `CardDiscarded`，两条飞行路径都认领得了它 | `_flyOutUid` 优先，立为铁律 57 |
| 2 | 签名比对若仍按 `Deck.Hand`，presenter 每播掉一条 `CardDrawn` 手牌本身并没变、签名不变，整帧被 early-out 跳过 → **牌永远不出现** | 签名改按可见手牌 |
| 3 | `Image.DOFade` 在 `DOTweenModuleUI.cs` 里，`Game.UI` 这个 asmdef 引用不到（`UnitView.PlayHit` 两年前踩过同一个坑） | 改 `DOTween.To`，立为铁律 60 |
| 4 | 落位弹跳若直接乘在 `_rt.localScale` 上，会把上一帧的弹跳系数喂回插值，弹完要拖好几帧才回得去 | 另存一份不含弹跳的 `_scale` 当插值源 |

另外三处是写的时候就避开的：归宿要问牌堆不要猜事件类型（铁律 58）、
快进标记忘了复位会让整场战斗停在 8 倍速（铁律 59）、
牌堆按钮的底色被每帧重写所以只能动 scale（铁律 61）。

**验证**

- 四个程序集 **0 error 0 warning**（Unity 编辑器占锁，用 VS2022 MSBuild 编译 Unity 生成的 csproj）
- **`Game.Runtime` 一行未改，`Assets/Tests/` 一行未改** → EditMode 198 条的断言对象一个未动。
  本次改动全在 `Game.UI`，而测试只依赖 `Game.Runtime`（同第十一、十二次会话的判断）。
- `Assets/GameData/` 一个字节没动，**零新增本地化 key**。

**已知取舍 / 尚未验证**

- ⚠️ **尚未在 Play 模式下点过。** 首次试玩重点看八处：
  ① 回合开始 5 张逐张出现，已在手的牌向两侧滑开腾位；
  ② 发牌途中点一下（或按空格 / E）立刻发完，且**之后的回合恢复正常速度**（铁律 59 那条）；
  ③ 「抽 3 张」这类卡牌也逐张——这是白赚的，接了队列就自动有；
  ④ 打出的牌仍飞向**目标**而不是弃牌堆（铁律 57）；
  ⑤ 出牌后多出的 0.05 秒黏不黏（`DurCardLeave`，嫌黏就调小它）；
  ⑥ 回合结束手牌逐张飞向左下弃牌堆，虚无牌飞向消耗堆；
  ⑦ 洗牌时看得见一叠卡背飞回去；
  ⑧ 战斗打到一半切界面 / 读档 / 打完点「继续」，没有牌卡在看不见或飞不走的状态。
- **横幅在发牌之后弹**：使用者选了不碰 `Game.Runtime`，而 `BeginTurn` 里
  `Post(TurnStarted)` 写在 `Deck.Draw(draw)` **之后**，所以顺序是「牌发完 → 才弹『第 N 回合』」。
  改回来只是把那一行上移，随时可议。
- **发牌期间手牌是灰的**：`InputLocked` = 事件队列非空，而 `TurnStarted` 是最后一条，
  于是发牌那 0.45 秒加横幅那 0.25 秒里手牌都是不可打出的灰色，之后才亮起来。
  逻辑上诚实（那段时间确实点不了），但如果觉得难看，与上一条是同一个开关。
- **抽牌堆计数不延迟**（使用者明确不要 L1-3）：牌还在飞，按钮上的数字已经是减完的。
  要做的话照 `UnitView._shownHp` 那个「表现值 vs 逻辑值」的写法办。
- **`PileFlyFx.cs.meta` 是手写的**（Unity 占着工程锁，没法让它自己生成）。
  GUID 是新生成的、格式与既有 meta 逐字节一致；Unity 首次导入若有异议，删掉让它重建即可。
