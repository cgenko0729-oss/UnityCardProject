using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 顿帧与慢放。全局唯一，**刻意不挂在战斗界面上**。
    ///
    /// ★ 这是本工程最危险的一个开关，理由值得写清楚：
    ///   场景——致命一击杀死最后一个敌人 → 进入慢放 → 战斗结束 → 玩家点「继续」
    ///   → <c>BattleHostScreen</c> 连同 <c>BattleScreen</c> 一起被 Destroy。
    ///   如果慢放的倒计时挂在战斗界面上，此刻 <c>Time.timeScale</c> 还是 0.25，
    ///   而唯一会把它调回来的那个组件已经死了——
    ///   **整个游戏永久变成 0.25 倍速，并且不报任何错**。
    ///   这和铁律 31（TooltipView.Suppressed 谁开谁负责放开）是同一类坑，但后果更大。
    ///
    ///   所以持有者必须活得比任何界面都久（DontDestroyOnLoad），
    ///   倒计时必须走 <see cref="Time.unscaledDeltaTime"/>（否则 timeScale 为 0 时它自己也停了，
    ///   那就真的再也回不来），并且有一个硬上限兜底。
    ///
    /// ★ 为什么慢放走 <c>Time.timeScale</c> 而不是只放慢表现层：
    ///   本工程的动效全是 UI 层按 <c>Time.deltaTime</c> 驱动的，timeScale 一改全都跟着慢，
    ///   一行就有「整个世界慢下来」的分量。唯一的例外是 <see cref="CardView"/>——
    ///   它用的是 <c>unscaledDeltaTime</c>，所以手牌的悬停与拖拽**不会**变粘。
    ///   这个组合是刻意的，不是巧合：慢放是给「看清那一击」用的，
    ///   玩家此刻的鼠标操作不该跟着变迟钝。改 CardView 的插值时钟前请先想清楚这条。
    /// </summary>
    public class TimeFeedback : MonoBehaviour
    {
        // ============================================================ 参数

        /// <summary>任何情况下 timeScale 被压住的最长时间（真实秒）。纯保险丝。</summary>
        private const float MaxHoldSeconds = 2f;

        // ============================================================ 单例

        private static TimeFeedback _instance;

        public static TimeFeedback Instance
        {
            get
            {
                if (_instance != null) return _instance;

                // 名字前缀 ~ 只是为了在 Hierarchy 里排到最后。刻意不加 HideFlags——
                // 这东西一旦出问题，能在 Hierarchy 里看见它是排查的第一步。
                var go = new GameObject("~TimeFeedback");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<TimeFeedback>();
                return _instance;
            }
        }

        /// <summary>
        /// 复原，但**不会**因为这次调用而把单例创建出来。
        /// 战斗界面在 OnDisable 里调它：界面都没了就没有理由继续慢放。
        /// </summary>
        public static void RestoreIfActive()
        {
            if (_instance != null) _instance.Restore();
        }

        // ============================================================ 状态

        /// <summary>顿帧剩余时间（真实秒）。大于 0 时 timeScale 恒为 0。</summary>
        private float _holdLeft;

        /// <summary>慢放剩余时间（真实秒）。</summary>
        private float _slowLeft;

        /// <summary>慢放期间的 timeScale。</summary>
        private float _slowScale = 1f;

        public bool IsActive => _holdLeft > 0f || _slowLeft > 0f;

        // ============================================================ 对外

        /// <summary>
        /// 命中停顿：把时间彻底冻住若干真实秒。
        /// ★ 顿帧比慢放更「重」，格斗游戏里绝大多数打击感来自它而不是慢放。
        /// </summary>
        public void HitStop(float seconds)
        {
            if (!FeedbackSettings.SlowMoEnabled) return;
            _holdLeft = Mathf.Max(_holdLeft, Mathf.Clamp(seconds, 0f, MaxHoldSeconds));
            Apply();
        }

        /// <summary>慢放。<paramref name="scale"/> 是 timeScale，<paramref name="seconds"/> 是真实秒。</summary>
        public void SlowMotion(float scale, float seconds)
        {
            if (!FeedbackSettings.SlowMoEnabled) return;
            _slowScale = Mathf.Clamp(scale, 0.05f, 1f);
            _slowLeft = Mathf.Max(_slowLeft, Mathf.Clamp(seconds, 0f, MaxHoldSeconds));
            Apply();
        }

        /// <summary>立刻恢复正常流速。</summary>
        public void Restore()
        {
            _holdLeft = 0f;
            _slowLeft = 0f;
            _slowScale = 1f;
            Time.timeScale = 1f;
        }

        // ============================================================ 生命周期

        private void Update()
        {
            if (!IsActive) return;

            // ★ 必须是 unscaled：顿帧时 Time.deltaTime 恒为 0，
            //   用它做倒计时的话这一帧永远过不去，游戏当场死锁。
            float dt = Time.unscaledDeltaTime;

            if (_holdLeft > 0f) _holdLeft -= dt;
            if (_slowLeft > 0f) _slowLeft -= dt;

            Apply();
        }

        private void Apply()
        {
            if (_holdLeft > 0f) { Time.timeScale = 0f; return; }
            if (_slowLeft > 0f) { Time.timeScale = _slowScale; return; }

            _slowScale = 1f;
            Time.timeScale = 1f;
        }

        // 退出 Play 模式、对象被销毁、组件被关掉——三条路都要把 timeScale 还回去
        private void OnDisable() => Restore();

        private void OnDestroy()
        {
            Restore();
            if (_instance == this) _instance = null;
        }
    }
}
