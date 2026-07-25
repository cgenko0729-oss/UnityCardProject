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
   - 战斗：点卡牌 → 需要目标的卡进入选择态 → 点敌人出牌；右键 / Esc 取消；空格 或 E 结束回合
   - 战斗结束后要点「继续」才推进流程（等表现事件播完，否则看不到致命一击）
   - 想复现某一局：选中 `GameApp`，把 `Fixed Seed` 改成非 0
5. 只调试单场战斗：菜单 `Tools/卡牌游戏/2. 创建战斗测试场景` → `Battle.unity`，
   选中 `BattleBootstrap` 改 `Encounter Id`。
6. 跑测试：`Window → General → Test Runner → EditMode → Run All`（应为 **164/164 通过**）。
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
| 阶段 5 存档与内容 | ⬜ 未开始 | SaveSystem / MetaSave / 内容量产 / 自动对战模拟器 |
| 阶段 6 动画音效打磨 | ⬜ 未开始 | DOTween / 音效 / 本地化 |

**当前代码规模**：`Assets/Game/` 下 96 个 .cs 文件；测试 11 个文件 164 个用例。
**内容规模**：10 个状态、57 张卡、6 个敌人、11 场战斗、16 个遗物、7 个事件、10 瓶药水。

> ⚠️ **新会话第一件事**：如果 `Assets/GameData/Potions/` 不存在或卡池不足 57 张，
> 说明内容资产还没生成，先跑菜单 `Tools/卡牌游戏/1. 生成示例内容`。

---

## 四、目录与文件清单

```
Assets/
├── Game/
│   ├── Game.Runtime.asmdef
│   ├── Core/        Rng, GameDatabase, EncounterDefinition, RunContext,
│   │                RunManager, RewardGenerator, ShopStock, ContentPicker
│   ├── Cards/       CardEnums, CardDefinition, CardInstance, DeckController
│   ├── Effects/     CardEffect, EffectContext, EffectValue, EffectCondition,
│   │                TargetSelector(+TargetResolver), EffectResolver
│   │   └── Impl/    Damage, Block, Draw, Energy, ApplyStatus, Heal, Discard, Exhaust,
│   │                AddCard, ModifyCardCost ｜ 组合子: Repeat, Conditional, RandomPick, Delayed
│   ├── Units/       BattleUnit
│   ├── Statuses/    StatusDefinition, StatusInstance, StatusBehaviour
│   │   └── Impl/    CombatBehaviours（力量/易伤/虚弱/中毒/壁垒/荆棘）
│   │                ProtectiveBehaviours（神器/回光/再生/恶魔形态）
│   ├── Enemies/     Intent, EnemyAction, EnemyDefinition, EnemyBrain
│   │   └── Impl/    GuardianBrain
│   ├── Battle/      BattleEnums, BattleEvent, BattleHooks, BattleContext, BattleController
│   ├── Map/         MapNode, GameMap, MapGenerator                        ← 阶段 4
│   ├── Relics/      RelicDefinition, RelicInstance                        ← 阶段 4
│   │   └── Impl/    RelicBehaviours（8 类行为，覆盖 4 个新 Hook）
│   ├── RunEffects/  RunEffect(+Resolver+Context), RunCondition            ← 阶段 4
│   │   └── Impl/    Resource / Deck / Combinator 三组共 11 个局外效果
│   ├── Events/      EventDefinition                                       ← 阶段 4
│   ├── UI/          Game.UI.asmdef, UIFactory, InputCompat, CardView, UnitView,
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

1. `Game/Save/`：`RunSave` / `MetaSave` / `SaveConstants`，`Game/Core/SaveSystem.cs`。
   - **只存 Id，绝不存 Definition 引用**。
   - `RunContext` 已经是唯一的可变数据源，序列化它即可；注意一并存
     `_nextCardUid`（读档后用 `EnsureCardUidAtLeast` 恢复）与 `Rng.Save()` 的各条流状态。
   - 只在节点级安全点存档，明确不做战斗中途存档。
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
| 1 | **手牌 UI 全量重建** | `BattleScreen.RefreshHandViews` | 手牌一变就销毁重建所有 CardView，做不了抽牌/出牌动画（阶段 6 的前置改动）。 |
| 2 | **每帧描述重算的 GC** | `CardInstance.GetDescription` | 每帧每张手牌 new 一个 `EffectContext` + 若干字符串。需要按「依赖指纹」缓存。 |
| 3 | 文案硬编码 | UI 各处 | 本地化仍未引入，越晚做迁移成本越高。 |
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
