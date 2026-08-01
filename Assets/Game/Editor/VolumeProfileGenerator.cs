#if URP_PRESENT
using System.IO;
using Game.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor
{
    /// <summary>
    /// 生成一份**可以在 Play 模式里拖着滑条调**的后处理配置资产。
    ///
    /// ★★ 为什么后处理参数要走资产、而这个工程别的界面配置全是代码：
    ///    卡框缩进、卡牌尺寸这些改一个数就能从代码里推出结果；
    ///    而 Bloom 的 threshold 该是 1.0 还是 0.9、暗角该 0.28 还是 0.35，
    ///    **只能盯着屏幕看**。代码化意味着每试一个数都要「改 → 退出 Play → 再进 → 再看」，
    ///    而这类参数一轮猜一个数根本收敛不了。
    ///    这与 <c>CardFrameSkin</c> 做成资产的理由是同一条（见 <c>CardView.ApplyLayout</c> 的注释）。
    ///
    /// ★ 资产不存在时游戏照样跑：<see cref="UIRenderSetup"/> 找不到资产会退回
    ///   代码里的那份默认值（<see cref="UIRenderSetup.ApplyDefaults"/>）。
    ///   所以这个菜单是「我要开始调参了」才需要点，不是必要步骤。
    ///
    /// ★ 必须落在 <c>Assets/Resources/</c> 下——运行时是用 <c>Resources.Load</c> 找它的。
    ///   这个工程本来就有 Resources 目录（DOTweenSettings 在那儿）。
    /// </summary>
    public static class VolumeProfileGenerator
    {
        private const string Dir = "Assets/Resources";
        private const string Path = Dir + "/GameVolumeProfile.asset";

        [MenuItem("Tools/卡牌游戏/7. 生成后处理配置", priority = 22)]
        public static void Generate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Path);
            if (existing != null)
            {
                // ★ 已经有了就**必须先问一句**：这个资产的全部价值就是被人手调过的那些数字，
                //   手一滑重新生成一次就全没了，而且没有任何提示。
                bool reset = EditorUtility.DisplayDialog(
                    "后处理配置已存在",
                    $"{Path} 已经存在。\n\n" +
                    "重置会把里面所有调过的参数**覆盖回默认值**，无法撤销。",
                    "重置为默认值", "取消");

                if (!reset) return;

                Apply(existing, clearFirst: true);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();

                Selection.activeObject = existing;
                Debug.Log($"[后处理] 已重置 {Path}");
                return;
            }

            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            // ★ 先落盘再往里加组件：VolumeProfile 的每个效果都是一个**子资产**，
            //   而 AddObjectToAsset 要求父资产已经存在于磁盘上。
            //   顺序反了不会报错，只会得到一个「打开是空的」的 Profile。
            AssetDatabase.CreateAsset(profile, Path);
            Apply(profile, clearFirst: false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = profile;
            Debug.Log($"[后处理] 已生成 {Path}。" +
                      "在 Play 模式里选中它就能拖滑条实时调；改动会保存进资产。");
        }

        private static void Apply(VolumeProfile profile, bool clearFirst)
        {
            if (clearFirst)
            {
                // 逐个从资产里摘掉再销毁，否则旧的子资产会变成孤儿留在文件里
                for (int i = profile.components.Count - 1; i >= 0; i--)
                {
                    var c = profile.components[i];
                    profile.components.RemoveAt(i);
                    if (c != null) Object.DestroyImmediate(c, allowDestroyingAssets: true);
                }
            }

            // ★ 默认值只有一份，写在运行时那边。两处各写一套的话，
            //   「资产里的初始状态」和「没有资产时的兜底」迟早会对不上，
            //   而那种分叉只有在删掉资产之后才看得见。
            UIRenderSetup.ApplyDefaults(profile);

            foreach (var comp in profile.components)
            {
                if (comp == null) continue;
                if (AssetDatabase.IsSubAsset(comp)) continue;

                // ★ HideInHierarchy：不加的话 Project 窗口里这个资产会展开成
                //   一串看起来像是误存进去的 ScriptableObject。URP 自己也是这么做的。
                comp.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(comp, profile);
            }

            EditorUtility.SetDirty(profile);
        }
    }
}
#endif
