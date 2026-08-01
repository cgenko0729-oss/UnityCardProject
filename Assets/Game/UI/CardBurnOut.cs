using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 被**消耗**的牌烧成灰。
    ///
    /// ★ 为什么消耗不能和弃牌共用 <see cref="CardFlyOut"/>：
    ///   弃牌是可逆的——洗牌之后它还会回来；消耗是不可逆的，这张牌**本局再也不会出现**。
    ///   两者在规则上的份量差着一个数量级，而原来在画面上是**同一段动画**：
    ///   都朝屏幕左下角飞过去缩小淡出，只是终点差了一个按钮的高度。
    ///   于是「我刚刚永久失去了一张牌」这件事，玩家只能靠事后去点消耗堆按钮才知道。
    ///
    /// ★ 表现由三样东西合成，缺一样就不像烧：
    ///   ① **扶正 + 举起**：先把牌摆正、鼓大一点点。这是动画的 anticipation——
    ///      没有这一下，牌会在扇形的斜角上突然开始烧，读起来像是渲染出错了。
    ///   ② **炽色层**：盖在卡面上的一层橙红，先烧透再随牌一起消失。
    ///   ③ **灰烬**（<see cref="AshBurst"/>）：真正让人读出「烧掉了」的是它。
    ///      灰烬**不挂在牌身上**——牌淡完就销毁了，而灰烬要继续飘一会儿才散。
    ///
    /// ★ 刻意**不**飞向消耗堆按钮：飞过去等于告诉玩家「它进了那一堆」，
    ///   而消耗的语义恰恰是「哪一堆都没进」。按钮那边仍然会闪一下
    ///   （<c>BattleScreen.FlashPileButton</c>），「它去哪了」这个问题由那一闪回答，
    ///   不需要让牌本人跑一趟。
    /// </summary>
    public class CardBurnOut : MonoBehaviour
    {
        // ============================================================ 参数

        /// <summary>扶正 + 举起的时长。</summary>
        private const float LiftTime = 0.15f;

        /// <summary>燃烧本身的时长。</summary>
        private const float BurnTime = 0.46f;

        /// <summary>举起阶段鼓到多大（相对进来时的缩放）。</summary>
        private const float LiftScale = 1.07f;

        /// <summary>烧完时缩到多小。</summary>
        private const float EndScale = 0.84f;

        /// <summary>燃烧过程中整张牌往上飘多少像素。</summary>
        private const float RiseDistance = 92f;

        /// <summary>炽色层的颜色。</summary>
        private static readonly Color EmberColor = new Color(1f, 0.42f, 0.12f);

        private RectTransform _rt;

        /// <summary>
        /// 让一张**已经离开手牌**的 CardView 原地烧掉。
        /// </summary>
        public static void Play(CardView view, RectTransform layer)
        {
            if (view == null) return;
            if (layer == null) { Destroy(view.gameObject); return; }

            // ★ 搬家与关射线的那一整套收在 CardFlyOut.Detach 里，两条离场路径共用一份。
            var group = CardFlyOut.Detach(view, layer);
            PlayOn((RectTransform)view.transform, layer, group);
        }

        /// <summary>
        /// 烧掉一个**已经搬到 <paramref name="layer"/> 上**的卡面矩形。
        /// 给 <see cref="CardFlyOut"/> 的 burnOnArrive 接力用：那时牌已经飞到目标点了。
        /// </summary>
        public static void PlayOn(RectTransform rt, RectTransform layer, CanvasGroup group = null)
        {
            if (rt == null) return;
            if (layer == null) { Destroy(rt.gameObject); return; }

            if (group == null)
            {
                group = rt.GetComponent<CanvasGroup>();
                if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            var burn = rt.gameObject.AddComponent<CardBurnOut>();
            burn._rt = rt;
            burn.Run(group, layer);
        }

        private void Run(CanvasGroup group, RectTransform layer)
        {
            // ---- 炽色层。铺满卡面，压在最上面（连费用球一起烧）。
            //      ★ raycastTarget 必须关：这张牌已经不在手里了，但它还是屏幕上一块实心图形，
            //        开着的话玩家点在它上面会什么都点不到，而且完全看不出原因。
            var ember = UIFactory.CreatePanel(_rt, "Ember", new Color(EmberColor.r, EmberColor.g, EmberColor.b, 0f));
            UIFactory.Stretch(ember);
            ember.SetAsLastSibling();
            var emberImg = ember.GetComponent<Image>();
            emberImg.raycastTarget = false;

            Vector3 startScale = _rt.localScale;
            Vector2 startPos = _rt.anchoredPosition;

            // ★ tween 的 target 一律挂在**自己的 gameObject** 上，不用 _rt。
            //   CardFlyOut 接力过来的那条路上，那个 RectTransform 上刚死过一个
            //   会 <c>DOTween.Kill(_rt)</c> 的组件（见 CardFlyOut.OnArrived 的注释）。
            var seq = DOTween.Sequence().SetTarget(gameObject);

            // ---- ① 扶正 + 举起
            //
            // ★ 连 DOScale / DOLocalRotate 也逐条 SetTarget(gameObject)：它们的默认 target 是
            //   那个 RectTransform，而从 CardFlyOut 接力过来时，同一个 RectTransform 上
            //   刚死过一个会 Kill 它的组件。全部挂到 gameObject 上，Kill 的边界才是干净的。
            seq.Append(_rt.DOLocalRotate(Vector3.zero, LiftTime).SetTarget(gameObject));
            seq.Join(_rt.DOScale(startScale * LiftScale, LiftTime).SetEase(Ease.OutQuad).SetTarget(gameObject));
            seq.Join(DOTween.To(() => emberImg.color, c => emberImg.color = c,
                                Alpha(EmberColor, 0.5f), LiftTime).SetTarget(gameObject));

            // ---- ② 烧：往上飘、缩小，炽色先烧透再随牌一起没
            seq.Append(DOTween.To(() => _rt.anchoredPosition, v => _rt.anchoredPosition = v,
                                  startPos + new Vector2(0f, RiseDistance), BurnTime)
                              .SetEase(Ease.OutQuad).SetTarget(gameObject));

            seq.Join(_rt.DOScale(startScale * EndScale, BurnTime).SetEase(Ease.InQuad).SetTarget(gameObject));

            seq.Join(DOTween.To(() => emberImg.color, c => emberImg.color = c,
                                Alpha(EmberColor, 0.95f), BurnTime * 0.35f).SetTarget(gameObject));

            // 整张牌淡出。★ 排在燃烧的后 3/4：一开始就淡的话「烧红」这一步根本看不见。
            seq.Insert(LiftTime + BurnTime * 0.25f,
                       DOTween.To(() => group.alpha, a => group.alpha = a, 0f, BurnTime * 0.75f)
                              .SetEase(Ease.InQuad).SetTarget(gameObject));

            // ---- ③ 灰烬。在「开始烧」那一刻放，不是烧完才放——烧完时牌已经淡没了，
            //      灰烬从一片空白里冒出来会像是另一个特效。
            var rect = _rt.rect;
            seq.InsertCallback(LiftTime, () =>
                AshBurst.Play(layer, _rt.anchoredPosition,
                              new Vector2(rect.width * startScale.x, rect.height * startScale.y)));

            seq.OnComplete(() => Destroy(gameObject));
        }

        private static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>
        /// ★ 铁律 45。战斗结束时整棵界面树被 Destroy，而这张牌正挂在 PopupLayer 下烧着；
        ///   tween 活在 DOTween 的全局队列里，不收的话它会继续去写一个已经销毁的对象。
        /// </summary>
        private void OnDisable() => DOTween.Kill(gameObject);
    }

    /// <summary>
    /// 一小撮往上飘散的灰烬。<see cref="CardBurnOut"/> 用它，但**不持有它**。
    ///
    /// ★ 为什么是独立的一棵树、而不是烧掉的那张牌的子节点：
    ///   牌在燃烧结束时就被销毁了，而灰烬要比它多飘 0.3 秒左右才散完。
    ///   挂在牌底下的话，灰烬会连同父节点一起在半空中被整批抹掉——
    ///   看起来像是特效被打断了。
    ///
    /// ★ 每一片都是一个纯色小方块，没有任何贴图。这一层的可信度来自
    ///   **数量 + 各自不同的速度/横漂/转速**，而不是单片长得像不像灰。
    /// </summary>
    public class AshBurst : MonoBehaviour
    {
        private const int Count = 14;

        /// <summary>灰烬片的边长范围（像素）。</summary>
        private const float MinSize = 4f, MaxSize = 11f;

        /// <summary>上升距离范围。</summary>
        private const float MinRise = 70f, MaxRise = 210f;

        /// <summary>横向漂移的最大幅度（相对卡宽的一半）。</summary>
        private const float DriftRatio = 0.55f;

        private const float MinLife = 0.5f, MaxLife = 0.95f;

        /// <summary>灰烬的取色范围：从亮橙的火星到烧透了的暗灰。</summary>
        private static readonly Color HotColor = new Color(1f, 0.62f, 0.22f);
        private static readonly Color ColdColor = new Color(0.34f, 0.31f, 0.30f);

        /// <summary>整撮灰烬散完之后自毁的时刻。</summary>
        private float _dieAt;

        /// <param name="center">卡面中心在 <paramref name="layer"/> 里的坐标。</param>
        /// <param name="size">卡面的实际显示尺寸（已乘过缩放），决定灰烬从多大范围里冒出来。</param>
        public static void Play(RectTransform layer, Vector2 center, Vector2 size)
        {
            if (layer == null) return;

            var root = UIFactory.CreateEmpty(layer, "Ashes");
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = center;

            var burst = root.gameObject.AddComponent<AshBurst>();
            burst.Run(size);
        }

        private void Run(Vector2 size)
        {
            float longest = 0f;

            for (int i = 0; i < Count; i++)
            {
                // 起点在卡面矩形里随机撒。偏下半张：火从底下烧起来
                var from = new Vector2(
                    Random.Range(-0.5f, 0.5f) * size.x,
                    Random.Range(-0.5f, 0.15f) * size.y);

                float life = Random.Range(MinLife, MaxLife);
                float side = Random.Range(-DriftRatio, DriftRatio) * size.x * 0.5f;
                var to = from + new Vector2(side, Random.Range(MinRise, MaxRise));

                float s = Random.Range(MinSize, MaxSize);
                var color = Color.Lerp(HotColor, ColdColor, Random.value);

                var flake = UIFactory.CreatePanel(transform, "Ash", color);
                flake.anchorMin = flake.anchorMax = new Vector2(0.5f, 0.5f);
                flake.pivot = new Vector2(0.5f, 0.5f);
                UIFactory.SetSize(flake, s, s);
                flake.anchoredPosition = from;
                flake.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var img = flake.GetComponent<Image>();
                img.raycastTarget = false;

                // ★ 全部 SetTarget(gameObject)：OnDisable 一句 Kill 就能把十几片一起收干净。
                DOTween.To(() => flake.anchoredPosition, v => flake.anchoredPosition = v, to, life)
                       .SetEase(Ease.OutQuad).SetTarget(gameObject);

                flake.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-220f, 220f)), life)
                     .SetRelative(true).SetTarget(gameObject);

                // ★ 用 DOTween.To 手写颜色，不用 Image.DOFade：那个扩展在 DOTweenModuleUI.cs 里，
                //   而 Game.UI 是独立 asmdef，引用不到（铁律 60）。
                DOTween.To(() => img.color, c => img.color = c,
                           new Color(color.r, color.g, color.b, 0f), life)
                       .SetEase(Ease.InQuad).SetTarget(gameObject);

                if (life > longest) longest = life;
            }

            _dieAt = Time.unscaledTime + longest + 0.05f;
        }

        // ★ 自毁用计时而不是最长那条 tween 的 OnComplete：
        //   十几条 tween 里挑「哪条最后完成」要么维护一个计数器，要么赌 DOTween 的完成顺序，
        //   而这个节点除了当容器之外没有任何职责，早一帧晚一帧都无所谓。
        private void Update()
        {
            if (Time.unscaledTime >= _dieAt) Destroy(gameObject);
        }

        /// <summary>★ 铁律 45：界面被销毁时把还在飘的这些收掉。</summary>
        private void OnDisable() => DOTween.Kill(gameObject);
    }
}
