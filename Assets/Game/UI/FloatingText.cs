using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>飘字。纯表现，自己管理生命周期。</summary>
    public class FloatingText : MonoBehaviour
    {
        private TMP_Text _text;
        private RectTransform _rt;
        private float _life;
        private float _maxLife;
        private Vector2 _velocity;

        public static void Spawn(Transform parent, Vector2 anchoredPos, string content, Color color, int size = 34)
        {
            var t = UIFactory.CreateText(parent, "Float", content, size);
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(260, 60);
            t.rectTransform.anchoredPosition = anchoredPos;

            var f = t.gameObject.AddComponent<FloatingText>();
            f._text = t;
            f._rt = t.rectTransform;
            f._maxLife = 0.9f;
            f._life = f._maxLife;
            f._velocity = new Vector2(Random.Range(-18f, 18f), 70f);
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) { Destroy(gameObject); return; }

            _rt.anchoredPosition += _velocity * Time.deltaTime;
            var c = _text.color;
            c.a = Mathf.Clamp01(_life / _maxLife);
            _text.color = c;
        }
    }
}
