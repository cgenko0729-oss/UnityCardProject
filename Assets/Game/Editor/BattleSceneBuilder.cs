using System.IO;
using Game.Core;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>一键创建可直接 Play 的战斗测试场景。</summary>
    public static class BattleSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Battle.unity";

        [MenuItem("Tools/卡牌游戏/2. 创建战斗测试场景", priority = 2)]
        public static void CreateScene()
        {
            var db = FindDatabase();
            if (db == null)
            {
                EditorUtility.DisplayDialog("缺少数据",
                    "找不到 GameDatabase。请先执行菜单 Tools/卡牌游戏/1. 生成示例内容。", "好");
                return;
            }

            if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("BattleBootstrap");
            var boot = go.AddComponent<BattleBootstrap>();
            boot.Database = db;
            boot.EncounterId = "slime";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[BattleSceneBuilder] 已创建场景 {ScenePath}。直接按 Play 即可开打。");
            EditorUtility.DisplayDialog("完成",
                $"场景已创建：{ScenePath}\n\n直接按 Play 就能玩。\n" +
                "换战斗：选中 BattleBootstrap，改 Encounter Id（slime / double_slime / jawworm / mixed / guardian）。", "好");
        }

        [MenuItem("Tools/卡牌游戏/打开战斗测试场景", priority = 3)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { CreateScene(); return; }
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static GameDatabase FindDatabase()
        {
            var guids = AssetDatabase.FindAssets("t:GameDatabase");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<GameDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
