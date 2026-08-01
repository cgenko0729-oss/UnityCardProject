using DG.Tweening;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 打出的牌飞向目标然后消散。
    ///
    /// ★ 为什么值得做：原来打出的牌是**当帧直接消失**的（<c>RefreshHandViews</c> 里 Destroy）。
    ///   于是屏幕上的因果关系断了——敌人闪白、被击退、飘出数字，而「谁干的」从来没被画出来。
    ///   打击反馈做得越足，这个缺口越明显：所有的「果」都在，唯独没有「因」。
    ///
    /// ★ 接管的是**已经离开手牌**的那个 CardView。接管后立刻关掉 <see cref="CardView"/> 组件：
    ///   它的 Update 每帧朝 BattleScreen 写的目标位姿插值，不关的话两边会抢同一个 RectTransform
    ///   （铁律 23：一张牌的位姿只能有一个出口）。
    /// </summary>
    public class CardFlyOut : MonoBehaviour
    {
        // ============================================================ 参数

        private const float FlyTime = 0.34f;
        private const float FadeTime = 0.22f;

        /// <summary>飞到目标时缩到多小。</summary>
        private const float EndScale = 0.32f;

        /// <summary>飞行途中的旋转幅度（度）。给一点，看起来是被甩出去的。</summary>
        private const float SpinDegrees = 28f;

        private RectTransform _rt;
        private RectTransform _layer;

        /// <summary>到了终点是「散掉」还是「烧掉」。见 <see cref="Play"/> 的 burnOnArrive。</summary>
        private bool _burn;

        /// <summary>
        /// 把一张**已经离开手牌**的 CardView 从手牌区摘下来搬到 <paramref name="layer"/> 上，
        /// 关掉它的组件与射线，返回它的 <see cref="CanvasGroup"/>。
        ///
        /// ★ 单独提出来是因为「飞走消散」和「烧成灰」（<see cref="CardBurnOut"/>）
        ///   共用这一段，而其中的坐标系换算是最容易写错的部分：写错的表现只是
        ///   「牌开始飞的那一帧跳了一下」，很容易被当成动画曲线的问题去调，怎么调都不对。
        /// </summary>
        public static CanvasGroup Detach(CardView view, RectTransform layer)
        {
            var rt = (RectTransform)view.transform;

            // ★ 顺序：先让它收掉悬停高光，**再**关组件。反过来的话 Update 已经不跑了，
            //   一张被悬停时打出去的牌会带着那块高光一路飞出去。
            view.ClearHoverFx();

            // ★ 先关组件再动它。CardView.Update 还活着的话，下面设的位置会被它下一帧插值回去。
            view.enabled = false;

            var group = view.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = view.gameObject.AddComponent<CanvasGroup>();

            // 飞行中的牌不该还能点、还能弹提示框
            group.blocksRaycasts = false;
            group.interactable = false;

            // ★ 坐标系搬家：卡牌原本活在 _handArea（pivot 在底边中点）里，
            //   而目标点是 PopupLayer 的坐标。先记住**矩形中心**的世界位置，
            //   换完父节点与锚点再把它放回去——记 rt.position 是不行的，
            //   那是 pivot 的位置，而下一步恰好要改 pivot。
            Vector3 worldCenter = rt.TransformPoint(rt.rect.center);

            rt.SetParent(layer, worldPositionStays: true);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.position = worldCenter;

            return group;
        }

        /// <summary>
        /// 让一张牌飞向 <paramref name="targetAnchored"/>（<paramref name="layer"/> 的本地坐标）。
        /// </summary>
        /// <param name="burnOnArrive">
        /// 到了终点交给 <see cref="CardBurnOut"/> 烧掉，而不是淡出消散。
        /// ★ 打出去的**消耗牌**走这条：它既要飞向目标（那是「谁干的」这句因果，铁律 57），
        ///   又要烧掉（消耗是不可逆的，得让玩家看见它没有进任何牌堆）。两件事是串联的，不是二选一。
        /// </param>
        public static void Play(CardView view, RectTransform layer, Vector2 targetAnchored,
                                bool burnOnArrive = false)
        {
            if (view == null) return;
            if (layer == null) { Destroy(view.gameObject); return; }

            var group = Detach(view, layer);

            var fly = view.gameObject.AddComponent<CardFlyOut>();
            fly._rt = (RectTransform)view.transform;
            fly._layer = layer;
            fly._burn = burnOnArrive;
            fly.Run(group, targetAnchored);
        }

        private void Run(CanvasGroup group, Vector2 targetAnchored)
        {
            float spin = Random.value < 0.5f ? -SpinDegrees : SpinDegrees;

            var seq = DOTween.Sequence().SetTarget(_rt);

            seq.Append(DOTween.To(() => _rt.anchoredPosition, v => _rt.anchoredPosition = v,
                                  targetAnchored, FlyTime).SetEase(Ease.InQuad));

            seq.Join(_rt.DOScale(Vector3.one * EndScale, FlyTime).SetEase(Ease.InQuad));

            // ★ 要烧的牌**不甩**：接下来 CardBurnOut 会把它扶正再点燃，
            //   带着 28° 冲过去只会让那一步看起来像是「先抖了一下」。
            seq.Join(_rt.DOLocalRotate(new Vector3(0f, 0f, _burn ? 0f : spin), FlyTime));

            // 淡出比飞行短，且排在后半段：牌要「到了才散」，一出手就透明看起来像是没打出去
            // ★ 要烧的牌不在这里淡出——它的消失由燃烧那一段负责，两边都淡会淡掉两次。
            if (!_burn)
                seq.Insert(FlyTime - FadeTime,
                           DOTween.To(() => group.alpha, a => group.alpha = a, 0f, FadeTime).SetTarget(_rt));

            seq.OnComplete(OnArrived);
        }

        private void OnArrived()
        {
            if (!_burn) { Destroy(gameObject); return; }

            var rt = _rt;
            var layer = _layer;

            // ★★ 必须先把 _rt 清掉再 Destroy(this)：
            //    Destroy(Component) 要到本帧末才真的执行，那时 OnDisable 会跑
            //    <c>DOTween.Kill(_rt)</c>——而 CardBurnOut 那时已经在同一个 RectTransform 上
            //    挂好了整条燃烧序列，会被连坐杀掉。表现是「牌飞到目标，然后**凭空消失**」，
            //    燃烧一帧都没播，而且不报任何错。
            _rt = null;
            Destroy(this);

            CardBurnOut.PlayOn(rt, layer);
        }

        /// <summary>
        /// ★ 铁律 45。战斗结束时整棵界面树被 Destroy，而这张牌正挂在 PopupLayer 下飞着；
        ///   tween 活在 DOTween 的全局队列里，不收的话它会继续去写一个已经销毁的对象。
        /// </summary>
        private void OnDisable()
        {
            if (_rt != null) DOTween.Kill(_rt);
        }
    }
}
