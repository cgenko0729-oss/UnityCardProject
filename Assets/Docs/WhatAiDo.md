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
| 阶段 6 动画音效打磨 | 🔶 部分完成 | ✅ 本地化 / ✅ TextMeshPro；⬜ DOTween / 音效 |

**当前代码规模**：`Assets/Game/` 下 123 个 .cs 文件；测试 12 个文件 198 个用例。
**内容规模**：10 个状态、57 张卡、6 个敌人、11 场战斗、16 个遗物、7 个事件、10 瓶药水、5 个关键字。
**本地化规模**：495 条文案，简体中文（源语言）+ 英文。

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
│   │                RestScreen, MainMenuScreen, GameOverScreen, CardPickerScreen
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
