// =====================================================================================
// 圆角面板：一个纯 SDF（有向距离场）的 uGUI shader。
//
// ★★ 它解决的是什么：
//    整套界面是代码搭的，不引入任何图片资产（见 UIFactory 的类注释）。
//    代价是**所有面板都是硬边纯色矩形**——圆角、描边、投影这些让界面「像成品」的东西，
//    在传统做法里全都要靠 9-slice 贴图，而那要求先有美术。
//    距离场把这一整类东西变成了算术：只要知道「当前像素离圆角矩形的边有多远」，
//    圆角 / 描边 / 内发光 / 外投影 / 渐变底**全部**是这一个距离值的函数。
//
// ★★ 逐实例参数走**顶点通道**，不走 material 属性。这是整个方案的要害：
//    uGUI 的 CanvasRenderer **不吃 MaterialPropertyBlock**，
//    所以「每个面板圆角半径不同」只有两条路——每个面板一个 material 实例（每个一次 draw call），
//    或者把参数塞进顶点。走顶点的话全工程共用**一个** material，合批完整保住：
//    CardListView 那种几十张卡的长列表仍然是一次 draw call。
//
// ★★ 通道分配（RoundedPanel.OnPopulateMesh 是唯一的写入方，两边必须逐字段对应）：
//      COLOR      面板底色（Image.color × Graphic 顶点色，uGUI 自动乘好）
//      TEXCOORD0  xy = 像素级局部坐标（**相对面板中心**，含外扩部分故可超出半尺寸）
//                 zw = 面板半尺寸
//      TEXCOORD1  x=圆角半径 y=描边宽度 z=内发光宽度 w=投影模糊半径
//      TEXCOORD2  x=投影下移量 y=渐变强度（正=顶亮，负=底亮） zw=保留
//      TEXCOORD3  xyz=投影颜色 w=投影不透明度
//      NORMAL     内发光颜色 RGB
//      TANGENT    描边颜色 RGBA
//
// ★★ 这些通道**默认是被 Canvas 丢掉的**。Canvas.additionalShaderChannels 默认只带 TexCoord1，
//    没开的通道到了顶点着色器里全是 0——表现是「圆角半径 0、描边 0、投影 0」，
//    也就是**一个平平无奇的直角矩形，而且不报任何错**。
//    开启在两处：UIFactory.CreateCanvas（所有 Canvas 的出生地）和 RoundedPanel.OnEnable（兜底）。
//    这是本方案最容易踩、也最难查的一个坑。
//
// ★ TANGENT 与 NORMAL 是浮点通道，**不像 COLOR 那样被 Color32 截到 [0,1]**。
//   于是描边色和内发光色天然支持 HDR（> 1 的亮度）——
//   而 UIRenderSetup 里 Bloom 的 threshold 正好定在 1.0。
//   把描边色调到 1.5 之类的值，它就会**真的发光**，不需要再动管线。底色则不行（走 COLOR，会被截）。
//
// ★ 刻意不支持图集（Sprite Atlas）：sprite UV 是从局部坐标推导的（uv = p/b*0.5+0.5），
//   只对「整张贴图、非图集」的 sprite 正确。本工程的 sprite 全是自己烘的独立贴图，正好落在这个前提里。
// =====================================================================================
Shader "Game/UI/RoundedPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // ---- 下面这一整块是 uGUI 的**规定动作**，不是可选的。
        //      Mask（走 Stencil）与 RectMask2D（走 _ClipRect）都是由 uGUI 在运行时
        //      往 material 上写这些属性来实现的。少了它们，任何放进 ScrollView 的圆角面板
        //      都会**溢出裁剪框**——而 CreateScrollView 建的每一个列表都带 RectMask2D。
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // ★ 3.0 而不是 UI/Default 的 2.0：抗锯齿靠 fwidth（屏幕空间偏导），
            //   而偏导在 target 2.0 下要靠扩展，不保证有。UI 本来就不跑在会缺 3.0 的设备上。
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 texcoord1: TEXCOORD1;
                float4 texcoord2: TEXCOORD2;
                float4 texcoord3: TEXCOORD3;
                float3 normal   : NORMAL;
                float4 tangent  : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float4 local         : TEXCOORD0;   // xy=局部坐标 zw=半尺寸
                float4 shape         : TEXCOORD1;   // radius, border, glowWidth, shadowSize
                float4 shape2        : TEXCOORD2;   // shadowOffsetY, gradient, --, --
                float4 shadowCol     : TEXCOORD3;   // rgb=投影色 a=投影不透明度
                float3 glowCol       : TEXCOORD4;
                float4 borderCol     : TEXCOORD5;
                float4 worldPosition : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);

                o.color     = v.color * _Color;
                o.local     = v.texcoord;
                o.shape     = v.texcoord1;
                o.shape2    = v.texcoord2;
                o.shadowCol = v.texcoord3;
                o.glowCol   = v.normal;
                o.borderCol = v.tangent;
                return o;
            }

            // 圆角矩形的有向距离场：内部为负、边上为 0、外部为正，且**数值就是像素距离**。
            // 「数值就是距离」这一条是后面所有效果的基础——描边宽度、发光宽度、投影半径
            // 全都可以直接拿像素值去比，不需要任何归一化。
            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            // ★ 返回 float4 而不是 UI/Default 那样的 fixed4：fixed 在移动端是 [-2,2] 的定点数，
            //   会把描边 / 内发光的 HDR 亮度截掉——而那正是这套 shader 唯一能喂给 Bloom 的东西。
            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.local.xy;
                float2 b = i.local.zw;

                // ★ 半径必须夹在半尺寸以内。超过一半时 sdRoundBox 的 q 会算出负的 b，
                //   形状会翻出去变成一坨十字——而「半径填个 999 表示胶囊形」是很自然的写法。
                float radius = min(i.shape.x, min(b.x, b.y));

                float border     = i.shape.y;
                float glowWidth  = i.shape.z;
                float shadowSize = i.shape.w;
                float shadowOffY = i.shape2.x;
                float gradient   = i.shape2.y;

                float d = sdRoundBox(p, b, radius);

                // ★ fwidth(d) = 「d 在屏幕上每走一个像素变化多少」。
                //   拿它当过渡带宽度，抗锯齿就自动适应 CanvasScaler 的任何缩放与任何分辨率——
                //   写死一个像素数的话，1080p 下调好的边在 4K 下会重新变糊。
                float aa = max(fwidth(d), 1e-5);

                // 面板本体的覆盖率。saturate(0.5 - d/aa) 与 smoothstep(-aa,aa,d) 等效但便宜。
                float fill = saturate(0.5 - d / aa);

                // ---- ① 底色 + 渐变
                float4 baseCol = i.color;

                // 沿 Y 的线性渐变。gradient 为正 = 顶部提亮、底部压暗，负则相反；0 时恒等于 1。
                float t = saturate(p.y / max(b.y, 1e-4) * 0.5 + 0.5);
                baseCol.rgb *= (1.0 - gradient) + (2.0 * gradient) * t;

                // sprite 采样。没配 sprite 时 Image 绑的是纯白图，乘上去恒等 —— 所以不必分支。
                float2 uv = saturate(p / max(b, 1e-4) * 0.5 + 0.5);
                baseCol *= tex2D(_MainTex, uv) + _TextureSampleAdd;

                // ---- ② 描边：距离落在 [-border, 0] 这一圈里
                float borderMask = border > 1e-4 ? saturate((d + border) / aa + 0.5) : 0.0;
                float3 rgb = lerp(baseCol.rgb, i.borderCol.rgb, borderMask * i.borderCol.a);

                // ---- ③ 内发光：从边缘往里衰减 glowWidth，二次方让它集中在边上
                //      加色而非混色 —— 它是「光」，压在描边之上会让描边一起变亮，这正是想要的。
                float g = glowWidth > 1e-4 ? saturate(1.0 + d / glowWidth) : 0.0;
                rgb += i.glowCol * (g * g);

                float panelA = baseCol.a * fill;

                // ---- ④ 外投影：把同一个形状往下挪一点再模糊
                //      ★ 必须乘 (1 - panelA)：不然半透明面板底下会透出自己的投影，
                //        整块面板看起来会脏一层。不透明面板则本来就挡住了，乘不乘都一样。
                float ds = sdRoundBox(p - float2(0.0, shadowOffY), b, radius);
                float sa = shadowSize > 1e-4 ? saturate(1.0 - ds / shadowSize) : 0.0;
                sa = sa * sa * (3.0 - 2.0 * sa);           // smoothstep 化，去掉外缘那道硬边
                sa *= i.shadowCol.a * (1.0 - panelA);

                // ---- ⑤ 合成。两层都是 straight alpha（uGUI 用 SrcAlpha/OneMinusSrcAlpha），
                //      所以要按 over 算符除回去，不能直接相加。
                float outA = panelA + sa * (1.0 - panelA);
                float3 outRGB = (rgb * panelA + i.shadowCol.rgb * sa * (1.0 - panelA)) / max(outA, 1e-4);

                float4 col = float4(outRGB, outA);

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
}
