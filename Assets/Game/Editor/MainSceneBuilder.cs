using System.IO;
using Game.Core;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 一键创建完整流程场景（主菜单 → 地图 → 战斗 → 奖励 → …… → Boss）。
    /// 场景里只有一个 GameApp，其余全部是运行时程序化搭建的。
    /// </summary>
    public static class MainSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/卡牌游戏/4. 创建完整流程场景", priority = 5)]
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

            var go = new GameObject("GameApp");
            var app = go.AddComponent<GameApp>();
            app.Database = db;
            app.StartingMaxHp = 80;
            app.StarterRelicId = "burning_blood";
            app.FixedSeed = 0;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[MainSceneBuilder] 已创建场景 {ScenePath}。按 Play 从主菜单开始。");
            EditorUtility.DisplayDialog("完成",
                $"场景已创建：{ScenePath}\n\n按 Play 从主菜单开始一局完整流程。\n" +
                "想复现某一局：选中 GameApp，把 Fixed Seed 改成非 0 的值。", "好");
        }

        [MenuItem("Tools/卡牌游戏/打开完整流程场景", priority = 6)]
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
