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

        /// <summary>
        /// 所有 Canvas 共用的那台相机。<c>null</c> = 走 Overlay（没有相机）。
        ///
        /// ★★ 全工程凡是 <c>RectTransformUtility.*</c> 需要 camera 参数的地方，
        ///    **一律传这个属性，不许再传字面量 null**。
        ///
        ///    这不是风格问题。那一族 API 的 camera 参数语义是「这个 Canvas 挂在哪台相机上」：
        ///    Overlay 模式下正确答案就是 null，Camera 模式下必须是那台相机。
        ///    传错的表现不是报错，而是**算出一个偏掉的坐标**——
        ///    拖拽的牌会离光标越来越远、目标箭头指不到敌人、飘字飘到屏幕外。
        ///    改管线之前工程里有 11 处写死的 null，全部收口到这里之后，
        ///    「一键退回 Overlay」（<see cref="UIRenderSetup.Enabled"/>）也跟着白送了。
        /// </summary>
        public static Camera CanvasCamera => UIRenderSetup.Camera;

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();

            var cam = CanvasCamera;
            if (cam != null)
            {
                // ★ ScreenSpaceCamera：Canvas 变成相机渲染流程里的透明几何体，
                //   排在 post-processing pass 之前 —— 后处理这才碰得到界面。见 UIRenderSetup。
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;

                // ★★ 三个 Canvas 必须用**同一个** planeDistance。
                //    同一台相机下，Canvas 之间的先后仍然由 sortingOrder 决定
                //    （Tooltip 的 5000 照样压住一切），但那只在**距离相同**时成立：
                //    距离不同的话它们变成两个深度不同的透明面，排序会改由距离接管，
                //    sortingOrder 就静默失效了。
                canvas.planeDistance = CanvasPlaneDistance;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            // ★★ 开齐 RoundedPanel 要用的顶点通道。**这一行漏了，圆角面板会静默变回直角矩形**：
            //    Canvas 默认只带 TexCoord1，没开的通道到了顶点着色器里全是 0，
            //    于是圆角半径 0、描边 0、投影 0——画面上什么都不缺，只是什么都没有，且不报错。
            //    RoundedPanel.OnEnable 里还有一道兜底，但那是给「手搓的 Canvas」用的；
            //    本工程所有 Canvas 都从这里出生，正解是在出生地开好。
            canvas.additionalShaderChannels |=
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.TexCoord3 |
                AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>
        /// 所有 Canvas 离相机的距离。★ 必须落在相机的 near/far 之间
        /// （见 <see cref="UIRenderSetup"/> 里那两个值），否则 Canvas 会被整个裁掉——
        /// 表现是「改完管线之后一片全黑，什么都不显示」。
        /// </summary>
        private const float CanvasPlaneDistance = 100f;

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// 圆角面板。与 <see cref="CreatePanel"/> 同构，只是把 <c>Image</c> 换成
        /// <see cref="RoundedPanel"/>，于是白得到圆角 / 描边 / 内发光 / 外投影 / 渐变底。
        ///
        /// ★★ 刻意**不**把 <see cref="CreatePanel"/> 整个换成这个：
        ///    那个方法同时还在建全屏遮罩、受创闪光层、地图连线、灰烬碎片、悬停判定垫——
        ///    这些东西要么本来就该是直角，要么根本不该有投影（连线加投影会变成一团糊）。
        ///    「哪里该圆」是一个逐处判断的问题，不是一个可以全局开关的问题。
        ///
        /// ★ 样式请从 <see cref="RoundedStyle"/> 的预设里挑，不要就地填数字。理由见那边的类注释。
        /// </summary>
        public static RoundedPanel CreateRoundedPanel(Transform parent, string name, Color color,
                                                      RoundedStyle style = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RoundedPanel));
            go.transform.SetParent(parent, false);

            var panel = go.GetComponent<RoundedPanel>();
            panel.color = color;
            panel.Apply(style ?? RoundedStyle.Panel);
            return panel;
        }

        public static RectTransform CreateEmpty(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // ================================================================= 圆形

        private static Sprite _circleSprite;

        /// <summary>
        /// 一张程序化烘出来的白色圆形 sprite（外缘一像素抗锯齿）。
        ///
        /// ★ 全工程不引入任何图片资产（整套 UI 都是代码搭的），要圆形只能自己烘。
        ///   烘成**纯白**，颜色一律交给 <c>Image.color</c> 去乘——
        ///   于是费用球、关键字色点、以及将来任何圆形元素共用这一张 64×64 的贴图。
        ///
        /// ★ 每次都判空重建，不能只在字段为 null 时建一次：
        ///   编辑器退出播放模式会销毁运行时生成的 Texture2D，而静态字段留下的是
        ///   一个「假 null」的 Unity 对象引用（<c>== null</c> 为真但引用还在）。
        ///   下一次进播放模式必须重新烘，否则整屏圆形元素变成不可见的白框。
        /// </summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (_circleSprite != null) return _circleSprite;

                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                float r = size * 0.5f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - r;
                        float dy = y + 0.5f - r;

                        // 「离圆边还有几个像素」：≥1 全不透明，≤0 全透明，
                        // 中间那一圈线性过渡就是抗锯齿——不做的话小尺寸的色点会是个锯齿八边形。
                        float edge = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(edge * 255f));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                _circleSprite.hideFlags = HideFlags.HideAndDontSave;
                return _circleSprite;
            }
        }

        /// <summary>圆形面板。与 <see cref="CreatePanel"/> 同构，只是贴了 <see cref="CircleSprite"/>。</summary>
        public static RectTransform CreateCircle(Transform parent, string name, Color color)
        {
            var rt = CreatePanel(parent, name, color);
            var img = rt.GetComponent<Image>();
            img.sprite = CircleSprite;
            img.type = Image.Type.Simple;
            return rt;
        }

        // ================================================================= 径向高光

        private static Sprite _glowSprite;

        /// <summary>
        /// 一张中心亮、向外平滑收敛到全透明的白色圆斑。卡牌悬停时的「光泽」用它。
        ///
        /// ★ 与 <see cref="CircleSprite"/> 的区别是**没有边**：圆形那张是「实心圆 + 一像素抗锯齿」，
        ///   拿它当高光会在光斑外圈画出一道清晰的圆边，看起来是贴了张白纸而不是照到了光。
        ///
        /// ★ alpha 用 <c>t²·(3−2t)</c>（smoothstep）而不是线性衰减：
        ///   线性衰减在半径处的一阶导数不为零，眼睛能看出那一圈「亮度突然不再降了」的硬边。
        ///
        /// ★ 「假 null」重建的理由与 <see cref="CircleSprite"/> 完全一样，
        ///   不能改成只在字段为 null 时建一次，见那边的注释。
        /// </summary>
        public static Sprite RadialGlowSprite
        {
            get
            {
                if (_glowSprite != null) return _glowSprite;

                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                float r = size * 0.5f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - r;
                        float dy = y + 0.5f - r;

                        // 1 = 正中心，0 = 半径之外
                        float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / r);
                        float a = t * t * (3f - 2f * t);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                _glowSprite.hideFlags = HideFlags.HideAndDontSave;
                return _glowSprite;
            }
        }

        // ================================================================= 卡背

        private static Sprite _cardBackSprite;

        /// <summary>
        /// 内置卡背。**灰度**图案，实际颜色一律交给 <c>Image.color</c> 去乘。
        ///
        /// ★★ 为什么要程序化烘、而不是「等美术给一张」：
        ///    卡背出现在两个地方——洗牌时飞过屏幕的那一叠（<see cref="PileFlyFx"/>）、
        ///    以及牌堆按钮上那一小摞（<c>BattleScreen.PileStack</c>）。
        ///    在这之前它们都是**纯色方块**，而纯色方块与「一叠牌」之间差的不是精度，
        ///    是「有没有图案」这个二元的东西：只要有边框和一个中心纹样，
        ///    哪怕 22×30 像素，眼睛也会立刻把它读成卡片。
        ///    没有它就得一直等美术，而这是全项目**最不需要美术**的一张图。
        ///
        /// ★ 用灰度而不是 alpha 来表达图案：alpha 会让卡背半透明，
        ///   背后的按钮底色 / 战场会透出来，一叠牌看起来像一叠玻璃。
        ///   灰度乘上 <c>Image.color</c> 得到的是「暗底 + 亮边框 + 中亮菱形」，全程不透明。
        ///
        /// ★ 可以被 <c>GameDatabase.CardBack</c> 整个换掉（见 <see cref="CardBackOr"/>）——
        ///   这一张是「没配也能看」的兜底，不是终点。
        ///
        /// ★ 「假 null」重建的理由与 <see cref="CircleSprite"/> 完全一样，见那边。
        /// </summary>
        public static Sprite CardBackSprite
        {
            get
            {
                if (_cardBackSprite != null) return _cardBackSprite;

                // 96×144 = 2:3，与卡面 230×346 基本同比。飞行块和小图标都是从这里缩下去的。
                const int w = 96, h = 144;

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                // 三个灰阶。差值刻意拉得比较开：这张图会被缩到 22×30，
                // 对比度不够的话缩完就糊成一块纯色，等于白画。
                const float fill = 0.46f;    // 内部底
                const float edge = 1.00f;    // 外框亮线
                const float motif = 0.80f;   // 中心菱形

                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        // 离最近一条边有多远（像素）
                        int d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));

                        float g;
                        if (d < 3) g = fill;                 // 最外圈留一点底色，框才不会贴着切边
                        else if (d < 7) g = edge;            // 框
                        else
                        {
                            // 中心菱形：|dx|/a + |dy|/b <= 1
                            float dx = Mathf.Abs(x + 0.5f - w * 0.5f) / (w * 0.30f);
                            float dy = Mathf.Abs(y + 0.5f - h * 0.5f) / (h * 0.30f);
                            float t = dx + dy;

                            // 1 → 菱形内，0 → 外。留一段过渡当抗锯齿，
                            // 否则缩小之后菱形的斜边会碎成台阶。
                            float k = Mathf.Clamp01((1.08f - t) / 0.16f);
                            g = Mathf.Lerp(fill, motif, k);
                        }

                        byte v = (byte)(Mathf.Clamp01(g) * 255f);
                        pixels[y * w + x] = new Color32(v, v, v, 255);
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _cardBackSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
                _cardBackSprite.hideFlags = HideFlags.HideAndDontSave;
                return _cardBackSprite;
            }
        }

        /// <summary>
        /// 配了就用配的，没配用内置的。
        /// ★ 收成一个方法，是因为「卡背从哪来」这件事有两个调用点
        ///   （洗牌动画、牌堆按钮），各写一次 null 判断迟早会漏一处，
        ///   表现是「洗牌用的是新卡背，牌堆图标还是旧的」。
        /// </summary>
        public static Sprite CardBackOr(Sprite configured)
            => configured != null ? configured : CardBackSprite;

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

            FitCover(rt, sprite, width, height, anchorY);
            return window;
        }

        /// <summary>
        /// 改一个已经建好的插画窗的尺寸，并重新裁切。
        ///
        /// ★ 必须有这个方法、不能只改窗口的 sizeDelta：cover 裁切是把**图片**缩放到
        ///   刚好盖住窗口，图片的尺寸是建窗那一刻按窗口比例算出来的。
        ///   只改窗口而不重算图片，窗变大就会露出背景、变小则裁得比预期多。
        ///
        /// ★ 存在的理由是「运行中拖着调排版」（见 <c>CardView.ApplyLayout</c>）：
        ///   没有它，每调一次卡框的缩进都得退出 Play 再进。
        /// </summary>
        public static void ResizeArtWindow(RectTransform window, float width, float height, float anchorY = 1f)
        {
            if (window == null) return;
            SetSize(window, width, height);

            // 窗自己没有 Image（只挂 RectMask2D），所以这一句拿到的必然是里面那张图
            var image = window.GetComponentInChildren<Image>();
            if (image == null || image.sprite == null) return;

            FitCover((RectTransform)image.transform, image.sprite, width, height, anchorY);
        }

        /// <summary>
        /// 把图片缩放到「刚好盖住」窗口（cover），溢出的那一边由 <paramref name="anchorY"/> 决定留哪段。
        /// 见 <see cref="CreateArtWindow"/> 类注释里关于「为什么不用 preserveAspect」的说明。
        /// </summary>
        private static void FitCover(RectTransform rt, Sprite sprite, float width, float height, float anchorY)
        {
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

        /// <summary>
        /// 所有按钮的出生地。★ 这里换成圆角是整次改动里**覆盖面最大的一处**——
        /// 主菜单 / 休息 / 事件 / 奖励 / 商店 / 结束回合 / 药水栏 全都从这条路出来。
        ///
        /// ★ <c>targetGraphic</c> 仍然拿得到：<see cref="RoundedPanel"/> 是 <c>Image</c> 的子类，
        ///   Button 的 ColorTint 过渡照旧改 <c>Graphic.color</c>，而那个 setter 会标脏顶点，
        ///   底色因此会连同渐变一起重算——按下去的变暗效果不但没丢，还比原来立体。
        /// </summary>
        public static Button CreateButton(Transform parent, string name, string label, int fontSize, Color bg)
        {
            var panel = CreateRoundedPanel(parent, name, bg, RoundedStyle.Button);
            var btn = panel.gameObject.AddComponent<Button>();
            btn.targetGraphic = panel;

            var txt = CreateText(panel.rectTransform, "Label", label, fontSize);
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
