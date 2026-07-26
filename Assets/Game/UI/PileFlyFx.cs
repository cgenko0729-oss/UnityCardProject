using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 一叠牌从一个牌堆飞到另一个牌堆。目前只有洗牌用它（弃牌堆 → 抽牌堆）。
    ///
    /// ★ 为什么值得做：洗牌原本只有日志里一行「洗牌」两个字。
    ///   而洗牌是玩家**最需要察觉**的时刻之一——它意味着「这一轮牌组过完了，
    ///   我打掉的牌马上会再回来」。一行会被后面十几条伤害刷掉的日志承担不了这个信息。
    ///
    /// ★ 飞的是几块纯色小方块，不是真的 CardInstance：
    ///   洗牌洗的可能是 30 张牌，为一次 0.35 秒的过场建 30 个完整卡面（每个都要查描述、
    ///   建六七个子节点、可能还带插画）是纯粹的浪费，而玩家在那个尺寸下什么也读不到。
    ///   固定 <see cref="BlockCount"/> 块就够表达「一叠东西过去了」。
    ///
    /// ★ 形状照抄 <see cref="CardFlyOut"/>：MonoBehaviour + 静态 Play。
    ///   每个方块要自己持有 tween，才能在界面被销毁时于 OnDisable 里收掉。
    /// </summary>
    public class PileFlyFx : MonoBehaviour
    {
        // ============================================================ 参数

        /// <summary>飞几块。再多就成一团糊，看不出是「一叠牌」。</summary>
        private const int BlockCount = 5;

        /// <summary>小方块的尺寸。按卡面 170×240 的比例缩到手指大小。</summary>
        private const float BlockWidth = 40f;
        private const float BlockHeight = 56f;

        private const float FlyTime = 0.30f;

        /// <summary>相邻两块的出发间隔。★ 有它才像「一叠」，同时出发就是一块大色斑。</summary>
        private const float Stagger = 0.035f;

        /// <summary>抛物线的抬升高度。牌堆按钮都贴着屏幕底边，不抬起来的话轨迹是一条看不出弧度的横线。</summary>
        private const float ArcHeight = 150f;

        /// <summary>翻滚角度的上限。飞行途中转小半圈，一叠牌被甩过去的感觉全在这里。</summary>
        private const float MaxSpin = 160f;

        /// <summary>卡背色。与 <see cref="CardView"/> 的任何一种类型色都不同——它不该被误读成某类牌。</summary>
        private static readonly Color BackColor = new Color(0.30f, 0.34f, 0.46f, 0.95f);

        private RectTransform _rt;

        /// <summary>
        /// 从 <paramref name="from"/> 飞到 <paramref name="to"/>（都是 <paramref name="layer"/> 的本地坐标）。
        /// </summary>
        public static void Play(RectTransform layer, Vector2 from, Vector2 to)
        {
            if (layer == null) return;

            for (int i = 0; i < BlockCount; i++)
                SpawnBlock(layer, from, to, i * Stagger);
        }

        private static void SpawnBlock(RectTransform layer, Vector2 from, Vector2 to, float delay)
        {
            var rt = UIFactory.CreatePanel(layer, "ShuffleBlock", BackColor);
            UIFactory.SetSize(rt, BlockWidth, BlockHeight);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = from;

            // 这些方块横穿屏幕，绝不能吃掉点击——洗牌时玩家可能正要点别的东西
            var img = rt.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;

            var fly = rt.gameObject.AddComponent<PileFlyFx>();
            fly._rt = rt;
            fly.Run(img, from, to, delay);
        }

        private void Run(Image img, Vector2 from, Vector2 to, float delay)
        {
            // 控制点抬到两端中点的上方 → 二次贝塞尔，看起来是「抛」过去的
            Vector2 ctrl = (from + to) * 0.5f + new Vector2(0f, ArcHeight);

            var seq = DOTween.Sequence().SetTarget(_rt);
            seq.AppendInterval(delay);

            seq.Append(DOVirtual.Float(0f, 1f, FlyTime, t =>
            {
                if (_rt == null) return;
                float u = 1f - t;
                _rt.anchoredPosition = u * u * from + 2f * u * t * ctrl + t * t * to;
            }).SetEase(Ease.InOutQuad).SetTarget(_rt));

            seq.Join(_rt.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-MaxSpin, MaxSpin)), FlyTime));

            // 到了才散：一出发就淡出会看起来像是根本没飞出去。
            //
            // ★ 用 DOTween.To 而不是 Image.DOFade：后者在 DOTweenModuleUI.cs 里，
            //   那些模块脚本没有 asmdef、编进 Assembly-CSharp，而 Game.UI 是 asmdef 程序集，
            //   引用不到它们。核心 DLL 里的 DOTween.To 才够得着（同 UnitView.PlayHit）。
            if (img != null)
            {
                var clear = new Color(BackColor.r, BackColor.g, BackColor.b, 0f);
                seq.Insert(delay + FlyTime * 0.6f,
                           DOTween.To(() => img.color, c => img.color = c, clear, FlyTime * 0.4f)
                                  .SetTarget(_rt));
            }

            seq.OnComplete(() => Destroy(gameObject));
        }

        /// <summary>
        /// ★ 同 <see cref="CardFlyOut"/> 的 OnDisable：战斗界面随时可能被整棵销毁（打完点「继续」），
        ///   而这些方块正挂在 PopupLayer 下飞着。tween 活在 DOTween 的全局队列里，
        ///   不收的话它会继续去写一个已经销毁的对象。
        /// </summary>
        private void OnDisable()
        {
            if (_rt != null) DOTween.Kill(_rt);
        }
    }
}
