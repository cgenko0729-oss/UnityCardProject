# ProjectUseGuide — 项目驾驭与内容扩展指南

> 适用工程：Unity 6（`6000.0.62f1`）卡牌构筑项目  
> 目标读者：需要直接修改内容和 C# 代码的项目维护者  
> 本文以当前源码为准；若本文与旧架构文档冲突，应先检查实际代码。

---

## 1. 先建立正确的项目心智模型

这个项目不是“每张卡写一个 MonoBehaviour”的结构。核心分为四层：

1. **静态定义 `*Definition`**
   - `CardDefinition`、`EnemyDefinition`、`StatusDefinition`、`PotionDefinition` 等。
   - 都是只读的 `ScriptableObject` 内容资产。
   - 存 Id、名称、数值、效果列表和美术引用。
   - 战斗或一局游戏进行中，不得修改这些对象。

2. **运行时实例 `*Instance` / Context**
   - `CardInstance`：同一张卡定义在牌库中的一份具体副本。
   - `StatusInstance`：某个单位身上的状态与当前层数。
   - `RelicInstance`：玩家持有的遗物及计数器。
   - `PotionInstance`：背包中的一瓶药水及唯一 Uid。
   - `BattleContext`：一场战斗的可变状态。
   - `RunContext`：一局游戏跨节点、跨战斗的可变状态。

3. **可复用逻辑**
   - 战斗内即时效果：`CardEffect`。
   - 状态与遗物的规则拦截：`StatusBehaviour` + Battle Hook。
   - 局外效果：`RunEffect`。
   - 敌人决策：`EnemyBrain`。

4. **流程与表现**
   - `BattleController` 管战斗状态机。
   - `RunManager` 管地图、战斗、奖励、商店、事件等局外阶段。
   - `GameApp` 根据 `RunPhase` 创建对应 UI。
   - UI 全部由代码运行时创建，没有 prefab。

因此，新增内容前先判断属于哪一级：

| 需求 | 通常是否写代码 | 应修改的位置 |
|---|---:|---|
| 用已有伤害、护甲、抽牌等拼一张新卡 | 否 | 新建 `CardDefinition` |
| 用已有战斗效果做一瓶药水 | 否 | 新建 `PotionDefinition` |
| 用已有行为做一个遗物 | 否 | 新建 `RelicDefinition` |
| 敌人使用固定序列、权重、条件、多阶段行动 | 否 | 新建 `EnemyDefinition` |
| 新增一种以前没有的即时效果 | 是 | 新建 `CardEffect` 子类 |
| 新增一种持续状态规则 | 是 | 新建 `StatusBehaviour` 子类并实现 Hook |
| 新增特殊敌人决策 | 是 | 新建 `EnemyBrain` 子类 |
| 新增局外资源或牌库操作 | 是 | 新建 `RunEffect` 子类 |
| 新增地图节点种类或全新界面阶段 | 是，而且影响较广 | `MapNodeType`、`RunPhase`、`RunManager`、`GameApp` 和 UI |

---

## 2. 两种内容制作流程

项目同时支持：

