using System.Collections.Generic;
using Game.Core;
using Game.Map;

namespace Game.Save
{
    /// <summary>
    /// 一局游戏的存档。★ 这是存档格式的**正式契约**，刻意不复用任何运行时类型。
    ///
    /// <para>为什么不直接序列化 <see cref="RunContext"/>：运行时类里 <c>Deck</c> / <c>Relics</c> /
    /// <c>GameMap.Nodes</c> 全是 <c>readonly</c> 字段，而且装的是 ScriptableObject 引用
    /// （<c>CardDefinition</c> 这种东西存进 JSON 毫无意义）。更要紧的是：
    /// 有了独立 DTO，改运行时类**不会无声地改掉存档格式**——改到对不上时是编译错误，
    /// 而不是几周后玩家报「读档后我的牌少了一张」。</para>
    ///
    /// <para>★ 只存 Id，绝不存 Definition 引用。读档时由
    /// <see cref="RunSaveReader"/> 拿 Id 去 <see cref="GameDatabase"/> 反查。</para>
    /// </summary>
    public class RunSave
    {
        /// <summary>
        /// ★ 默认值刻意是 0 而不是 <see cref="SaveConstants.CurrentVersion"/>。
        /// 字段只在 <see cref="RunSaveWriter"/> 里显式赋值，于是「JSON 里根本没有 Version 这个键」
        /// （别的程序写的文件、被截断的文件、手改坏的文件）读出来就是 0，能被当场识别为非法；
        /// 若默认成当前版本，这类文件会**冒充成一份合法的当前存档**走进 Reader。
        /// </summary>
        public int Version;

        // ================================================================= 随机

        public int Seed;

        /// <summary>
        /// 每条随机流各自的状态。★ **不能只存 Seed**——只存 Seed 等于读档后
        /// 把所有随机序列倒回开局，玩家能靠存读反复刷同一批奖励。
        /// </summary>
        public List<RngStreamSave> RngStates = new List<RngStreamSave>();

        // ================================================================= 玩家资源

        public int Hp;
        public int MaxHp;
        public int Gold;
        public int EnergyPerTurn;
        public int CardsPerTurn;
        public int PotionSlots;

        public List<CardSave> Deck = new List<CardSave>();
        public List<RelicSave> Relics = new List<RelicSave>();
        public List<PotionSave> Potions = new List<PotionSave>();

        /// <summary>
        /// Uid 分配器的下一个值。★ 必须存：不存的话读档后新造的牌会从 1 开始，
        /// 与牌库里已有的牌撞号，而 UI 的手牌复用正是按 Uid 索引的（第五次会话）。
        /// </summary>
        public int NextCardUid = 1;
        public int NextPotionUid = 1;

        // ================================================================= 地图与进度

        public MapSave Map;

        public int CurrentNodeId = -1;
        public List<int> VisitedNodeIds = new List<int>();

        /// <summary>
        /// ★ 存档那一刻玩家「应该处于」的阶段，不一定等于存盘时界面正在显示的阶段。
        ///
        /// <para>节点快照是在 <c>RunManager.EnterNode</c> 里、**执行节点行为之前**打的，
        /// 那时候 <c>PhaseChanged</c> 还没广播、商店库存和宝箱奖励都还没生成。
        /// 存的是「这个节点该进入哪个阶段」，读档时由
        /// <c>RunManager.Resume</c> 重放一次节点行为把内容生成出来——
        /// 因为 Rng 也是同一份快照，重放出来的库存与奖励逐字节相同。</para>
        ///
        /// <para>★ 这个字段绝不能存成 <c>Map</c>：<c>CurrentNodeId</c> 已经指向新节点了，
        /// 读档回到地图等于让玩家白嫖跳过一个节点。</para>
        /// </summary>
        public RunPhase Phase = RunPhase.Map;

        public int BattlesWon;
        public bool LastBattleVictory;
        public string LastEncounterId;

        public string PendingBattleEncounterId;
        public bool PendingBattleGivesReward;

        /// <summary>
        /// <see cref="Phase"/> 为 <c>Battle</c> 时，正在打的是哪一场。
        /// 读档要靠它重开战斗——不能拿 <c>CurrentNode.ContentId</c> 代替，
        /// 事件里开的战斗当前节点是那个事件。
        /// </summary>
        public string ActiveBattleEncounterId;

