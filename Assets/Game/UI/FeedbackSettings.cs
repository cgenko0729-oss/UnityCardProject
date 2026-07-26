using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 打击反馈的总开关。震屏 / 闪白 / 慢放 / 播放倍速全部先问这里一句。
    ///
    /// ★ 为什么第一天就要有它，而不是等做完特效再回来加：
    ///   ① 屏幕震动与闪光对晕动症、光敏感人群是硬伤，没有关闭选项在 Steam 上会直接吃差评；
    ///      而做完十几个特效再回头去每一个入口插判断，成本是现在的十倍。
    ///   ② 「动画速度 2×/4×」是这类游戏的标配，玩家二周目之后不会想再看完整演出。
    ///      它必须是**播放节奏**的一个乘数，而不是每个特效各自缩短时长。
    ///
    /// ★ 暂时没有设置界面，值走 PlayerPrefs（与 <see cref="GameApp.LanguagePrefKey"/> 同一套路）。
    ///   将来做统一设置菜单时，界面只要读写这几个属性即可，特效那边一行都不用改。
    ///   阶段 5 做 MetaSave 时，这几个键与语言一起迁进去。
    /// </summary>
    public static class FeedbackSettings
    {
        private const string KeyShake = "feedback.shake";
        private const string KeyFlash = "feedback.flash";
        private const string KeySlowMo = "feedback.slowmo";
        private const string KeySpeed = "feedback.speed";

        /// <summary>播放倍速的合法档位。界面按这个顺序轮换。</summary>
        public static readonly float[] SpeedSteps = { 1f, 2f, 4f };

        private static bool _loaded;

        private static float _shakeScale = 1f;
        private static bool _flashEnabled = true;
        private static bool _slowMoEnabled = true;
        private static float _speedMultiplier = 1f;

        /// <summary>震屏强度倍率，0 = 完全关闭，1 = 设计值。</summary>
        public static float ShakeScale
        {
            get { EnsureLoaded(); return _shakeScale; }
            set { EnsureLoaded(); _shakeScale = Mathf.Clamp01(value); Save(); }
        }

        /// <summary>受击闪白 / 全屏闪光。关掉之后受击仍有位移与飘字，只是不闪。</summary>
        public static bool FlashEnabled
        {
            get { EnsureLoaded(); return _flashEnabled; }
            set { EnsureLoaded(); _flashEnabled = value; Save(); }
        }

        /// <summary>致命一击的顿帧与慢放。</summary>
        public static bool SlowMoEnabled
        {
            get { EnsureLoaded(); return _slowMoEnabled; }
            set { EnsureLoaded(); _slowMoEnabled = value; Save(); }
        }

        /// <summary>表现播放倍速（1 / 2 / 4）。<see cref="BattlePresenter"/> 拿它当事件推进的乘数。</summary>
        public static float SpeedMultiplier
        {
            get { EnsureLoaded(); return _speedMultiplier; }
            set { EnsureLoaded(); _speedMultiplier = Mathf.Clamp(value, 1f, 8f); Save(); }
        }

        /// <summary>在 1× → 2× → 4× → 1× 之间轮换。给以后的战斗界面倍速按钮用。</summary>
        public static float CycleSpeed()
        {
            EnsureLoaded();
            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                if (!Mathf.Approximately(SpeedSteps[i], _speedMultiplier)) continue;
                SpeedMultiplier = SpeedSteps[(i + 1) % SpeedSteps.Length];
                return _speedMultiplier;
            }
            SpeedMultiplier = SpeedSteps[0];
            return _speedMultiplier;
        }

        /// <summary>
        /// 一次性关掉所有"会动"的东西。无障碍设置里的「减少动态效果」按这个来。
        /// ★ 不关飘字与血条——那是信息，不是特效。
        /// </summary>
        public static void ReduceMotion()
        {
            EnsureLoaded();
            _shakeScale = 0f;
            _flashEnabled = false;
            _slowMoEnabled = false;
            Save();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _shakeScale = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyShake, 1f));
            _flashEnabled = PlayerPrefs.GetInt(KeyFlash, 1) != 0;
            _slowMoEnabled = PlayerPrefs.GetInt(KeySlowMo, 1) != 0;
            _speedMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(KeySpeed, 1f), 1f, 8f);
        }

        private static void Save()
        {
            PlayerPrefs.SetFloat(KeyShake, _shakeScale);
            PlayerPrefs.SetInt(KeyFlash, _flashEnabled ? 1 : 0);
            PlayerPrefs.SetInt(KeySlowMo, _slowMoEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(KeySpeed, _speedMultiplier);
            PlayerPrefs.Save();
        }
    }
}
