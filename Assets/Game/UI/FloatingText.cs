using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>飘字的运动形态。决定弹出幅度、抛物线和存活时长。</summary>
    public enum FloatKind
    {
        /// <summary>护甲、治疗、提示这类。平缓上飘，不抢戏。</summary>
        Info,

        /// <summary>伤害数字。弹出 + 抛物线，有重量。</summary>
        Damage,

        /// <summary>致命一击。更大、更慢、带一点旋转。</summary>
        Lethal,
    }

    /// <summary>
    /// 飘字。纯表现，自己管理生命周期。
    ///
    /// ★ 必须池化，这不是优化而是硬要求：
    ///   本工程的 TMP 字体资产走 <c>AtlasPopulationMode.DynamicOS</c>（为了中文两万多个字，
    ///   见 <see cref="UIFactory.FontAsset"/>），**新建一个文字节点会触发字形光栅化**。
    ///   一次五段攻击 + 荆棘反弹能在同一瞬间要十几个飘字，边打边建会肉眼可见地卡。
    ///
    /// ★ 运动是手写的，没有用 DOTween（本层其余动效都用了）。理由：
    ///   ① 抛物线 + 弹出 + 淡出是三条不同步的曲线作用在同一个对象上，
    ///      写成一个 Update 比编排三条 tween 短得多也直观得多；
    ///   ② 更要紧的是——**池化对象 + tween 是个已知的坑**：
    ///      实例被回收再租出去时，上一轮的 tween 可能还活着，
    ///      它会继续往这个已经属于别人的节点上写位置和透明度。
    ///      不引入 tween，这个失效模式根本不存在。
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        // ============================================================ 参数（改手感只改这里）

        private const float LifeInfo = 0.90f;
        private const float LifeDamage = 0.85f;
        private const float LifeLethal = 1.30f;

        /// <summary>伤害飘字的初速与重力。抛物线让数字有重量，纯匀速上飘像气泡。</summary>
        private const float DamageRiseSpeed = 300f;
        private const float DamageGravity = -900f;

        private const float LethalRiseSpeed = 210f;
        private const float LethalGravity = -420f;

        /// <summary>提示类：匀速上飘，保持原来的观感。</summary>
        private const float InfoRiseSpeed = 70f;

        /// <summary>弹出阶段占生命的比例。</summary>
        private const float PopPhase = 0.22f;

        /// <summary>弹出的过冲量。0.3 = 最大冲到 1.3 倍再落回 1。</summary>
        private const float PopOvershootDamage = 0.32f;
        private const float PopOvershootLethal = 0.55f;
        private const float PopOvershootInfo = 0.12f;

        /// <summary>生命的最后这段比例里才开始淡出。★ 数字是信息，要先让人读清再消失。</summary>
        private const float FadeTail = 0.35f;

        // ============================================================ 状态

        private TMP_Text _text;
        private RectTransform _rt;

        private float _life;
        private float _maxLife;
        private Vector2 _velocity;
        private float _gravity;
        private float _overshoot;
        private float _spin;

        // ============================================================ 对象池

        /// <summary>
        /// 空闲实例。★ 静态，跨战斗存活——所以里面的元素**可能已经被 Unity 销毁**：
        ///   实例是挂在 <c>BattleScreen.PopupLayer</c> 下的，切界面时整棵界面树会被 Destroy，
        ///   池里留下的就是一批 Unity 意义上为 null 的空壳。取的时候必须一路跳过它们。
        /// </summary>
        private static readonly Stack<FloatingText> Pool = new Stack<FloatingText>(32);

        /// <summary>池子上限。超过就直接销毁，不无限攒。</summary>
        private const int PoolCapacity = 32;

        public static void Spawn(Transform parent, Vector2 anchoredPos, string content, Color color,
                                 int size = 34, FloatKind kind = FloatKind.Info)
        {
            var f = Rent(parent);

            var t = f._text;
            // ★ 每次都重设字体：池是静态的，而字体资产会在切语言时被 UIFactory 换掉
            //   （见 UIFactory.InvalidateFont）。不重设的话，切完语言之后飘字仍用旧字体，
            //   表现是英文界面里飘字缺字变方块，而且只有飘字这一处不对。
            t.font = UIFactory.FontAsset;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = FontStyles.Bold;

            var rt = f._rt;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300, 70);
            rt.anchoredPosition = anchoredPos;
            rt.localRotation = Quaternion.identity;

            f.Configure(kind);
            f.gameObject.SetActive(true);

            // 生成的当帧就摆到起始位姿，否则会有一帧是上一次留下的大小
            f.ApplyPose();
        }

        private void Configure(FloatKind kind)
        {
            // 横向初速给一点随机，配合 UnitView 的槽位轮转，同时出现的数字不会完全平行
            float drift = Random.Range(-26f, 26f);

            switch (kind)
            {
                case FloatKind.Damage:
                    _maxLife = LifeDamage;
                    _velocity = new Vector2(drift, DamageRiseSpeed);
                    _gravity = DamageGravity;
                    _overshoot = PopOvershootDamage;
                    _spin = 0f;
                    break;

                case FloatKind.Lethal:
                    _maxLife = LifeLethal;
                    _velocity = new Vector2(drift * 0.5f, LethalRiseSpeed);
                    _gravity = LethalGravity;
                    _overshoot = PopOvershootLethal;
                    _spin = Random.value < 0.5f ? -26f : 26f;
                    break;

                default:
                    _maxLife = LifeInfo;
                    _velocity = new Vector2(Random.Range(-18f, 18f), InfoRiseSpeed);
                    _gravity = 0f;
                    _overshoot = PopOvershootInfo;
                    _spin = 0f;
                    break;
            }

            _life = _maxLife;
        }

        private static FloatingText Rent(Transform parent)
        {
            while (Pool.Count > 0)
            {
                var pooled = Pool.Pop();
                if (pooled == null) continue;   // 上一场战斗的界面连它一起销毁了

                pooled.transform.SetParent(parent, false);
                return pooled;
            }

            var t = UIFactory.CreateText(parent, "Float", "", 34);
            var f = t.gameObject.AddComponent<FloatingText>();
            f._text = t;
            f._rt = t.rectTransform;
            return f;
        }

        private void Release()
        {
            gameObject.SetActive(false);

            if (Pool.Count >= PoolCapacity) { Destroy(gameObject); return; }
            Pool.Push(this);
        }

        // ============================================================ 播放

        private void Update()
        {
            // ★ Time.deltaTime（不是 unscaled）：致命一击顿帧 / 慢放时飘字要跟着一起慢下来。
            //   用 unscaled 的话，世界停住了而数字照常往上飞，慢放就白做了。
            float dt = Time.deltaTime;

            _life -= dt;
            if (_life <= 0f) { Release(); return; }

            _velocity.y += _gravity * dt;
            _rt.anchoredPosition += _velocity * dt;

            ApplyPose();
        }

        private void ApplyPose()
        {
            float age01 = Mathf.Clamp01((_maxLife - _life) / _maxLife);

            _rt.localScale = Vector3.one * PopScale(age01, _overshoot);

            if (_spin != 0f)
                _rt.localRotation = Quaternion.Euler(0f, 0f, _spin * age01);

            var c = _text.color;
            // 只在最后一段淡出：数字是信息，全程线性淡出会让它一出生就已经开始变灰
            c.a = age01 < 1f - FadeTail ? 1f : Mathf.Clamp01((1f - age01) / FadeTail);
            _text.color = c;
        }

        /// <summary>
        /// 弹出曲线：从 0.55 冲到 1+overshoot，再落回 1。
        /// ★ 从小冲到大再回落，比直接淡入更能抓住眼睛——
        ///   一屏同时有五个数字时，「刚出现的那个」必须一眼可辨。
        /// </summary>
        private static float PopScale(float age01, float overshoot)
        {
            if (age01 >= PopPhase) return 1f;

            float u = age01 / PopPhase;
            float peak = 1f + overshoot;

            // 前 45% 冲到峰值，后 55% 回落。两段都用 SmoothStep，接缝处不会有折角。
            return u < 0.45f
                ? Mathf.Lerp(0.55f, peak, Mathf.SmoothStep(0f, 1f, u / 0.45f))
                : Mathf.Lerp(peak, 1f, Mathf.SmoothStep(0f, 1f, (u - 0.45f) / 0.55f));
        }
    }
}
