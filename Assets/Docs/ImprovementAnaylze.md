卡牌游戏工程改进分析报告

总体评价

这套架构的骨架质量明显高于一般个人项目：Definition/Instance 分离干净、asmdef 用编译器强制分层、逻辑可脱离 UI 跑测试、随机流分离、Hook 拉取式收集避免订阅泄漏——这些是很多商业项目做到中后期才被迫补的课。

但它目前处在一个特定的危险位置：阶段 1~3 的实现质量是为「能跑通」优化的，而阶段 4~6 要求的是「能扩展」。下面按 8 个视角展开，每条都标注了具体文件与行号。

---
一、架构扩展性缺口（最紧急，阶段 4 会直接撞上）

这一节是我认为最有价值的部分。当前 8 个 Hook 接口覆盖了阶段 1~3 的需求，但遗物系统（阶段 4 的核心）需要的拦截点有一大半不存在。

1.1 死亡单位自己的 Hook 收不到 —— 「亡语」类效果无法实现

BattleContext.Collect<T>() 第 159 行：

if (!unit.IsAlive) continue;

而 BattleUnit.ResolveDeath（BattleUnit.cs:242）是在 Hp == 0 之后才 Collect<IDeathHook>()。结果：濒死单位身上的状态永远无法响应自己的死亡。

这意味着以下设计一律做不出来：
- 「死亡时爆炸，对全场造成 10 伤害」（史莱姆分裂、自爆虫）
- 「死亡时给玩家一张诅咒牌」
- 「持有此状态时，致死伤害改为留 1 血」（不死图腾 / 复活甲）
- 遗物「濒死时回血」（ST 的 Lizard Tail）

建议：给 Collect<T>() 加一个 bool includeDead = false 参数，ResolveDeath 用 Collect<IDeathHook>(includeDead: true)。同时新增一个 IFatalHook.ModifyLethalDamage(ctx, src, ref DamageInfo dmg, ref bool preventDeath)，在 TakeDamage 第 184 行的死亡判定前调用。

1.2 状态施加没有拦截点 —— 「神器 / 免疫减益」无法实现

BattleUnit.AddStatus（BattleUnit.cs:66）直接写入，全程没有 Hook。ST 里 **Artifact（神器）**是核心机制之一，Boss 也大量使用。当前架构无法表达。

建议：新增 IStatusHook：
1.1 死亡单位自己的 Hook 收不到 —— 「亡语」类效果无法实现

BattleContext.Collect<T>() 第 159 行：

if (!unit.IsAlive) continue;

而 BattleUnit.ResolveDeath（BattleUnit.cs:242）是在 Hp == 0 之后才 Collect<IDeathHook>()。结果：濒死单位身上的状态永远无法响应自己的死亡。

这意味着以下设计一律做不出来：
- 「死亡时爆炸，对全场造成 10 伤害」（史莱姆分裂、自爆虫）
- 「死亡时给玩家一张诅咒牌」
- 「持有此状态时，致死伤害改为留 1 血」（不死图腾 / 复活甲）
- 遗物「濒死时回血」（ST 的 Lizard Tail）

BattleUnit.AddStatus（BattleUnit.cs:66）直接写入，全程没有 Hook。ST 里 **Artifact（神器）**是核心机制之一，Boss 也大量使用。当前架构无法表达。

建议：新增 IStatusHook：
public interface IStatusHook : IBattleHook
{
    void ModifyStatusApply(BattleContext ctx, in HookSource src,
                           StatusDefinition def, BattleUnit target, ref int stacks);
    void OnStatusApplied(BattleContext ctx, in HookSource src,
                         StatusDefinition def, BattleUnit target, int stacks);
}
ModifyStatusApply 里把 stacks 改成 0 就实现了免疫。

1.3 抽牌数量写死在 RunContext —— 遗物改不了

BattleController.cs:122：
Ctx.Deck.Draw(Ctx.Run.CardsPerTurn);

遗物「每回合多抽一张」（ST 的 Snecko Eye、Bag of Preparation）无法实现，因为没有任何 Hook 能介入。同理，能量上限只有 EnergyPerTurn 一个来源。

建议：把 ITurnHook 扩成
void ModifyTurnDraw(BattleContext ctx, in HookSource src, ref int count);
void ModifyTurnEnergy(BattleContext ctx, in HookSource src, ref int amount);
或者更通用地引入一个 BattleContext.Stat 概念（见 1.6）。

1.4 卡牌打出无法被拦截或改写

