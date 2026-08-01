using System.Collections.Generic;
using Game.Cards;
using Game.Enemies;
using Game.Events;
using Game.Localization;
using Game.Potions;
using Game.Relics;
using Game.Statuses;
using UnityEngine;

namespace Game.Core
{
    /// <summary>所有 Definition 的索引。只读，运行时不写入（除首次建索引）。</summary>
    [CreateAssetMenu(menuName = "Game/Database", fileName = "GameDatabase")]
    public class GameDatabase : ScriptableObject
    {
        public List<CardDefinition> Cards = new List<CardDefinition>();
        public List<EnemyDefinition> Enemies = new List<EnemyDefinition>();
        public List<StatusDefinition> Statuses = new List<StatusDefinition>();
        public List<EncounterDefinition> Encounters = new List<EncounterDefinition>();
        public List<RelicDefinition> Relics = new List<RelicDefinition>();
        public List<EventDefinition> Events = new List<EventDefinition>();
        public List<PotionDefinition> Potions = new List<PotionDefinition>();
        public List<KeywordDefinition> Keywords = new List<KeywordDefinition>();

        /// <summary>
        /// 地图节点的图标。
        ///
        /// ★ 为什么挂在数据库上而不是像别的美术那样挂在 Definition 里：
        ///   节点类型是个**枚举**（<c>MapNodeType</c>），根本没有对应的 ScriptableObject。
        ///   为 7 个固定类型各造一个 SO 只会多出 7 个几乎空白的资产和一套要维护的 Id，
        ///   而它们本来就是一组一起换的东西。
        ///
        /// <para>留空的类型继续用现在的符号（⚔ ☠ ♨ …）。</para>
        /// </summary>
        public List<MapNodeIcon> MapIcons = new List<MapNodeIcon>();

        /// <summary>
        /// 卡框。按<b>卡牌类型</b>配，不是按单张卡——理由与 <see cref="MapIcons"/> 一模一样：
        /// <c>CardType</c> 是枚举，没有对应的 ScriptableObject，而这几张框本来就是一组一起换的。
        ///
        /// <para>★ 与 <c>CardDefinition.Art</c> 是两件事：Art 是**这张卡自己的立绘**，
        /// 框是**这一类卡共用的边饰**。别把框塞进 Art，那样 57 张卡要各配一次同一张图。</para>
        ///
        /// <para>★ 留空 = 完全不显示框，卡面走接框之前那套排版（见 <c>CardView.Create</c>）。
        /// 所以「有没有框」是个可以随时开关的选项，不是一次不可逆的改版。</para>
        /// </summary>
        public List<CardFrameSkin> CardFrames = new List<CardFrameSkin>();

        /// <summary>
        /// 卡背。洗牌时飞过屏幕的那一叠、以及牌堆按钮上那一小摞用它。
        ///
        /// <para>★ 只有一张、不分类型：卡背的全部意义就是**看不出是哪张牌**。
        /// 按类型分卡背等于把手里的暗牌信息泄露出去。</para>
        ///
        /// <para>★ 留空 = 用 <c>UIFactory.CardBackSprite</c> 那张程序化烘出来的兜底，
        /// 不会变成纯色方块。所以这个字段是「想换就换」，不是「必须配」。</para>
        /// </summary>
        public Sprite CardBack;

        /// <summary>
        /// 翻译表，一种语言一张。
        /// ★ 简体中文<b>不在</b>这里——它是源语言，文案就写在代码与各 Definition 里。
        /// </summary>
        public List<LocaleTable> Locales = new List<LocaleTable>();

        private Dictionary<string, CardDefinition> _cards;
        private Dictionary<string, EnemyDefinition> _enemies;
        private Dictionary<string, StatusDefinition> _statuses;
        private Dictionary<string, EncounterDefinition> _encounters;
        private Dictionary<string, RelicDefinition> _relics;
        private Dictionary<string, EventDefinition> _events;
        private Dictionary<string, PotionDefinition> _potions;

        /// <summary>关键字用枚举位当键，不像其余定义那样用字符串 Id。</summary>
        private Dictionary<CardKeyword, KeywordDefinition> _keywords;

        public void BuildIndex()
        {
            _potions = Index(Potions, p => p.Id);
            _cards = Index(Cards, c => c.Id);
            _enemies = Index(Enemies, e => e.Id);
            _statuses = Index(Statuses, s => s.Id);
            _encounters = Index(Encounters, e => e.Id);
            _relics = Index(Relics, r => r.Id);
            _events = Index(Events, e => e.Id);

            _keywords = new Dictionary<CardKeyword, KeywordDefinition>(Keywords != null ? Keywords.Count : 0);
            if (Keywords != null)
            {
                for (int i = 0; i < Keywords.Count; i++)
                {
                    var k = Keywords[i];
                    // 只收单一位的定义。组合值（Exhaust | Retain）反查时永远匹配不到单个位，
                    // 收进来只会让「有定义却查不到」变成一个难查的谜；校验器会另行报错。
                    if (k != null && k.IsSingleBit) _keywords[k.Keyword] = k;
                }
            }
        }

