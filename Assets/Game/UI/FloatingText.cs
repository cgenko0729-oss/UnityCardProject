using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 飘字。纯表现，自己管理生命周期。
    ///
    /// ★ 必须池化，这不是优化而是硬要求：
    ///   本工程的 TMP 字体资产走 <c>AtlasPopulationMode.DynamicOS</c>（为了中文两万多个字，
    ///   见 <see cref="UIFactory.FontAsset"/>），**新建一个文字节点会触发字形光栅化**。
    ///   一次五段攻击 + 荆棘反弹能在同一瞬间要十几个飘字，边打边建会肉眼可见地卡。
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        private TMP_Text _text;
        private RectTransform _rt;
        private float _life;
        private float _maxLife;
        private Vector2 _velocity;

        // ============================================================ 对象池

        /// <summary>
        /// 空闲实例。★ 静态，跨战斗存活——所以里面的元素**可能已经被 Unity 销毁**：
        ///   实例是挂在 <c>BattleScreen.PopupLayer</c> 下的，切界面时整棵界面树会被 Destroy，
        ///   池里留下的就是一批 Unity 意义上为 null 的空壳。取的时候必须一路跳过它们。
        /// </summary>
        private static readonly Stack<FloatingText> Pool = new Stack<FloatingText>(32);

        /// <summary>池子上限。超过就直接销毁，不无限攒。</summary>
        private const int PoolCapacity = 32;

        public static void Spawn(Transform parent, Vector2 anchoredPos, string content, Color color, int size = 34)
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
            rt.sizeDelta = new Vector2(260, 60);
            rt.anchoredPosition = anchoredPos;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            f._maxLife = 0.9f;
            f._life = f._maxLife;
            f._velocity = new Vector2(Random.Range(-18f, 18f), 70f);

            f.gameObject.SetActive(true);
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
            _life -= Time.deltaTime;
            if (_life <= 0f) { Release(); return; }

            _rt.anchoredPosition += _velocity * Time.deltaTime;
            var c = _text.color;
            c.a = Mathf.Clamp01(_life / _maxLife);
            _text.color = c;
        }
    }
}