ICardPlayHook.OnCardPlayed（BattleHooks.cs:45）是打出后的通知，无法：
- 阻止一张牌被打出
- 让一张牌打出两次（ST 的 Duplication Potion、Echo Form）
- 改写目标（「所有单体攻击改为攻击全体」）
- 打出后把牌洗回抽牌堆而不是弃牌堆

BattleController.TryPlayCard 第 287~298 行的「归宿」逻辑是硬编码的 if/else 链，没有扩展点。

建议：
void PreCardPlay(BattleContext ctx, in HookSource src, CardInstance card, ref bool cancel, ref int extraPlays);
void ModifyCardDestination(BattleContext ctx, in HookSource src, CardInstance card, ref CardPile pile);

1.5 没有「召唤 / 单位入场」的表达能力

BattleController.EnemyTurn（第 166 行）用索引 for 遍历 Ctx.AllUnits。一旦有 SummonEffect（Boss 召唤小怪、ST 的 Reptomancer），会出现两个问题：
- 遍历中修改集合 → 新单位当回合就行动，或索引错位
- BattleScreen.BuildUnitViews 只在 Bind 时调用一次，新单位没有 UI

阶段 4 的精英/Boss 几乎必然需要召唤。

建议：现在就加 BattleContext.SpawnUnit(EnemyDefinition def)，内部 Post 一个 UnitSpawned 事件；EnemyTurn 遍历前先 RentUnitBuffer 做快照；BattleScreen 监听事件增量建 UnitView。

1.6 缺少统一的「属性修正」概念

目前每种数值都有自己的 Hook（伤害、护甲、治疗、能量、费用），加一种数值就要加一个接口。到阶段 4 加了遗物、阶段 5 加了角色天赋之后，接口会膨胀到 15+ 个。

