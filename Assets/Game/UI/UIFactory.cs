using System;
using System.Collections.Generic;
using TMPro;
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
        // ================================================================= 字体

        /// <summary>
        /// 按语言给出系统字体的候选链，取第一个装了的。
        ///
        /// ★ 中日韩共用汉字码位但<b>字形不同</b>（直 / 骨 / 令 在简中、繁中、日文里是三套写法），
        ///   所以不能一份字体走天下——那样日文会渲染成中国字形，母语玩家一眼看得出来。
        /// </summary>
        private static readonly Dictionary<string, string[]> FontCandidates = new Dictionary<string, string[]>
        {
            ["zh-Hans"] = new[] { "Microsoft YaHei", "微软雅黑", "Source Han Sans SC", "Noto Sans SC", "SimHei", "黑体" },
            ["zh-Hant"] = new[] { "Microsoft JhengHei", "微軟正黑體", "Source Han Sans TC", "Noto Sans TC", "PMingLiU" },
            ["ja"] = new[] { "Yu Gothic UI", "Yu Gothic", "游ゴシック", "Meiryo", "メイリオ", "Source Han Sans", "Noto Sans JP", "MS Gothic" },
            ["en"] = new[] { "Segoe UI", "Arial", "Helvetica", "Liberation Sans" },
        };

        /// <summary>候选链全落空时的兜底顺序（覆盖面最广的几个）。</summary>
        private static readonly string[] LastResortFonts =
            { "Microsoft YaHei", "Segoe UI", "Arial", "Arial Unicode MS" };

        /// <summary>
        /// 缺字兜底用的字体族，**按顺序全部挂上**。
        ///
        /// ★ 界面里混着大量<b>非拉丁符号</b>：♥ ◆ ⚔ ☠ ♨ ▣ ▤ ✦ ♒ ▲ ▼ 🛡 以及提示框的 【】。
        ///   缺的字形 TMP 会渲染成 □，并在 Console 刷
        ///   "was not found in the font asset or any potential fallbacks"。
        ///
        /// ★ 顺序不是随手排的，是查过 cmap 的：微软雅黑这一批符号里**只有 ◆**，
        ///   ⚔ ☠ ♨ ▣ ▤ ✦ ♒ 🛡 一个都没有；Segoe UI Symbol 则**全部覆盖**。
        ///   所以它必须排第一。
        ///   刻意<b>不</b>收 Segoe UI Emoji：它能补的字形 Segoe UI Symbol 已经全有，
        ///   而它是 COLR/CPAL 彩色字体，TMP 动态光栅化它只会取轮廓，收益为零、风险不为零。
        ///
        /// ★★ 这里必须是「把整条链都挂上」，不能只取第一个装了的。
        ///    原来的写法是 <c>TryBuild(FallbackFonts)</c>——它**返回第一个命中的**，
        ///    于是简中下兜底字体就是「微软雅黑」，而主字体本来就是微软雅黑：
        ///    等于给一个字体挂了一份自己当兜底，覆盖范围一模一样，一个字都补不了，
        ///    而真正需要的 Segoe UI Symbol **永远不会被构建**。
        ///    表现就是地图上的战斗 / 精英 / 休息 / 宝箱节点、敌人意图的 ⚔ 全是方块，
        ///    而 ♥ ◆ 却好好的——因为后两个雅黑自己就有。
        ///
        /// ★ 这比「把符号也翻译掉」更根本：翻译只能处理已知的那几个，
        ///   而 fallback 对<b>将来任何人加的任何字符</b>都有效。
        /// </summary>
        private static readonly string[] FallbackFonts =
        {
            "Segoe UI Symbol",   // ⚔ ☠ ♨ ▣ ▤ ✦ ♒ 🛡 这一整批全靠它
            "Microsoft YaHei",   // 英文主字体时的中日韩兜底
            "微软雅黑",
            "Arial Unicode MS",
            "Noto Sans SC",
        };

        private static TMP_FontAsset _fontAsset;
        private static string _fontAssetLanguage;
        private static List<TMP_FontAsset> _fallbackAssets;
        private static HashSet<string> _installedFonts;

        /// <summary>
        /// 当前语言用的 TMP 字体资产。
        ///
        /// ★ 用 <c>CreateFontAsset(familyName, styleName)</c> 建，它走 <c>AtlasPopulationMode.DynamicOS</c>：
        ///   字形在用到的那一刻才从系统字体文件光栅化进图集。中文有两万多个常用字，
        ///   预烘一张静态图集要么巨大要么缺字，按需光栅化是唯一现实的做法。
        /// </summary>
        public static TMP_FontAsset FontAsset
        {
            get
            {
                string lang = CurrentFontLanguage;
                if (_fontAsset != null && _fontAssetLanguage == lang) return _fontAsset;

                _fontAsset = BuildFontAsset(lang) ?? TMP_Settings.defaultFontAsset;
                AttachFallback(_fontAsset);
                _fontAssetLanguage = lang;
                return _fontAsset;
            }
        }

        /// <summary>
        /// 字体该按哪个语言选。由 <see cref="GameApp"/> 在语言变化时写入。
        /// ★ 写完必须调 <see cref="InvalidateFont"/>，否则已缓存的字体资产不会重建。
        /// </summary>
        public static string CurrentFontLanguage = Game.Localization.Loc.SourceLanguage;

        /// <summary>
        /// 语言变了要重建字体资产。切语言时由 <see cref="GameApp"/> 调。
        /// ★ 兜底字体<b>不</b>跟着重建：它与语言无关，而且重建一次要重新光栅化整张图集。
        /// </summary>
        public static void InvalidateFont()
        {
            _fontAsset = null;
            _fontAssetLanguage = null;
        }

        /// <summary>
        /// 给主字体挂上整条缺字兜底链。
        ///
        /// ★ 必须防「把自己挂成自己的 fallback」：简中语言下主字体就是微软雅黑，
        ///   自引用会让 TMP 在查不到字形时无限绕圈。
        ///   判据是**字族名**而不是对象引用——同一个字体用 CreateFontAsset 建两次
        ///   会得到两个不同的实例，引用比较看起来「不是同一个」，但覆盖范围完全一样。
        ///   原来那条 <c>_fallbackAsset == primary</c> 正是这样漏掉的。
        /// </summary>
        private static void AttachFallback(TMP_FontAsset primary)
        {
            if (primary == null) return;

            EnsureFallbackAssets();
            if (_fallbackAssets.Count == 0) return;

            if (primary.fallbackFontAssetTable == null)
                primary.fallbackFontAssetTable = new List<TMP_FontAsset>(_fallbackAssets.Count);

            string primaryFamily = FamilyOf(primary);

            for (int i = 0; i < _fallbackAssets.Count; i++)
            {
                var fb = _fallbackAssets[i];
                if (fb == null || fb == primary) continue;
                if (FamilyOf(fb) == primaryFamily) continue;
                if (primary.fallbackFontAssetTable.Contains(fb)) continue;

                primary.fallbackFontAssetTable.Add(fb);
            }
        }

        /// <summary>
        /// 一次性构建全部装了的兜底字体。★ 与语言无关，所以 <see cref="InvalidateFont"/> 不重建它们
        /// ——重建一次要重新光栅化整批图集，而它们的内容不会因为切语言而变化。
        /// </summary>
        private static void EnsureFallbackAssets()
        {
            if (_fallbackAssets != null) return;
            _fallbackAssets = new List<TMP_FontAsset>(FallbackFonts.Length);

            var seenFamilies = new HashSet<string>();

            for (int i = 0; i < FallbackFonts.Length; i++)
            {
                if (!IsInstalled(FallbackFonts[i])) continue;

                var fa = TMP_FontAsset.CreateFontAsset(FallbackFonts[i], "Regular");
                if (fa == null) continue;

                // 「Microsoft YaHei」和「微软雅黑」是同一个字体的两个名字，
                // 建两份只是白占一张图集。按字族名去重。
                if (!seenFamilies.Add(FamilyOf(fa))) continue;

                // 兜底字体自己不再挂兜底，否则会连成一张互相指来指去的网，
                // 查一个不存在的字形要把整张网走一遍。
                fa.fallbackFontAssetTable = new List<TMP_FontAsset>(0);
                _fallbackAssets.Add(fa);
            }
        }

        private static string FamilyOf(TMP_FontAsset fa)
            => fa != null ? fa.faceInfo.familyName : null;

        private static TMP_FontAsset BuildFontAsset(string lang)
        {
            if (_installedFonts == null)
                _installedFonts = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);

            if (FontCandidates.TryGetValue(lang, out var candidates))
            {
                var built = TryBuild(candidates);
                if (built != null) return built;
            }

            return TryBuild(LastResortFonts);
        }

        private static TMP_FontAsset TryBuild(string[] families)
        {
            for (int i = 0; i < families.Length; i++)
            {
                // ★ 先查装没装再建。CreateFontAsset 找不到字体时会 Debug.Log 一条，
                //   直接盲试整条候选链会在控制台刷出一串「找不到字体」的假错误。
                if (!IsInstalled(families[i])) continue;

                var fa = TMP_FontAsset.CreateFontAsset(families[i], "Regular");
                if (fa != null) return fa;
            }
            return null;
        }

        private static bool IsInstalled(string family)
        {
            // ★ 自己保证初始化：兜底链的构建时机不一定在 BuildFontAsset 之后
            if (_installedFonts == null)
                _installedFonts = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);

            if (_installedFonts.Contains(family)) return true;

            // 系统字体名常带样式后缀（"Microsoft YaHei" ↔ "Microsoft YaHei Regular"），做一次前缀匹配
            foreach (var name in _installedFonts)
                if (name.StartsWith(family, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
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

        // ================================================================= 美术

        /// <summary>
        /// 建一个「插画窗」：一块固定比例的窗口，图片**裁切填满**它，多出来的部分被裁掉。
        /// 返回窗口容器；<paramref name="sprite"/> 为 null 时返回 null（调用方据此走无图布局）。
        ///
        /// <para>★ 为什么不用 <c>Image.preserveAspect</c>：那个是 **letterbox（留黑边）**，
        ///   不是 cover。一张 512×768 的立绘塞进 170×96 的横窗，preserveAspect 会把它
        ///   缩成中间一条细竖图、两边大片空白——看起来就像图没挂上。
        ///   要 cover 只能自己算：按宽高比决定「贴宽还是贴高」，另一边溢出，由父节点裁掉。</para>
        ///
        /// <para>★ 裁切用 <see cref="RectMask2D"/> 而不是 <c>Mask</c>：后者要一张 sprite 当模板、
        ///   还会占用模板缓冲；<c>RectMask2D</c> 是纯矩形裁剪，正好是我们要的，而且不需要任何资产。</para>
        /// </summary>
        /// <param name="anchorY">
        /// 图片纵向溢出时保留哪一段。0 = 底部，0.5 = 居中，1 = 顶部。
        /// ★ 立绘默认给 1（保头不保脚）——人物图裁中间往往正好把脸切掉。
        /// </param>
        public static RectTransform CreateArtWindow(Transform parent, string name, Sprite sprite,
                                                    float width, float height, float anchorY = 1f)
        {
            if (sprite == null) return null;

            var window = CreateEmpty(parent, name);
            SetSize(window, width, height);
            window.gameObject.AddComponent<RectMask2D>();

            var img = new GameObject("Art", typeof(RectTransform), typeof(Image));
            img.transform.SetParent(window, false);

            var image = img.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;   // 插画不吃点击，射线要冒泡到卡根上去

            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var size = sprite.rect.size;
            float spriteAspect = size.y > 0f ? size.x / size.y : 1f;
            float windowAspect = height > 0f ? width / height : 1f;

            float w, h;
            if (spriteAspect > windowAspect)
            {
                // 图比窗「更宽」→ 贴高，左右溢出
                h = height;
                w = height * spriteAspect;
            }
            else
            {
                // 图比窗「更高」→ 贴宽，上下溢出
                w = width;
                h = width / spriteAspect;
            }

            rt.sizeDelta = new Vector2(w, h);

            // 纵向溢出多少，按 anchorY 决定往哪边推。横向一律居中——
            // 横向裁掉的通常是背景，纵向裁掉的可能是人物的脸。
            //
            // ★ 符号别写反：「保留顶部」(anchorY = 1) 要把图**往下**推，
            //   让图的上缘与窗的上缘对齐 → y = -overflowY / 2。
            float overflowY = h - height;
            rt.anchoredPosition = new Vector2(0f, overflowY * (0.5f - anchorY));

            return window;
        }

        /// <summary>
        /// 建一个文字节点。
        ///
        /// ★ 参数仍然收 <see cref="TextAnchor"/> 而不是 TMP 自己的 <see cref="TextAlignmentOptions"/>：
        ///   全工程几十个调用点写的都是 <c>TextAnchor.MiddleCenter</c>，
        ///   在这里做一次映射，迁 TMP 时那些调用点一行都不用动。
        /// </summary>
        public static TMP_Text CreateText(Transform parent, string name, string content, int size,
                                          TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = FontAsset;
            t.fontSize = size;
            t.text = content;
            t.alignment = ToTmpAlignment(anchor);
            t.color = color ?? Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// 让文字在框放不下时自动缩字号。
        ///
        /// ★ 本地化必备：中文换英文后文本平均膨胀 1.6–2 倍，而本工程的卡面、按钮尺寸
        ///   全是程序化写死的。给定宽区域开这个开关，比逐个语言手调字号现实得多。
        /// </summary>
        public static void EnableAutoSize(TMP_Text t, float min, float max)
        {
            if (t == null) return;
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
            t.overflowMode = TextOverflowModes.Truncate;
        }

        /// <summary>用 uGUI 的 <see cref="TextAnchor"/> 设置对齐，省得调用点去背 TMP 的枚举名。</summary>
        public static void SetAlignment(TMP_Text t, TextAnchor anchor)
        {
            if (t != null) t.alignment = ToTmpAlignment(anchor);
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
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

        /// <summary>取按钮上的文字节点，用于改文字 / 改颜色。</summary>
        public static TMP_Text LabelOf(Button btn)
            => btn != null ? btn.GetComponentInChildren<TMP_Text>() : null;

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
