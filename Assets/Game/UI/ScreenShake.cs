using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 屏幕震动。挂在战斗界面的「战场层」上，只震敌人区与玩家区。
    ///
    /// ★ 为什么不震整个战斗界面：
    ///   ① 手牌区的拖拽判定要把屏幕坐标换算成 <c>_handArea</c> 的本地坐标
    ///      （推导写在 <see cref="BattleScreen"/> 的 HandWidth / pivot 注释里），
    ///      手牌区一动，出牌线与举牌位置的判定就全部带着抖动的偏移；
    ///   ② 手牌、能量球、日志这些 HUD 抖起来非常晕，而且它们不是「被打中的东西」。
    ///   震战场、留 HUD，是同类游戏的通行做法，也顺便让上面那个坐标问题不存在。
    ///
    /// ★ 位移用柏林噪声而不是 <c>Random.Range</c>：
    ///   随机数每帧跳到无关的新值，看起来是「抽搐」；
    ///   柏林噪声是连续的，看起来才是「震动」。这是两者唯一但决定性的区别。
    ///
    /// ★ 用 <c>Time.deltaTime</c>（不是 unscaled）：致命一击顿帧时震动要一起冻住，
    ///   否则画面停了而战场还在抖，会非常怪。
    /// </summary>
    public class ScreenShake : MonoBehaviour
    {
        // ============================================================ 参数（改手感只改这里）

        /// <summary>噪声采样速度。越大抖得越碎。</summary>
        private const float Frequency = 24f;

        /// <summary>两条轴用同一张噪声图的不同区域采样，避免 x/y 同步移动变成一条斜线。</summary>
        private const float AxisSeparation = 31.7f;

        private RectTransform _rt;

        /// <summary>不震时该待的位置。★ 在 Awake 里存一次，之后只认它——绝不用「当前位置」当基准。</summary>
        private Vector2 _home;

        private float _left;
        private float _total;
        private float _amplitude;
        private float _seed;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _home = _rt.anchoredPosition;
            _seed = Random.Range(0f, 1000f);
        }

        /// <summary>
        /// 震一下。同时来第二下时取较大的振幅并重置时长，**不叠加**——
        /// 多段攻击每一下都叠的话，第五下会把屏幕甩出去。
        /// </summary>
        public void Shake(float amplitude, float duration)
        {
            amplitude *= FeedbackSettings.ShakeScale;
            if (amplitude <= 0.01f || duration <= 0f) return;

            _amplitude = _left > 0f ? Mathf.Max(_amplitude, amplitude) : amplitude;
            _total = duration;
            _left = duration;
        }

        public void StopNow()
        {
            _left = 0f;
            _amplitude = 0f;
            if (_rt != null) _rt.anchoredPosition = _home;
        }

        // ★ LateUpdate：BattleScreen 也在 LateUpdate 里刷界面，但它不碰战场层的位置，
        //   两者互不干扰。放在这里是为了确保任何布局重建都已经跑完。
        private void LateUpdate()
        {
            if (_left <= 0f) return;

            _left -= Time.deltaTime;
            if (_left <= 0f)
            {
                StopNow();
                return;
            }

            // 二次衰减：起手够狠，收尾干净。线性衰减会拖出一条软绵绵的尾巴。
            float damp = _left / _total;
            damp *= damp;

            float t = (_total - _left) * Frequency;
            float x = (Mathf.PerlinNoise(_seed, t) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(_seed + AxisSeparation, t) - 0.5f) * 2f;

            _rt.anchoredPosition = _home + new Vector2(x, y) * (_amplitude * damp);
        }

        private void OnDisable() => StopNow();
    }
}
