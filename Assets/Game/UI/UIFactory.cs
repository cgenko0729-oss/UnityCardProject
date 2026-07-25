using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 程序化建 uGUI 的小工具。整个战斗界面在运行时用代码搭出来，不依赖任何 prefab，
    /// 这样 AI 或新人改 UI 只需要改代码，不需要在编辑器里连线。
    /// </summary>
    public static class UIFactory
    {
        private static Font _font;

        /// <summary>取一个能显示中文的字体。优先系统中文字体，最后退回内置字体。</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                string[] candidates = { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "Arial Unicode MS", "Segoe UI" };
                for (int i = 0; i < candidates.Length; i++)
                {
                    var f = UnityEngine.Font.CreateDynamicFontFromOSFont(candidates[i], 16);
                    if (f != null) { _font = f; return _font; }
                }

                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static RectTransform CreateEmpty(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Text CreateText(Transform parent, string name, string content, int size,
                                      TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.text = content;
            t.alignment = anchor;
            t.color = color ?? Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button CreateButton(Transform parent, string name, string label, int fontSize, Color bg)
        {
            var rt = CreatePanel(parent, name, bg);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Image>();

            var txt = CreateText(rt, "Label", label, fontSize);
            Stretch(txt.rectTransform);
            return btn;
        }

        /// <summary>把 rect 拉伸填满父节点。</summary>
        public static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        public static void SetAnchored(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
                                       Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        public static void SetSize(RectTransform rt, float w, float h)
        {
            rt.sizeDelta = new Vector2(w, h);
        }

        // ================================================================= 阶段 4 新增

        /// <summary>带回调的按钮。局外界面全是按钮，每次都写三行 AddListener 太啰嗦。</summary>
        public static Button CreateTextButton(Transform parent, string name, string label, int fontSize,
                                              Color bg, UnityEngine.Events.UnityAction onClick)
        {
            var btn = CreateButton(parent, name, label, fontSize, bg);
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>取按钮上的 Text，用于改文字 / 改颜色。</summary>
        public static Text LabelOf(Button btn)
            => btn != null ? btn.GetComponentInChildren<Text>() : null;

        /// <summary>
        /// 设置按钮可用性。★ 除了 interactable 还要改颜色——uGUI 默认的禁用色很不明显，
        /// 玩家会以为按钮坏了而不是「条件不满足」。
        /// </summary>
        public static void SetInteractable(Button btn, bool on, Color enabledColor)
        {
            if (btn == null) return;
            btn.interactable = on;
            var img = btn.targetGraphic as Image;
            if (img != null) img.color = on ? enabledColor : new Color(0.20f, 0.20f, 0.22f);
            var label = LabelOf(btn);
            if (label != null) label.color = on ? Color.white : new Color(0.55f, 0.55f, 0.55f);
        }

        /// <summary>
        /// 建一个垂直滚动列表，返回内容容器。往返回值里塞子节点即可，高度会自动增长。
        /// </summary>
        public static RectTransform CreateScrollView(Transform parent, string name, float spacing = 8f,
                                                     RectOffset padding = null)
        {
            var root = CreateEmpty(parent, name);
            var mask = root.gameObject.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.001f);   // 需要一张图才能裁剪
            root.gameObject.AddComponent<RectMask2D>();

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var content = CreateEmpty(root, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0, 0);
            content.offsetMax = new Vector2(0, 0);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = padding ?? new RectOffset(8, 8, 8, 8);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = root;
            return content;
        }

        /// <summary>给一个节点加固定高度，配合 VerticalLayoutGroup 使用。</summary>
        public static LayoutElement SetLayoutHeight(RectTransform rt, float height)
        {
            var le = rt.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return le;
        }

        /// <summary>水平排列容器。用于遗物条、商品行。</summary>
        public static RectTransform CreateHorizontalGroup(Transform parent, string name, float spacing = 8f,
                                                          TextAnchor align = TextAnchor.MiddleLeft)
        {
            var rt = CreateEmpty(parent, name);
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = align;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>半透明遮罩面板，铺满父节点。弹窗类界面用。</summary>
        public static RectTransform CreateOverlay(Transform parent, string name, float alpha = 0.82f)
        {
            var rt = CreatePanel(parent, name, new Color(0.03f, 0.03f, 0.05f, alpha));
            Stretch(rt);
            return rt;
        }
    }
}
