using System.Collections.Generic;
using Game.Cards;
using Game.Core;
using Game.Effects;
using Game.Effects.Impl;
using Game.Statuses;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 把卡牌 / 药水的描述染成富文本。TMP 的 <c>&lt;color&gt;</c> / <c>&lt;u&gt;</c> 标签。
    ///
    /// ★★ 分两层，来源完全不同，不能混为一谈：
    ///
    ///    ① **数值**（「造成 <b>9</b> 点伤害」里的 9）——零匹配。
    ///       它是 <c>{0}</c> 被替换出来的，替换的那一刻我们手里就攥着产出它的那个
    ///       <see cref="CardEffect"/>，看类型即可（<see cref="ColorForEffect"/>）。
    ///       没有任何猜测成分。
    ///
    ///    ② **词条名**（「施加 2 层<b>易伤</b>」里的「易伤」）——定向替换。
    ///       它是**写死在模板文案里**的，不经过 {N}，所以只能在文本里找。
    ///
    /// ★★ 关于 ② 与铁律 29 的关系，这是本文件最需要说清楚的一点：
    ///
    ///    铁律 29 禁止的是「**从描述文字里猜**这张牌牵扯到哪些词条」——
    ///    那个方向是 文本 → 数据，一旦文案换个措辞（「令目标变得脆弱」里没有「易伤」二字），
    ///    词条就静默消失了。
    ///
    ///    这里的方向是**反的**：先用 <see cref="EffectTree.CollectStatuses"/> 和
    ///    <see cref="CardKeyword"/> 的位**从数据得出**「这张牌确实涉及易伤」，
    ///    再拿这个已经确定的名字去文案里上色。
    ///    文案改了措辞的后果只是「这两个字没被染色」——一个纯视觉的退化，
    ///    而不是「tooltip 里少了一条解释」那种信息丢失。
    ///    tooltip 那边照旧从数据推导，一个字都不受影响。
    ///
    /// ★ 装饰器是**有状态**的（要知道当前是哪张牌），所以每个调用方持有自己的一份实例。
    ///   不做成静态单例：手牌里十几张卡在同一帧里各自算描述，共享一份实例就要靠
    ///   「用之前记得先 SetCard」这种约定，忘一次就是一张牌顶着另一张牌的词条颜色。
    /// </summary>
    public class RichDescription : IDescriptionDecorator
    {
        // ============================================================ 配色
        //
        // ★ 词条名的颜色**直接取自 TooltipContent**，不另开一套：
        //   卡面上「易伤」是红的、悬停弹出来的 tooltip 里「易伤」的标题也是红的，
        //   玩家才会把颜色读成「同一个东西」。两处各配一套的话，
        //   改了一边忘了另一边，颜色就从信息退化成装饰。

        /// <summary>伤害。★ 亮红而不是纯红：攻击牌的底色本身就是深红（CardView.ColAttack），
        /// 纯红字压在上面明度差不够，读起来是糊的。</summary>
        private static readonly Color Damage = new Color(1.00f, 0.54f, 0.50f);

        /// <summary>护甲。同理避开技能牌的深蓝底，用偏青的亮蓝。</summary>
        private static readonly Color Block = new Color(0.66f, 0.85f, 1.00f);

        private static readonly Color Heal = new Color(0.56f, 0.89f, 0.60f);

        /// <summary>能量。与能量球的金色同源。</summary>
        private static readonly Color Energy = new Color(1.00f, 0.84f, 0.38f);

        /// <summary>
        /// 其余一切数值（抽牌、弃牌、重复次数、费用变化……）。
        ///
        /// ★ 刻意也给一个颜色，而不是留成正文白：这一条本身就是可读性的大头——
        ///   「抽 2 张牌」里的 2 比周围的字亮一档，眼睛才能在一屏十几张卡里扫到数字。
        ///   给每类数值都单配一个颜色反而会让卡面变成调色盘，那时颜色就不再有意义了。
        /// </summary>
        private static readonly Color Generic = new Color(1.00f, 0.93f, 0.72f);

        // ============================================================ 状态

        // ★ 来源存成「效果列表 + 关键字位」这两样通用的东西，而不是直接存 CardInstance：
        //   卡和药水的描述走的是同一套模板 + {N} 机制（铁律 19：药水的效果就是 List<CardEffect>），
        //   词条上色自然也该是同一套。存成 CardInstance 的话，药水就得另写一份，
        //   而那两份迟早会在「又加了一类词条」时分叉。
        private List<CardEffect> _effects, _extraEffects;
        private CardKeyword _keywords;
        private GameDatabase _db;

        /// <summary>缓存的归属者（<c>CardDefinition</c> 或 <c>PotionDefinition</c>）。只用来比对，不解引用。</summary>
        private object _owner;

        /// <summary>
        /// 模板染色的结果缓存。★ 不是可选的优化：
        /// <see cref="CardView.Refresh"/> 每帧对每张手牌调一次描述，而词条染色要
        /// 扫一遍效果树 + 对每个词条名做一次 <c>string.Replace</c>（每次都分配新字符串）。
        /// 十几张手牌 × 每张两三个词条 × 每帧，是实打实的 GC 压力。
        /// 而它的**输入每帧都一样**——模板、词条集合、语言都不随帧变。
        /// </summary>
        private string _cachedTemplate, _cachedResult;

        /// <summary>缓存对应的来源身份。见 <see cref="DecorateTemplate"/> 里的失效判据。</summary>
        private object _cachedOwner;
        private CardKeyword _cachedKeywords;

        private readonly List<StatusDefinition> _statusBuffer = new List<StatusDefinition>(4);

        /// <summary>关键字的位顺序。与 <see cref="TooltipContent"/> 那份保持一致。</summary>
        private static readonly CardKeyword[] AllKeywords =
        {
            CardKeyword.Exhaust, CardKeyword.Retain, CardKeyword.Innate,
            CardKeyword.Ethereal, CardKeyword.Unplayable
        };

        /// <summary>
        /// 换一张牌。★ 每次取描述之前都要调——装饰器要靠它才知道该给哪些词条上色。
        /// <paramref name="db"/> 为 null 时关键字不上色（拿不到本地化名字），状态照常。
        /// </summary>
        public void SetCard(CardInstance card, GameDatabase db)
        {
            if (card == null || card.Def == null) { Clear(); return; }

            _owner = card.Def;
            _effects = card.Def.Effects;
            _extraEffects = card.Def.InHandEndOfTurnEffects;

            // ★ ExtraKeywords 也要算进去：战斗中有效果会给某张牌临时挂上「消耗」，
            //   那时文案里的「消耗」两个字也该跟着亮起来。
            _keywords = card.Def.Keywords | card.ExtraKeywords;
            _db = db;
        }

        /// <summary>
        /// 换一瓶药水。★ 药水没有 <see cref="CardKeyword"/>（那是牌堆语义），只有状态要上色。
        /// </summary>
        public void SetPotion(Game.Potions.PotionDefinition def, GameDatabase db)
        {
            if (def == null) { Clear(); return; }

            _owner = def;
            _effects = def.Effects;
            _extraEffects = null;
            _keywords = CardKeyword.None;
            _db = db;
        }

        private void Clear()
        {
            _owner = null;
            _effects = null;
            _extraEffects = null;
            _keywords = CardKeyword.None;
        }

        // ============================================================ 数值

        public string DecorateValue(string value, CardEffect effect)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return Wrap(value, ColorForEffect(effect), underline: false);
        }

        /// <summary>
        /// 一个数值该染什么色，只看**产出它的效果是什么类型**。
        ///
        /// ★ <see cref="ApplyStatusEffect"/> 是唯一需要再看一层的：同一个效果类既发增益也发减益
        ///   （「获得 2 层力量」和「施加 2 层易伤」都是它），正负写在
        ///   <see cref="StatusDefinition.Polarity"/> 上。层数的颜色跟着极性走，
        ///   于是「2 层力量」的 2 是绿的、「2 层易伤」的 2 是红的——
        ///   与那两个词条名自己的颜色一致，读起来是一整块。
        /// </summary>
        private static Color ColorForEffect(CardEffect effect)
        {
            switch (effect)
            {
                case DamageEffect _: return Damage;
                case BlockEffect _: return Block;
                case HealEffect _: return Heal;
                case EnergyEffect _: return Energy;

                case ApplyStatusEffect apply:
                    return apply.Status != null
                        ? TooltipContent.AccentOf(apply.Status.Polarity)
                        : Generic;

                default: return Generic;
            }
        }

        // ============================================================ 词条名

        public string DecorateTemplate(string template)
        {
            if (string.IsNullOrEmpty(template) || _owner == null) return template;

            // ★ 缓存的失效判据是「模板 + 来源身份 + 关键字位」三样。
            //   ① 模板：语言切换会把它整个换掉（LocalizedDescriptionTemplate 走 Loc.T），
            //      所以比模板本身就等于把语言也比了，不需要再去问 Loc 当前是什么语言；
            //   ② 来源：同一个 CardView 会被复用给不同的牌（按 Uid 增量复用）；
            //   ③ 关键字位：战斗中有效果会给某张牌临时挂上「消耗」。
            if (_cachedResult != null
                && ReferenceEquals(_cachedOwner, _owner)
                && _cachedKeywords == _keywords
                && string.Equals(_cachedTemplate, template))
                return _cachedResult;

            string result = Colorize(template);

            _cachedTemplate = template;
            _cachedOwner = _owner;
            _cachedKeywords = _keywords;
            _cachedResult = result;
            return result;
        }

        private string Colorize(string template)
        {
            string s = template;

            // ---- 关键字（消耗 / 保留 / 固有 / 虚无 / 不可打出）
            //
            // ★ 加下划线：这几个词在悬停提示里有完整解释，下划线是「这里有更多信息」的
            //   通用暗示。状态名不加——一张牌上四五条下划线会让描述看起来像一堆链接。
            if (_db != null && _keywords != CardKeyword.None)
            {
                for (int i = 0; i < AllKeywords.Length; i++)
                {
                    var bit = AllKeywords[i];
                    if ((_keywords & bit) == 0) continue;

                    var def = _db.GetKeyword(bit);
                    if (def == null) continue;

                    s = ReplaceOnce(s, def.LocalizedName, TooltipContent.KeywordAccent, underline: true);
                }
            }

            // ---- 状态（易伤 / 虚弱 / 中毒 / 力量 …）
            //
            // ★ 从效果树收集，不是从文本里找——见类注释里关于铁律 29 的那一段。
            _statusBuffer.Clear();
            if (_effects != null) EffectTree.CollectStatuses(_effects, _statusBuffer);
            if (_extraEffects != null) EffectTree.CollectStatuses(_extraEffects, _statusBuffer);

            for (int i = 0; i < _statusBuffer.Count; i++)
            {
                var def = _statusBuffer[i];
                if (def == null) continue;
                s = ReplaceOnce(s, def.LocalizedName, TooltipContent.AccentOf(def.Polarity), underline: false);
            }

            return s;
        }

        /// <summary>
        /// 把 <paramref name="word"/> 在 <paramref name="s"/> 里的**第一次**出现包上颜色。
        ///
        /// ★★ 只替换第一次，且从头找，是三条约束合起来的结果：
        ///
        ///    ① <b>不能用 string.Replace</b>：它替换**全部**出现。而上一轮插进去的
        ///       <c>&lt;color=#RRGGBB&gt;</c> 已经在字符串里了，下一个词条名若恰好是
        ///       之前某个词条名的子串（「力量」与「力量之源」、英文的 "Weak" 与 "Weaken"），
        ///       就会替换到**标记内部**，产出一个畸形的标签。TMP 遇到畸形标签不会报错，
        ///       它会把整段当普通文字画出来——表现是「卡面上突然出现一串 &lt;color=#FF8A80&gt;」。
        ///
        ///    ② <b>要跳过已经在标记里的位置</b>：<see cref="IndexOutsideTags"/> 负责这个。
        ///
        ///    ③ <b>空词条名必须挡掉</b>：没配 DisplayName 又没有本地化的资产会给出空串，
        ///       而 <c>IndexOf("")</c> 恒返回 0——那会在描述开头插一对空的颜色标签，
        ///       每帧一对，而且完全看不出来是哪来的。
        /// </summary>
        private static string ReplaceOnce(string s, string word, Color color, bool underline)
        {
            if (string.IsNullOrEmpty(word)) return s;

            int at = IndexOutsideTags(s, word);
            if (at < 0) return s;

            return s.Substring(0, at)
                   + Wrap(word, color, underline)
                   + s.Substring(at + word.Length);
        }

        /// <summary>
        /// 找 <paramref name="word"/> 第一次出现的位置，**跳过所有落在 &lt;…&gt; 里面的匹配**。
        /// 找不到返回 -1。
        /// </summary>
        private static int IndexOutsideTags(string s, string word)
        {
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;

                if (i + word.Length > s.Length) break;

                // ★ 匹配本身也不能跨进标记里：「易<b>伤</b>」这种不算命中。
                bool hit = true;
                for (int k = 0; k < word.Length; k++)
                {
                    char sc = s[i + k];
                    if (sc == '<' || sc == '>' || sc != word[k]) { hit = false; break; }
                }

                if (hit && IsAsciiWord(word) && !HasWordBoundary(s, i, word.Length)) hit = false;
                if (hit) return i;
            }
            return -1;
        }

        /// <summary>
        /// 词条名是不是「拉丁字母词」——只有这种才需要检查词边界。
        ///
        /// ★★ 为什么不能一律检查边界：中文没有词边界。
        ///    「施加 2 层易伤」里「易伤」的前一个字是「层」，
        ///    而 <c>char.IsLetterOrDigit('层')</c> 是 **true** ——
        ///    照搬英文那套边界判断，中文词条会**一个都匹配不上**，
        ///    表现是「换成英文之后上色好了，中文下全是白的」。
        /// </summary>
        private static bool IsAsciiWord(string word)
        {
            for (int i = 0; i < word.Length; i++)
                if (IsAsciiLetter(word[i])) return true;
            return false;
        }

        /// <summary>
        /// 命中处的前后是不是词边界。
        ///
        /// ★ 这一条挡的是本地化之后才会出现的 bug：状态「Weak」会命中「Weaken」的前四个字母，
        ///   染完留下一个孤零零的 "en"。中文下不会发生，所以很容易在中文环境里测不出来。
        /// </summary>
        private static bool HasWordBoundary(string s, int at, int len)
        {
            if (at > 0 && IsAsciiLetter(s[at - 1])) return false;
            int end = at + len;
            if (end < s.Length && IsAsciiLetter(s[end])) return false;
            return true;
        }

        private static bool IsAsciiLetter(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static string Wrap(string text, Color color, bool underline)
        {
            // ★ ColorUtility.ToHtmlStringRGB 输出的是不带 # 的 6 位十六进制，正是 TMP 要的格式。
            string hex = ColorUtility.ToHtmlStringRGB(color);
            return underline
                ? $"<color=#{hex}><u>{text}</u></color>"
                : $"<color=#{hex}>{text}</color>";
        }
    }
}
