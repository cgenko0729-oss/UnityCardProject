using System.Collections.Generic;
using Game.Cards;
using Game.Map;
using Game.Potions;
using Game.Relics;

namespace Game.Core
{
    public enum RunPhase { MainMenu, Map, Battle, Reward, Shop, Event, Rest, Treasure, GameOver, Victory }

    /// <summary>
    /// 一局游戏的全部可变数据（局外）。战斗中的数据在 BattleContext，不在这里。
    /// ★ 这个类是阶段 5 存档的唯一序列化源——凡是「关掉游戏再打开要恢复」的数据都必须在这里。
    /// </summary>
    public class RunContext
    {
        public int Seed;
        public Rng Rng;

        /// <summary>
        /// 这一局是不是真人在玩。
        ///
        /// ★ 目前唯一的用途：决定战斗内选牌要不要挂起等玩家点。
        ///   true  → <c>BattleContext.Selector</c> 置 null，结算挂起，UI 弹选牌面板；
        ///   false → 保留默认的随机选择器，结算一口气跑完（EditMode 测试 / 自动模拟器）。
        ///
        /// <para>★★ 这个开关**必须由创建这一局的人设置**，绝不能交给界面在每帧循环里去打开。
        ///   最初的实现放在 <c>BattleScreen.LateUpdate</c> 的「Ctx 变化了」分支里，
        ///   而 <c>Bind</c> 结尾已经把 _boundCtx 设成了当前 Ctx，那个分支从此永不成立——
        ///   于是整个选牌功能被静默地降级成「系统替你随机选」，
        ///   界面上什么异常都看不到，只有玩家会觉得「怎么不问我」。</para>
        /// </summary>
        public bool InteractivePlayer;

        /// <summary>只读数据库引用，方便效果/AI 按 id 取 Definition。</summary>
        public GameDatabase Database;

        public int Hp = 80;
        public int MaxHp = 80;
        public int Gold = 99;

        public int EnergyPerTurn = 3;
        public int CardsPerTurn = 5;

        public readonly List<CardInstance> Deck = new List<CardInstance>(32);

        /// <summary>已持有的遗物。顺序即获得顺序，也是 Hook 的收集顺序。</summary>
        public readonly List<RelicInstance> Relics = new List<RelicInstance>();

        // ================================================================= 药水

        /// <summary>药水槽位数。遗物可以改大它。</summary>
        public int PotionSlots = 3;

        /// <summary>背包里的药水。长度不会超过 <see cref="PotionSlots"/>。</summary>
        public readonly List<PotionInstance> Potions = new List<PotionInstance>(3);

        public bool HasPotionSpace => Potions.Count < PotionSlots;

        // ================================================================= 地图进度

        /// <summary>本局的地图。开新游戏时由 MapGenerator 生成。</summary>
        public GameMap Map;

        /// <summary>当前所在节点的 Id，-1 表示还没进入任何节点（在起点，可选第 0 行）。</summary>
        public int CurrentNodeId = -1;

        /// <summary>已走过的节点 Id，用于地图上画路径。</summary>
        public readonly List<int> VisitedNodeIds = new List<int>();

        public RunPhase Phase = RunPhase.MainMenu;

        /// <summary>本局已完成的战斗场次，用于奖励缩放与「打了几场」统计。</summary>
        public int BattlesWon;

        /// <summary>上一场战斗是否胜利（RunManager 结算奖励时读）。</summary>
        public bool LastBattleVictory;

        /// <summary>上一场战斗的 Encounter，用于决定奖励规格（精英/Boss 给更多金币）。</summary>
        public EncounterDefinition LastEncounter;

        /// <summary>
        /// 事件里的 <c>StartBattleRunEffect</c> 写在这里，由 RunManager 读走后清空。
        /// ★ RunEffect 只操作数据，流程控制权归 RunManager——否则每个效果类都能改流程，
        ///   状态机就退化成谁都能跳转的意大利面。
        /// </summary>
        public string PendingBattleEncounterId;
        public bool PendingBattleGivesReward;

        /// <summary>当前正在结算的战斗奖励 / 宝箱奖励。领完后置 null。</summary>
        public BattleReward PendingReward;

