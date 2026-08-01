using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Cards;
using Game.Core;
using Game.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 通用选牌面板。奖励三选一、删卡、升级卡、事件里的选牌全部共用这一个。
    ///
    /// 两种数据源二选一：
    ///   - <c>deckCards</c>：从玩家牌库里选（删卡 / 升级），回调返回在牌库中的下标；
    ///   - <c>defCards</c>：从一组候选配置里选（奖励 / 事件给牌），回调返回候选列表的下标。
    /// </summary>
    public class CardPickerScreen : MonoBehaviour
    {
        private GameApp _app;
        private RectTransform _root;
        private Action<List<int>> _onConfirm;

        private int _pickCount;
        private bool _cancellable;

        private readonly List<int> _selected = new List<int>(4);
        private readonly List<CardMiniView> _views = new List<CardMiniView>();

        private Button _confirmButton;
        private TMP_Text _counterText;

        /// <summary>★ 组件挂在遮罩根自己身上，<see cref="Close"/> 一个 Destroy 就能收干净。</summary>
        public void Open(GameApp app, string title,
                         IReadOnlyList<CardInstance> deckCards,
                         IReadOnlyList<CardDefinition> defCards,
                         int pickCount, bool cancellable, Action<List<int>> onConfirm)
        {
            _app = app;
            _pickCount = Mathf.Max(1, pickCount);
            _cancellable = cancellable;
            _onConfirm = onConfirm;

            _root = (RectTransform)transform;

            var titleText = UIFactory.CreateText(_root, "Title", title, 34,
                TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.72f));
            UIFactory.SetAnchored(titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -110), new Vector2(0, -50));

            _counterText = UIFactory.CreateText(_root, "Counter", "", 22,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));
            UIFactory.SetAnchored(_counterText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -146), new Vector2(0, -110));

            BuildGrid(deckCards, defCards);
            BuildButtons();
            RefreshSelection();

            // ★★ 必须排在 RefreshSelection **之后**。
            //    RefreshSelection → SetSelected(false) → ApplySelectionVisuals 会把 localScale 写回 1，
            //    而 PlayIntro 把起点设成 0.9。DOTween 是在 tween **真正开始那一刻**才捕获起始值的
            //    （这里每张牌前面还挂着一段 delay），于是起点会被那一句冲成 1，
            //    翻面时的「由小变大」整个消失——而动画照播，只是少了一半的分量，
            //    很容易被当成「参数调小了」而去改 Ease。
            PlayIntroAnimations();
        }

        /// <summary>
        /// 逐张翻开。
        ///
        /// ★ 间隔不是定值 120ms，而是**按张数摊**：这个面板同时服务
        ///   「奖励三选一」（3 张）和「从牌库里删一张」（几十张）。
        ///   定值 120ms 在前者上正是想要的节奏，在后者上会变成 4 秒的开场——
        ///   而玩家在删卡界面想做的事只是赶紧找到那张诅咒。
        ///   张数一多就自动压缩，总铺开时间封顶在 <see cref="IntroTotalCap"/>。
        /// </summary>
        private void PlayIntroAnimations()
        {
            int n = _views.Count;
            if (n == 0) return;

            float step = n <= IntroFullPaceCount
                ? IntroStep
                : IntroTotalCap / n;

            for (int i = 0; i < n; i++)
                _views[i].PlayIntro(i * step);
        }

        /// <summary>逐张翻开的间隔（张数不多时）。</summary>
        private const float IntroStep = 0.12f;

        /// <summary>不超过这么多张就按 <see cref="IntroStep"/> 的节奏走。三选一、四选一都在里面。</summary>
        private const int IntroFullPaceCount = 6;

        /// <summary>张数多时，整批铺开的总时长上限。</summary>
        private const float IntroTotalCap = 0.55f;

        private void BuildGrid(IReadOnlyList<CardInstance> deckCards, IReadOnlyList<CardDefinition> defCards)
        {
            var viewport = UIFactory.CreateEmpty(_root, "Viewport");
            UIFactory.SetAnchored(viewport, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(80, 130), new Vector2(-80, -160));
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.001f);
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

            // ★ 只用来给描述里的关键字名上色。拿不到（没有 GameApp / 没有 Database）时是 null，
            //   小卡会安静地退化成「关键字不上色」，而不是不显示描述。
            var db = _app != null ? _app.Database : null;

            int count = deckCards != null ? deckCards.Count : (defCards != null ? defCards.Count : 0);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                // ★★ 两条路都走 **def 重载**，刻意不走 Create(parent, CardInstance, db)。
                //    那个重载的 db 参数语义是「挂 tooltip」（见它的注释），
                //    而这里只是想要上色。走过去会顺带给整个选牌面板加上悬停解释——
                //    那是一件独立的、该单独验证的事，不该由「描述上色」顺手带进来。
                CardMiniView view = deckCards != null
                    ? CardMiniView.Create(content, deckCards[i].Def, deckCards[i].UpgradeLevel > 0, db)
                    : CardMiniView.Create(content, defCards[i], upgraded: false, db);

                view.SetClickHandler(() => Toggle(index));
                _views.Add(view);
            }
        }

        private void BuildButtons()
        {
            _confirmButton = UIFactory.CreateTextButton(_root, "Confirm", Loc.T("ui.picker.confirm", "确定"), 28,
                new Color(0.30f, 0.44f, 0.32f), Confirm);
            Place((RectTransform)_confirmButton.transform, _cancellable ? -180 : 0);

            if (_cancellable)
            {
                var skip = UIFactory.CreateTextButton(_root, "Skip", Loc.T("ui.picker.skip", "跳过"), 28,
                    new Color(0.34f, 0.26f, 0.26f), Cancel);
                Place((RectTransform)skip.transform, 180);
            }
        }

        private void Place(RectTransform rt, float x)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(300, 76);
            rt.anchoredPosition = new Vector2(x, 34);
        }

        // ================================================================= 选择

        private void Toggle(int index)
        {
            if (_selected.Contains(index)) _selected.Remove(index);
            else
            {
                // 只需要选一张时，点新的就换掉旧的——比强迫玩家先取消再选顺手得多
                if (_pickCount == 1) _selected.Clear();
                if (_selected.Count >= _pickCount) return;
                _selected.Add(index);
            }
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            // ★ 「已经选够了」才让没选中的退居其次。选不够时它们仍然是候选，压暗会误导——
            //   多选（弃 2 张）的中途把剩下的全压暗，看起来像是那些牌已经不能选了。
            bool dimOthers = _selected.Count >= _pickCount;

            for (int i = 0; i < _views.Count; i++)
            {
                bool on = _selected.Contains(i);
                _views[i].SetSelected(on);
                _views[i].SetDimmed(dimOthers && !on);
            }

            _counterText.text = _pickCount > 1
                ? Loc.T("ui.picker.counter", "已选 {0} / {1}", _selected.Count, _pickCount)
                : "";

            UIFactory.SetInteractable(_confirmButton, _selected.Count == _pickCount,
                new Color(0.30f, 0.44f, 0.32f));
        }

        private void Confirm()
        {
            if (_selected.Count != _pickCount) return;
            var result = new List<int>(_selected);
            var cb = _onConfirm;
            _onConfirm = null;
            Close();
            cb?.Invoke(result);
        }

        private void Cancel()
        {
            var cb = _onConfirm;
            _onConfirm = null;
            Close();
            cb?.Invoke(new List<int>());
        }

        /// <summary>
        /// ★ 先清空回调再销毁：回调里常常会立刻再开一个选牌面板（事件的连续选择），
        ///   不清空的话「关闭旧面板」和「打开新面板」的顺序会互相踩。
        /// </summary>
        private void Close() => Destroy(gameObject);
    }

    /// <summary>选牌面板里的一张小卡。只显示费用 / 名字 / 描述，够做决策就行。</summary>
    public class CardMiniView : MonoBehaviour, IPointerClickHandler, ITooltipSource
    {
        public const float Width = 190f;
        public const float Height = 250f;

        // 插画窗。数值与 CardView 那组同构，只是卡略大一点。
        private const float ArtSideMargin = 8f;
        private const float ArtTop = 50f;
        private const float ArtHeight = 88f;

        /// <summary>没有插画时描述区从卡顶往下多少开始。保持接美术之前的原值。</summary>
        private const float DescTopNoArt = 52f;

        private Image _bg;
        private Action _onClick;
        private Color _baseColor;

        // ---- 入场翻面 / 选择态
        //
        // ★★ 这一整套动画**一个像素都不碰 anchoredPosition**，只动
        //    localRotation / localScale / CanvasGroup.alpha。
        //    理由是这些小卡活在 <see cref="GridLayoutGroup"/> 底下，位置由布局组每次重建时写死——
        //    在这里去 tween 位置，就是铁律 65 那条「布局组接管哪个轴，别处写那个轴就会被当场覆盖」
        //    的同一个形状，而表现是「有时候动、有时候不动」，取决于布局组当帧有没有重算。
        //
        // ★ 于是「从画面外飞进来」这个原始想法被换成了**翻面**。
        //   这不是妥协——恰恰相反，翻面在这个工程里是**唯一能做对**的那个：
        //   Canvas 是 ScreenSpaceOverlay（正交投影），绕 Y 轴转 90° 在屏幕上的效果
        //   正好就是「宽度压到 0」，而那就是一张牌立起来时该有的样子。
        //   同样的正交投影让 CardView 的悬停倾斜只能靠位移和高光去凑（见那边的注释），
        //   在这里却是白送的。

        /// <summary>单张翻开的时长。</summary>
        private const float FlipTime = 0.34f;

        /// <summary>选中 / 退居其次的过渡时长。</summary>
        private const float SelectTime = 0.16f;

        /// <summary>没被选中的那些缩到多小、淡到多透。</summary>
        private const float DimScale = 0.94f, DimAlpha = 0.45f;

        private const float SelectedScale = 1.05f;

        private CanvasGroup _group;
        private CardRarity _rarity = CardRarity.Basic;
        private bool _selected, _dimmed;

        /// <summary>入场动画还在播。★ 期间不让选择态去写 localScale，两边会对着打。</summary>
        private bool _introPlaying;

        /// <summary>已经生效的选择态目标值。见 <see cref="ApplySelectionVisuals"/> 的短路。</summary>
        private float _appliedScale = 1f, _appliedAlpha = 1f;

        /// <summary>这张小卡对应的运行时实例。只由 <c>Create(parent, card, db)</c> 那条路设，tooltip 用它。</summary>
        private CardInstance _card;

        /// <summary>Tooltip 用它按关键字位反查 <c>KeywordDefinition</c>。null = 本卡不挂 tooltip。</summary>
        private GameDatabase _db;

        public static CardMiniView Create(Transform parent, CardInstance card)
            => Create(parent, card, null);

        /// <summary>
        /// 从运行时实例建一张小卡。
        ///
        /// ★ <paramref name="db"/> 默认 null = **不挂 tooltip**，所以既有的调用点
        ///   （奖励三选一 / 删卡 / 升级 / 事件给牌 / 战斗内选牌）一行都不用改、行为一个像素不变。
        ///   想给它们也加悬停解释，各自传一个 db 即可，那是独立的一步。
        /// </summary>
        public static CardMiniView Create(Transform parent, CardInstance card, GameDatabase db)
        {
            // ★ 同一个 db 在这里担了两件事：往下传是给描述上色，往下面几行是挂 tooltip。
            //   两者的前提相同（「拿得到 GameDatabase 吗」），但**决定不同**——
            //   只想上色、不想要 tooltip 的调用方请直接用 def 重载（见 CardPickerScreen.BuildGrid）。
            var view = Create(parent, card.Def, card.UpgradeLevel > 0, db);

            // ★ 悬停解释关键字（消耗 / 虚无…）与状态（易伤 / 虚弱…），与手牌上的 CardView 共用
            //   同一套内容与样式。原本只有大卡有提示、小卡没有，是一处没人注意到的不一致。
            if (db != null)
            {
                view._card = card;
                view._db = db;
                TooltipTarget.Attach(view.gameObject, view);
            }
            return view;
        }

        /// <param name="db">
        /// 只用来把描述里的**关键字名**上色（要靠它查 <c>KeywordDefinition</c> 的本地化名字）。
        /// ★ 传 null 是安全的：状态名照常上色，关键字退化成正文色。
        ///   战斗测试场景没有 GameApp、也就没有 Database，那条路必须能跑。
        /// </param>
        public static CardMiniView Create(Transform parent, CardDefinition def, bool upgraded, GameDatabase db = null)
        {
            var color = ColorOf(def != null ? def.Type : CardType.Skill);
            // ★ 与 CardView 用**同一个** RoundedStyle.Card。两处必须一致：
            //   奖励三选一里的迷你卡和手上那张真牌是同一个东西的两个尺寸，
            //   一个有圆角一个没有，会读成「这两种是不同的卡」。
            var rt = UIFactory.CreateRoundedPanel(parent, "Mini_" + (def != null ? def.Id : "null"),
                color, RoundedStyle.Card).rectTransform;

            var view = rt.gameObject.AddComponent<CardMiniView>();
            view._bg = rt.GetComponent<Image>();
            view._baseColor = color;
            view._rarity = def != null ? def.Rarity : CardRarity.Basic;

            var cost = UIFactory.CreateRoundedPanel(rt, "Cost",
                new Color(0.08f, 0.08f, 0.10f, 0.92f), RoundedStyle.Chip).rectTransform;
            UIFactory.SetAnchored(cost, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -46), new Vector2(46, -6));
            var costText = UIFactory.CreateText(cost, "CostText", CostTextOf(def), 24);
            UIFactory.Stretch(costText.rectTransform);

            var name = UIFactory.CreateText(rt, "Name",
                (def != null ? def.LocalizedName : "?") + (upgraded ? "+" : ""), 20, TextAnchor.UpperCenter);
            UIFactory.SetAnchored(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(50, -44), new Vector2(-6, -8));

            // ---- 插画窗。规则与 CardView 完全一致（没配图就当它不存在），
            //      两处必须一起接：只接一处的话，抽到的牌有图、奖励三选一里没图。
            var art = UIFactory.CreateArtWindow(rt, "Art", def != null ? def.Art : null,
                Width - ArtSideMargin * 2f, ArtHeight);

            float descTop = -DescTopNoArt;
            if (art != null)
            {
                art.anchorMin = art.anchorMax = new Vector2(0.5f, 1f);
                art.pivot = new Vector2(0.5f, 1f);
                art.anchoredPosition = new Vector2(0f, -ArtTop);
                descTop = -(ArtTop + ArtHeight + 6f);
            }

            var desc = UIFactory.CreateText(rt, "Desc", DescriptionOf(def, db), 15, TextAnchor.UpperLeft);
            UIFactory.SetAnchored(desc.rectTransform, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(10, 30), new Vector2(-10, descTop));

            if (art != null) UIFactory.EnableAutoSize(desc, 11f, 15f);

            var footer = UIFactory.CreateText(rt, "Footer", FooterOf(def), 14, TextAnchor.LowerCenter,
                new Color(1f, 1f, 1f, 0.6f));
            UIFactory.SetAnchored(footer.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(4, 4), new Vector2(-4, 26));

            return view;
        }

        public void SetClickHandler(Action onClick) => _onClick = onClick;

        public void SetSelected(bool on)
        {
            _selected = on;
            ApplySelectionVisuals();
        }

        /// <summary>
        /// 「别人被选中了，你退到后面去」。
        ///
        /// ★ 原始想法里这一步是「其余两张**下沉**」，改成了缩小 + 淡出：
        ///   下沉要写位置，而位置归 GridLayoutGroup（见类头那段注释）。
        ///   缩小 + 淡出传达的是同一件事——「这几张不再是焦点」——而且它对
        ///   三选一（3 张）和删卡（几十张）两种规模都成立，下沉在后者上会糊成一片。
        /// </summary>
        public void SetDimmed(bool on)
        {
            _dimmed = on;
            ApplySelectionVisuals();
        }

        private void ApplySelectionVisuals()
        {
            // 底色不受入场动画管辖，随时可以改
            _bg.color = _selected ? Color.Lerp(_baseColor, new Color(1f, 0.95f, 0.6f), 0.55f) : _baseColor;

            // ★ 入场还在播时不碰缩放：那条序列每帧都在写 localScale，
            //   这里再写一次的话，玩家在翻牌途中点一下，牌会当场卡在一个半大不小的尺寸上。
            //   序列结束时会自己回头调一次本方法，状态不会丢。
            if (_introPlaying) return;

            float scale = _selected ? SelectedScale : (_dimmed ? DimScale : 1f);
            float alpha = _dimmed ? DimAlpha : 1f;

            // ★ 目标没变就什么都不做。RefreshSelection 每次点击都会遍历**全部**小卡各调一次，
            //   删卡界面上是几十张；不短路的话每点一下就凭空造出上百条「从 1 补间到 1」的 tween。
            if (Mathf.Approximately(scale, _appliedScale) && Mathf.Approximately(alpha, _appliedAlpha))
                return;

            _appliedScale = scale;
            _appliedAlpha = alpha;

            // ★ target 用 transform，与入场序列的 gameObject **刻意错开**：
            //   这一句 Kill 只收上一次的选择态过渡，绝不会误伤别的东西。
            DOTween.Kill(transform);
            transform.DOScale(Vector3.one * scale, SelectTime).SetEase(Ease.OutQuad).SetTarget(transform);

            if (_dimmed || _group != null)
            {
                EnsureGroup();
                DOTween.To(() => _group.alpha, a => _group.alpha = a, alpha, SelectTime)
                       .SetTarget(transform);
            }
        }

        /// <summary>
        /// 翻开这张牌。<paramref name="delay"/> 由调用方按次序拉开，做出「逐张翻」的节奏。
        /// </summary>
        public void PlayIntro(float delay)
        {
            EnsureGroup();
            _introPlaying = true;

            // ★★ 必须先收掉选择态那条 tween。
            //    Open 的流程是 BuildGrid → RefreshSelection → PlayIntro，
            //    而 RefreshSelection 会启动一条 0.16s 的 <c>DOScale(1)</c>（把牌摆成未选中的样子）。
            //    它的 target 是 transform、入场序列的 target 是 gameObject，两条互不认识，
            //    于是**前 0.16 秒里两条 tween 同时写 localScale**，那条晚跑的赢。
            //    表现是翻面动画的「由小变大」在开头被啃掉一截，而且啃掉多少取决于帧序——
            //    看起来就像 Ease 曲线选得不对。
            DOTween.Kill(transform);

            // 起点：立起来（正交投影下就是宽度为 0 的一条线）、略小、全透明
            _group.alpha = 0f;
            transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            transform.localScale = Vector3.one * 0.9f;

            var seq = DOTween.Sequence().SetTarget(gameObject);
            seq.AppendInterval(delay);

            seq.Append(transform.DOLocalRotate(Vector3.zero, FlipTime)
                                .SetEase(Ease.OutCubic).SetTarget(gameObject));

            // OutBack：翻到位时略微过冲再收回来，牌才有「啪」地拍在桌上的重量
            seq.Join(transform.DOScale(Vector3.one, FlipTime)
                              .SetEase(Ease.OutBack).SetTarget(gameObject));

            // ★ 淡入只占翻面的前半段：后半段牌已经转过正面了，再透着看得见后面的网格
            //   会让人以为是渲染顺序出了问题。
            seq.Join(DOTween.To(() => _group.alpha, a => _group.alpha = a, 1f, FlipTime * 0.5f)
                            .SetTarget(gameObject));

            AppendRarityFlare(seq, delay);

            seq.OnComplete(() =>
            {
                _introPlaying = false;

                // ★ 必须补这一下：翻牌途中玩家完全可能已经点过一张牌了，
                //   那时 ApplySelectionVisuals 因为 _introPlaying 直接 return 了。
                ApplySelectionVisuals();
            });
        }

        /// <summary>
        /// 罕见 / 稀有卡翻开时额外闪一下。
        ///
        /// ★ 这是三选一里唯一「不用读字就知道有东西」的信号。
        ///   稀有度现在只写在卡底那行小字（<see cref="FooterOf"/>）里，
        ///   而奖励界面恰恰是玩家扫一眼就要做决定的地方。
        ///
        /// ★ 基础 / 普通卡**不闪**。每张都闪等于每张都不闪。
        /// </summary>
        private void AppendRarityFlare(Sequence seq, float delay)
        {
            Color flare;
            switch (_rarity)
            {
                case CardRarity.Rare: flare = new Color(1f, 0.84f, 0.35f); break;
                case CardRarity.Uncommon: flare = new Color(0.52f, 0.82f, 1f); break;
                default: return;
            }

            var sheen = UIFactory.CreatePanel(transform, "Flare", new Color(flare.r, flare.g, flare.b, 0f));
            UIFactory.Stretch(sheen);
            sheen.SetAsLastSibling();

            var img = sheen.GetComponent<Image>();
            img.sprite = UIFactory.RadialGlowSprite;

            // ★ 不吃射线：它铺满整张卡，开着的话点击会打在它身上而不是卡上——
            //   而卡的点击是这个界面存在的全部意义。
            img.raycastTarget = false;

            // 翻到一半（牌正面刚开始露出来）时最亮，然后化掉
            seq.Insert(delay + FlipTime * 0.45f,
                       DOTween.To(() => img.color, c => img.color = c,
                                  new Color(flare.r, flare.g, flare.b, 0.55f), FlipTime * 0.25f)
                              .SetTarget(gameObject));

            seq.Insert(delay + FlipTime * 0.70f,
                       DOTween.To(() => img.color, c => img.color = c,
                                  new Color(flare.r, flare.g, flare.b, 0f), 0.42f)
                              .SetTarget(gameObject));
        }

        private void EnsureGroup()
        {
            if (_group != null) return;
            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>★ 铁律 45：面板被销毁（确定 / 跳过 / 换界面）时把还在播的收掉。</summary>
        private void OnDisable()
        {
            DOTween.Kill(gameObject);
            DOTween.Kill(transform);
        }

        public void OnPointerClick(PointerEventData e) => _onClick?.Invoke();

        /// <summary>
        /// 悬停时解释这张牌涉及的关键字与状态。内容与 <see cref="CardView.BuildTooltip"/> 完全同源
        /// （状态是**扫效果树**得到的，不是拿描述文字做子串匹配——铁律 29）。
        /// </summary>
        public bool BuildTooltip(List<TooltipEntry> buffer)
            => TooltipContent.BuildForCard(_card, _db, buffer);

        /// <summary>
        /// 战斗外没有 BattleContext，描述模板里的 {N} 用效果的静态数值填。
        ///
        /// ★ 小卡也要上色，而且**必须与大卡同源**：奖励三选一里看到的那张牌，
        ///   加进牌组、抽到手上之后应该长得一样。原来这里是纯文本、CardView 那边是富文本的话，
        ///   同一张牌在两个界面上的信息密度会明显不同。
        ///
        /// ★ 这里是**一次性**生成的（建视图时算一次，之后不再变），
        ///   所以不需要 CardView 那套缓存——装饰器用完就扔。
        /// </summary>
        private static string DescriptionOf(CardDefinition def, GameDatabase db)
        {
            if (def == null) return "";

            var probe = new Game.Cards.CardInstance(0, def);

            var rich = new RichDescription();
            rich.SetCard(probe, db);

            // ★ 没有效果的牌也走 GetDescription：它内部会把模板交给装饰器上色
            //   （诅咒 / 状态牌大多没有效果，但文案里全是「消耗」「虚无」这类词条）。
            return probe.GetDescription(null, null, null, rich);
        }

        private static string CostTextOf(CardDefinition def)
        {
            if (def == null) return "?";
            return def.CostMode switch
            {
                CostMode.X => "X",
                CostMode.Unplayable => "-",
                _ => def.Cost.ToString(),
            };
        }

        private static string FooterOf(CardDefinition def)
        {
            if (def == null) return "";
            string s = TypeLabel(def.Type) + " · " + RarityLabel(def.Rarity);
            if (def.HasKeyword(CardKeyword.Exhaust)) s += " · " + Loc.T("keyword.exhaust.name", "消耗");
            if (def.HasKeyword(CardKeyword.Retain)) s += " · " + Loc.T("keyword.retain.name", "保留");
            if (def.HasKeyword(CardKeyword.Innate)) s += " · " + Loc.T("keyword.innate.name", "固有");
            if (def.HasKeyword(CardKeyword.Ethereal)) s += " · " + Loc.T("keyword.ethereal.name", "虚无");
            return s;
        }

        private static string TypeLabel(CardType t) => t switch
        {
            CardType.Attack => Loc.T("cardtype.attack", "攻击"),
            CardType.Skill => Loc.T("cardtype.skill", "技能"),
            CardType.Power => Loc.T("cardtype.power", "能力"),
            CardType.Status => Loc.T("cardtype.status", "状态"),
            CardType.Curse => Loc.T("cardtype.curse", "诅咒"),
            _ => ""
        };

        private static string RarityLabel(CardRarity r) => r switch
        {
            CardRarity.Basic => Loc.T("rarity.basic", "基础"),
            CardRarity.Common => Loc.T("rarity.common", "普通"),
            CardRarity.Uncommon => Loc.T("rarity.uncommon", "罕见"),
            CardRarity.Rare => Loc.T("rarity.rare", "稀有"),
            _ => Loc.T("rarity.special", "特殊")
        };

        private static Color ColorOf(CardType t) => t switch
        {
            CardType.Attack => new Color(0.50f, 0.19f, 0.19f),
            CardType.Skill => new Color(0.19f, 0.32f, 0.50f),
            CardType.Power => new Color(0.39f, 0.26f, 0.50f),
            _ => new Color(0.24f, 0.24f, 0.24f),
        };
    }
}