- **流程 A：通过生成器代码创建和维护内容**（`SampleContent*.cs`，全部内容类型）
- **流程 B：通过 Inspector 手工创建内容**（全部内容类型）
- **流程 C：通过 `CardTable.json` 维护卡牌**（只有卡牌，见 [4.9](#49-流程-c卡表-json新卡推荐走这条)）

三者可以共存，但必须理解所有权规则。

> **新增卡牌优先走流程 C。** A 要编译 + 跑生成器才能看到一次数值改动；
> B 要在裸 Inspector 里对付 `[SerializeReference]` 的扁平表单
> （一个 `DamageEffect` 展开是 14 行，其中 9 行是当下无意义的字段）。
> 流程 C 把一张卡压成十几行 JSON，并且带 Id 冲突、稀有度、目标一致性的即时校验。
> 状态 / 敌人 / 遗物 / 药水 / 事件仍然只有 A 和 B。

### 2.1 标准内容目录

手工资产必须放进下列标准目录，生成器才会自动发现：

| 内容 | 目录 | 创建菜单 |
|---|---|---|
| 卡牌 | `Assets/GameData/Cards/` | `Assets → Create → Game → Card` |
| 状态 | `Assets/GameData/Statuses/` | `Assets → Create → Game → Status` |
| 敌人 | `Assets/GameData/Enemies/` | `Assets → Create → Game → Enemy` |
| 战斗组合 | `Assets/GameData/Encounters/` | `Assets → Create → Game → Encounter` |
| 药水 | `Assets/GameData/Potions/` | `Assets → Create → Game → Potion` |
| 遗物 | `Assets/GameData/Relics/` | `Assets → Create → Game → Relic` |
| 事件 | `Assets/GameData/Events/` | `Assets → Create → Game → Event` |
| 关键字说明 | `Assets/GameData/Keywords/` | `Assets → Create → Game → Keyword` |
| 语言表 | `Assets/GameData/Locales/` | `Assets → Create → Game → Locale Table` |

子目录也会递归扫描，例如：

```text
Assets/GameData/Cards/Warrior/
Assets/GameData/Cards/Neutral/
Assets/GameData/Enemies/Act1/
```

### 2.2 流程 A：生成器代码

主要入口是：

- [SampleContentGenerator.cs](../Game/Editor/SampleContentGenerator.cs)
- [SampleContentPotions.cs](../Game/Editor/SampleContentPotions.cs)
- [SampleContentRelics.cs](../Game/Editor/SampleContentRelics.cs)
- [SampleContentEvents.cs](../Game/Editor/SampleContentEvents.cs)
- 其他按机制拆分的 `SampleContent*.cs`

一般步骤：

1. 在对应 `Create...` 方法中加入内容定义。
2. 若引用其他内容，确认创建顺序正确。例如敌人会塞状态牌，因此卡牌必须先于敌人创建。
3. 回到 Unity，执行：

   ```text
   Tools/卡牌游戏/1. 生成示例内容
   ```

4. 生成器使用固定路径加载或创建资产，把代码中的字段写入资产。
5. 生成完成后，所有生成资产和标准目录里的手工资产会合并进 `GameDatabase.asset`。

生成器内容的事实来源是 C# 代码。再次生成时，代码明确赋值的字段会覆盖 Inspector 改动。

生成器目前通常不会写入 `Art` 或 `Icon`，因此为生成资产手工指定的图片引用一般可以保留；但不要依赖“某字段今天没写”作为长期规则。修改生成器时应检查它是否开始给该字段赋值。

### 2.3 流程 B：Inspector 手工资产

一般步骤：

1. 在对应标准目录创建资产，或复制一份结构最接近的现有资产。
2. 立刻填写唯一 Id。
3. 修改名称、稀有度、目标类型、效果或行为。
4. 若不想立刻运行生成器，可先手动把资产拖入
   [GameDatabase.asset](../GameData/GameDatabase.asset) 对应列表。
5. 推荐最终执行一次“生成示例内容”，让自动发现逻辑统一重建数据库列表。

`Effects`、`Behaviours` 和事件效果使用 `[SerializeReference]`。如果当前 Inspector 不方便从空列表直接创建所需的具体子类，最稳妥的手工方法是：

1. 找到使用相同效果或行为的现有资产。
2. 复制资产。
3. 保留所需的 managed-reference 元素。
4. 删除多余元素并修改参数。

复杂嵌套效果树通常更适合用生成器代码搭建。`[SerializeReference]` 的序列化规则可参考 [Unity 官方文档](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializeReference.html)。

### 2.4 两套流程的合并与冲突规则

运行生成器时，数据库按以下规则重建：

1. 先加入本次由生成器代码管理的资产。
2. 再按资产路径排序，递归扫描标准目录。
3. 不冲突的手工资产自动加入。
4. 若手工资产与生成资产的 Id/键相同：
   - 生成资产优先。
   - 冲突手工资产不加入数据库。
   - Console 输出包含双方资产路径的错误。
5. 两个手工资产 Id 相同：
   - 路径排序靠前者先被加入。
   - 后者被跳过并输出错误。

删除规则：

- 从生成器代码删除一项，不等于删除内容。
- 只要原 `.asset` 仍在标准目录，它会被当成手工资产重新发现。
- 要彻底删除，必须同时删除 `.asset` 文件。

不要通过重命名文件来“修改 Id”。运行时查找使用的是资产里的 `Id` 字段，而不是文件名。

---

## 3. 所有内容共用的 Id 规则

推荐格式：

```text
小写 snake_case
```

例如：

```text
flame_strike
flame_strike_plus
ancient_guardian
greater_healing
```

Id 会影响：

- `GameDatabase.GetCard/GetEnemy/GetStatus/...`
- 初始牌组配置
- 敌人或效果引用
- 事件触发战斗的 `EncounterId`
- 存档中将来保存的 Definition 标识
- 本地化 key

已经发布或已经进入存档的 Id 不应随意修改。重命名 Id 相当于删除旧内容再创建新内容。

---

## 4. 新增卡牌

相关核心文件：

- [CardDefinition.cs](../Game/Cards/CardDefinition.cs)
- [CardInstance.cs](../Game/Cards/CardInstance.cs)
- [BattleController.cs](../Game/Battle/BattleController.cs)
- [EffectResolver.cs](../Game/Effects/EffectResolver.cs)
- [SampleContentGenerator.cs](../Game/Editor/SampleContentGenerator.cs)

### 4.1 先判断是否需要新代码

以下需求都可以只配置，不需要新效果类：

- 伤害、护甲、抽牌、能量、治疗
- 施加状态
- 弃牌、消耗、选牌
- 生成卡牌
- 修改费用
- 重复、条件分支、随机分支、延迟
- X 费
- 根据状态层数、手牌数、弃牌堆数量、护甲等动态缩放

只有现有 `CardEffect` 无法表达规则时，才新增效果类。

### 4.2 生成器流程示例

下面是一张“造成伤害并获得护甲”的卡：

```csharp
var flameGuard = MakeCard(
    "flame_guard",
    "焰盾",
    1,
    CardType.Attack,
    CardTargetKind.SingleEnemy,
    "造成 {0} 点伤害，获得 {1} 点护甲。",
    new DamageEffect
    {
        Target = TargetSelector.Chosen,
        Amount = EffectValue.Flat(7),
    },
    new BlockEffect
    {
        Target = TargetSelector.SelfOnly,
        Amount = EffectValue.Flat(5),
    });

flameGuard.Rarity = CardRarity.Uncommon;
EditorUtility.SetDirty(flameGuard);
```

建议放入 `CreateCards()` 或按机制拆出的 `SampleContent*.cs`。

### 4.3 带升级版的卡牌

先创建升级版，再创建基础版：

```csharp
var flameGuardPlus = MakeCard(
    "flame_guard_plus", "焰盾+", 1,
    CardType.Attack, CardTargetKind.SingleEnemy,
    "造成 {0} 点伤害，获得 {1} 点护甲。",
    new DamageEffect
    {
        Target = TargetSelector.Chosen,
        Amount = EffectValue.Flat(10),
    },
    new BlockEffect
    {
        Target = TargetSelector.SelfOnly,
        Amount = EffectValue.Flat(7),
    });

var flameGuard = MakeCard(
    "flame_guard", "焰盾", 1,
    CardType.Attack, CardTargetKind.SingleEnemy,
    "造成 {0} 点伤害，获得 {1} 点护甲。",
    new DamageEffect
    {
        Target = TargetSelector.Chosen,
        Amount = EffectValue.Flat(7),
    },
    new BlockEffect
    {
        Target = TargetSelector.SelfOnly,
        Amount = EffectValue.Flat(5),
    });

flameGuard.Rarity = CardRarity.Uncommon;
flameGuard.UpgradedVersion = flameGuardPlus;
EditorUtility.SetDirty(flameGuard);
```

升级版必须是 `CardRarity.Special`，否则会独立进入奖励和商店。当前 `MakeCard` 会自动把以 `_plus` 结尾的 Id 设为 `Special`。

### 4.4 Inspector 流程

1. 在 `Assets/GameData/Cards/` 创建 `Game/Card`。
2. 建议命名为 `Card_FlameGuard`。
3. 填写：
   - `Id = flame_guard`
   - `DisplayName = 焰盾`
   - `Cost = 1`
   - `CostMode = Fixed`
   - `Type = Attack`
   - `Rarity = Uncommon`
   - `TargetKind = SingleEnemy`
4. 在 `Effects` 中按顺序放：
   - `DamageEffect`
   - `BlockEffect`
5. 填 `DescriptionTemplate`：

   ```text
   造成 {0} 点伤害，获得 {1} 点护甲。
   ```

6. 如果有升级版：
   - 另建 `flame_guard_plus`
   - 把升级版 `Rarity` 设为 `Special`
   - 将它拖到基础版的 `UpgradedVersion`
7. 执行生成器，让它自动进入 `GameDatabase`。

### 4.5 卡牌字段如何影响游戏

| 字段 | 影响 |
|---|---|
| `Id` | 查找、本地化、存档身份 |
| `DisplayName` | 简中原文及其他语言缺译时的回退 |
| `Art` | 当前 UI 尚未读取，单独赋图不会自动显示 |
| `Cost` | 固定费用；X 费卡通常仍写 0 |
| `CostMode` | `Fixed`、`X`、`Unplayable` |
| `Type` | 卡面颜色、遗物筛选、能力牌归宿等 |
| `Rarity` | 奖励池、商店价格和是否进入公共池 |
| `TargetKind` | UI 是否要求玩家选择敌人 |
| `Keywords` | 消耗、保留、固有、虚无、不可打出 |
| `DescriptionTemplate` | 卡面描述模板 |
| `Effects` | 打出后依次执行的效果 |
| `InHandEndOfTurnEffects` | 留在手中到回合结束时触发 |
| `UpgradedVersion` | 升级后切换到哪个 Definition |

`CardType.Power` 打出后默认进入 `CardPile.None`，不会进弃牌堆或消耗堆。

`CardType.Status` 和 `CardType.Curse` 若不应进入奖励与商店，必须设为 `Special`。

### 4.6 目标类型必须与效果目标一致

卡牌级 `TargetKind` 决定 UI 是否让玩家选目标；每个效果自己的 `Target` 决定实际作用对象。

| 卡牌行为 | `CardTargetKind` | 效果 `Target` |
|---|---|---|
| 点选一个敌人 | `SingleEnemy` | 至少一个效果使用 `Chosen` |
| 攻击所有敌人 | `None` 或 `AllEnemies` | `AllEnemies` |
| 随机攻击敌人 | `None` | `RandomEnemy` |
| 自己获得护甲 | `None` | `SelfOnly` |
| 第一效果打敌人，第二效果继续作用同一目标 | `SingleEnemy` | 第一项 `Chosen`，第二项 `Previous` |

> ⚠ **卡牌级 `TargetKind` 不声明打击范围。**
>
> 全工程只有三处读它，全都是 `== SingleEnemy`：
> `BattleController.NeedsTargetSelection`、`BattleController.CanPlayCard` 的目标合法性检查、
> 以及 `PotionDefinition.NeedsTarget`。
>
> 也就是说 `None` / `AllEnemies` / `Self` / `RandomEnemy` **行为完全等价**，
> 都只表示「不要让玩家点目标」。真正决定打谁的是每个效果自己的 `Target`。
>
> 字段名叫「目标」、取值里又有 `AllEnemies`，读起来非常像「这张卡打全体」——
> 这是本工程最容易误读的一处配置。

两个方向的配错，后果完全不同：

**方向一（警告）**：声明要选目标，但没有效果用 `chosen`

```text
CardTargetKind = SingleEnemy
DamageEffect.Target  = RandomEnemy
```

玩家会被要求点一个敌人，然后那次点击被忽略。烦人，但卡的其它效果照常生效。

**方向二（错误，后果重得多）**：有效果用 `chosen`，但卡牌级不是 `SingleEnemy`

```text
CardTargetKind = AllEnemies      ← 不会让玩家点目标
DamageEffect.Target  = ChosenTarget    ← 于是 chosen 恒为空
```

出牌时 `ChosenTarget` 是 null，`TargetResolver` 解析 `chosen` 得到空集合，
于是**那些效果静默命中 0 个目标**。表象是：

```text
卡打出去了、能量扣了、动画播了、护甲和抽牌都生效了，只有伤害没有。
```

看起来像「只有攻击那一半坏了」，离根因（卡牌级目标不是 `SingleEnemy`）非常远。

同理，**`InHandEndOfTurnEffects` 里永远不能用 `chosen`** ——
回合末结算那个时机根本不存在玩家点选。

这三条都由 `CardRules` 检查（菜单 3 与卡牌编辑器共用同一份），
方向二和 `inHandEndOfTurn` 那条报**错误**，会阻止卡表导入。

### 4.7 描述模板

`{0}` 对应 `Effects[0].Describe()`，`{1}` 对应 `Effects[1].Describe()`。

效果顺序和描述占位符顺序不必与中文语序相同。例如：

```csharp
Effects[0] = DrawEffect(2);
Effects[1] = EnergyEffect(1);
DescriptionTemplate = "获得 {1} 点能量，抽 {0} 张牌。";
```

切勿引用不存在的下标。组合子通常只描述重复次数，内部效果数值需要直接写在文案里，或为组合子扩展更完整的 `Describe()`。

### 4.8 卡牌运行调用链

```text
CardView / BattleScreen
  → BattleController.CanPlayCard
  → 费用、回合、目标、CanApply 检查
  → BattleController.TryPlayCard
  → 扣能量并把牌移出手牌
  → EffectResolver.ResolveAll
  → 每个 CardEffect.Apply
  → Hook / 触发队列 / 战斗事件
  → FinishPlay
  → 弃牌、消耗、抽牌堆或消失
  → UI 消费 BattleEvent 更新表现
```

因此：

- 修改卡牌资产通常只影响内容数值。
- 修改 `CardEffect` 会影响所有使用它的卡牌、药水和敌人行动。
- 修改 `EffectResolver` 会影响几乎全部战斗内容。
- 修改 `BattleController.TryPlayCard` 会影响所有卡牌的出牌规则。

### 4.9 流程 C：卡表 JSON（新卡推荐走这条）

相关文件：

- [CardTable.json](../GameData/CardTable.json) — 事实来源
- [CardTableImporter.cs](../Game/Editor/CardTable/CardTableImporter.cs) — 表 → 资产
- [CardTableJson.cs](../Game/Editor/CardTable/CardTableJson.cs) — 序列化契约
- [CardRules.cs](../Game/Editor/CardTable/CardRules.cs) — 校验规则（全工程唯一一份）
- [CardTableSelfCheck.cs](../Game/Editor/CardTable/CardTableSelfCheck.cs) — 序列化层的自检

#### 4.9.1 所有权

```text
CardTable.json  →（菜单 7）→  GameData/Cards/Authored/*.asset  →  GameDatabase
```

- **表是唯一事实来源。** `Cards/Authored/` 完全归导入器所有，是 build 产物，不要手改。
- 表里删掉一张卡，下次导入会把对应 `.asset` 一起删掉。
- 生成器写 `Cards/` 根目录，导入器写 `Cards/Authored/`，两者物理隔离。
  现有 57 张卡继续由 `SampleContent*.cs` 管，不受影响。
- Id 撞车（表 vs 生成器 vs 其它手工资产）会**阻止导入**，不会像 2.4 规则 4 那样静默丢卡。

#### 4.9.2 菜单

```text
Tools/卡牌游戏/7. 导入卡表     表 → 资产 → 重建 GameDatabase
Tools/卡牌游戏/8. 卡表自检     序列化层的往返幂等断言（改了效果类之后跑一次）
Tools/卡牌游戏/9. 卡牌编辑器   图形界面，编的是这张表（见 4.9.9）
```

导入器的 CI 入口是 `Game.Editor.CardTables.CardTableImporter.ImportBatch`，
自检是 `CardTableSelfCheck.RunBatch`，都在有错误时返回退出码 1。

#### 4.9.3 一张卡的完整写法

```jsonc
{
  "version": 1,
  "cards": [
    {
      "id": "flame_guard",
      "name": "焰盾",
      "cost": 1,
      "type": "Attack",           // Attack / Skill / Power / Status / Curse
      "rarity": "Uncommon",       // Basic / Common / Uncommon / Rare / Special
      "target": "SingleEnemy",    // None / SingleEnemy / AllEnemies / Self / RandomEnemy
      "keywords": ["Exhaust"],    // Exhaust / Retain / Innate / Ethereal / Unplayable
      "desc": "造成 {0} 点伤害，获得 {1} 点护甲。",
      "effects": [
        { "$kind": "damage", "target": "chosen", "amount": 7 },
        { "$kind": "block",  "target": "self",   "amount": 5 }
      ],
      "upgrade": {
        "effects": [
          { "$kind": "damage", "target": "chosen", "amount": 10 },
          { "$kind": "block",  "target": "self",   "amount": 7 }
        ]
      }
    }
  ]
}
```

`costMode` 省略即 `Fixed`（可写 `X` / `Unplayable`）。`inHandEndOfTurn` 与 `effects` 同结构。

#### 4.9.4 四条压缩规则

**① `$kind` 是效果类型，短名 = 类名去掉 `Effect` 后首字母小写。**

| 类 | `$kind` |
|---|---|
| `DamageEffect` | `damage` |
| `ApplyStatusEffect` | `applyStatus` |
| `ModifyCardCostEffect` | `modifyCardCost` |
| `SelectCardsEffect` | `selectCards` |

字段名 = C# 字段名的 camelCase。**两者都由反射推导，没有映射表要维护** ——
在 `Effects/Impl/` 新建一个效果类，它立刻能在表里用。写错 `$kind` 或字段名会当场报错并列出合法取值。

> `$` 前缀不是装饰。判别符与效果字段共处一个 JSON 对象，
> 而 `DamageEffect` 有一个 `Kind` 字段（就是铁律 20 那个 `DamageKind`），camelCase 后正是 `kind`。
> 撞名会让「非攻击伤害」的卡在读取时把效果类型变成 `Loss`，
> 且因为 `DamageKind.Attack` 是默认值会被省略，**只有设了非攻击伤害的卡会坏**。
> `$` 开头的名字永远不可能由字段推导而来，所以这个冲突结构上不可能再发生。

**② 与构造函数默认值相同的字段一律省略。**

于是 `{ "$kind": "block", "amount": 5 }` 就是完整的「获得 5 点护甲」——
`BlockEffect()` 的构造函数已经把 `target` 设成 `self`。反过来，**省略 = 构造函数默认值**，
不是 `default(T)`；这条两个方向都成立。

**③ 固定数值写成裸数字，需要缩放时才展开。**

```jsonc
"amount": 7
"amount": { "base": 3, "per": "statusOnSelf:strength", "each": 2, "min": 1, "max": 30 }
```

`per` 的取值：`statusOnSelf:<id>`、`statusOnTarget:<id>`、`cardInHand`、`cardInDiscard`、
`cardPlayedThisTurn`、`enemyAlive`、`blockOnSelf`、`missingHpOnSelf`、`x`、`repeatIndex`。
前两个必须带状态 Id —— 不带会报错，因为那是一个恒等于 0 且不报错的哑配置。

**④ `target` 写成裸字符串，有附加参数时才展开。**

```jsonc
"target": "chosen"
"target": { "kind": "randomEnemy", "count": 2, "allowDuplicates": true }
```

取值：`none`、`self`、`chosen`、`allEnemies`、`allAllies`、`allUnits`、
`randomEnemy`、`lowestHpEnemy`、`highestHpEnemy`、`prev`。

#### 4.9.5 `upgrade` 内嵌块

导入器自动产出 `<id>_plus`、自动设 `Special`（铁律 14）、自动接 `UpgradedVersion`。

- 标量字段（`name` / `cost` / `desc` / `keywords`）省略即继承基础版；`name` 省略时默认加 `+`。
- **`effects` 只要出现就是整体替换，不做按下标 patch。**
  按下标 patch 会重现铁律 33 那类错位隐患：往基础版效果列表中间插一个效果，
  patch 会静默打到错误的效果上。多写几行换掉一整类不报错的故障。
- 继承 `effects` 时是深拷贝，两个资产不共享效果实例。

#### 4.9.6 引用其它内容

`applyStatus.status`、`addCard.card` 这类字段在表里写 **Id 字符串**，不写路径也不写 GUID：

```jsonc
{ "$kind": "applyStatus", "target": "chosen", "status": "vulnerable", "stacks": 2 }
{ "$kind": "addCard", "card": "strike", "pile": "Hand", "count": 2, "temporary": true }
```

解析不到的 Id 会**中断导入并列出所有已知 Id**，不会静默变成 null
（那会产出一张能进游戏、打出去什么都不发生、且不报任何错的卡）。
同一张表里的卡可以互相引用。

#### 4.9.7 这条流程不管的事

- **美术**：导入器不写 `Art`，手配的图会保留（铁律 47）。
- **英文文案**：仍然走菜单 5 / 6 的 CSV 往返。表只管简中原文。
- **状态 / 敌人 / 遗物 / 药水 / 事件**：不在卡表范围内，继续走流程 A 或 B。

#### 4.9.8 改了效果类之后

给已有效果类加字段、或新建效果类之后，跑一次 `Tools/卡牌游戏/8. 卡表自检`。

它会给每个效果类造一个「所有字段都非默认」的实例做往返幂等断言，
覆盖范围由反射决定，所以新字段自动进入测试范围，不需要有人记得来补一行。
`Game.Tests.EditMode` 只引用 `Game.Runtime`，够不到 `Game.Editor`
（与 `Game.UI` 同一个覆盖盲区，铁律 52），这个自检就是那一层的替代品。

### 4.9.9 卡牌编辑器窗口

```text
Tools/卡牌游戏/9. 卡牌编辑器
```

相关文件：[CardEditorWindow.cs](../Game/Editor/CardTable/CardEditorWindow.cs)

**这个窗口编辑的是 `CardTable.json`，不是 `.asset`。** 它是表的图形前端：
点几下 → 写回 JSON → 点工具栏「导入卡表」→ 资产更新。资产始终是 build 产物。

一个直接编辑资产的窗口会与「表是唯一事实来源」打架 ——
你在窗口里改的东西下次导入就被表冲掉。

#### 界面

- **左栏**：搜索 + 类型/稀有度筛选 + 卡列表。
  行首红点 = 有错误，黄点 = 有警告，不用点开就知道哪张卡有问题。
- **右上**：卡面近似预览。**描述文字是真实算出来的** ——
  走 `CardInstance.GetDescription(null)`，也就是牌库界面用的同一条路径，
  所以 `{N}` 的替换结果与游戏里逐字一致。
- **右中**：字段区。描述模板上方有一行 `{0}=damage  {1}=block` 的对照提示，
  不用自己数下标。
- **右下**：效果列表。折叠头直接显示摘要（`damage amount=8 → ChosenTarget`）。
- **底部**：内联校验，规则与菜单 3 完全同一份（`CardRules`）。

#### 三件它主动替你做的事

1. **`{N}` 自动重排。** 增删效果、上下移动效果时，描述模板里的占位符跟着改。
   不做这件事的话，新窗口会原样重现旧痛点：往效果列表中间插一个效果，
   后面所有 `{N}` 全部错位，而校验器只抓「下标越界」，抓不到「错位」。
2. **改 id 时弹确认。** 本地化 key 由 id 派生，改 id 等于删一张卡再建一张新卡 ——
   旧译文变孤儿，旧资产在下次导入时被当孤儿删掉。
3. **复制卡 / 升级版继承效果时深拷贝。** 共用同一批 `CardEffect` 实例的话，
   改副本会同时改原件。

#### 没有「未保存」状态

任何改动立刻写回磁盘（不调 `AssetDatabase.Refresh()`，所以没有导入抖动）。
理由与铁律 56 相同：一个「未保存」标志意味着要回答「域重载时谁存 / 关窗口时谁存 /
崩了怎么办」三个问题，而写几 KB 文本是亚毫秒操作，那三个问题根本不必存在。

写盘失败时错误挂在窗口顶部，不打 `Debug.LogError` —— 窗口每帧重绘，
每帧一条错误会瞬间刷爆 Console，而你看到的只是「窗口好像没反应」。

#### 已知限制

| 限制 | 说明 |
|---|---|
| **只画一层组合子** | 使用者拍板的深度。组合子的子列表里不能再放组合子（菜单项会灰掉并提示）。**表本身支持任意深度**，深嵌套直接改 JSON。 |
| **注释会被冲掉** | 窗口保存时重写整个文件，`CardTable.json` 里的 `//` 注释会丢。目前那些注释的内容已经全部写进本节和 4.9.1–4.9.8。 |
| **升级版继承描述 + 重排升级版效果** | 升级版 `desc` 省略（继承基础版）而 `effects` 自己一套时，重排升级版效果**不会**重排那句继承来的描述 —— 因为它属于基础版。这种情况下建议勾上升级版自己的「描述模板」。 |
| **窗口不认识的字段类型** | 会画一行灰字「请在 JSON 里改」，而不是静默跳过。静默跳过意味着那个字段永远无法在窗口里设置，且没人会发现。 |

#### 新增效果类之后不用改这个窗口

字段控件由**反射**从字段类型推导，「是不是组合子」也由反射判断
（看字段里装不装得下 `CardEffect`），所以在 `Effects/Impl/` 新建一个效果类之后，
它立刻在窗口里可编辑、在添加菜单里出现、嵌套逻辑也正确。

唯一纯装饰的部分是添加菜单的分类名，它有兜底桶 ——
没归类的新效果落进「其他」，**不会从菜单里消失**。

---

## 5. 现有战斗效果目录

效果实现位于 [Effects/Impl](../Game/Effects/Impl/)。

| 效果 | 用途 | 关键参数 |
|---|---|---|
| `DamageEffect` | 伤害、多段伤害 | `Amount`、`Times`、`Kind`、`IgnoreBlock` |
| `BlockEffect` | 获得护甲 | `Amount` |
| `DrawEffect` | 抽牌 | `Count` |
| `EnergyEffect` | 正数获得、负数消耗能量 | `Amount` |
| `ApplyStatusEffect` | 施加 Buff/Debuff | `Status`、`Stacks` |
| `HealEffect` | 治疗 | `Amount` |
| `DiscardEffect` | 随机、全部或玩家选择弃牌 | `DiscardMode`、`Count` |
| `ExhaustEffect` | 消耗自身或手牌 | `ExhaustMode`、`Count` |
| `AddCardEffect` | 生成卡到指定牌堆 | `Card`、`Pile`、`Temporary`、`Upgraded` |
| `ModifyCardCostEffect` | 修改手牌费用 | `Where`、`Delta`、`ThisTurnOnly` |
| `SelectCardsEffect` | 从牌堆选牌后执行处置 | `Source`、`Count`、`Action` |
| `RepeatEffect` | 重复一组子效果 | `Times`、`Effects` |
| `ConditionalEffect` | 条件分支 | `Condition`、`Then`、`Else` |
| `RandomPickEffect` | 按权重抽子效果 | `Options`、`PickCount` |
| `DelayedEffect` | 回合末或下回合开始执行 | `Timing`、`Effects` |

### 5.1 效果目标选择器

| `TargetKind` | 实际目标 |
|---|---|
| `None` | 没有单位目标 |
| `Self` | 效果来源单位 |
| `ChosenTarget` | 玩家或 AI 指定的目标 |
| `AllEnemies` | 来源单位的所有存活敌人 |
| `AllAllies` | 来源单位的所有存活友军，包括自己 |
| `AllUnits` | 全部存活单位 |
| `RandomEnemy` | 随机敌人 |
| `LowestHpEnemy` | 当前生命最低的敌人 |
| `HighestHpEnemy` | 当前生命最高的敌人 |
| `PreviousTargets` | 上一个效果命中的目标 |

额外参数：

- `Count`：`RandomEnemy` 选几个；0 视为 1。
- `AllowDuplicates`：随机选择是否允许重复命中。
- `ExcludeSelf`：解析后排除来源单位。
- `RequireStatusId`：只保留带指定状态的单位。

### 5.2 条件效果和敌人行动可用的条件

| `ConditionKind` | 条件 |
|---|---|
| `Always` | 恒成立 |
| `SelfHasStatus` | 来源单位有指定状态和层数 |
| `TargetHasStatus` | 目标有指定状态和层数 |
| `SelfHpBelowPercent` | 来源生命低于百分比 |
| `TargetHpBelowPercent` | 目标生命低于百分比 |
| `HandCountAtLeast` | 手牌至少 N 张 |
| `EnergyAtLeast` | 能量至少 N |
| `TurnNumberAtLeast` | 回合数至少 N |
| `EnemyCountAtLeast` | 存活敌人至少 N |
| `LastCardWasAttack` | 上一张完整结算的卡是攻击牌 |
| `IsFirstTurn` | 当前为第一回合 |
| `SelfBlockAtLeast` | 来源护甲至少 N |

需要状态 Id 的条件使用 `Id` 字段，数值条件使用 `Value`，`Invert` 反转结果。

### 5.3 选牌效果的处置

`SelectCardsEffect.Action` 支持：

```text
Discard
Exhaust
Retain
Duplicate
ToDrawTop
ToDrawBottom
ToHand
ToDiscard
```

`Source` 可从 `Hand`、`Draw`、`Discard` 或 `Exhaust` 选择。交互局会挂起等待玩家；非交互局由 `BattleContext.Selector` 同步选择。

`DamageKind` 与穿透护甲是两件事：

- `DamageKind.Loss` 表示这不是普通攻击，不会触发荆棘等攻击逻辑。
- `IgnoreBlock = true` 才会无视护甲。

---

## 6. 动态数值 `EffectValue`

大部分“伤害随某项数值增长”的需求不需要新效果类。

公式：

```text
最终值 = Base + 缩放单位数 × PerUnit
```

之后再应用 `Min` / `Max`。

| `ValueScale` | 缩放来源 |
|---|---|
| `None` | 固定值 |
| `PerStatusStackOnSelf` | 来源单位的指定状态层数 |
| `PerStatusStackOnTarget` | 目标的指定状态层数 |
| `PerCardInHand` | 手牌数 |
| `PerCardInDiscard` | 弃牌堆数量 |
| `PerCardPlayedThisTurn` | 本回合已打出卡数 |
| `PerEnemyAlive` | 存活敌人数 |
| `PerBlockOnSelf` | 来源单位当前护甲 |
| `PerMissingHpOnSelf` | 来源单位已损失生命 |
| `XValue` | X 费实际消耗的能量 |
| `PerRepeatIndex` | 当前重复下标，从 0 开始 |

例：造成等同于当前护甲的伤害：

```csharp
Amount = new EffectValue
{
    Base = 0,
    Scale = ValueScale.PerBlockOnSelf,
    PerUnit = 1,
}
```

例：基础 3 点，每层力量额外 2 点：

```csharp
Amount = new EffectValue
{
    Base = 3,
    Scale = ValueScale.PerStatusStackOnSelf,
    ScaleId = "strength",
    PerUnit = 2,
}
```

---

## 7. 新增一种战斗效果

只有现有效果和组合子确实无法表达时才新增。

示例：新增“移除指定状态”的通用效果：

```csharp
using System;
using Game.Statuses;

namespace Game.Effects.Impl
{
    [Serializable]
    public class RemoveStatusEffect : CardEffect
    {
        public StatusDefinition Status;

        public RemoveStatusEffect()
        {
            Target = TargetSelector.SelfOnly;
        }

        public override bool CanApply(EffectContext ctx)
            => Status != null;

        public override void Apply(EffectContext ctx)
        {
            for (int i = 0; i < ctx.Targets.Count; i++)
                ctx.Targets[i].RemoveStatus(ctx.Battle, Status.Id);
        }

        public override string Describe(EffectContext ctx)
            => Status != null ? Status.LocalizedName : "?";
    }
}
```

新增步骤：

1. 在 `Assets/Game/Effects/Impl/` 新建类。
2. 加 `[Serializable]`。
3. 继承 `CardEffect`。
4. 所有可配置参数使用序列化字段。
5. 不得添加会在 `Apply()` 中改变的私有实例字段。
6. 实现 `CanApply()`、`Apply()`，需要动态描述时实现 `Describe()`。
7. 在生成器卡牌或 Inspector 资产里使用。
8. 若该效果引用状态且应显示状态 Tooltip，更新 `EffectTree.CollectStatuses`。
9. 若敌人意图需要预览这个效果，更新 `EnemyBrain.BuildIntent`。
10. 若效果会引入全新表现，增加合适的 `BattleEventType`，由 UI 消费事件，不要让逻辑层直接控制动画。

如果新增的是第五种组合子，还必须更新所有效果树递归入口。目前至少包括：

- `EffectTree`
- `ContentValidator` 中的目标检查
- 任何新加入的效果分析工具

结算可能因选牌而挂起。组合子不能依赖普通 `for` 循环执行完子效果，也不能把“结算完成后的操作”写在 `ResolveAll()` 下一行；应使用结算栈或 `onComplete`。

---

## 8. 新增状态

相关文件：

- [StatusDefinition.cs](../Game/Statuses/StatusDefinition.cs)
- [StatusInstance.cs](../Game/Statuses/StatusInstance.cs)
- [StatusBehaviour.cs](../Game/Statuses/StatusBehaviour.cs)
- [BattleHooks.cs](../Game/Battle/BattleHooks.cs)
- [CombatBehaviours.cs](../Game/Statuses/Impl/CombatBehaviours.cs)
- [ProtectiveBehaviours.cs](../Game/Statuses/Impl/ProtectiveBehaviours.cs)

### 8.1 只用已有行为

如果新状态只是已有机制的不同参数，可以直接建立 `StatusDefinition` 并复用行为。

例如“脆弱加强版”仍可使用 `VulnerableBehaviour`，把 `PercentBonus` 改成 75。

### 8.2 生成器流程

```csharp
var frail = MakeStatus(
    "frail",
    "破甲",
    StatusPolarity.Debuff,
    StatusDecay.LoseOneAtTurnEnd,
    "获得的护甲减少 25%，剩余 {stacks} 回合。",
    new FrailBehaviour { PercentPenalty = 25 });

frail.MaxStacks = 99;
EditorUtility.SetDirty(frail);
```

### 8.3 Inspector 流程

1. 在 `Assets/GameData/Statuses/` 创建 `Game/Status`。
2. 填唯一 `Id`、名称、极性和衰减方式。
3. `Description` 使用 `{stacks}` 表示当前层数。
4. 在 `Behaviours` 中加入行为。
5. 设置 `MaxStacks`。
6. 执行生成器自动登记。

### 8.4 新状态行为示例

```csharp
using System;
using Game.Battle;

namespace Game.Statuses.Impl
{
    [Serializable]
    public class FrailBehaviour : StatusBehaviour, IBlockHook
    {
        public int PercentPenalty = 25;

        public override int Order => HookOrder.Multiply;

        public void ModifyBlockGain(
            BattleContext ctx,
            in HookSource src,
            ref int amount)
        {
            amount = amount * (100 - PercentPenalty) / 100;
        }

        public void ModifyBlockDecay(
            BattleContext ctx,
            in HookSource src,
            ref bool clear)
        {
        }
    }
}
```

行为类不能保存运行时计数。运行时数据应放在：

- 当前状态层数：`HookSource.Status.Stacks`
- 战斗上下文：`BattleContext`
- 单位状态：`BattleUnit`

### 8.5 Hook 选择表

| Hook | 用途 |
|---|---|
| `ITurnHook` | 回合开始、回合结束 |
| `IBattleFlowHook` | 战斗开始、战斗结束 |
| `ICardPlayHook` | 出牌、抽牌、弃牌、修改费用 |
| `IDamageHook` | 修改输出/输入伤害、受伤后触发 |
| `IBlockHook` | 修改护甲获得、阻止护甲衰减 |
| `IHealHook` | 修改治疗量 |
| `IEnergyHook` | 修改能量获得 |
| `IDeathHook` | 单位死亡后 |
| `IStatusHook` | 状态施加前后 |
| `IFatalHook` | 致死伤害拦截 |
| `ICardFlowHook` | 出牌前拦截、修改打出后的归宿 |
| `IResourceHook` | 每回合抽牌数、能量数 |

`Order` 决定同类 Hook 顺序：

```text
Early(-100) → AddFlat(0) → Multiply(100) → Late(200)
```

当前已有状态行为：

| 行为 | 作用 |
|---|---|
| `StrengthBehaviour` | 攻击伤害加固定值 |
| `VulnerableBehaviour` | 受到的攻击伤害按百分比增加 |
| `WeakBehaviour` | 造成的攻击伤害按百分比减少 |
| `PoisonBehaviour` | 回合末造成无视护甲的状态伤害 |
| `BarricadeBehaviour` | 阻止回合开始清空护甲 |
| `ThornsBehaviour` | 受到攻击后排队反伤 |
| `ArtifactBehaviour` | 抵消外部施加的 Debuff |
| `ReviveBehaviour` | 拦截致死伤害并消耗层数 |
| `TurnStartGrantStatusBehaviour` | 回合开始按自身层数施加另一状态 |
| `RegenerateBehaviour` | 回合末按层数治疗 |

### 8.6 状态衰减

衰减只由 `BattleController.TickStatusDecay` 处理：

- `None`
- `LoseOneAtTurnEnd`
- `RemoveAtTurnEnd`
- `LoseAllAtTurnStart`

不要在 `StatusBehaviour.OnTurnEnd()` 里再手动做自然衰减，否则可能出现“先减层再结算”或重复减层。

---

## 9. 新增敌人

相关文件：

- [EnemyDefinition.cs](../Game/Enemies/EnemyDefinition.cs)
- [EnemyAction.cs](../Game/Enemies/EnemyAction.cs)
- [EnemyBrain.cs](../Game/Enemies/EnemyBrain.cs)
- [Intent.cs](../Game/Enemies/Intent.cs)

### 9.1 默认 AI 已能处理的规则

无需自定义 Brain 即可实现：

- 固定行动序列
- 固定序列后进入权重随机
- 固定序列循环
- 行动条件
- 最大连续使用次数
- 按血量百分比切阶段
- 不同阶段使用不同行动

### 9.2 生成器示例

```csharp
var knight = LoadOrCreate<EnemyDefinition>(
    $"{EnemyDir}/Enemy_AshenKnight.asset");

knight.Id = "ashen_knight";
knight.DisplayName = "灰烬骑士";
knight.MinHp = 55;
knight.MaxHp = 62;
knight.IsElite = false;
knight.IsBoss = false;

knight.StartingStatuses = new List<StartingStatus>
{
    new StartingStatus
    {
        Status = Statuses["strength"],
        Stacks = 2,
    },
};

knight.Actions = new List<EnemyAction>
{
    new EnemyAction
    {
        Name = "斩击",
        Intent = IntentKind.Attack,
        Weight = 70,
        MaxConsecutive = 2,
        Effects = new List<CardEffect>
        {
            new DamageEffect
            {
                Target = TargetSelector.Chosen,
                Amount = EffectValue.Flat(9),
            },
        },
    },
    new EnemyAction
    {
        Name = "架势",
        Intent = IntentKind.Defend,
        Weight = 30,
        MaxConsecutive = 1,
        Effects = new List<CardEffect>
        {
            new BlockEffect
            {
                Target = TargetSelector.SelfOnly,
                Amount = EffectValue.Flat(8),
            },
            new ApplyStatusEffect
            {
                Target = TargetSelector.SelfOnly,
                Status = Statuses["strength"],
                Stacks = EffectValue.Flat(1),
            },
        },
    },
};

knight.FixedSequence = new List<int>();
knight.LoopSequence = false;
knight.PhaseHpThresholds = new List<int>();
knight.CustomBrainType = "";

EditorUtility.SetDirty(knight);
Enemies[knight.Id] = knight;
```

### 9.3 Inspector 流程

1. 在 `Assets/GameData/Enemies/` 创建 `Game/Enemy`。
2. 填 Id、名称、最低/最高生命。
3. 配置 `StartingStatuses`。
4. 在 `Actions` 中加入行动。
5. 每个行动配置：
   - 名称
   - 意图图标类型
   - 权重
   - 连续使用限制
   - 条件
   - 阶段位掩码
   - 效果列表
6. 按需配置 `FixedSequence`。
7. 如果要进入地图，还必须创建至少一个 `EncounterDefinition` 引用它。
8. 执行生成器自动登记。

### 9.4 行动字段含义

| 字段 | 含义 |
|---|---|
| `Weight` | 权重随机的相对权重；0 表示不会被随机选中 |
| `MaxConsecutive` | 最多连续使用次数；0 表示不限 |
| `Condition` | 不满足时不进入候选池 |
| `PhaseMask` | 0 表示所有阶段；否则第 N 位表示阶段 N |
| `Effects` | 行动真正执行的战斗效果 |

`PhaseMask` 示例：

```text
0b001 = 只在阶段 0
0b010 = 只在阶段 1
0b100 = 只在阶段 2
0b011 = 阶段 0 或 1
```

`FixedSequence` 存的是 `Actions` 下标，不是 Id。往 `Actions` 中间插入行动会改变后续下标。

固定序列规则：

- 序列还没走完：固定序列优先。
- `LoopSequence = true`：走完后从头循环。
- `LoopSequence = false`：走完后转入权重随机。

所有候选都失败时，默认 AI 会回退到当前阶段第一项可用行动。

### 9.5 敌人的两个当前限制

1. `EnemyDefinition.Art` 当前没有被 `UnitView` 使用，赋图不会自动显示。
2. `EnemyDefinition.IsElite/IsBoss` 当前不决定地图池或奖励。

真正决定战斗属于普通、精英还是 Boss 的是 `EncounterDefinition.IsElite/IsBoss`。

### 9.6 意图预览限制

默认 `EnemyBrain.BuildIntent` 只在行动的顶层效果中寻找第一个：

- `DamageEffect`
- `BlockEffect`

如果伤害藏在 `RepeatEffect` 或其他组合子里，意图数字可能显示为 0。新增会影响意图数字的效果时，需要扩展 `BuildIntent` 的预览逻辑。

---

## 10. 新增自定义敌人 AI

只有默认数据驱动规则无法表达时才写自定义 Brain，例如：

- 某阶段切换时清除状态并触发特殊机制
- 根据玩家护甲、牌库或其他复杂信息强制选择特定行动
- 行动选择依赖默认 `EffectCondition` 没有的数据

示例：

```csharp
using Game.Battle;

namespace Game.Enemies.Impl
{
    public class AshenKnightBrain : EnemyBrain
    {
        private const int ACTION_BREAK_GUARD = 2;

        protected override int ChooseAction(BattleContext ctx)
        {
            if (ctx.Player.Block >= 15 &&
                ACTION_BREAK_GUARD < Def.Actions.Count)
            {
                return ACTION_BREAK_GUARD;
            }

            return base.ChooseAction(ctx);
        }

        protected override void OnPhaseChanged(
            BattleContext ctx,
            int newPhase)
        {
            base.OnPhaseChanged(ctx, newPhase);

            if (newPhase == 1)
                Unit.RemoveStatus(ctx, "weak");
        }
    }
}
```

在敌人资产中填写：

```text
Game.Enemies.Impl.AshenKnightBrain, Game.Runtime
```

注意：

- 必须写完整类型名和程序集名。
- 找不到类型时会退回默认 `EnemyBrain`。
- 自定义 Brain 引用行动下标时必须定义有名字的常量。
- `EnemyBrain` 是每个敌人实例独立创建的，可以保存该敌人的运行时历史；这与共享的 `CardEffect`/`StatusBehaviour` 不同。
- 随机必须使用 `ctx.Rng` 和适当的 `RngStream`。

调用链：

```text
BattleController.StartBattle
  → Type.GetType(CustomBrainType)
  → Activator.CreateInstance
  → brain.Init
  → 每回合 DecideIntent
  → UpdatePhase
  → ChooseAction
  → BuildIntent
  → 敌人回合 ExecuteIntent
  → EffectResolver.ResolveAll
```

---

## 11. 新增战斗组合 `Encounter`

`EnemyDefinition` 是敌人模板；`EncounterDefinition` 才是一场实际战斗的敌人组合。

### 11.1 生成器流程

```csharp
MakeEncounter(
    "ashen_patrol",
    "灰烬巡逻队",
    false,
    false,
    "ashen_knight",
    "louse");
```

参数顺序：

```text
Id、显示名、是否精英、是否 Boss、敌人 Id 列表
```

同一个敌人 Id 可以重复出现，每次都会创建独立 `BattleUnit`。

### 11.2 Inspector 流程

1. 在 `Assets/GameData/Encounters/` 创建 `Game/Encounter`。
2. 填唯一 Id 和显示名。
3. 把敌人资产拖进 `Enemies`。
4. 设置 `IsElite` / `IsBoss`。
5. 执行生成器自动登记。

### 11.3 标记实际影响

| Encounter 标记 | 地图池 | 奖励 |
|---|---|---|
| 普通 | 普通战斗节点 | 普通金币、卡牌、普通药水概率 |
| `IsElite` | 精英节点 | 更多金币、更高稀有卡概率、遗物、较高药水概率 |
| `IsBoss` | Boss 节点 | Boss 金币、更高稀有卡概率、Boss 遗物、必定尝试掉药水 |

地图生成时，`RunManager` 从 `GameDatabase.GetEncounterIds()` 分别取得普通、精英和 Boss 池。

---

## 12. 新增药水

相关文件：

- [PotionDefinition.cs](../Game/Potions/PotionDefinition.cs)
- [SampleContentPotions.cs](../Game/Editor/SampleContentPotions.cs)
- [BattleController.cs](../Game/Battle/BattleController.cs)

药水效果就是 `List<CardEffect>`。不要为药水另建一套效果系统。

### 12.1 生成器流程

```csharp
Make(
    dir,
    output,
    "greater_fire",
    "强效火焰药水",
    PotionRarity.Rare,
    "对一个敌人造成 {0} 点伤害。",
    CardTargetKind.SingleEnemy,
    new DamageEffect
    {
        Target = TargetSelector.Chosen,
        Amount = EffectValue.Flat(35),
    });
```

### 12.2 Inspector 流程

1. 在 `Assets/GameData/Potions/` 创建 `Game/Potion`。
2. 填 Id、名称、稀有度、描述模板。
3. `TargetKind` 只使用：
   - `None`
   - `SingleEnemy`
4. 配置已有 `CardEffect`。
5. `ShopPrice = 0` 表示使用稀有度默认价格。
6. 执行生成器自动登记。

### 12.3 药水调用链

```text
药水栏点击
  → BattleController.CanUsePotion
  → 从 RunContext.Potions 移除
  → EffectResolver.ResolveAll
  → BattleEventType.PotionUsed
  → UI 刷新药水槽
```

药水在结算前先从背包移除，因此药水效果可以安全地再获得药水，而不会错误占用原槽位。

药水允许重复持有和重复出现在商店。背包受 `RunContext.PotionSlots` 限制。

`PotionDefinition.Icon` 当前没有被药水栏 UI 使用。

---

## 13. 新增遗物

相关文件：

- [RelicDefinition.cs](../Game/Relics/RelicDefinition.cs)
- [RelicInstance.cs](../Game/Relics/RelicInstance.cs)
- [RelicBehaviours.cs](../Game/Relics/Impl/RelicBehaviours.cs)

遗物行为与状态共用 Battle Hook。新增遗物不应修改 `BattleController`。

### 13.1 用已有行为配置

生成器示例：

```csharp
Make(
    dir,
    output,
    "war_horn",
    "战号",
    RelicRarity.Uncommon,
    "每场战斗的第一回合额外获得 1 点能量并抽 1 张牌。",
    new TurnResourceBehaviour
    {
        ExtraEnergy = 1,
        ExtraDraw = 1,
        FirstTurnOnly = true,
    });
```

Inspector 流程：

1. 在 `Assets/GameData/Relics/` 创建 `Game/Relic`。
2. 填 Id、名称、稀有度、描述。
3. 在 `Behaviours` 中加入已有行为。
4. 设置 `ShopPrice`；0 使用默认价。
5. 执行生成器自动登记。

### 13.2 现有遗物行为

| 行为 | 用途 |
|---|---|
| `GrantStatusOnBattleStartBehaviour` | 战斗开始挂状态 |
| `BattleStartResourceBehaviour` | 开战治疗或获得护甲 |
| `BattleRewardBehaviour` | 胜利后治疗或金币 |
| `TurnResourceBehaviour` | 改每回合抽牌或能量 |
| `FirstCardCostReductionBehaviour` | 第一张指定类型卡减费 |
| `EchoFirstCardBehaviour` | 第一张指定类型卡额外结算 |
| `CardDestinationBehaviour` | 改变卡牌打出后的归宿 |
| `EveryNCardsHealBehaviour` | 每打出 N 张指定类型卡治疗 |

### 13.3 新遗物行为

新行为仍然继承 `StatusBehaviour` 并实现合适 Hook。

计数规则：

- 每场战斗独立、可表示为层数：开战挂一个状态，用 `StatusInstance.Stacks`。
- 遗物自己的计数：使用 `HookSource.Relic.Counter`。
- 不得在 Behaviour 的私有字段中保存运行时计数。

如果计数应该每场清零，在 `IBattleFlowHook.OnBattleStart` 中重置。

### 13.4 稀有度与当前公共池行为

`RelicRarity.Starter` 不进入默认遗物池。

当前 `GameDatabase.GetRelicsByRarity(null)` 只排除 `Starter`，因此：

- `Boss`
- `Shop`

也可能被普通的“任意稀有度遗物”抽取逻辑选中。枚举注释把它们描述为专属类型，但当前池过滤并未完全强制专属。设计新遗物时不要假设只设枚举就一定隔离；若需要严格专属，应同时调整 `GetRelicsByRarity` 或调用方筛选规则。

`RelicDefinition.Icon` 当前没有被顶栏 UI 使用。

---

## 14. 新增事件

相关文件：

- [EventDefinition.cs](../Game/Events/EventDefinition.cs)
- [SampleContentEvents.cs](../Game/Editor/SampleContentEvents.cs)
- [RunEffect.cs](../Game/RunEffects/RunEffect.cs)
- [RunCondition.cs](../Game/RunEffects/RunCondition.cs)
- [EventScreen.cs](../Game/UI/EventScreen.cs)

### 14.1 生成器流程

```csharp
Make(
    dir,
    output,
    "forgotten_cache",
    "被遗忘的储藏室",
    "灰尘之下似乎还剩下一些补给。",
    new EventOption
    {
        Text = "拿走金币",
        ResultText = "你找到了一小袋金币。",
        Effects = new List<RunEffect>
        {
            new GoldRunEffect { Amount = 60 },
        },
        EndsEvent = true,
    },
    new EventOption
    {
        Text = "离开",
        Effects = new List<RunEffect>(),
        EndsEvent = true,
    });
```

### 14.2 Inspector 流程

1. 在 `Assets/GameData/Events/` 创建 `Game/Event`。
2. 填 Id、标题、描述。
3. 建立 `Options`。
4. 每个选项配置：
   - `Text`
   - `Condition`
   - `DisabledHint`
   - `ResultText`
   - `Effects`
   - `EndsEvent`
5. 至少保留一个无条件且会结束事件的出口。
6. 执行生成器自动登记。

### 14.3 现有局外条件

| 条件 | 判断 |
|---|---|
| `Always` | 恒成立 |
| `GoldAtLeast` | 金币至少 N |
| `HpBelowPercent` | 当前生命低于百分比 |
| `HpAtLeast` | 当前生命至少 N |
| `HasRelic` | 持有指定遗物 Id |
| `HasCard` | 牌库有指定卡牌 Id |
| `DeckSizeAtLeast` | 牌库至少 N 张 |
| `HasUpgradableCard` | 至少有一张可升级卡 |
| `BattlesWonAtLeast` | 已胜利至少 N 场 |

`Invert` 会反转结果。

### 14.4 现有局外效果

| 效果 | 用途 |
|---|---|
| `GoldRunEffect` | 增减金币 |
| `HpRunEffect` | 固定值或最大生命百分比增减当前生命 |
| `MaxHpRunEffect` | 改最大生命 |
| `RestHealRunEffect` | 按最大生命百分比治疗 |
| `AddCardRunEffect` | 给指定卡、随机卡或三选一 |
| `RemoveCardRunEffect` | 删除牌库卡牌 |
| `UpgradeCardRunEffect` | 升级卡牌 |
| `GainRelicRunEffect` | 获得指定或随机遗物 |
| `GainPotionRunEffect` | 获得指定或随机药水 |
| `PotionSlotsRunEffect` | 改药水槽位 |
| `ConditionalRunEffect` | 条件分支 |
| `RandomPickRunEffect` | 按权重随机分支 |
| `StartBattleRunEffect` | 记录待开始的战斗 |

`StartBattleRunEffect` 不直接切换流程。它只写：

```text
RunContext.PendingBattleEncounterId
RunContext.PendingBattleGivesReward
```

之后由 `RunManager.ReturnToMap()` 统一启动战斗。

### 14.5 事件当前限制

- `EventDefinition.Art` 当前没有被 `EventScreen` 使用。
- `EventDefinition.MinRow` 当前没有进入 `RunManager.GetEventIds()` 或 `MapGenerator` 的筛选，因此设置它不会限制事件出现层数。
- 事件选项和敌人行动的本地化 key 使用列表下标。往列表中间插入新项会使后续译文 key 改变。

---

## 15. 新增局外效果或条件

### 15.1 新 `RunEffect`

```csharp
using System;
using Game.Localization;

namespace Game.RunEffects.Impl
{
    [Serializable]
    public class SetGoldRunEffect : RunEffect
    {
        public int Amount;

        public override bool CanApply(RunEffectContext ctx)
            => ctx?.Run != null;

        public override void Apply(RunEffectContext ctx)
        {
            ctx.Run.Gold = Math.Max(0, Amount);
            ctx.AddLog(
                Loc.T("run.setgold.done", "金币变为 {0}", ctx.Run.Gold));
        }

        public override string Describe(RunEffectContext ctx)
            => Loc.T("run.setgold.desc", "将金币设为 {0}", Amount);
    }
}
```

规则：

- 不得直接切换 `RunPhase`。
- 不得持有 UI 类型。
- 需要玩家选卡时，向 `RunEffectContext.Choices` 排 `RunChoiceRequest`。
- 可变数据必须写进 `RunContext`。
- 玩家可见文案使用 `Loc.T`。

### 15.2 新 `RunCondition`

当前条件是 `RunConditionKind` 枚举 + `RunCondition.Test()` 的集中实现。

新增步骤：

1. 在 `RunConditionKind` 增加枚举项。
2. 在 `RunCondition.Test()` 增加分支。
3. 明确使用 `Id` 还是 `Value`。
4. 检查事件 Inspector 中的含义是否清楚。

如果条件只供一个组合效果使用，也应优先考虑复用 `RunCondition`，不要在 UI 内硬编码。

---

## 16. 新增关键字

关键字分两类。

### 16.1 只新增说明文案

如果枚举位和运行逻辑已经存在，只需创建 `KeywordDefinition`：

1. 在 `Assets/GameData/Keywords/` 创建 `Game/Keyword`。
2. `Keyword` 只能选一个位。
3. 填显示名和描述。
4. 执行生成器自动登记。

### 16.2 新增真正的新机制关键字

例如新增“打出后回到手牌”：

1. 在 `CardKeyword` 增加新的独立二进制位：

   ```csharp
   ReturnToHand = 1 << 5
   ```

2. 在真正执行规则的位置接线。
   - 开局抽到：`DeckController.Init`
   - 回合末处理：`DeckController.EndTurnDiscard`
   - 是否可打：`BattleController.CanPlayCard`
   - 打出后归宿：`BattleController.SendCardToDestination`
3. 在 `TooltipContent.AllKeywords` 加入新位。
4. 在 `CardView` 和 `CardPickerScreen` 增加卡面标签显示。
5. 创建 `KeywordDefinition` 或在生成器的 `CreateKeywordDefinitions()` 中生成。
6. 加入本地化。

仅增加枚举和 `KeywordDefinition` 只会让它有名字，不会自动产生玩法行为。

---

## 17. 奖励池、商店池与稀有度

相关文件：

- [ContentPicker.cs](../Game/Core/ContentPicker.cs)
- [RewardGenerator.cs](../Game/Core/RewardGenerator.cs)
- [ShopStock.cs](../Game/Core/ShopStock.cs)
- [GameDatabase.cs](../Game/Core/GameDatabase.cs)

### 17.1 卡牌

默认公共卡池排除：

- `Basic`
- `Special`

并包含：

- `Common`
- `Uncommon`
- `Rare`

基础权重：

```text
Common 60
Uncommon 32
Rare 8
```

精英和 Boss 奖励会提高 Rare 权重。

商店每次取 5 张卡，默认价格：

```text
Common 50
Uncommon 75
Rare 150
```

实际价格再做 ±10% 浮动。

### 17.2 药水

权重：

```text
Common 65
Uncommon 25
Rare 10
```

商店每次尝试放 3 瓶，允许同款重复。

默认价格：

```text
Common 50
Uncommon 75
Rare 100
```

资产 `ShopPrice > 0` 时覆盖默认价。

### 17.3 遗物

商店每次尝试放 2 个，不允许同店重复，也不返回玩家已经持有的遗物。

资产 `ShopPrice > 0` 时覆盖默认价。

修改权重会改变相应随机流之后的全部结果。固定种子仍可复现新规则，但不会保持旧版本的同一奖励序列。

---

## 18. 修改初始牌组和开局配置

完整流程入口：

- [GameApp.cs](../Game/UI/GameApp.cs)

字段：

```text
StartingMaxHp
StarterRelicId
StarterDeck
FixedSeed
```

在完整流程场景选中 `GameApp`，可直接在 Inspector 修改：

```text
CardId = strike
Count = 5
```

单场战斗测试入口：

- [BattleBootstrap.cs](../Game/UI/BattleBootstrap.cs)

它有独立的：

```text
EncounterId
MaxHp
EnergyPerTurn
CardsPerTurn
StarterDeck
Seed
```

修改 `GameApp.StarterDeck` 不会同步修改 `BattleBootstrap.StarterDeck`，反之亦然。

如果未来加入多角色，不建议继续把所有角色配置塞进 `GameApp`。应新增角色 Definition，包含初始生命、初始牌组 Id、起始遗物 Id，再由 `StartNewRun` 接收角色定义。

---

## 19. 本地化

相关文件：

- [Loc.cs](../Game/Localization/Loc.cs)
- [LocaleTable.cs](../Game/Localization/LocaleTable.cs)
- [LocalizationKeys.cs](../Game/Editor/LocalizationKeys.cs)
- [LocalizationTool.cs](../Game/Editor/LocalizationTool.cs)

### 19.1 基本规则

- 简体中文是源语言。
- 中文直接写在代码或 Definition 字段里。
- 不创建 `zh-Hans` 语言表。
- 其他语言缺少翻译时回退到中文。

Definition 的 key 自动由 Id 派生：

```text
card.<id>.name
card.<id>.desc
status.<id>.name
status.<id>.desc
enemy.<id>.name
enemy.<id>.action.<index>.name
encounter.<id>.name
relic.<id>.name
relic.<id>.desc
potion.<id>.name
potion.<id>.desc
event.<id>.title
event.<id>.option.<index>.text
keyword.<enum-name>.name
```

### 19.2 新内容翻译流程

1. 先完成简中内容资产。
2. 执行：

   ```text
   Tools/卡牌游戏/5. 导出本地化 CSV
   ```

3. 在 `en`、`zh-Hant`、`ja` 等列填写翻译。
4. 不要修改 `key`。
5. 保持所有占位符完全一致。
6. 执行：

   ```text
   Tools/卡牌游戏/6. 导入本地化 CSV
   ```

7. 导入工具会创建或更新 `Locale_<code>.asset`，并登记进数据库。

占位符示例：

```text
中文：造成 {0} 点伤害并抽 {1} 张牌。
英文：Deal {0} damage and draw {1} card(s).
```

不能把 `{0}` 改为 `｛0｝`、删掉或换成 `{2}`。

### 19.3 新代码中的文案

玩家可见字符串：

```csharp
Loc.T("run.example.key", "中文回退文案")
```

带参数：

```csharp
Loc.T("run.example.amount", "获得 {0} 金币", amount)
```

不要在外面拼接受语序影响的句子。

---

## 20. 随机数与固定种子

所有随机必须使用 [Rng.cs](../Game/Core/Rng.cs)，禁止使用 `UnityEngine.Random`。

现有随机流：

| 流 | 用途 |
|---|---|
| `Map` | 地图结构和节点内容 |
| `Encounter` | 敌人初始生命等 |
| `CardDraw` | 抽牌、洗牌 |
| `Battle` | 一般战斗随机 |
| `EnemyAction` | 敌人行动选择 |
| `Reward` | 金币、卡牌、遗物奖励 |
| `Shop` | 商店库存与价格浮动 |
| `Event` | 事件随机分支 |
| `CardEffect` | 随机目标、随机弃牌、随机子效果 |
| `Potion` | 药水掉落和抽取 |

修改随机逻辑的影响：

- 在某条流中多调用一次随机，会改变该流之后的结果。
- 不会改变其他流。
- 新增一种完全独立的随机系统时，优先新增 `RngStream`。
- 只做 UI 预览时必须设置 `EffectContext.PreviewMode = true`，否则每帧预览会消耗随机流。

需要复现问题时：

- 完整流程：设置 `GameApp.FixedSeed`。
- 单场战斗：设置 `BattleBootstrap.Seed` 并打开 `DeterministicSeed`。

---

## 21. 存档影响

当前阶段 5 存档尚未实现，但代码已经约定：

- Definition 只按 Id 保存，不保存 Unity 资产引用。
- 一局内需要跨战斗保留的数据必须放在 `RunContext`。
- 一场战斗内的数据放在 `BattleContext`、`BattleUnit`、`CardInstance`、`StatusInstance`。

新增内容对未来存档的影响：

| 改动 | 存档影响 |
|---|---|
| 只新增卡牌/敌人/遗物资产 | 保存其 Id 即可 |
| 修改已有 Id | 旧存档无法找到原内容 |
| 新增跨战斗计数 | 必须进入 `RunContext` 或 `RelicInstance.Counter` |
| 新增卡牌/药水实例类型 | 必须保存 Uid 计数器 |
| 新增随机流 | 必须保存和恢复该流状态 |
| 新增地图节点数据 | 必须进入 `GameMap/MapNode` 的序列化结构 |

现有 `_nextCardUid` 和 `_nextPotionUid` 在读档时必须恢复到大于所有已存在 Uid 的位置。

---

## 22. UI 与美术影响

项目没有 prefab，所有 UI 由 `Assets/Game/UI/` 下的代码创建。

当前 Definition 虽然有以下字段：

```text
CardDefinition.Art
EnemyDefinition.Art
EventDefinition.Art
StatusDefinition.Icon
PotionDefinition.Icon
RelicDefinition.Icon
```

但当前 UI 没有读取这些 Sprite。为资产填图不会自动显示。要显示图片，需要修改对应 View：

| 内容 | 主要 UI |
|---|---|
| 卡牌 | `CardView`、`CardPickerScreen` |
| 敌人/状态/意图 | `UnitView` |
| 药水 | `BattleScreen` 药水栏与 `TooltipContent` |
| 遗物 | `TopBarView` |
| 事件 | `EventScreen` |

新增全新 `RunPhase` 时通常需要同时修改：

1. `RunContext.RunPhase`
2. `RunManager` 的进入和退出流程
3. `GameApp.CreateScreen`
4. 新的 `ScreenBase` 子类
5. 地图节点或触发入口
6. 本地化文案

逻辑层只应发布数据和事件，不应引用 UI 类型或直接播放动画。

### 22.1 新增地图节点类型

新增一种完全不同的地图节点不是内容资产级改动，需要同时检查：

1. [MapNode.cs](../Game/Map/MapNode.cs) 的 `MapNodeType`。
2. [MapGenerator.cs](../Game/Map/MapGenerator.cs)：
   - 节点权重
   - 最早出现层数
   - 连续节点约束
   - `AssignContent` 如何写 `ContentId`
3. [RunManager.cs](../Game/Core/RunManager.cs) 的 `EnterNode`，决定进入节点后执行什么。
4. [MapNodeView.cs](../Game/UI/MapNodeView.cs) 的图标和颜色。
5. 如果节点需要独立界面，再新增 `RunPhase`、Screen 和 `GameApp.CreateScreen` 分支。
6. 如果节点需要一种新的 Definition，还要加入 `GameDatabase` 列表、索引、自动扫描目录和本地化收集。
7. 新节点的可变数据必须进入 `RunContext`，不能只存在 Screen 里。

当前地图固定规则包括：

- 第 0 行必定是普通战斗。
- 中间固定一层宝箱。
- Boss 前一行必定是休息点。
- 最后一行是唯一 Boss。
- 精英、休息和商店默认不会过早出现。

改变这些规则会改变相同种子的整张地图。

### 22.2 改战斗中的卡牌尺寸

**只改一个地方**：[HandFanLayout.cs](../Game/UI/HandFanLayout.cs) 顶部的两个常量。

```csharp
public const float CardWidth  = 230f;
public const float CardHeight = 330f;
```

其余全部由它们推导。**不要**再去手工改扇形间距、手牌区宽度、出牌线高度、插画窗高度——
那些都已经写成推导式了，手工改反而会把推导关系打断。

#### 22.2.1 会自动跟上的量

| 量 | 在哪 | 推导式 |
|---|---|---|
| 相邻间距 `MaxSpacing` | `HandFanLayout` | `CardWidth × SpacingRatio`（0.86 → 14% 叠压） |
| 扇形下沉 `MaxArcDepth` / `ArcPerCard` | `HandFanLayout` | `CardHeight × 比例` |
| 外探距离 `OuterReach` | `HandFanLayout` | `半宽·cosθ + 牌高·sinθ`（θ = `MaxEndTilt`） |
| 手牌区宽 `HandWidth` | `BattleScreen` | 由 `OuterReach` 与左侧 HUD 竖排反解 |
| 出牌线 `PlayLineY` | `BattleScreen` | `HandBaseY + CardHeight + 36` |
| 举牌位 `AimSlot` | `BattleScreen` | 由「不能盖住敌人区」反解 |
| 提示文字 / 结束回合 / 日志底边 | `BattleScreen` | 由 `SelectedTopY` 反解 |
| 插画窗高 `ArtHeight` | `CardView` | `CardHeight −` 名字栏 `−` 描述区 `−` 留白 |

**卡面的分配方向是「文字区定死，剩下全给插画」**：名字栏、描述区、底部留白都是固定像素
（它们装的是字，字号不随卡变大），插画窗吃掉所有增量。所以把卡加高 40，插画窗就长 40。

#### 22.2.2 改完必须确认的三条余量

这三条**都不会报错**，坏了只表现为「点不动某个按钮」或「牌互相糊住」。

1. **左侧 HUD 不能被啃到。**
   `HandWidth` 是反解出来的，所以左侧余量恒等于 `HudClearance`（16px），这条自动成立。
   真正的失败模式是卡太大把 `HandWidth` 解得**太小**——牌一多就被压缩，等于白改。
   判据：`HandWidth ≥ 4.44 × CardWidth`，否则 5 张手牌就开始压缩间距。

2. **举牌位不能低于静止位。**
   `AimSlot.y = 694 − 20 − CardHeight × 1.12 − 6` 必须明显大于 `HandBaseY`(24)。
   解成负数说明卡太高了：举起来的牌会盖住敌人，玩家看不见自己要打谁。

3. **「结束回合」不能撞进敌人区。**
   `EndTurnBottomY + 90` 必须 `< 694`。这是当前布局最先撑爆的一条。

#### 22.2.3 当前的尺寸上限

按现有布局实测，**约 245 × 350** 是不动其他东西的天花板（受第 3 条约束）。
再往上要先做下面之一：

- 把敌人区 `_enemyRow` 整体上移（`BuildUI` 里 `top - 330 / top - 80` 那两行）；
- 把「结束回合」挪出右侧，例如做成屏幕正下方居中、手牌绕开它；
- 缩小 `SelectedLift` / `SelectedScale`（抬起幅度小了，上方需要的净空也小）。

#### 22.2.4 改完怎么验

代码只保证编译，**布局对不对只能看**。项目的 EditMode 测试全是逻辑层的，
`Game.UI` 整个是覆盖盲区，没有任何测试会因为布局错位而失败。

1. 编译：`dotnet build Game.UI.csproj`（比等 Unity 重载快，且能单独验这一个程序集）。
2. 进 Play，开一场战斗，按顺序看四件事：
   - **手牌 8～10 张**时两端的牌有没有压到左下 HUD 竖排 / 有没有溢出屏幕；
   - **悬停**最右那张牌，抬起来之后有没有盖住「结束回合」（盖住就点不动了）；
   - **拖一张不需要目标的牌**（如「防御」），出牌线是否在静止手牌**上方**；
   - **拖一张需要目标的牌**（如「打击」），举起来的牌有没有挡住敌人。
3. 顺带看一眼卡面：名字有没有被费用球压住、描述有没有溢出到关键字色点上。

#### 22.2.5 另外两个可调的旋钮

| 想要的效果 | 改哪个 |
|---|---|
| 牌叠得更松 / 更紧 | `HandFanLayout.SpacingRatio`（越接近 1 越松） |
| 扇形张得更开 / 更平 | `HandFanLayout.MaxEndTilt`（注意它会同时吃掉两侧空间，`HandWidth` 会自动缩） |
| 悬停抬得更高 | `BattleScreen.HoverLift` / `HoverScale` |

---

## 23. “改这段代码会影响什么”速查表

| 修改位置 | 直接影响 |
|---|---|
| `CardDefinition` 资产 | 单张卡的配置 |
| `CardEffect` 子类 | 所有使用它的卡、药水、敌人行动 |
| `EffectValue` | 所有动态数值 |
| `EffectResolver` | 几乎全部即时战斗效果 |
| `TargetResolver` | 所有效果目标选择 |
| `StatusBehaviour` 子类 | 使用该行为的状态和遗物 |
| `BattleHooks` | 战斗规则扩展面，影响范围很大 |
| `BattleUnit.TakeDamage` | 全部伤害、护甲、死亡、荆棘、致死拦截 |
| `DeckController` | 抽牌、洗牌、回合末、牌数守恒 |
| `BattleController` | 回合、出牌、敌人行动、胜负、药水 |
| `EnemyBrain` | 所有未使用自定义 Brain 的敌人 |
| 自定义 Brain | 绑定该类型的敌人 |
| `EncounterDefinition` | 一场战斗组合和奖励等级 |
| `ContentPicker` | 奖励、商店、事件中的内容抽取 |
| `RewardGenerator` | 战斗和宝箱奖励 |
| `ShopStock` | 商店数量、价格、库存 |
| `RunEffect` 子类 | 使用它的事件、休息点或其他局外内容 |
| `RunManager` | 整局阶段流转 |
| `RunContext` | 未来存档格式 |
| `MapGenerator` | 地图结构和固定种子地图结果 |
| `GameDatabase` | 所有按 Id 的内容查找和公共池 |
| `SampleContentGenerator` | 重新生成后的资产内容与数据库收录 |
| `Loc` / 本地化 key | 所有玩家可见文案 |
| `GameApp` | 应用入口、初始配置、界面切换 |

---

## 24. 常见错误与排查

### 24.1 资产存在，但游戏里找不到

依次检查：

1. 资产是否在标准 `Assets/GameData/<Type>/` 目录。
2. Id 是否为空。
3. Console 是否有 Id 冲突。
4. `GameDatabase.asset` 对应列表是否包含它。
5. 场景里的 `GameApp.Database` 或 `BattleBootstrap.Database` 是否指向正确数据库。
6. 是否在运行中才新增资产；数据库索引需要重新开始 Play 或显式 `Invalidate()`。

### 24.2 重新生成后手工资产“不见了”

现在生成器会自动发现标准目录资产。若仍不见：

- 资产可能放错目录。
- 类型可能不对。
- Id 与生成资产冲突。
- Id 为空。

### 24.3 Inspector 改动被生成器改回

该资产由生成器代码管理。去对应 `SampleContent*.cs` 修改事实来源，或创建一个使用不同 Id 的纯手工资产。

### 24.4 卡牌要求选敌人，但点谁都一样

检查卡牌 `TargetKind` 与效果 `Target`。至少一个实际效果必须使用 `ChosenTarget`。

### 24.5 群体或随机卡却要求点目标

卡牌级 `TargetKind` 应设为 `None` 或适当的非点选类型；效果自己使用 `AllEnemies` 或 `RandomEnemy`。

### 24.6 卡牌描述显示错误数字或 `{N}`

检查：

- `{N}` 是否对应实际 `Effects[N]`
- 效果顺序是否改变
- 效果是否实现 `Describe()`
- 本地化译文占位符是否与中文一致

### 24.7 新效果在 Inspector 里变成 null

常见原因：

- 类被重命名
- 命名空间改变
- 程序集改变
- 类型不再 `[Serializable]`

`[SerializeReference]` 会记录具体类型名。改名属于资产迁移，不是普通重构。

### 24.8 敌人只会使用第一个行动

检查：

- 所有随机行动是否都为 `Weight = 0`
- 固定序列是否为空或不循环
- 条件是否全部失败
- `PhaseMask` 是否排除了当前阶段
- `FixedSequence` 下标是否越界

### 24.9 自定义 AI 没生效

检查完整字符串：

```text
命名空间.类名, Game.Runtime
```

并检查类是否继承 `EnemyBrain`、是否有无参数构造函数。

### 24.10 遗物计数互相串了

行为对象被 Definition 共享。不要把计数写在 Behaviour 字段中；改用 `RelicInstance.Counter`、`StatusInstance.Stacks` 或 Context。

### 24.11 状态衰减两次或时机不对

自然衰减只能放在 `BattleController.TickStatusDecay`。Behaviour 负责触发效果，不负责重复实现自然衰减。

### 24.12 固定种子突然得到不同奖励

检查是否：

- 在同一 `RngStream` 新增了随机调用
- UI 预览忘记设置 `PreviewMode`
- 把新系统随机复用了旧流
- 改了内容池数量或稀有度权重

### 24.13 新增事件设置了 `MinRow` 但仍在早期出现

这是当前实现限制：`MinRow` 尚未接入地图事件筛选。

### 24.14 给卡牌或敌人设置图片但没有显示

这是当前实现限制：相关 UI 尚未读取 Definition 的 Sprite 字段。

---

## 25. 推荐的日常扩展顺序

新增一组完整内容时，建议按依赖顺序：

1. 确定唯一 Id 和简中原文。
2. 尽量复用现有 `CardEffect`、`StatusBehaviour`、`RunEffect`。
3. 如有新持续机制，先实现状态行为和状态资产。
4. 创建卡牌和药水。
5. 创建敌人行动。
6. 默认 AI 不够时再写自定义 Brain。
7. 创建 Encounter，把敌人放进地图池。
8. 创建遗物和事件。
9. 检查奖励池、商店和稀有度是否符合设计。
10. 更新初始牌组或入口配置。
11. 导出并补齐本地化。
12. 用固定种子进入 Play 模式，逐项确认数值、目标、意图、归宿、Tooltip 和流程。

判断“应该改哪里”的最短原则：

```text
内容差异 → Definition
一次性战斗动作 → CardEffect
持续规则/拦截规则 → StatusBehaviour + Hook
敌人选择动作 → EnemyBrain
局外资源或牌库变化 → RunEffect
阶段跳转 → RunManager
显示与交互 → UI
跨战斗可变数据 → RunContext
```
