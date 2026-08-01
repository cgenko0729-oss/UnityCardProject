using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 一套圆角面板的参数。
    ///
    /// ★★ 它存在的理由不是「少打几个字」，而是**让界面有统一的语汇**。
    ///    圆角 / 投影这类东西一旦让每个调用点各填各的，三天之内界面上就会出现
    ///    六种不同的圆角半径和四种不同深浅的投影——那比全是直角还难看，
    ///    因为不统一会被读成「没做完」，而统一的直角至少读起来像一种风格。
    ///    所以下面这几个 <c>static readonly</c> 就是本工程圆角语汇的**全集**，
    ///    要加新的请先问「已有的六个哪个不够用」。
    ///
    /// ★ 半径的梯度是刻意拉开的（4 / 6 / 10 / 12 / 16）：相邻两级差 2px 眼睛分不出来，
    ///   只会得到「好像有点不整齐」的印象。
    ///
    /// ★ 投影颜色不用纯黑而是带一点冷色（0.01, 0.01, 0.03）：
    ///   背景本来就是 0.08/0.09/0.11 的暗色，纯黑投影压在上面几乎看不见，
    ///   而偏冷一点点会让它读起来像「暗处」而不是「一块脏」。
    /// </summary>
    public sealed class RoundedStyle
    {
        public float Radius = 12f;
        public float Border;
        public Color BorderColor = Color.white;
        public float GlowWidth;
        public Color GlowColor = Color.clear;
        public float ShadowSize;
        public float ShadowOffsetY = -3f;
        public Color ShadowColor = new Color(0.01f, 0.01f, 0.03f, 0.55f);
        public float Gradient;

        /// <summary>卡牌底板。不描边——卡框有自己的贴图，再加一道边会打架。</summary>
        public static readonly RoundedStyle Card = new RoundedStyle
        {
            Radius = 10f,
            Gradient = 0.10f,
            ShadowSize = 10f,
            ShadowOffsetY = -4f,
        };

        /// <summary>弹窗、日志、结算面板这类大块。描边极淡，只是把边缘从背景里「拎」出来。</summary>
        public static readonly RoundedStyle Panel = new RoundedStyle
        {
            Radius = 16f,
            Border = 1.5f,
            BorderColor = new Color(1f, 1f, 1f, 0.10f),
            Gradient = 0.07f,
            ShadowSize = 18f,
            ShadowOffsetY = -5f,
            ShadowColor = new Color(0.01f, 0.01f, 0.03f, 0.50f),
        };

        /// <summary>按钮。渐变最重的一个——「可以按」这件事靠的就是它看起来有厚度。</summary>
        public static readonly RoundedStyle Button = new RoundedStyle
        {
            Radius = 10f,
            Border = 1f,
            BorderColor = new Color(1f, 1f, 1f, 0.14f),
            Gradient = 0.16f,
            ShadowSize = 8f,
            ShadowOffsetY = -3f,
            ShadowColor = new Color(0.01f, 0.01f, 0.03f, 0.45f),
        };

        /// <summary>提示框。描边最亮、投影最重——它必须明确地「浮」在所有东西之上。</summary>
        public static readonly RoundedStyle Tooltip = new RoundedStyle
        {
            Radius = 12f,
            Border = 1.5f,
            BorderColor = new Color(0.62f, 0.70f, 0.88f, 0.30f),
            Gradient = 0.06f,
            ShadowSize = 20f,
            ShadowOffsetY = -6f,
            ShadowColor = new Color(0.01f, 0.01f, 0.03f, 0.62f),
        };

        /// <summary>小方块：迷你卡、标签、费用底。投影收得很小，否则一排小东西会糊成一片。</summary>
        public static readonly RoundedStyle Chip = new RoundedStyle
        {
            Radius = 6f,
            Gradient = 0.10f,
            ShadowSize = 5f,
            ShadowOffsetY = -2f,
            ShadowColor = new Color(0.01f, 0.01f, 0.03f, 0.40f),
        };

        /// <summary>
        /// 条状物：进度条、分隔线、顶栏。
        /// ★ 唯一一个**不投影**的预设。这类东西通常紧贴着别的东西，投影只会让接缝变脏；
        ///   而且它们常常在 RectMask2D 里，撑出去的网格会被裁掉一刀。
        /// </summary>
        public static readonly RoundedStyle Bar = new RoundedStyle
        {
            Radius = 4f,
            Gradient = 0.08f,
        };
    }

    /// <summary>
    /// 圆角面板。<see cref="Image"/> 的子类，配合 <c>Assets/Resources/RoundedPanel.shader</c>
    /// 画出圆角 / 描边 / 内发光 / 外投影 / 渐变底——**不需要任何图片资产**。
    ///
    /// ★★ 为什么是 Image 的子类而不是一个挂件：
    ///    逐实例参数（这块面板圆角 12、那块 4）必须走**顶点通道**——
    ///    uGUI 的 CanvasRenderer 不吃 MaterialPropertyBlock，
    ///    而给每个面板一个 material 实例等于给每个面板一次 draw call。
    ///    而要写顶点，唯一的入口就是 <see cref="OnPopulateMesh"/>，那是 Graphic 的方法。
    ///    换来的是全工程共用**一个** material：CardListView 那种几十行的长列表仍是一次 draw call。
    ///
    /// ★★ 外投影会把网格**撑到 RectTransform 之外**（见 <see cref="Padding"/>）。
    ///    这件事有三个连带后果，用之前必须知道：
    ///      ① **布局不受影响**。LayoutGroup / ContentSizeFitter 读的是 RectTransform，不看网格。
    ///      ② **点击区不受影响**。Graphic.Raycast 判的也是 RectTransform——
    ///         也就是说投影不吃射线，这正是想要的。
    ///      ③ **会被 RectMask2D 裁掉**。放进 CreateScrollView 的面板，投影到了视口边缘会被切一刀。
    ///         这也是对的（本来就该裁），但如果哪天看到「列表最后一项的投影缺了一块」，原因在这。
    ///
    /// ★ <see cref="OnPopulateMesh"/> 是**整个重写**的，于是 Image 基类那几个靠网格实现的功能
    ///   全部失效：<c>type</c>（Sliced / Tiled / Filled）、<c>fillAmount</c>、<c>preserveAspect</c>。
    ///   它们不会报错，只是**静默地没有效果**。要九宫格或进度条填充，请老老实实用 <c>Image</c>。
    ///
    /// ★ 不支持 Sprite Atlas。sprite 的 UV 是从局部坐标推导的，只对整张贴图正确。
    ///   本工程的 sprite 全是 UIFactory 自己烘的独立贴图，正好落在这个前提里。
    ///
    /// ★ 描边色与内发光色走 TANGENT / NORMAL 两个**浮点**通道，不像底色那样被 Color32 截到 [0,1]，
    ///   所以它们可以填 &gt; 1 的 HDR 亮度，直接被 UIRenderSetup 里那个 threshold = 1.0 的 Bloom 吃掉。
    ///   底色做不到这件事（走 COLOR 通道）。
    /// </summary>
    [AddComponentMenu("UI/Rounded Panel", 12)]
    public class RoundedPanel : Image
    {
        // ============================================================ 形状参数

        [SerializeField] private float _cornerRadius = 12f;
        [SerializeField] private float _borderWidth;
        [SerializeField] private Color _borderColor = Color.white;
        [SerializeField] private float _innerGlowWidth;
        [SerializeField] private Color _innerGlowColor = Color.clear;
        [SerializeField] private float _shadowSize;
        [SerializeField] private float _shadowOffsetY = -3f;
        [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private float _gradient;

        /// <summary>圆角半径（像素）。会被自动夹到半宽 / 半高以内，填一个很大的值即得胶囊形。</summary>
        public float CornerRadius
        {
            get => _cornerRadius;
            set { if (SetFloat(ref _cornerRadius, value)) SetVerticesDirty(); }
        }

        /// <summary>描边宽度（像素，往**内**长）。0 = 不描边。</summary>
        public float BorderWidth
        {
            get => _borderWidth;
            set { if (SetFloat(ref _borderWidth, value)) SetVerticesDirty(); }
        }

        /// <summary>描边颜色。★ RGB 可以超过 1（HDR），会被 Bloom 吃到。</summary>
        public Color BorderColor
        {
            get => _borderColor;
            set { if (_borderColor != value) { _borderColor = value; SetVerticesDirty(); } }
        }

        /// <summary>内发光从边缘往内衰减多少像素。0 = 不发光。</summary>
        public float InnerGlowWidth
        {
            get => _innerGlowWidth;
            set { if (SetFloat(ref _innerGlowWidth, value)) SetVerticesDirty(); }
        }

        /// <summary>
        /// 内发光颜色。★ <b>alpha 是强度</b>，不是透明度——发光是加色的，没有「半透明的光」这回事。
        /// 实际加进画面的是 RGB × alpha。
        /// </summary>
        public Color InnerGlowColor
        {
            get => _innerGlowColor;
            set { if (_innerGlowColor != value) { _innerGlowColor = value; SetVerticesDirty(); } }
        }

        /// <summary>外投影的模糊半径（像素）。0 = 不投影。★ 非 0 时网格会往外撑，见类注释。</summary>
        public float ShadowSize
        {
            get => _shadowSize;
            set { if (SetFloat(ref _shadowSize, value)) SetVerticesDirty(); }
        }

        /// <summary>投影相对面板下移多少（负值 = 往下，符合「光从上面来」）。</summary>
        public float ShadowOffsetY
        {
            get => _shadowOffsetY;
            set { if (SetFloat(ref _shadowOffsetY, value)) SetVerticesDirty(); }
        }

        /// <summary>投影颜色。深色背景上纯黑投影几乎看不见，那种场合把它调成一个暗色调的有色阴影更有效。</summary>
        public Color ShadowColor
        {
            get => _shadowColor;
            set { if (_shadowColor != value) { _shadowColor = value; SetVerticesDirty(); } }
        }

        /// <summary>
        /// 底色的纵向渐变强度。正 = 顶亮底暗（默认的受光方向），负 = 反过来，0 = 纯色。
        /// 0.12 左右就足够让一块纯色不再「平」，再大会开始像个塑料按钮。
        /// </summary>
        public float Gradient
        {
            get => _gradient;
            set { if (SetFloat(ref _gradient, value)) SetVerticesDirty(); }
        }

        /// <summary>网格往 RectTransform 外撑多少像素。由投影推导，没有投影时恒为 0。</summary>
        public float Padding => _shadowSize > 0f
            ? _shadowSize + Mathf.Abs(_shadowOffsetY) + 1f   // +1 给抗锯齿留一像素，否则外缘会被切平
            : 0f;

        private static bool SetFloat(ref float field, float value)
        {
            if (Mathf.Approximately(field, value)) return false;
            field = value;
            return true;
        }

        /// <summary>
        /// 套用一整套预设。★ 一次性写完所有字段再统一标脏，而不是逐个走属性 setter——
        /// 那样会在一帧里把同一块网格标脏九次。
        /// </summary>
        public void Apply(RoundedStyle style)
        {
            if (style == null) return;

            _cornerRadius = style.Radius;
            _borderWidth = style.Border;
            _borderColor = style.BorderColor;
            _innerGlowWidth = style.GlowWidth;
            _innerGlowColor = style.GlowColor;
            _shadowSize = style.ShadowSize;
            _shadowOffsetY = style.ShadowOffsetY;
            _shadowColor = style.ShadowColor;
            _gradient = style.Gradient;

            SetVerticesDirty();
        }

        /// <summary>
        /// 点亮 / 熄灭内发光。选中态、可打出、悬停这类「此刻这块面板与众不同」全走这一个入口。
        ///
        /// ★ 颜色的 alpha 是强度不是透明度，见 <see cref="InnerGlowColor"/>。
        ///   想让它被 Bloom 吃到就把 RGB 填到 1 以上。
        /// </summary>
        public void SetGlow(Color color, float width = 10f)
        {
            bool on = color.a > 0f && width > 0f;
            _innerGlowColor = on ? color : Color.clear;
            _innerGlowWidth = on ? width : 0f;
            SetVerticesDirty();
        }

        // ============================================================ 共享 material

        private const string ShaderResourceName = "RoundedPanel";
        private const string ShaderName = "Game/UI/RoundedPanel";

        private static Material _shared;
        private static bool _warned;

        /// <summary>
        /// 全工程唯一的那个 material。
        ///
        /// ★ 每次都判空重建，不能只在字段为 null 时建一次：编辑器退出播放模式会销毁运行时生成的
        ///   Material，而静态字段留下的是一个「假 null」的 Unity 对象引用（<c>== null</c> 为真但引用还在）。
        ///   这与 <see cref="UIFactory.CircleSprite"/> 是同一个坑。
        ///
        /// ★ 找不到 shader 时返回 null。null 的意思恰好是「用 Graphic 的默认 UI material」，
        ///   面板会退回**直角纯色**——也就是这次改动之前的样子。不崩、不黑屏、不留一片洋红。
        /// </summary>
        public static Material SharedMaterial
        {
            get
            {
                if (_shared != null) return _shared;

                var shader = ResolveShader();
                if (shader == null) return null;

                _shared = new Material(shader)
                {
                    name = "RoundedPanel (shared)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                return _shared;
            }
        }

        /// <summary>
        /// 找 shader。
        ///
        /// ★★ 先走 <c>Resources.Load</c> 才是关键的那一步，<c>Shader.Find</c> 只是兜底。
        ///    打包时 Unity 只收「被引用到」的 shader，而本工程没有任何 prefab / 场景引用它——
        ///    界面全是代码搭的。放在 Resources 下是让它进 build 的**唯一**保证，
        ///    否则会出现「编辑器里好好的，打出来的包全是直角」这种只在发布后才现形的问题。
        /// </summary>
        private static Shader ResolveShader()
        {
            var shader = Resources.Load<Shader>(ShaderResourceName);
            if (shader != null) return shader;

            shader = Shader.Find(ShaderName);
            if (shader != null) return shader;

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning($"[UI] 找不到 shader「{ShaderName}」" +
                                 $"（应在 Assets/Resources/{ShaderResourceName}.shader）。" +
                                 "圆角面板全体退回直角纯色。");
            }
            return null;
        }

        // ============================================================ 生命周期

        /// <summary>
        /// 这个 shader 要读的顶点通道。
        ///
        /// ★★ Canvas 默认**只带 TexCoord1**，没开的通道到了顶点着色器里全是 0。
        ///    表现是「圆角半径 0、描边 0、投影 0」——一个平平无奇的直角矩形，而且不报任何错。
        ///    这是本方案最容易踩、也最难查的一个坑。
        /// </summary>
        private const AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplySharedMaterial();
            EnsureChannels();
        }

        /// <summary>换了父节点就可能换了 Canvas，通道要在新的那个上重开一次。</summary>
        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureChannels();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            EnsureChannels();
        }

        private void ApplySharedMaterial()
        {
            var mat = SharedMaterial;
            if (mat != null && material != mat) material = mat;
        }

        /// <summary>
        /// 在自己所属的 Canvas 上补齐需要的顶点通道。
        ///
        /// ★ 只在「缺」的时候写，不无条件赋值：Canvas.additionalShaderChannels 的 setter
        ///   会把整个 Canvas 标脏并触发一次重建，每帧写一次等于每帧重建整个界面。
        ///
        /// ★ UIFactory.CreateCanvas 那边已经开好了，这里是兜底——
        ///   给的保证是「哪怕有人手搓了一个 Canvas 再往上挂圆角面板，它照样是圆的」。
        /// </summary>
        private void EnsureChannels()
        {
            var c = canvas;
            if (c == null) return;
            if ((c.additionalShaderChannels & RequiredChannels) == RequiredChannels) return;
            c.additionalShaderChannels |= RequiredChannels;
        }

        // ============================================================ 建网格

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var r = GetPixelAdjustedRect();
            float halfW = r.width * 0.5f;
            float halfH = r.height * 0.5f;
            if (halfW <= 0f || halfH <= 0f) return;

            float cx = r.x + halfW;
            float cy = r.y + halfH;

            float pad = Padding;

            // ★ 半径在 CPU 侧也夹一次。shader 里同样夹（那边是防御「填 999 表示胶囊形」这种写法），
            //   这里夹是为了让 shader 的投影与本体用**同一个**半径——两边算出不同的形状会露馅。
            float radius = Mathf.Max(0f, Mathf.Min(_cornerRadius, Mathf.Min(halfW, halfH)));

            var shape = new Vector4(radius, Mathf.Max(0f, _borderWidth),
                                    Mathf.Max(0f, _innerGlowWidth), Mathf.Max(0f, _shadowSize));
            var shape2 = new Vector4(_shadowOffsetY, _gradient, 0f, 0f);
            var shadow = new Vector4(_shadowColor.r, _shadowColor.g, _shadowColor.b, _shadowColor.a);
            var border = new Vector4(_borderColor.r, _borderColor.g, _borderColor.b, _borderColor.a);

            // ★ 内发光的 alpha 在这里就乘进 RGB：NORMAL 只有三个分量，装不下第四个。
            //   语义上也对——发光是加色的，「半透明的光」本来就不是一回事。
            var glow = new Vector3(_innerGlowColor.r, _innerGlowColor.g, _innerGlowColor.b)
                       * _innerGlowColor.a;

            Color32 col = color;

            // 四角顺序：左下 → 左上 → 右上 → 右下。与下面两个三角形的索引对应。
            AddCorner(vh, -1f, -1f, cx, cy, halfW, halfH, pad, col, shape, shape2, shadow, glow, border);
            AddCorner(vh, -1f, 1f, cx, cy, halfW, halfH, pad, col, shape, shape2, shadow, glow, border);
            AddCorner(vh, 1f, 1f, cx, cy, halfW, halfH, pad, col, shape, shape2, shadow, glow, border);
            AddCorner(vh, 1f, -1f, cx, cy, halfW, halfH, pad, col, shape, shape2, shadow, glow, border);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private static void AddCorner(VertexHelper vh, float sx, float sy,
                                      float cx, float cy, float halfW, float halfH, float pad,
                                      Color32 col, Vector4 shape, Vector4 shape2,
                                      Vector4 shadow, Vector3 glow, Vector4 border)
        {
            // ★ 局部坐标算的是**撑出去之后**的位置，而 uv0.zw 仍是**没撑之前**的半尺寸。
            //   于是外扩出来的那一圈顶点，其 |局部坐标| > 半尺寸，距离场自然为正——
            //   也就是「在面板外面」，正好是投影该待的地方。整个外扩不需要 shader 那边做任何特判。
            float lx = sx * (halfW + pad);
            float ly = sy * (halfH + pad);

            vh.AddVert(new Vector3(cx + lx, cy + ly),
                       col,
                       new Vector4(lx, ly, halfW, halfH),
                       shape,
                       shape2,
                       shadow,
                       glow,
                       border);
        }
    }
}