可选的更优方案（成本中等，收益长期）：引入一个通用的
public enum StatKind { Damage, Block, Heal, EnergyGain, DrawCount, CardCost, StatusStack
public interface IStatModifierHook : IBattleHook {
    void ModifyStat(BattleContext ctx, in HookSource src, StatKind kind, object subject, ref int value);
}
保留现有专用接口（性能敏感的伤害管线），把长尾数值都走通用通道。注意：这是一个 trade-off，通用通道牺牲了类型安全和可读性。我的建议是不要现在做——先按 1.1~1.4 补齐具体接口，等接口数量真的超过 12 个再考虑。

1.7 BattleController 是 MonoBehaviour，阻塞了并行模拟

BattleController : MonoBehaviour（BattleController.cs:17），但它完全不用 Update、不用协程、不用任何 Unity 生命周期。它继承 MonoBehaviour 的唯一后果是：
- 测试必须建 GameObject（BattleTestFixture.cs:29-30）
- 无法在一个进程里同时跑 N 场战斗（自动平衡模拟器的刚需）


建议（改动小、收益大）：
public class BattleController { ... }                      // 纯 C#，现在的全部逻辑
public class BattleControllerHost : MonoBehaviour {        // 只做转发
    public BattleController Controller { get; private set; }
}
这是我认为性价比最高的单项重构，30 分钟内可完成，且直接解锁第七节的模拟器。

1.8 static Uid 计数器 —— 存档与并行的硬阻塞

CardInstance.cs:13 和 BattleUnit.cs:15：
private static int _nextUid = 1;
public static void ResetUidCounter() => _nextUid = 1;   // 仅供测试使用

问题：
- 存档/读档后 Uid 会与新生成的对象冲突（阶段 5 必撞）
- 两场并行模拟的 Uid 互相干扰
- 测试之间有隐式耦合（ResetUidCounter 只是打补丁）

建议：把计数器移进 RunContext（卡牌 Uid）和 BattleContext（单位 Uid），作为可序列化字段。

---
二、正确性问题与隐藏 Bug

按严重程度排序。

2.1 【中】敌人意图数值不会随玩家行动更新

BattleController.BeginTurn 第 124~128 行在回合开始时调用 Brain.DecideIntent，BuildIntent（EnemyBrain.cs:137）此时算出 PreviewDamage 并存进 Unit.CurrentIntent。之后 UnitView.Refresh 只是格式化这个存好的快照（UnitView.cs:112）。

结果：玩家给敌人上「虚弱」后，意图上显示的伤害数字不会变。玩家会以为要挨 12 点，实际只挨 9 点。这直接破坏了「意图预览 == 实际伤害」这条架构承诺（文档明确写了这条，还有测试
IntentPreview_MatchesActualDamage——但那个测试是在不改状态的情况下跑的，没抓到这个问题）

同理，玩家给自己上「易伤」后数字也不会更新。

修复：Intent 只存 ActionIndex 和 Kind，数值改为 UI 每帧（或每次状态变化时）通过 Brain.PreviewIntentValue(ctx) 现算。或者在 BattleContext 里加一个 IntentDirty 标志，任何状态变化后置位，BattleScreen 检测到就重算。

2.2 【中】_playCtx 复用带来的重入隐患

BattleController 只有一个 _playCtx（第 26 行），CanPlayCard 和 TryPlayCard 共用。当前流程是安全的，但：

TryPlayCard 第 283 行 EffectResolver.ResolveAll(card.Def.Effects, _playCtx) 执行期间，如果任何代码路径调用了 CanPlayCard（未来的「打出手牌中随机一张牌」效果、UI 在事件回调中查询、遗物 Hook 里判断可打性），PrepareContext 会 Reset 掉正在使用的 _playCtx，导致 Targets 被清空、XValue 归零。

这类 bug 的特征是只在特定卡牌组合下出现，且无法稳定复现——正是文档 9.6 想要避免的那类问题。

修复：CanPlayCard 用一个独立的 _previewCtx；或者做一个 EffectContext 池，TryPlayCard 期间租用。

2.3 【中】FireDelayed 可能死循环

BattleContext.cs:130-141：
for (int i = 0; i < _delayed.Count; i++)   // Count 每次迭代重新求值
{
    if (_delayed[i].when != when) continue;
    var act = _delayed[i].act;
    _delayed.RemoveAt(i);
    i--;
    act.Invoke();     // ← 如果 act 内部又 ScheduleDelayed(同一 when, ...)
}
新排入的项会被追加到列表末尾，循环会走到它并执行，如此往复。一张「回合结束时：抽一张牌，并在回合结束时再触发一次本效果」的牌就能把游戏卡死。

RunTriggerQueue 有 512 次守卫，FireDelayed 没有。

修复：先把本次要执行的项 List 取出来，清出原列表，再统一 Invoke；或者加同样的 guard。

2.4 【低】EffectResolver.CanApplyAll 提前返回时留下脏 Targets

EffectResolver.cs:10-22：return false 的路径没有 ctx.Targets.Clear()。当前无害（TryPlayCard 之后必然 Reset），但属于「靠调用顺序碰巧安全」的脆弱代码。

2.5 【低】TickStatusDecay 不过滤死亡单位

BattleController.cs:184：遍历 Ctx.AllUnits 时没有 IsAlive 检查。死亡单位身上的中毒层数照样每回合 -1。因为 Collect 过滤了死者，所以不会造成伤害，行为上无害，但和 Collect 的语义不一致——将来如果有「复活」机制就会出现「复活后 debuff 已经掉光了」的意外行为。

2.6 【低】_deathResolved 单向不可逆

BattleUnit.cs:33/237：一旦 _deathResolved = true，即使单位被复活并再次死亡，UnitDied 事件和 IDeathHook 都不会再触发。做复活机制时必须一起改。

2.7 【低】随机目标的预览与实际不符

TargetResolver.cs:104：
int idx = ctx.PreviewMode ? 0 : b.Rng.Range(...);
预览模式永远取第 0 个敌人。这个处理对「不消耗随机流」的目的是对的，但副作用是：一张「对随机敌人造成 X 伤害」的牌，描述里显示的永远是第一个敌人身上的修正后数值。多敌人且身上带不同 debuff 时，玩家看到的数字是错的。

建议：这类效果的 Describe 应显示「基础值 + 说明」，或者显示所有可能目标的数值范围。

2.8 【低】CanPlayCard 只校验了 SingleEnemy

BattleController.cs:234：CardTargetKind 有 5 个枚举值，但只有 SingleEnemy 有校验分支。AllEnemies/RandomEnemy 在场上无存活敌人时（理论上战斗已结束，但 hook 造成的中间状态下可能出现）没有保护。

2.9 【低】手牌满时静默丢牌

DeckController.cs:137：
if (Hand.Count >= MaxHandSize) DiscardPile.Add(card);
Post 的事件里 pile 参数仍然是 CardPile.Hand（第 155 行），表现层无法知道牌其实进了弃牌堆。玩家会困惑。应该 Post 一个专门的事件或修正 pile 参数。

2.10 【低】没有回合数上限 —— 自动模拟会死循环

如果玩家和敌人都无法造成有效伤害（全是防御牌 + 敌人只会加护甲），战斗永远不会结束。手动游玩时玩家会自己退出，但自动对战模拟器会挂死。建议加 BattleContext.MaxTurns = 200，超过判负或判平。

2.11 【低】GuardianBrain 的魔数下标

GuardianBrain.cs:14：ACTION_DESTROY = 4。文档里承认了这个问题并说「用常量 + 注释」来缓解，但常量并没有解决 SO 里调整 Actions 顺序后指向错误的根本问题。

建议：改为按名字查找 Def.Actions.FindIndex(a => a.Name == "毁灭")，在 Init 时解析一次并缓存到 Brain 的实例字段（Brain 是 per-unit 的，可以有可变字段）。

---
三、性能与 GC

对这个体量的游戏，性能不是瓶颈，但GC 尖峰会造成可感知的卡顿，尤其在移动端。

3.1 每帧的分配热点（最明显的问题）

BattleScreen.LateUpdate（第 229 行）每帧做：

┌───────────────────────────────────────────────────────────┬───────────────────────┬────────────────────────┐
│                           调用                            │ 每帧次数（10 张手牌） │          分配          │
├───────────────────────────────────────────────────────────┼───────────────────────┼────────────────────────┤
│ CanPlayCard → GetCost → Collect<ICardPlayHook>            │ 10                    │ 池化，0（好）          │
├───────────────────────────────────────────────────────────┼───────────────────────┼───
│ CanPlayCard → CanApplyAll → TargetResolver.Resolve        │ 10 × 效果数           │ 0                      │
├───────────────────────────────────────────────────────────┼───────────────────────┼────────────────────────┤
│ CardView.Refresh → GetDescription → new EffectContext()   │ 10                    │ 10 个对象 + 20 个 List │
├───────────────────────────────────────────────────────────┼───────────────────────┼────────────────────────┤
│ GetDescription → new StringBuilder + Substring + ToString │ 10                    │ ~40 个字符串           │
├───────────────────────────────────────────────────────────┼───────────────────────┼────────────────────────┤
│ GetCostText → int.ToString()                              │ 10                    │ 10
├───────────────────────────────────────────────────────────┼───────────────────────┼────────────────────────┤
│ RefreshHud → 4 个 $"..." 插值                             │ 1                     │ 4 个字符串             │
└───────────────────────────────────────────────────────────┴───────────────────────┴───

粗算 每秒约 4000~5000 次小对象分配。在 Unity 上这会造成每隔几秒一次 GC 尖峰。

修复建议（按性价比排序）：
1. 描述缓存：CardInstance 缓存上次生成的描述 + 一个「依赖指纹」（力量层数、X 值、手牌数、Depth 等），指纹不变就复用字符串。这一条能砍掉 80% 的分配。
2. CardInstance.GetDescription 里的 EffectContext 改为传入（由 BattleScreen 持有一个复用实例）。
3. RefreshHud 里的字符串用 StringBuilder + 缓存上次结果，只在值变化时赋 Text.text（赋值给 Text.text 即使内容相同也会触发 Canvas 重建，这在 uGUI 里是经典性能坑）。
4. 手牌费用/可打性只在「能量变化 / 手牌变化 / 状态变化」时重算，而不是每帧。可以用一个 Ctx.Revision 版本号，任何写入操作递增。

3.2 其他分配点

- EffectContext.Child()（EffectContext.cs:73）每次 new 一个对象 + 2 个 List。RepeatEffect、ConditionalEffect、RandomPickEffect、DelayedEffect 都在用。建议做一个 EffectContext 对象池挂在 BattleContext 上。
- RandomPickEffect.Apply（第 46 行）每次 new List<Option>(Options)。
- EnemyBrain.ExecuteIntent（第 169 行）每次 new EffectContext()。可以用已有的 _scratch（注意重入）。
- BattleUnit.TakeDamage 第 187 行 ctx.EnqueueTrigger(() => self.ResolveDeath(ctx)) —— 闭包捕获 → 每次死亡分配一个闭包对象 + 一个委托。ThornsBehaviour 同理（每次被打都分配）。建议把触发队列改成结构体 + switch，或者接受这个成本（死亡不频繁，荆棘频繁）。

3.3 Rng 的 Dictionary

Rng.cs:36：Dictionary<RngStream, uint>。每次 Range() 做一次 TryGetValue + 一次索引赋值 = 2 次哈希查找。洗牌是热路径（Shuffle 里每张牌一次）。

建议：改成 uint[] _states = new uint[9]，用 (int)stream 索引。同时 Save/Restore 直接存数组，序列化更简单。

3.4 随机数质量：模偏差

Rng.Range（第 75 行）用 Next(s) % (uint)(max-min)。xorshift32 的低位质量本来就不如高位，再加取模偏差。对 Range(0, 3) 这种小范围影响可以忽略，但对洗牌（Range(0, i+1)，i 最大 30+）会有轻微的不均匀。

建议（可选）：用高位 (uint)(((ulong)Next(s) * (ulong)range) >> 32)，一行改动，无偏且更快（避免除法）。

---
四、表现层与玩家体验（UX）

这是目前离「好玩」最远的一块。

4.1 【严重】逻辑瞬间完成，输入没有门禁

EndTurn（BattleController.cs:136）→ EnemyTurn → BeginTurn 全部在同一帧同步跑完。玩家按下结束回合的瞬间，逻辑上已经进入下一个自己的回合、手牌已经抽好、Phase == PlayerTurn。

而 BattlePresenter 还在以 0.12 秒/条的节奏播放敌人攻击的飘字。

结果：玩家可以在敌人还没「打出来」的时候就出牌，视觉和逻辑完全脱节。而且 BattleScreen.RefreshHandViews 会在抽牌那一刻立刻重建手牌 UI，新牌是瞬间出现的。

这是「逻辑同步 + 表现队列」架构的必然代价，必须显式补一个门：
// BattleScreen 里
private bool InputLocked => Ctx.Events.Count > 0 || _presenter.IsPlaying;
所有输入入口（OnCardClicked / OnUnitClicked / OnEndTurnClicked / Update 的键盘）都先检查。同时手牌 UI 也应该等事件播完再刷新，否则牌会「凭空出现」。


4.2 手牌 UI 全量重建，无法做动画

BattleScreen.RefreshHandViews（第 240 行）：只要手牌的 Uid 序列有任何变化，就 Destroy 全部 CardView 再全部重建。

后果：
- 抽一张牌 → 10 张牌全部销毁重建（每张牌 6 个 GameObject，共 ~60 个 GameObject 的 Destroy + Instantiate）
- 无法实现任何手牌动画（抽牌飞入、打出飞出、弃牌滑走）——这是阶段 6 的核心内容，现在的结构会挡住它
- 悬停状态丢失
- 第 256 行 _selected = null：如果在选择目标时手牌发生变化（敌人给你塞了一张状态牌），选择会被静默取消，玩家的点击白费了

建议：改成按 Uid 的差量同步（复用现有 View，只增删差异部分）+ 位置插值。这个改动现在做成本低，等到阶段 6 再做要连带改动画系统。

4.3 缺失的关键 UI

对一个卡牌构筑游戏，以下几乎是必备的，目前一个都没有：

┌────────────────────────────────────────────┬────────┬─────────────────────────────────────┐
│                    功能                    │ 重要性 │                说明                 │
├────────────────────────────────────────────┼────────┼─────────────────────────────────────┤
│ 查看抽牌堆 / 弃牌堆 / 消耗堆内容           │ ★★★    │ ST 玩家高频使用，影响决策深度       │
├────────────────────────────────────────────┼────────┼─────────────────────────────────────┤
│ 伤害预览（悬停卡牌时敌人身上显示预计伤害） │ ★★★    │ 直接决定「爽感」                    │
├────────────────────────────────────────────┼────────┼─────────────────────────────────────┤
│ 状态图标 + 悬停 tooltip                    │ ★★★    │ 现在是纯文字 "力量 3"，看不出正负面 │
├────────────────────────────────────────────┼────────┼─────────────────────────────────────┤
│ 关键字 tooltip（消耗、虚无、易伤…）        │ ★★     │ 新玩家无法理解                      │
├────────────────────────────────────────────┼────────┼─────────────────────────────────
│ 敌人意图的 tooltip / 图标                  │ ★★     │ 现在是 ⚔ 12，缺 icon                │
├────────────────────────────────────────────┼────────┼─────────────────────────────────────┤
│ 手牌上限提示                               │ ★      │ 满手牌时抽牌静默失败                │
├────────────────────────────────────────────┼────────┼─────────────────────────────────────┤
│ 撤销/确认（针对高消耗决策）                │ ★      │ 可选                                │
└────────────────────────────────────────────┴────────┴─────────────────────────────────────┘

4.4 输入细节

害数字的弹跳曲线、血条的平滑过渡（现在是 fillAmount 瞬间跳变）。这些都归在阶段 6，但血条平滑和飘字曲线成本极低、收益极高，建议提前做。

---
五、测试与质量保障

现有 35 个测试的质量是好的（尤其 Thorns_DoesNotRecurseInfinitely 抓出了真 bug），但覆盖有明显盲区。

5.1 缺失的测试

┌─────────────────────────────────────────────────────────────────────────────┬────────────────────────────────────────────┐
│                                    缺口                                     │                    风险                    │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ DelayedEffect 完全无测试                                                    │ 2.3 的死循环没被发现                       │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ ConditionalEffect / RandomPickEffect 无测试                                 │ 组合子是效果系统的核心                     │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ AddCardEffect / ModifyCardCostEffect / DiscardEffect / ExhaustEffect 无测试 │ 4 个效果类零覆盖                           │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ Innate / Retain / Ethereal 关键字无测试                                     │ DeckController.EndTurnDiscard 的三分支逻辑 │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ 多敌人 AoE / PreviousTargets 跨多目标                                       │ CarryTargetsForward 的语义                 │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ 洗牌确定性（同种子 → 同洗牌结果）                                           │ 只测了抽牌顺序                             │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ 死亡时序（多个单位同帧死亡）                                                │ 触发队列的顺序                             │
├─────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────────┤
│ AllEnemies 目标下有单位中途死亡                                             │ 遍历中集合变化                             │
└─────────────────────────────────────────────────────────────────────────────┴───────────────┘

public void Fuzz_ThousandBattles_NeverCrashOrHang()
{
    for (int seed = 0; seed < 1000; seed++)
    {
        var run = MakeRandomRun(seed);
        var ctrl = new BattleController();
        ctrl.StartBattle(run, RandomEncounter(seed));

        int turnGuard = 0;
        while (ctrl.IsRunning && ++turnGuard < 200)
        {
            // 随机策略：随机打出所有能打的牌，然后结束回合
            PlayRandomAffordableCards(ctrl, seed);
            ctrl.EndTurn();
        }
        Assert.Less(turnGuard, 200, $"seed {seed} 战斗未在 200 回合内结束");
        Assert.IsTrue(ctrl.Ctx.BattleEnded);
        // 不变量检查
        AssertInvariants(ctrl.Ctx);
    }
}

配合不变量断言：
- Hp 永远在 [0, MaxHp]
- Block >= 0、Energy >= 0
- 牌的总数守恒（TotalCards == 初始牌数 + 生成的牌数）
- 没有牌同时存在于两个堆里
已被移除）

这一个测试能抓到的问题，比再手写 50 个用例都多，而且每次改动都能免费复用。跑 1000 场在 EditMode 下大概几秒钟。

5.3 黄金测试（Golden Test）

固定种子 + 固定牌组 + 固定策略跑一场，把完整的事件序列 dump 成文本，存成基线文件。任何改动导致行为变化，diff 会立刻显示出来。这是防止「重构时静默改变游戏平衡」的最有效手段。

5.4 完全没有 PlayMode 测试

文档承认「运行时 UI 只在编译层面验证过，尚未在 Play 模式下实际点过」。至少应该有一个 PlayMode 冒烟测试：加载场景 → 等一帧 → 断言没有 Console Error → 模拟点击第一张牌 → 断言能量减少。

---
六、内容生产与工具链

6.1 硬编码的内容生成器不可持续

SampleContentGenerator.cs 是 440 行硬编码 C#，占了全工程最大的单文件。目前生成 6 张卡、3 个敌人。一个像样的卡牌 Roguelike 需要 150~250 张卡、40~60 个敌人、80+ 遗物。按当前方式，这个文件会膨胀到 5000+ 行。

建议的折中方案（[SerializeReference] 效果树确实不适合表格化）：
- 数值与文案走表：卡名、费用、类型、稀有度、描述模板 → CSV/Google Sheet，Editor 工具导入并只更新这些字段，不碰 Effects
- 效果结构走 Inspector：[SerializeReference] 的效果树在 Unity Inspector 里手工搭，这是它的强项
- 或者做一个卡牌 DSL："damage 6 @chosen; status vulnerable 2 @previous" → 解析成效果树。对程序员友好，对策划不友好。适合个人项目。

我倾向第一种：表管数值，SO 管结构。数值调平衡的频率是结构调整的 100 倍。

6.2 [SerializeReference] 的重命名陷阱


6.4 缺少调试工具

阶段 4 开始，靠 Debug.Log 调试卡牌交互会非常痛苦。建议做一个 Editor 窗口：
- 实时显示 BattleContext 的全部状态（四个牌堆内容、所有单位的状态列表、Hook 收集结果）
- 「作弊」按钮：给任意单位加任意状态、抽指定卡、设置能量、直接杀死敌人
- 事件队列的实时日志（带时间戳和调用栈）

这个工具在阶段 4~5 会为你省下几十小时。

---
七、游戏设计层面（玩法深度）

前面都是工程视角，这一节是**「怎么让它好玩」**。

7.1 当前的机制密度太低

6 个状态、14 个效果类、5 个关键字。作为对比，ST 有 ~40 个状态、~80 个关键字/机制。你的效果系统设计得足够通用，缺的不是能力，是内容。

优先补充的机制（按对玩法深度的贡献排序）：

┌─────────────────────────────────────────┬───────────────────────────────────────┬───────────────────────────────────────────────┐
│                  机制                   │                 说明                  │                 当前是否支持                  │
├─────────────────────────────────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 神器 / 免疫                             │ 抵消下 N 次减益                       │ ❌ 需要 1.2 的 IStatusHook                    │
├─────────────────────────────────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 充能 / 蓄力                             │ 攒 N 回合放大招                       │ ⚠️ 可用状态+条件拼，但需要 Brain 支持         │
├─────────────────────────────────────────┼───────────────────────────────────────┼──────────────────┤
────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 每回合固定伤害的护盾（吸收 N 点后破碎） │ 与 Block 不同的防御维度               │ ⚠️ 需要 IDamageHook + StatusInstance 可变层数 │
├─────────────────────────────────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 无形 / 减伤上限（伤害不超过 N）         │ ✅ 用 HookOrder.Late 钳制             │ ✅                                            │
├─────────────────────────────────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 卡牌回响 / 复制打出                     │ ❌ 需要 1.4 的 PreCardPlay            │ ❌                                            │
├─────────────────────────────────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 牌堆操作（洗牌进抽牌堆顶、搜索特定卡）  │ ⚠️ AddCard 部分支持，缺「从牌堆检索」 │ ⚠️                                            │
├─────────────────────────────────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 能量存储 / 溢出                         │ ❌                                    │ ❌                                            │
├─────────────────────────────────────────┼───────────────────────────────────────┼───────────────────────────────────────────────┤
│ 卡牌降级 / 诅咒机制                     │ Curse 类型已定义但无内容              │ ⚠️                                            │
└─────────────────────────────────────────┴───────────────────────────────────────┴───────────────────────────────────────────────┘

7.2 缺少「构筑」这个核心动词

现在只有战斗。卡牌构筑游戏 90% 的乐趣来自做取舍：
- 三选一奖励（要哪张牌）
- 删卡（花钱移除一张烂牌）
- 遗物的构筑倾向（这个遗物让我该走攻击流还是防御流）
- 商店的资源分配

这些是阶段 4 的内容，路线图已经规划了。我的建议只有一条：遗物系统的设计要走「改变规则」而不是「增加数值」。+3 最大生命 类遗物是内容填充，每回合第一张攻击牌费用 -1 类遗物才创造构筑决策。而后者需要 1.4 的 PreCardPlay Hook。

7.3 敌人设计的表达能力

EnemyBrain 支持固定序列、权重随机、条件、连续限制、多阶段——这已经覆盖 ST 里 80% 的敌人。
- 敌人之间的联动（「治疗者」给其他敌人加护甲、「指挥官」让全体强化）：TargetSelector 里有 AllAllies，理论上支持，但 BuildIntent 只看第一个 Damage/Block 效果，联动行为的意图显示不出来
- 反应式行为（「玩家打出攻击牌时反击」）：需要敌人身上挂 ICardPlayHook 的状态——架构上已经支持，只需要内容
- 意图的多段显示（同时显示「攻击 12 + 加 5 护甲」）：Intent 结构只有一个 Value（Intent.cs:12），BuildIntent 找到第一个就 break（EnemyBrain.cs:151）

建议：把 Intent 改成小数组或加第二个 SecondaryValue。

7.4 数值平衡完全没有工具

配合第五节的模拟器，可以做：

八、工程规范与可维护性

8.1 本地化：现在是引入的最佳时机

中文硬编码散布在至少 6 处：
- BattleScreen.PhaseText / ReasonText（第 375、386 行）
- CardView.TypeLabel（第 106 行）+ 关键字文本（第 84~88 行）
- UnitView.FormatIntent（第 122 行）
- BattlePresenter.Play 的全部日志字符串（14 处）
- SampleContentGenerator 里的所有卡名和描述

路线图把本地化放在阶段 6，但每晚一天，迁移成本就高一分。建议现在就引入一个 20 行的 Loc.Get(string key)（先用 Dictionary 存中文，将来换成 SO/CSV），新代码一律走它。成本几乎为零。

8.2 UI 的硬编码坐标

BattleScreen.BuildUI（第 58~130 行）里全是魔数坐标：new Vector2(-700, -330)、spacing = 300f、Mathf.Min(190f, 1500f / n)。

后果：调 UI 布局必须改代码重编译，且没有可视化反馈。「零 prefab、程序化 UI」这个决策对 AI 协作友好，但对迭代美术布局非常不友好。

建议：至少把所有布局常量抽到一个 BattleLayout 静态类或一个 ScriptableObject 配置，让调整不需要在 400 行代码里找数字。

8.3 BattleScreen 承担了太多职责

398 行里同时做：UI 构建、输入处理、每帧刷新、布局计算、坐标转换、文案映射。建议拆成 BattleScreenBuilder（构建）/ BattleInputHandler（输入）/ BattleHud（刷新）。这个拆分在阶段 4 加了 5 个新 Screen 之后会变成刚需（否则会有 5 个 400 行的类）。

8.4 文档与代码的同步风险
e 的 512 守卫用 UnityEngine.Debug.LogError，而 BattleContext 号称「没有任何 Unity 依赖」（第 11 行注释）——实际依赖了 UnityEngine.Debug。同样在 EffectResolver.cs:37。建议抽一个 GameLog 接口，测试时可注入。这也顺带解决了「测试跑 fuzz 时 LogError 会让 NUnit 判定失败」的问题。
- BattleUnit.ResolveDeath 是 public，但注释说「由触发队列调用」。可以改 internal。

---
九、优先级建议

如果按「改动成本 ÷ 收益」排序，我会这样做：

立刻做（在开始阶段 4 之前）

┌─────┬──────────────────────────────────────────┬─────────┬───────────────────────────────────────────────────┐
│  #  │                   事项                   │  成本   │                       理由                        │
├─────┼──────────────────────────────────────────┼─────────┼───────────────────────────────────────────────────┤
│ 1   │ Fuzz 自动对战测试（5.2）                 │ 半天    │ 一次投入，永久收益，立刻会抓出 2.3 这类 bug       │
├─────┼──────────────────────────────────────────┼─────────┼───────────────────────────────────────────────────┤
│ 2   │ BattleController 去 MonoBehaviour（1.7） │ 半小时  │ 解锁并行模拟，测试变干净                          │
├─────┼──────────────────────────────────────────┼─────────┼───────────────────────────────────────────────────┤
│ 3   │ 补齐 4 个 Hook 缺口（1.1~1.4）           │ 1 天    │ 遗物系统的地基，等阶段 4 写到一半再加代价大 10 倍 │
├─────┼──────────────────────────────────────────┼─────────┼───────────────────────────────────────────────────┤
│ 4   │ Uid 计数器去 static（1.8）               │ 1 小时  │ 阶段 5 存档的硬阻塞                               │
├─────┼──────────────────────────────────────────┼─────────┼───────────────────────────────────────────────────┤
│ 5   │ 修 2.1 意图不更新（2.1）                 │ 半天    │ 这是玩家能直接感知到的错误信息                    │
├─────┼──────────────────────────────────────────┼─────────┼───────────────────────────────────────────────────┤
│ 6   │ 修 2.3 FireDelayed 死循环（2.3）         │ 20 分钟 │ 潜在挂死                                          │
├─────┼──────────────────────────────────────────┼─────────┼───────────────────────────────────────────────────┤
│ 7   │ 输入门禁（4.1）                          │ 半天    │ 当前手感的最大问题                                │
└─────┴──────────────────────────────────────────┴─────────┴────────────────────────────

阶段 4 期间顺带做

┌─────┬───────────────────────────────┬───────────────────────────┐
│  #  │             事项              │           理由            │
├─────┼───────────────────────────────┼───────────────────────────┤
│ 8   │ 引入 Loc.Get（8.1）           │ 越早越便宜                │
├─────┼───────────────────────────────┼───────────────────────────┤
│ 9   │ 手牌 UI 差量同步（4.2）       │ 阶段 6 动画的前置条件     │
├─────┼───────────────────────────────┼───────────────────────────┤
│ 10  │ 描述缓存 + Text 脏检查（3.1） │ 消除 GC 尖峰              │
├─────┼───────────────────────────────┼───────────────────────────┤
│ 11  │ 拆分 BattleScreen（8.3）      │ 5 个新 Screen 之前必须做  │
├─────┼───────────────────────────────┼───────────────────────────┤
│ 12  │ 内容表格化管线（6.1）         │ 内容量产的前置条件        │
├─────┼───────────────────────────────┼───────────────────────────┤
│ 13  │ 调试 Editor 窗口（6.4）       │ 会在阶段 4~5 省下几十小时 │
└─────┴───────────────────────────────┴───────────────────────────┘

可以推迟

- 通用 IStatModifierHook（1.6）—— 等接口数量真的失控再说
- Rng 优化（3.3、3.4）—— 不是瓶颈
- 音效/粒子/震屏（4.5）—— 阶段 6