        /// <summary>
        /// 当前商店的库存。★ 必须存在这里而不是在 ShopScreen 里现生成，
        /// 否则玩家反复进出商店就能刷到想要的商品。
        /// key 为节点 Id，这样每个商店节点各有各的库存。
        /// </summary>
        public readonly Dictionary<int, ShopStock> ShopStocks = new Dictionary<int, ShopStock>();

        /// <summary>已在商店购买过几次删卡服务，用于逐次涨价。</summary>
        public int CardRemovalsPurchased;

        // ================================================================= Uid 分配

        private int _nextCardUid = 1;
        private int _nextPotionUid = 1;

        /// <summary>分配一个卡牌 Uid。★ 存档时必须一并存下 _nextCardUid，否则读档后会撞号。</summary>
        public int NextCardUid() => _nextCardUid++;

        /// <summary>分配一个药水 Uid。存档规则与卡牌 Uid 完全相同。</summary>
        public int NextPotionUid() => _nextPotionUid++;

        /// <summary>存档恢复用：把计数器推到不小于 value 的位置。</summary>
        public void EnsureCardUidAtLeast(int value)
        {
            if (value > _nextCardUid) _nextCardUid = value;
        }

        public void EnsurePotionUidAtLeast(int value)
        {
            if (value > _nextPotionUid) _nextPotionUid = value;
        }

        public RunContext(int seed, GameDatabase database)
        {
            Seed = seed;
            Database = database;
            Rng = new Rng(seed);
        }

        // ================================================================= 牌库操作

        /// <summary>造一张牌（自动分配 Uid），但不加入牌库。</summary>
        public CardInstance NewCard(CardDefinition def, bool temporary = false)
            => def == null ? null : new CardInstance(NextCardUid(), def, temporary);

        public CardInstance AddCard(CardDefinition def)
        {
            if (def == null) return null;
            var inst = NewCard(def);
            Deck.Add(inst);
            return inst;
        }

        public void AddCards(CardDefinition def, int count)
        {
            for (int i = 0; i < count; i++) AddCard(def);
        }

        public void RemoveCard(CardInstance c) => Deck.Remove(c);

        /// <summary>牌库里可升级的牌。休息点/事件的「升级一张牌」用。</summary>
        public void GetUpgradableCards(List<CardInstance> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < Deck.Count; i++)
                if (Deck[i].CanUpgrade) buffer.Add(Deck[i]);
        }

        // ================================================================= 遗物操作

        public bool HasRelic(string id)
        {
            for (int i = 0; i < Relics.Count; i++)
                if (Relics[i].Id == id) return true;
            return false;
        }

        /// <summary>获得一个遗物。已持有则返回 false（遗物不叠加）。</summary>
        public bool AddRelic(RelicDefinition def)
        {
            if (def == null || HasRelic(def.Id)) return false;
            Relics.Add(new RelicInstance(def));
            return true;
        }

        // ================================================================= 药水操作

        /// <summary>
        /// 获得一瓶药水。★ 槽位满时返回 null——**调用方必须处理这个 null**，
        /// 否则玩家会以为拿到了而实际没有。奖励界面据此显示「背包已满」。
        /// </summary>
        public PotionInstance AddPotion(PotionDefinition def)
        {
            if (def == null || !HasPotionSpace) return null;
            var inst = new PotionInstance(NextPotionUid(), def);
            Potions.Add(inst);
            return inst;
        }

        public bool RemovePotion(PotionInstance potion) => potion != null && Potions.Remove(potion);

        public PotionInstance FindPotion(string id)
        {
            for (int i = 0; i < Potions.Count; i++)
                if (Potions[i].Id == id) return Potions[i];
            return null;
        }

        // ================================================================= 生命值

        /// <summary>受伤/回血的统一入口，负数为受伤。返回实际变化量。</summary>
        public int ModifyHp(int delta)
        {
            int before = Hp;
            Hp = UnityEngine.Mathf.Clamp(Hp + delta, 0, MaxHp);
            return Hp - before;
        }

        public void ModifyMaxHp(int delta, bool alsoHeal = true)
        {
            MaxHp = UnityEngine.Mathf.Max(1, MaxHp + delta);
            if (alsoHeal && delta > 0) Hp = UnityEngine.Mathf.Min(MaxHp, Hp + delta);
            if (Hp > MaxHp) Hp = MaxHp;
        }

        public bool IsDead => Hp <= 0;
    }
}
