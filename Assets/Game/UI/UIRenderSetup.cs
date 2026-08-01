using UnityEngine;
#if URP_PRESENT
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Game.UI
{
    /// <summary>
    /// 让整套 uGUI 界面**走进 URP 的渲染流程**，从而能被后处理作用到。
    ///
    /// ★★ 这件事解决的是什么：
    ///    界面原本用的是 <c>RenderMode.ScreenSpaceOverlay</c>。Overlay 的 Canvas 是在
    ///    **整条渲染管线跑完之后**由 UI 系统单独贴到 backbuffer 上的，
    ///    URP 的后处理栈根本碰不到它。而本工程的画面 **100% 由 UI 构成**
    ///    （没有任何 3D/2D 场景物体参与），于是 <c>Assets/Settings/</c> 底下那一整套
    ///    Volume Profile / Renderer 配置对游戏画面的作用**恰好是零**——
    ///    它们是 Unity 模板留下来的摆设。
    ///
    ///    换成 <c>ScreenSpaceCamera</c> 之后，Canvas 变成相机渲染的透明几何体，
    ///    排在 post-processing pass 之前，Bloom / Vignette / 色调映射这些才开始真的生效。
    ///
    /// ★★ 相机结构：<b>叠在场景原有的 Main Camera 上</b>，而不是取而代之。
    ///    Main Camera 当 Base（将来渲染 3D/2D 背景、真粒子），本类建的 UI Camera 当 Overlay
    ///    叠在它的 Camera Stack 顶上。URP 的后处理**在整条 stack 的最后应用一次**，
    ///    所以 UI 与将来的背景会一起被同一套后处理罩住——这正是想要的：
    ///    两层各调一套色，接缝会非常明显。
    ///
    /// ★ 找不到 Base Camera 时（<c>BattleScreen</c> 可以脱离 GameApp 独立跑，
    ///   SampleScene 里也未必有）UI Camera **自己当 Base**。
    ///   没有这条兜底，那些场景会渲染出一片纯黑，而且不报任何错。
    ///
    /// ★ 整件事可以一键退回 Overlay，见 <see cref="Enabled"/>。
    /// </summary>
    public static class UIRenderSetup
    {
        private const string EnabledKey = "render.cameracanvas";

        /// <summary>Volume Profile 资产在 Resources 下的名字。见 <see cref="ResolveProfile"/>。</summary>
        private const string ProfileResourceName = "GameVolumeProfile";

        private static bool _loaded;
        private static bool _enabled = true;

        /// <summary>
        /// 是否走「Canvas 挂相机」这条路。关掉就整体退回 <c>ScreenSpaceOverlay</c>，
        /// 与这次改动之前**逐像素相同**。
        ///
        /// ★★ 这个开关不是可有可无的洁癖，它是这次改动的安全绳：
        ///    Overlay → Camera 会同时动到三样东西——Canvas 的渲染路径、
        ///    <see cref="UIFactory.CanvasCamera"/> 那 11 处坐标换算、以及后处理。
        ///    出了问题时，能一键回到已知可用的状态，才分得清「是管线改坏了」
        ///    还是「后处理参数难看」还是「坐标换算漏改了一处」。
        ///
        /// ★ 走 PlayerPrefs 与 <see cref="FeedbackSettings"/> 同一套路数：
        ///   暂时没有设置界面，将来有了直接读写这个属性即可。
        /// </summary>
        public static bool Enabled
        {
            get
            {
                if (!_loaded)
                {
                    _enabled = PlayerPrefs.GetInt(EnabledKey, 1) != 0;
                    _loaded = true;
                }
                return _enabled;
            }
            set
            {
                _enabled = value;
                _loaded = true;
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            }
        }

        private static Camera _camera;

        /// <summary>
        /// 给 Canvas 用的那台相机。<see cref="Enabled"/> 为 false、
        /// 或者工程没装 URP 时返回 null——而 null 恰好就是「按 Overlay 处理」的意思，
        /// 所有调用点都不需要为这两种情况写分支。
        /// </summary>
        public static Camera Camera
        {
            get
            {
                if (!Enabled) return null;

                // ★ 每次都判空重建，不能只在字段为 null 时建一次：
                //   换场景会把上一台相机连同它的 GameObject 一起销毁，而静态字段留下的是
                //   一个「假 null」的 Unity 对象引用（== null 为真但引用还在）。
                //   这与 UIFactory.CircleSprite 那边是同一个坑。
                if (_camera != null) return _camera;

                _camera = Build();
                return _camera;
            }
        }

        /// <summary>
        /// 建 UI 相机、挂进 Camera Stack、确保场景里有一个全局 Volume。
        /// ★ 由 <see cref="Camera"/> 惰性触发，也就是**第一个 Canvas 被建出来的那一刻**。
        ///   不做成 <c>RuntimeInitializeOnLoad</c>：那会在换场景时错过时机，
        ///   而且会让「不显示任何 UI 的场景」也白建一台相机。
        /// </summary>
        private static Camera Build()
        {
#if !URP_PRESENT
            // 工程没装 URP：返回 null，全体退回 Overlay。★ 这条分支不是假想——
            // 换回 Built-in 管线时，下面那些 UniversalAdditionalCameraData 会整片编译不过，
            // 而 asmdef 的 versionDefines 让它自动降级而不是把工程弄成红的。
            return null;
#else
            var go = new GameObject("UICamera");

            // ★★ 把 UI 相机挪到离场景很远的地方，是**必须的**，不是洁癖。
            //
            //    ScreenSpaceCamera 的 Canvas 会把自己摆在 worldCamera 前方 planeDistance 处，
            //    并按视口缩放——它是**世界空间里一块真实存在的面片**。
            //    而场景里那台 Main Camera 的 cullingMask 是 Everything、far 是 1000，
            //    Canvas 又建在 Default 层上，于是 Base 相机会把整个界面**当成远处的一块牌子
            //    再渲染一遍**（缩得很小、还带透视变形）。
            //
            //    Overlay 相机只清深度、**不清颜色**，所以那一份不会被盖掉：
            //    它会从 UI 任何半透明的地方透出来。表现是「界面上有一块糊掉的迷你界面」，
            //    而且只在半透明元素（提示框底板、遮罩、飘字）经过时才看得见。
            //
            //    另一条路是给 UI 单独一个 layer 并收窄两台相机的 cullingMask，那更「正规」，
            //    但要求 UIFactory 建的**每一个**节点都设成 UI 层（GameObject.layer 不从父节点继承），
            //    是把这次改动面扩大一倍的事。位置隔离一行解决，且不依赖任何人记得设 layer。
            go.transform.position = new Vector3(0f, 5000f, 0f);

            var cam = go.AddComponent<Camera>();

            // ★ 正交：UI 不需要透视，而正交下 Canvas 的像素与屏幕像素是 1:1 的，
            //   任何「近大远小」都只会让 CanvasScaler 的推导失效。
            //   （代价是 CardView 的悬停倾斜仍然拿不到真透视，见那边的注释——
            //     要真透视得把这里改成透视相机，那会牵动整套 UI 的尺寸推导，是另一件事。）
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;

            // ★ 不加 AudioListener：场景里的 Main Camera 上已经有一个，
            //   两个 AudioListener 会让 Unity 每帧刷一条警告，而声音只认第一个。
            //   （这也是为什么用 AddComponent<Camera>() 而不是复制一台现成的相机。）

            // ★ cullingMask 保持 Everything。
            //   「只渲 UI 层」听起来更干净，但要求 UIFactory 建的**每一个**节点都设成 UI 层
            //   （GameObject.layer 不会从父节点继承），那是把这次改动的面扩大一倍的事，
            //   而当前场景里除了相机和灯之外一个可渲染物体都没有，收窄的收益恰好是零。
            //   ★ 将来真加了 3D 背景物体，这里要收窄成只渲 UI 层，否则背景会被画两遍。

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;

            // ★ volumeLayerMask 必须包含 Volume 所在的层。全局 Volume 建在 Default(0) 上，
            //   而这个字段的默认值在某些 URP 版本里是 0（什么都采不到）——
            //   采不到的表现是「后处理开着但一点效果都没有」，非常难查。显式写死。
            data.volumeLayerMask = ~0;

            var baseCam = FindBaseCamera();
            if (baseCam != null)
            {
                data.renderType = CameraRenderType.Overlay;

                // ★ 关于清深度：Overlay 相机**默认就清深度**（clearDepth 在 URP 里是只读属性，
                //   由序列化的默认值 true 决定，代码改不了）。这正是这里需要的——
                //   UI 不该被 Base 相机里任何 3D 物体的深度挡住。
                //   ★ 将来若有人在 Inspector 里把 UI Camera 的 Clear Depth 取消掉，
                //     表现会是「加了 3D 背景之后 UI 被吃掉一块」，而这里一行代码都不会报错。

                var stack = baseCam.GetUniversalAdditionalCameraData();
                stack.renderType = CameraRenderType.Base;

                // ★ 后处理开在 Base 上：URP 是在**整条 stack 跑完之后**才应用一次后处理的，
                //   逐台相机各开一次不会更强，只会让人以为它是逐台生效的。
                stack.renderPostProcessing = true;
                stack.volumeLayerMask = ~0;

                if (!stack.cameraStack.Contains(cam)) stack.cameraStack.Add(cam);
            }
            else
            {
                // 没有 Base 可叠——自己当 Base。见类注释里那条兜底。
                data.renderType = CameraRenderType.Base;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f);
            }

            EnsureVolume();

            // ★ 这一条日志是这次改动**唯一**的运行时自述。
            //   Overlay → Camera 的差别在画面上是「说不清哪里不一样」的那种，
            //   而失败模式（找不到 Base、退回 Overlay、URP 没装）又全都不报错。
            //   没有这一句，验证的人只能靠猜自己到底跑在哪条路上。
            Debug.Log($"[渲染] UI 走 ScreenSpaceCamera；" +
                      (baseCam != null ? $"叠在「{baseCam.name}」的 Camera Stack 上。" : "没找到 Base 相机，UI 相机自己当 Base。") +
                      " 要退回 Overlay：UIRenderSetup.Enabled = false。");

            return cam;
#endif
        }

#if URP_PRESENT
        /// <summary>
        /// 找一台能当 Base 的相机。
        ///
        /// ★ 不能只认 <c>Camera.main</c>：那个只找 tag 为 MainCamera **且 enabled** 的相机，
        ///   而且如果场景里那台已经被谁设成了 Overlay，把 UI 叠上去会得到一条
        ///   「Overlay camera in stack of another overlay camera」的报错 + 一片黑屏。
        ///   所以拿到之后还要把它显式设回 Base（见调用处）。
        /// </summary>
        private static Camera FindBaseCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;

            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.isActiveAndEnabled) continue;
                if (cam.name == "UICamera") continue;
                return cam;
            }
            return null;
        }

        /// <summary>
        /// 确保场景里有一个全局 Volume。已经有别人放的就不管——那多半是故意的。
        /// </summary>
        private static void EnsureVolume()
        {
            var existing = Object.FindFirstObjectByType<Volume>();
            if (existing != null) return;

            var go = new GameObject("GameVolume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.profile = ResolveProfile();
        }

        /// <summary>
        /// 后处理参数从哪来：**Resources 里有资产就用资产，没有就用代码里的默认值**。
        ///
        /// ★★ 这个「两条腿」不是骑墙：
        ///    ① 后处理是纯目测的东西，必须能在 Play 模式里拖着滑条调
        ///       （与 <c>CardFrameSkin</c> 做成资产的理由完全一样）；
        ///    ② 但资产是可以被误删、被 gitignore、或者干脆还没生成的，
        ///       而那时游戏必须照样能跑、画面照样成立，不能变成一片死黑或者一片惨白。
        ///    代码里的这份默认值就是资产的「出厂设置」，也是它的规格说明。
        ///
        /// ★ 生成可调资产：菜单 <c>Tools/卡牌游戏/6. 生成后处理配置</c>。
        /// </summary>
        private static VolumeProfile ResolveProfile()
        {
            var asset = Resources.Load<VolumeProfile>(ProfileResourceName);
            if (asset != null) return asset;

            return BuildDefaultProfile();
        }

        /// <summary>
        /// 代码里的那份「克制」默认值。<see cref="Game.Editor"/> 的生成器也调它，
        /// 好让资产的初始状态与这里**逐个参数相同**——两边各写一套迟早会分叉。
        /// </summary>
        public static VolumeProfile BuildDefaultProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = ProfileResourceName;
            ApplyDefaults(profile);
            return profile;
        }

        /// <summary>
        /// 往一个 Profile 上写默认的三件套。
        ///
        /// ★★ 每一项都要显式 <c>overrideState = true</c>。
        ///    Volume 的每个参数各带一个「我到底管不管这一项」的开关，默认是**不管**；
        ///    只设 <c>value</c> 而不开 overrideState，效果会静静地按默认值走，
        ///    表现就是「参数改了但画面没变」——这是 Volume 最常见的一个坑。
        /// </summary>
        public static void ApplyDefaults(VolumeProfile profile)
        {
            if (profile == null) return;

            // ---- ① 色调映射
            //
            // ★ 排第一位，因为它是唯一一个**不加东西、只是把已有的东西映射对**的效果。
            //   现在这套配色的明度全挤在 0.08~0.55 之间（见 CardView / BattleScreen 的常量），
            //   线性输出下暗部会糊成一团黑。Neutral 把暗部拉开而不改变色相——
            //   刻意不用 ACES：那个会给整个画面压一层电影感的偏色，
            //   而这一步的目标是「别改变现有观感」。
            var tonemapping = Get<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            // ---- ② 暗角
            //
            // ★ 这是三项里对「视线收到战场中央」贡献最大的一项，而且零风险：
            //   它只压四角，而四角本来就是空背景。
            //   0.28 是刻意压得很低的值——高到能一眼看出来的暗角会吃掉最外侧的手牌。
            var vignette = Get<Vignette>(profile);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.28f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.45f;
            vignette.color.overrideState = true;
            vignette.color.value = new Color(0.02f, 0.02f, 0.04f);

            // ---- ③ 极轻 Bloom
            //
            // ★★ threshold 定在 1.0 是这一项的**全部要害**：
            //    只有亮度**超过 1** 的像素才会发光，而 UI 的颜色几乎全部落在 [0,1] 里。
            //    也就是说，现在这套界面里**没有任何东西会发光**——这是故意的。
            //    它是为将来准备的地基：等哪个元素（能量球、暴击数字、稀有卡边框）
            //    真的被调成 HDR 亮度，它会自动开始发光，而不需要再回来改管线。
            //    threshold 一旦压到 1 以下，整个界面会立刻蒙上一层灰雾——
            //    因为那等于让所有浅色元素互相渗光。
            var bloom = Get<Bloom>(profile);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.0f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.55f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;
        }

        /// <summary>取已有的 override，没有就加一个。</summary>
        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
            => profile.TryGet<T>(out var c) ? c : profile.Add<T>(overrides: true);
#endif
    }
}
