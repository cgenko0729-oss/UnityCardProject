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

        /// <summary>
        /// 让一张牌飞向 <paramref name="targetAnchored"/>（<paramref name="layer"/> 的本地坐标）。
        /// </summary>
        public static void Play(CardView view, RectTransform layer, Vector2 targetAnchored)
        {
            if (view == null) return;

            var rt = (RectTransform)view.transform;
            if (layer == null) { Destroy(view.gameObject); return; }

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

            var fly = view.gameObject.AddComponent<CardFlyOut>();
            fly._rt = rt;
            fly.Run(group, targetAnchored);
        }

        private void Run(CanvasGroup group, Vector2 targetAnchored)
        {
            float spin = Random.value < 0.5f ? -SpinDegrees : SpinDegrees;

            var seq = DOTween.Sequence().SetTarget(_rt);

            seq.Append(DOTween.To(() => _rt.anchoredPosition, v => _rt.anchoredPosition = v,
                                  targetAnchored, FlyTime).SetEase(Ease.InQuad));

            seq.Join(_rt.DOScale(Vector3.one * EndScale, FlyTime).SetEase(Ease.InQuad));
            seq.Join(_rt.DOLocalRotate(new Vector3(0f, 0f, spin), FlyTime));

            // 淡出比飞行短，且排在后半段：牌要「到了才散」，一出手就透明看起来像是没打出去
            seq.Insert(FlyTime - FadeTime,
                       DOTween.To(() => group.alpha, a => group.alpha = a, 0f, FadeTime).SetTarget(_rt));

            seq.OnComplete(() => Destroy(gameObject));
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
