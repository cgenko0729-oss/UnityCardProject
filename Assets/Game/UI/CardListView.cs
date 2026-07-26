using System;
using System.Collections.Generic;
using Game.Cards;
using Game.Core;
using Game.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>展示顺序。</summary>
    public enum CardListOrder
    {
        /// <summary>按传入的顺序原样显示。弃牌堆 / 消耗堆用它——那两个堆的顺序本来就是公开信息。</summary>
        AsIs,

        /// <summary>
        /// 排序后显示（费用 → 类型 → 名字 → Uid）。抽牌堆必须用它。
        ///
        /// ★ 这不是审美，是玩法约束（铁律 53）：抽牌堆按真实顺序显示等于直接告诉玩家
        ///   接下来 N 张抽什么，抽牌的随机性在决策层面当场消失。
        ///   排序之后只回答「里面有什么」，不回答「什么时候来」。
        /// </summary>
        Sorted,
    }

    /// <summary>
    /// 只读的卡牌浏览面板。战斗中的抽牌堆 / 弃牌堆 / 消耗堆，以及局外的「当前卡组」共用这一个。
    ///
    /// <para>★ 为什么不复用 <see cref="CardPickerScreen"/>：那个类的语义是「必须选够 N 张才能确定，
    ///   回调返回下标」，它已经在服务 6 个调用点，其中战斗内选牌还牵着可挂起的结算栈
    ///   （<c>EffectResolutionStack</c>，铁律 16/17）。往里加一套「不选、不回调、随时可关」的
    ///   生命周期，就多出一个「面板关掉时该不该 ResolveSelection」的分支——
    ///   漏掉它的表现是**战斗永久卡在挂起态**，而且不报任何错。
    ///   共享的是 <see cref="CardMiniView"/> 和网格代码，不是那套生命周期。</para>
    ///
    /// <para>★ 组件挂在遮罩根自己身上，<see cref="Close"/> 一个 Destroy 就能收干净
    ///   （同 <see cref="CardPickerScreen"/> / <see cref="ScreenBase"/>，见第三次会话的界面泄漏）。</para>
    /// </summary>
    public class CardListView : MonoBehaviour
    {
        private Action _onClosed;

        /// <summary>放大卡面那一层。null = 现在没有放大任何卡。</summary>
        private RectTransform _bigLayer;

        /// <summary>本面板打开时有没有由我们把 tooltip 压住。见 <see cref="ReleaseTooltipSuppression"/>。</summary>
        private bool _suppressedTooltip;

        /// <summary>
        /// 本面板现在是否要求压住 tooltip（= 放大卡面开着）。
        ///
        /// ★ 存在的唯一理由：<c>BattleScreen.LateUpdate</c> **每帧**都会写一次
        ///   <c>TooltipView.Suppressed = 正在拖牌</c>。那是个无条件赋值，
        ///   会把本面板压下去的开关在下一帧原样冲掉，表现是「大卡开着，底下网格的
        ///   tooltip 照样从大卡背后冒出来」。持有者必须把本属性 OR 进它那行赋值里。
        /// </summary>
        public bool SuppressesTooltip => _suppressedTooltip;

        // ================================================================= 打开

        /// <summary>
        /// 弹出面板。
        /// </summary>
        /// <param name="parent">
        /// 挂在哪一层。
        /// ★ 战斗内必须传 <c>BattleScreen</c> 自己的模态层，**不能**用 <c>GameApp.OverlayLayer</c>——
        ///   单场战斗调试场景 <c>Battle.unity</c> 里根本没有 <see cref="GameApp"/>。
        /// </param>
        /// <param name="db">Tooltip 靠它按关键字位反查 <c>KeywordDefinition</c>。null = 小卡不挂 tooltip。</param>
        public static CardListView Open(RectTransform parent, string title,
                                        IReadOnlyList<CardInstance> cards,
                                        GameDatabase db, CardListOrder order,
                                        Action onClosed = null)
        {
            if (parent == null) return null;

            var root = UIFactory.CreateOverlay(parent, "CardList", 0.90f);
            var view = root.gameObject.AddComponent<CardListView>();
            view._onClosed = onClosed;
            view.Build(root, title, cards, db, order);
            return view;
        }

        private void Build(RectTransform root, string title,
                           IReadOnlyList<CardInstance> cards, GameDatabase db, CardListOrder order)
        {
            var shown = Arrange(cards, order);

            var titleText = UIFactory.CreateText(root, "Title", title, 34,
                TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.72f));
            UIFactory.SetAnchored(titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -110), new Vector2(0, -50));

            if (shown.Count == 0)
            {
                var empty = UIFactory.CreateText(root, "Empty",
                    Loc.T("ui.cardlist.empty", "这里还没有牌。"), 26,
                    TextAnchor.MiddleCenter, new Color(0.65f, 0.68f, 0.75f));
                UIFactory.Stretch(empty.rectTransform);
            }
            else
            {
                BuildGrid(root, shown, db);
            }

            var close = UIFactory.CreateTextButton(root, "Close", Loc.T("ui.cardlist.close", "关闭"), 28,
                new Color(0.30f, 0.32f, 0.38f), Close);
            var rt = (RectTransform)close.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(300, 76);
            rt.anchoredPosition = new Vector2(0, 34);
        }

        /// <summary>
        /// 网格 + 滚动。结构与 <c>CardPickerScreen.BuildGrid</c> 同构，只是不接选择回调。
        /// </summary>
        private void BuildGrid(RectTransform root, List<CardInstance> shown, GameDatabase db)
        {
            var viewport = UIFactory.CreateEmpty(root, "Viewport");
            UIFactory.SetAnchored(viewport, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(80, 130), new Vector2(-80, -160));
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.001f);   // 需要一张图才能裁剪
            viewport.gameObject.AddComponent<RectMask2D>();

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var content = UIFactory.CreateEmpty(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardMiniView.Width, CardMiniView.Height);
            grid.spacing = new Vector2(16, 16);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(10, 10, 10, 10);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = viewport;

            for (int i = 0; i < shown.Count; i++)
            {
                var card = shown[i];
                var view = CardMiniView.Create(content, card, db);
                view.SetClickHandler(() => ShowBigCard(card));
            }
        }

        // ================================================================= 排序

        /// <summary>
        /// 按 <paramref name="order"/> 整理出要显示的列表。
        ///
        /// ★★ 一定是**新建一个 List** 再排，绝不能就地 Sort 传进来的那个。
        ///    <c>DeckController.DrawPile</c> 等四个堆是 <c>public readonly List</c>，
        ///    而 <c>readonly</c> 只锁引用不锁内容——就地 <c>Sort</c> 会**真的改掉玩家的抽牌顺序**，
        ///    编译通过、不抛异常、`Game.UI` 也没有测试程序集能覆盖它（铁律 52）。
        ///    这是本面板唯一能造成**逻辑**损坏的地方，别的失误都只是显示问题。
        /// </summary>
        private static List<CardInstance> Arrange(IReadOnlyList<CardInstance> cards, CardListOrder order)
        {
            var shown = new List<CardInstance>(cards != null ? cards.Count : 0);
            if (cards != null)
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i] != null) shown.Add(cards[i]);

            if (order == CardListOrder.Sorted) shown.Sort(CompareForDisplay);
            return shown;
        }

        /// <summary>
        /// 费用 → 类型 → 名字 → Uid。
        ///
        /// ★ 最后那道 Uid 不是多余的：<c>List.Sort</c> 是**不稳定**排序，
        ///   前面几个键全部相同时（牌组里三张一样的「打击」）两次打开的排列可能不同，
        ///   看起来像面板在自己闪。加上 Uid 之后任何两张牌都有确定的先后。
        /// </summary>
        private static int CompareForDisplay(CardInstance a, CardInstance b)
        {
            int c = SortCost(a).CompareTo(SortCost(b));
            if (c != 0) return c;

            var da = a.Def;
            var db = b.Def;

            c = ((int)(da != null ? da.Type : CardType.Skill)).CompareTo((int)(db != null ? db.Type : CardType.Skill));
            if (c != 0) return c;

            c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
            if (c != 0) return c;

            return a.Uid.CompareTo(b.Uid);
        }

        /// <summary>X 费与不可打出的牌排到最后——它们的 <c>Cost</c> 字段没有可比的含义。</summary>
        private static int SortCost(CardInstance card)
        {
            var def = card.Def;
            if (def == null) return 1000;
            return def.CostMode switch
            {
                CostMode.X => 100,
                CostMode.Unplayable => 101,
                _ => def.Cost,
            };
        }

        // ================================================================= 放大卡面

        /// <summary>
        /// 点一张小卡 → 中间弹一张放大的卡面，点任意处关掉。
        ///
        /// ★ 用等比放大的 <see cref="CardMiniView"/> 而不是 <see cref="CardView"/>：
        ///   后者的 <c>Create</c> 签名要一个 <see cref="BattleScreen"/>，还带拖拽、悬停抬牌、
        ///   位姿插值（铁律 23）——局外根本没有 BattleScreen，而那些行为在浏览面板里全是负担。
        /// </summary>
        private void ShowBigCard(CardInstance card)
        {
            if (card == null) return;
            CloseBigCard();

            var layer = UIFactory.CreateOverlay((RectTransform)transform, "BigCard", 0.55f);
            _bigLayer = layer;

            // 点空白处关掉。★ 这一层必须自己是个 Button：遮罩只吃射线不产生点击，
            //   没有它的话玩家只能靠 Esc 退出，而 Esc 在这种「点开的小窗」上不是第一直觉。
            var closeArea = layer.gameObject.AddComponent<Button>();
            closeArea.targetGraphic = layer.GetComponent<Image>();
            closeArea.onClick.AddListener(CloseBigCard);

            // ★ db 传 null：下面刚把 tooltip 全局压住了，挂上去也永远不会弹，
            //   留一个永不触发的 TooltipTarget 只会让下一个人以为它坏了。
            var big = CardMiniView.Create(layer, card, null);
            var rt = (RectTransform)big.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            // 网格里的小卡尺寸是 GridLayoutGroup 给的，这里没有布局组，得自己设
            UIFactory.SetSize(rt, CardMiniView.Width, CardMiniView.Height);
            rt.localScale = Vector3.one * BigCardScale;

            big.SetClickHandler(CloseBigCard);

            // 大卡自己就写着完整描述了，再让关键字 tooltip 从底下的网格里冒出来只会两层弹窗打架
            SuppressTooltip();
        }

        private const float BigCardScale = 2.0f;

        private void CloseBigCard()
        {
            if (_bigLayer != null) Destroy(_bigLayer.gameObject);
            _bigLayer = null;
            ReleaseTooltipSuppression();
        }

        private bool BigCardOpen => _bigLayer != null;

        // ================================================================= Tooltip 抑制

        /// <summary>
        /// ★ <see cref="TooltipView.Suppressed"/> 是**全局静态**开关，谁打开谁负责放开（铁律 31）。
        ///   忘了放开的话整个游戏的 tooltip 会永久哑掉，并且不报任何错。
        ///   所以这里用 <see cref="_suppressedTooltip"/> 记账，并且在 <see cref="OnDisable"/> 里兜一次。
        /// </summary>
        private void SuppressTooltip()
        {
            _suppressedTooltip = true;
            TooltipView.Suppressed = true;
        }

        private void ReleaseTooltipSuppression()
        {
            if (!_suppressedTooltip) return;
            _suppressedTooltip = false;
            TooltipView.Suppressed = false;
        }

        // ================================================================= 关闭

        /// <summary>
        /// 由持有者在自己的「取消」输入分支**最前面**调用。
        /// 返回 true = 这次的 Esc / 右键被本面板吃掉了，持有者不要再跑自己的取消逻辑。
        ///
        /// <para>★ 为什么是「持有者来问」而不是本面板自己在 Update 里轮询：
        ///   两个 MonoBehaviour 的 Update 先后顺序是不确定的。自己轮询的话，
        ///   <c>BattleScreen.Update</c> 有可能在同一帧先跑，Esc 会被 <c>CancelTargeting</c>
        ///   先吃掉，于是面板「有时候关得掉、有时候关不掉」——这种时序 bug 极难复现。
        ///   改成拉取式，优先级就是写死的。</para>
        ///
        /// <para>优先级：放大卡面 → 本面板 → 交回持有者。</para>
        /// </summary>
        public bool ConsumeCancelInput()
        {
            if (!InputCompat.EscapeDown && !InputCompat.RightMouseDown) return false;

            if (BigCardOpen) { CloseBigCard(); return true; }

            Close();
            return true;
        }

        public void Close()
        {
            var cb = _onClosed;
            _onClosed = null;

            // ★ 先清回调再销毁，同 CardPickerScreen.Close：回调里可能立刻再开一个面板，
            //   「关旧的」与「开新的」的顺序会互相踩。
            Destroy(gameObject);
            cb?.Invoke();
        }

        /// <summary>
        /// ★ 兜底：本面板可能不是被 <see cref="Close"/> 关掉的，而是跟着父节点一起被销毁的
        ///   （战斗界面被销毁、<c>GameApp</c> 切界面、切语言重建整棵界面）。
        ///   那时 Close 不会被调用，而 tooltip 的全局开关还压着。
        /// </summary>
        private void OnDisable() => ReleaseTooltipSuppression();
    }
}
