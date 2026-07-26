using System.Collections.Generic;
using System.Text;
using Game.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 全局唯一的悬停提示面板。
    ///
    /// ★ 自建一个 sortingOrder 很高的独立 Canvas，而不是挂进某个界面的层级：
    ///   tooltip 要能盖住战斗界面、局外界面、选牌模态框，甚至单场战斗调试场景
    ///   （那里根本没有 GameApp）。挂进别人的层级就得为每个宿主重新回答一次
    ///   「该插在哪一层之后」，而这个问题每加一个界面就会再错一次。
    ///
    /// ★ Canvas 上的 GraphicRaycaster 被关掉，面板里所有 Graphic 都不收射线：
    ///   提示框弹出来的位置紧挨着玩家正要点的东西，只要它能吃射线就一定会挡到点击。
    /// </summary>
    public class TooltipView : MonoBehaviour
    {
        /// <summary>悬停多久才弹。★ 没有它的话，光标扫过整排手牌会一路疯狂闪。</summary>
        private const float ShowDelay = 0.25f;

        private const float PanelWidth = 430f;

        /// <summary>面板与目标之间的间隙。</summary>
        private const float Gap = 16f;

        /// <summary>面板与屏幕边缘的最小距离。</summary>
        private const float ScreenMargin = 12f;

        private const int TooltipSortingOrder = 5000;

        private static TooltipView _instance;
        private static bool _quitting;

        /// <summary>临时压制。拖拽出牌时打开——正举着一张牌找目标时不该有提示框跳出来碍事。</summary>
        public static bool Suppressed { get; set; }

        private Canvas _canvas;
        private RectTransform _panel;

        private readonly List<TMP_Text> _lines = new List<TMP_Text>();
        private readonly List<TooltipEntry> _buffer = new List<TooltipEntry>();
        private readonly StringBuilder _sb = new StringBuilder(256);
        private readonly Vector3[] _corners = new Vector3[4];

        private object _owner;
        private ITooltipSource _source;
        private RectTransform _anchor;
        private float _showAt;
        private bool _visible;

        // ============================================================ 静态入口

        /// <summary>请求显示。<paramref name="anchor"/> 是提示要贴着的那个节点。</summary>
        public static void Request(object owner, ITooltipSource source, RectTransform anchor)
        {
            if (owner == null || source == null || anchor == null) return;

            var view = Instance;
            if (view == null) return;

            view._owner = owner;
            view._source = source;
            view._anchor = anchor;
            view._showAt = Time.unscaledTime + ShowDelay;
            view.HidePanel();
        }

        /// <summary>
        /// 取消显示。只有当前的持有者才取消得掉——
        /// 光标从 A 移到 B 时，`Enter(B)` 可能先于 `Exit(A)` 到达，
        /// 不做这个判断的话 A 的 Exit 会把 B 刚请求好的提示顺手关掉。
        /// </summary>
        public static void Cancel(object owner)
        {
            // ★ 用 _instance 而不是 Instance：取消时不该顺手把面板创建出来
            var view = _instance;
            if (view == null || !ReferenceEquals(view._owner, owner)) return;

            view._owner = null;
            view._source = null;
            view._anchor = null;
            view.HidePanel();
        }

        private static TooltipView Instance
        {
            get
            {
                if (_instance != null) return _instance;
                if (_quitting) return null;

                var go = new GameObject("TooltipLayer");
                _instance = go.AddComponent<TooltipView>();
                _instance.Build();
                return _instance;
            }
        }

        private void OnApplicationQuit() => _quitting = true;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ============================================================ 构建

        private void Build()
        {
            _canvas = UIFactory.CreateCanvas("TooltipCanvas");
            _canvas.transform.SetParent(transform, false);
            _canvas.sortingOrder = TooltipSortingOrder;

            var raycaster = _canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            var canvasRect = (RectTransform)_canvas.transform;

            _panel = UIFactory.CreatePanel(canvasRect, "TooltipPanel", new Color(0.05f, 0.06f, 0.09f, 0.96f));
            _panel.GetComponent<Image>().raycastTarget = false;

            // 锚点钉在左下角、pivot 在左上角：于是 anchoredPosition 就是
            // 「面板左上角距屏幕左下角的距离（画布单位）」，摆放时不用再换算参考系。
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0f, 1f);
            _panel.sizeDelta = new Vector2(PanelWidth, 120f);

            var layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 14);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // 宽度固定、高度随内容长
            var fitter = _panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _panel.gameObject.SetActive(false);
        }

        // ============================================================ 每帧

        private void Update()
        {
            if (Suppressed || _source == null || _anchor == null)
            {
                if (_visible) HidePanel();
                return;
            }

            if (!_visible && Time.unscaledTime < _showAt) return;

            _buffer.Clear();
            if (!_source.BuildTooltip(_buffer) || _buffer.Count == 0)
            {
                // 这个目标没有可说的。清掉 _source 免得每帧白算一遍，
                // 但保留 _owner，这样它的 Exit 仍然对得上号。
                _source = null;
                HidePanel();
                return;
            }

            Render(_buffer);

            if (!_visible)
            {
                _panel.gameObject.SetActive(true);
                _visible = true;
            }

            // 先把布局算实，才量得到真实高度，才摆得准
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            Place();
        }

        private void HidePanel()
        {
            if (!_visible) return;
            _visible = false;
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        private void Render(List<TooltipEntry> entries)
        {
            EnsureLines(entries.Count);

            for (int i = 0; i < _lines.Count; i++)
            {
                bool used = i < entries.Count;
                if (_lines[i].gameObject.activeSelf != used) _lines[i].gameObject.SetActive(used);
                if (!used) continue;

                var e = entries[i];
                _sb.Clear();
                // ★ 标题的装饰符号本身也是文案：中文用「【】」，英文里那对括号既是
                //   中日文标点、又要额外的字体覆盖，所以英文表里把它翻成不带括号。
                //   富文本标签留在外面拼——它是格式不是文案，不该交给译者。
                _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(e.Accent)).Append("><b>")
                   .Append(Loc.T("tooltip.title_format", "【{0}】", e.Title))
                   .Append("</b></color>");
                if (!string.IsNullOrEmpty(e.Body)) _sb.Append('\n').Append(e.Body);

                string text = _sb.ToString();
                // 文本没变就不要写回去：给 Text.text 赋值会无条件把网格标脏并重排版
                if (_lines[i].text != text) _lines[i].text = text;
            }
        }

        private void EnsureLines(int count)
        {
            while (_lines.Count < count)
            {
                var t = UIFactory.CreateText(_panel, "Line" + _lines.Count, "", 19, TextAnchor.UpperLeft);
                _lines.Add(t);
            }
        }

        private void Place()
        {
            float scale = _canvas.scaleFactor <= 0f ? 1f : _canvas.scaleFactor;
            Vector2 size = _panel.rect.size * scale;      // 面板在屏幕上的实际像素尺寸

            // 目标在屏幕上的包围盒。工程里所有 Canvas 都是 ScreenSpaceOverlay，
            // 因此世界坐标直接就是屏幕像素——这也是 BattleScreen.AnchoredPosOf 一直依赖的前提。
            //
            // ★ 取四个角的 min/max 而不是直接用 corners[0] / corners[2]：
            //   扇形手牌是带旋转的，旋转之后「左下角」未必还是 x 最小的那个点。
            _anchor.GetWorldCorners(_corners);
            float left = _corners[0].x, right = _corners[0].x, top = _corners[0].y;
            for (int i = 1; i < 4; i++)
            {
                if (_corners[i].x < left) left = _corners[i].x;
                if (_corners[i].x > right) right = _corners[i].x;
                if (_corners[i].y > top) top = _corners[i].y;
            }

            // 优先放右边；右边放不下就翻到左边
            float x = right + Gap;
            if (x + size.x > Screen.width - ScreenMargin) x = left - Gap - size.x;
            x = Mathf.Clamp(x, ScreenMargin, Mathf.Max(ScreenMargin, Screen.width - ScreenMargin - size.x));

            // y 是面板的**顶边**（pivot 在左上角）。与目标顶边对齐，然后压回屏幕内。
            float maxTop = Screen.height - ScreenMargin;
            float minTop = Mathf.Min(size.y + ScreenMargin, maxTop);
            float y = Mathf.Clamp(top, minTop, maxTop);

            _panel.anchoredPosition = new Vector2(x, y) / scale;
        }
    }
}