        /// <summary>
        /// 尚未领完的奖励。null 表示当前没有待领奖励。
        /// 战斗奖励的存盘点在生成**之后**（<c>AcknowledgeBattleEnd</c>），所以这里有值；
        /// 宝箱的存盘点在生成**之前**，此时为 null，由 Resume 重放生成。
        /// </summary>
        public RewardSave PendingReward;

        /// <summary>
        /// 各商店节点的库存。key 为节点 Id。
        /// ★ 必须存：不存就等于读档后重新生成，玩家反复存读就能刷到想要的商品（铁律 13）。
        /// </summary>
        public Dictionary<int, ShopStockSave> ShopStocks = new Dictionary<int, ShopStockSave>();

        public int CardRemovalsPurchased;
    }

    /// <summary>
    /// 一条随机流的状态。刻意不直接用 <c>Rng.StreamState</c>：
    /// 有了这一层，谁把运行时那个 struct 的字段改了名，映射处会**编译不过**，
    /// 而不是默默产出一份读不回来的存档。
    /// </summary>
    public class RngStreamSave
    {
        public int Stream;
        public uint State;
    }

    /// <summary>
    /// 牌库里的一张牌。
    ///
    /// <para>★ <see cref="DefId"/> 存的是**当前**的 <c>Def.Id</c>，不是基础版的 Id。
    /// <c>CardInstance.Upgrade()</c> 是把 <c>Def</c> 换成 <c>Def.UpgradedVersion</c>，
    /// 所以一张升过级的「打击」它的 <c>Def.Id</c> 已经是 <c>strike_plus</c> 了。
    /// 若按「存基础 Id + 读档时 Upgrade N 次」来做，就会**双重升级**。
    /// 架构文档 03 里那份两年前的草稿正是这么写的，不要照抄。</para>
    ///
    /// <para>不存 <c>IsTemporary</c> / <c>BattleCostDelta</c> / <c>TurnCostDelta</c> /
    /// <c>ExtraKeywords</c>：前者只出现在战斗内临时生成的牌上（<c>Run.Deck</c> 永远收不到），
    /// 后三者在 <c>OnBattleEnd</c> 里就被清零了，而存档点全在战斗之外。</para>
    /// </summary>
    public class CardSave
    {
        public int Uid;
        public string DefId;
        public int UpgradeLevel;
    }

    /// <summary>
    /// 一个遗物。<see cref="Counter"/> 是跨战斗计数的唯一合法存储（铁律 12），必须存。
    /// </summary>
    public class RelicSave
    {
        public string Id;
        public int Counter;
    }

    public class PotionSave
    {
        public int Uid;
        public string Id;
    }

    /// <summary>
    /// 整张地图。★ 存整图而不是「只存种子、读档时重新生成」。
    ///
    /// <para>重新生成要喂给 <c>MapGenerator</c> 一份 <c>MapGenerationConfig</c>，
    /// 而它装的是**当前数据库里有哪些 encounter / event 的 Id 及其顺序**。
    /// 于是你一加新敌人，老存档读出来就是另一张地图，而
    /// <c>CurrentNodeId</c> / <c>VisitedNodeIds</c> 是按下标索引的——
    /// 玩家会被瞬移到一个类型完全不同的节点上，**全程不报任何错**。
    /// 省下的只有几 KB。</para>
    /// </summary>
    public class MapSave
    {
        public List<MapNodeSave> Nodes = new List<MapNodeSave>();

        /// <summary>按行索引的节点 Id。</summary>
        public List<List<int>> Rows = new List<List<int>>();
    }

    public class MapNodeSave
    {
        public int Id;
        public int Row;
        public int Column;
        public MapNodeType Type;
        public string ContentId;
        public List<int> Next = new List<int>();
        public List<int> Prev = new List<int>();
    }

    public class RewardSave
    {
        public int Gold;
        public List<string> CardChoiceIds = new List<string>();
        public string RelicId;
        public string PotionId;

        public bool CardTaken;
        public bool GoldTaken;
        public bool RelicTaken;
        public bool PotionTaken;
    }

    public class ShopStockSave
    {
        public List<ShopItemSave> Items = new List<ShopItemSave>();
    }

    /// <summary>
    /// 一件商品。四个内容字段同时只有一个非空（或都为空而 <see cref="IsCardRemoval"/> 为 true）。
    /// </summary>
    public class ShopItemSave
    {
        public string CardId;
        public string RelicId;
        public string PotionId;
        public bool IsCardRemoval;

        public int Price;
        public bool Sold;
    }
}