        private static Dictionary<string, T> Index<T>(List<T> list, System.Func<T, string> idOf)
            where T : ScriptableObject
        {
            var dict = new Dictionary<string, T>(list != null ? list.Count : 0);
            if (list == null) return dict;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                var id = idOf(list[i]);
                if (!string.IsNullOrEmpty(id)) dict[id] = list[i];
            }
            return dict;
        }

        private void EnsureIndex()
        {
            if (_cards == null) BuildIndex();
        }

        public CardDefinition GetCard(string id) { EnsureIndex(); return Get(_cards, id); }
        public EnemyDefinition GetEnemy(string id) { EnsureIndex(); return Get(_enemies, id); }
        public StatusDefinition GetStatus(string id) { EnsureIndex(); return Get(_statuses, id); }
        public EncounterDefinition GetEncounter(string id) { EnsureIndex(); return Get(_encounters, id); }
        public RelicDefinition GetRelic(string id) { EnsureIndex(); return Get(_relics, id); }
        public EventDefinition GetEvent(string id) { EnsureIndex(); return Get(_events, id); }
        public PotionDefinition GetPotion(string id) { EnsureIndex(); return Get(_potions, id); }

        /// <summary>
        /// 按语言标签取翻译表。传源语言（zh-Hans）或未知标签一律返回 null，
        /// 而 <c>Loc.Use(null)</c> 就是「切回源语言」，因此调用点不需要特判。
        /// </summary>
        public LocaleTable GetLocale(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode) || languageCode == Loc.SourceLanguage) return null;
            for (int i = 0; i < Locales.Count; i++)
            {
                var t = Locales[i];
                if (t != null && t.LanguageCode == languageCode) return t;
            }
            return null;
        }

        /// <summary>取某类地图节点的图标。没配就返回 null，调用方退回符号文字。</summary>
        public Sprite GetMapIcon(Game.Map.MapNodeType type)
        {
            for (int i = 0; i < MapIcons.Count; i++)
                if (MapIcons[i].Type == type && MapIcons[i].Icon != null) return MapIcons[i].Icon;
            return null;
        }

        /// <summary>
        /// 取某类卡牌的卡框。没配就返回 null，调用方走无框排版。
        ///
        /// ★ 找不到精确匹配时**不**退回任何默认框：半套框（攻击有、技能没有）在牌堆里
        ///   看起来像是漏配了资产，而这正是我们希望你一眼看出来的。
        /// </summary>
        public CardFrameSkin GetCardFrame(CardType type)
        {
            for (int i = 0; i < CardFrames.Count; i++)
            {
                var skin = CardFrames[i];
                if (skin == null || skin.Type != type || skin.Frame == null) continue;

                // ★ 出口处补默认值，而不是只靠 OnValidate：OnValidate 只在编辑器里跑，
                //   打包出去的那份序列化数据是什么样就是什么样。见 CardFrameSkin.Normalize。
                skin.Normalize();
                return skin;
            }
            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 把 Inspector 里新加的空白卡框条目补成可用的默认值。
        /// ★ 只是让你在 Inspector 里**看得见**这些数字；真正保证正确的是
        ///   <see cref="GetCardFrame"/> 里那次 Normalize（那条在打包后也有效）。
        /// </summary>
        private void OnValidate()
        {
            for (int i = 0; i < CardFrames.Count; i++) CardFrames[i]?.Normalize();
        }
#endif

        /// <summary>按单个关键字位取定义。传组合值恒返回 null。</summary>
        public KeywordDefinition GetKeyword(CardKeyword keyword)
        {
            EnsureIndex();
            return _keywords != null && _keywords.TryGetValue(keyword, out var v) ? v : null;
        }

        private static T Get<T>(Dictionary<string, T> dict, string id) where T : ScriptableObject
            => id != null && dict != null && dict.TryGetValue(id, out var v) ? v : null;

        // ================================================================= 按条件取集合

        /// <summary>取出符合条件的 Encounter Id，供 MapGenerator 用。</summary>
        public void GetEncounterIds(List<string> buffer, bool elite, bool boss)
        {
            buffer.Clear();
            for (int i = 0; i < Encounters.Count; i++)
            {
                var e = Encounters[i];
                if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                if (e.IsElite == elite && e.IsBoss == boss) buffer.Add(e.Id);
            }
        }

        public void GetEventIds(List<string> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < Events.Count; i++)
                if (Events[i] != null && !string.IsNullOrEmpty(Events[i].Id)) buffer.Add(Events[i].Id);
        }

        /// <summary>取出某稀有度的全部卡牌。rarity 为 null 表示「奖励池」（排除 Basic / Special）。</summary>
        public void GetCardsByRarity(List<CardDefinition> buffer, CardRarity? rarity)
        {
            buffer.Clear();
            for (int i = 0; i < Cards.Count; i++)
            {
                var c = Cards[i];
                if (c == null) continue;
                if (rarity.HasValue) { if (c.Rarity == rarity.Value) buffer.Add(c); }
                else if (c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Special) buffer.Add(c);
            }
        }

        /// <summary>取出某稀有度的全部遗物。rarity 为 null 表示「掉落池」（排除 Starter）。</summary>
        public void GetRelicsByRarity(List<RelicDefinition> buffer, RelicRarity? rarity)
        {
            buffer.Clear();
            for (int i = 0; i < Relics.Count; i++)
            {
                var r = Relics[i];
                if (r == null) continue;
                if (rarity.HasValue) { if (r.Rarity == rarity.Value) buffer.Add(r); }
                else if (r.Rarity != RelicRarity.Starter) buffer.Add(r);
            }
        }

        /// <summary>取出某稀有度的全部药水。rarity 为 null 表示整个掉落池。</summary>
        public void GetPotionsByRarity(List<PotionDefinition> buffer, PotionRarity? rarity)
        {
            buffer.Clear();
            for (int i = 0; i < Potions.Count; i++)
            {
                var p = Potions[i];
                if (p == null) continue;
                if (!rarity.HasValue || p.Rarity == rarity.Value) buffer.Add(p);
            }
        }

        /// <summary>资产被外部工具改动后调用，强制重建索引。</summary>
        public void Invalidate() => _cards = null;
    }

    /// <summary>一种地图节点类型对应的图标。见 <see cref="GameDatabase.MapIcons"/>。</summary>
    [System.Serializable]
    public struct MapNodeIcon
    {
        public Game.Map.MapNodeType Type;
        public Sprite Icon;
    }

    /// <summary>
    /// 一种卡牌类型的卡框皮肤。见 <see cref="GameDatabase.CardFrames"/>。
    ///
    /// ★ 做成 class 而不是 struct：<see cref="GameDatabase.GetCardFrame"/> 要能返回 null
    ///   表示「这类卡没配框」。struct 得再包一层 <c>bool Found</c>，或者靠 <c>Frame == null</c>
    ///   在调用点二次判断——那种「返回了一个空壳，你得自己检查」的接口正是容易漏检的形状。
    /// </summary>
    [System.Serializable]
    public class CardFrameSkin
    {
        public CardType Type;

        /// <summary>框图。中心必须是透明的，它会盖在卡面之上。</summary>
        public Sprite Frame;

        /// <summary>
        /// 框图相对卡面的缩放，绕卡心放大。1 = 正好铺满卡面。
        ///
        /// ★ 存在的理由有两个，分开理解：
        ///   ① **想让花饰探到卡外**。框正好铺满时，边饰是「长在卡里」的，会吃掉卡面空间；
        ///      放大到约 1.13 之后花饰套在卡的外圈上，卡面本身一分不少。
        ///   ② **补图上的透明废边**。用没裁过的原图时（例如 2048 见方、框只占中间 1338），
        ///      横向要额外乘 2048/1338 ≈ 1.53 才能把框撑到卡宽。裁过的图不需要这一项。
        ///   ①②会乘在一起，所以两轴分开给，而不是一个统一倍数。
        ///
        /// <para>★ 放大之后**要回头调小三个 Inset**：边饰移到卡外了，卡内的安全区随之变大。</para>
        /// </summary>
        public Vector2 FrameScale = Vector2.one;

        /// <summary>
        /// 卡面底图。留空 = 用按类型写死的纯色（攻击红 / 技能蓝 / 能力紫）。
        ///
        /// ★ 与 <see cref="Frame"/> 是上下两层：底图在**最底下**（插画之下），框在最上面。
        ///   所以底图可以是任何不透明的纹理，不必留透明中心。
        /// ★ 带九宫格边距（Sprite Editor 里设了 Border）的图会自动按 Sliced 画，
        ///   不带的按 Simple 拉伸——不用你在这里选。
        /// </summary>
        public Sprite Background;

        /// <summary>
        /// 底图染色。★ 底图留空时**这个字段不生效**——那种情况下底色由卡牌类型决定，
        /// 因为类型栏已经删掉了，颜色是「这是攻击牌还是技能牌」的唯一表达。
        /// 配了底图就由你自己负责让三种类型区分得开。
        /// </summary>
        public Color BackgroundTint = Color.white;

        /// <summary>
        /// 染色。★ 这几张框都是浅色的，乘色染得很干净，所以**一张框能供全部五种类型用**
        /// ——攻击染红、技能染蓝、能力染紫，不必让美术出五套。白 = 不染。
        /// </summary>
        public Color Tint = Color.white;

        /// <summary>
        /// 框的边饰吃掉卡面多少（占卡宽 / 卡高的比例），文字要缩进到这个安全区里面。
        ///
        /// ★ 必须一框一配，而不是写死一个常量：不同的框边宽差很多
        ///   （现有这批实测左右 9%–13%、上下 7%–13%，而顶部正中还挂着一颗垂下来的宝石）。
        ///
        /// ★ 默认值取实测区间的**中位**而不是最大值，是一次刻意的取舍：
        ///   缩进每多 1%，插画可见面积就少一截（Frame_47 接上去已经让插画少了约 47%）。
        ///   偶尔有个字压到一片花瓣上仍然读得出来；而按最大值缩进换来的是永久变小的插画。
        ///   某张框的名字真被顶部那颗垂宝石压到了，就单独调**那一张**的 InsetTop。
        /// </summary>
        [Range(0f, 0.30f)] public float InsetSide = 0.10f;
        [Range(0f, 0.30f)] public float InsetTop = 0.115f;
        [Range(0f, 0.30f)] public float InsetBottom = 0.095f;

        /// <summary>
        /// 卡面内部的三块排版尺寸（像素）。★ 放在这里而不是 <c>CardView</c> 的常量里，
        /// 是为了能在**运行中**拖着调——<c>CardView</c> 每帧比对这几个数，
        /// 一变就当场重排屏幕上所有的牌，不必退出 Play 再进（见 <c>CardView.ApplyLayout</c>）。
        ///
        /// <para>★ 它们只在配了框时生效。没配框的卡走 <c>CardView</c> 里那套常量，
        /// 与接框之前逐像素相同——调这里不会把无框排版也一起改坏。</para>
        /// </summary>
        [Header("卡面内部排版（像素，仅本框生效）")]
        [Range(20f, 60f)] public float NameBarHeight = 30f;
        [Range(40f, 140f)] public float DescHeight = 72f;
        [Range(2f, 20f)] public float DotMargin = 6f;

        /// <summary>
        /// 把「在 Inspector 里刚加出来、还没配过」的条目补成可用的默认值。
        ///
        /// ★★ 为什么非要有这个方法：**Unity 在 Inspector 里给 List 加元素时，
        ///    不会执行上面那些 C# 字段初始值**。点一下 <c>+</c> 得到的是一个逐字段清零的元素，
        ///    于是 <c>Tint = (0,0,0,0)</c>——全透明黑，框画上去等于**完全隐形**；
        ///    三个 Inset 也全是 0，文字一点都不缩进。
        ///    两者都不报任何错，表现就是「我明明配了，但画面毫无变化」。
        ///    第一次接卡框就是栽在这里，所以这个补丁留着。
        ///
        /// ★ 两条判据各自独立，因为它们会分别触发：
        ///   有人可能先手工染了色（于是 alpha 不再是 0），但三个 Inset 还是加出来时的 0。
        ///   ——第一次接卡框正是卡在这一步：染色配好了，缩进还全是 0。
        ///
        /// ★ 两条判据都取「绝不可能是有人真心想要的值」：
        ///   alpha 为 0 的染色是一个**看不见的框**；三个 Inset 全为 0 意味着
        ///   名字和色点直接压在花饰边下面。没有哪张卡框想要这两种结果之一。
        /// </summary>
        public void Normalize()
        {
            if (Tint.a <= 0.001f) Tint = Color.white;

            if (InsetSide <= 0f && InsetTop <= 0f && InsetBottom <= 0f)
            {
                InsetSide = 0.10f;
                InsetTop = 0.115f;
                InsetBottom = 0.095f;
            }

            // ★ 这三个各自单独判：0 高的名字栏 / 0 高的描述区 / 0 的色点边距
            //   都不是任何人想要的配置，所以「等于 0」在这里就是「没配过」，不会误判。
            if (NameBarHeight <= 0f) NameBarHeight = 30f;
            if (DescHeight <= 0f) DescHeight = 72f;
            if (DotMargin <= 0f) DotMargin = 6f;

            // ★ 同理：0 倍缩放 = 把框缩成一个点（看不见），全透明底色 = 底图整个消失。
            //   Inspector 新加的元素这两项都是 0，不补的话又是一次「配了但画面没反应」。
            if (FrameScale.x <= 0f) FrameScale.x = 1f;
            if (FrameScale.y <= 0f) FrameScale.y = 1f;
            if (BackgroundTint.a <= 0.001f) BackgroundTint = Color.white;
        }
    }
}
