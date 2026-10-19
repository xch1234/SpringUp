using System.Linq;
using SpringUp.Flow;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(BattleMapSettings))]
    public sealed class BattleMapSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("地图大小", EditorStyles.boldLabel);
            Field("columns", "横向格数（3–200）"); Field("rows", "纵向格数（3–200）");
            Field("cellSize", "每格边长（世界单位）");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("玩家视野", EditorStyles.boldLabel);
            Field("cameraHalfHeight", "基础视野半高");
            EditorGUILayout.HelpBox("战斗与休整区共用基础视野。数值越大，看得越远。屏幕可见高度 = 半高 × 2；宽度还取决于窗口比例。器官视野倍率在此基础上叠加。", MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("中间地砖（原 5.png 的位置）", EditorStyles.boldLabel);
            Field("centerVariants", "随机图片列表");
            Field("randomizeOnEntry", "每次进入战斗重新随机");
            Field("seed", "固定种子 / 编辑器预览种子");
            EditorGUILayout.HelpBox("每个中间格独立等概率选图。将 5.png 和其他正方形 Sprite 拖入列表即可；边缘及四角保留原九宫格。空项跳过，空列表回退到 5 号图。", MessageType.Info);
            EditorGUILayout.Space();
            Field("borderSprites", "九宫格图片（1–9）"); Field("tileMaterial", "地砖基础材质");
            serializedObject.ApplyModifiedProperties();
            var settings = (BattleMapSettings)target;
            EditorGUILayout.LabelField($"实际地图：{settings.WorldSize.x:0.##} × {settings.WorldSize.y:0.##} 世界单位");
            EditorGUILayout.LabelField($"地砖数：{settings.Columns * settings.Rows}（含四边和四角）");
            if (!settings.Validate(out var error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.HelpBox("尺寸和图片修改会在下次进入战斗时生效；运行中修改基础视野会立即生效。视野大于地图时，相机会居中，画面会显示地图外区域。", MessageType.Info);
        }

        void Field(string name, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label), true);
    }

    public static class BattleMapAuthoring
    {
        public const string SettingsPath = "Assets/Data/Maps/BattleMapSettings.asset";

        [MenuItem("Tools/SpringUp/战斗地图/选择地图与视野配置")]
        public static void SelectSettings()
        {
            Selection.activeObject = EnsureSettings();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        public static BattleMapSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<BattleMapSettings>(SettingsPath);
            if (settings != null) return settings;
            if (!AssetDatabase.IsValidFolder("Assets/Data/Maps")) AssetDatabase.CreateFolder("Assets/Data", "Maps");
            settings = ScriptableObject.CreateInstance<BattleMapSettings>();
            settings.borderSprites = Enumerable.Range(1, 9)
                .Select(i => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/bk1440-s/{i}.png")).ToArray();
            settings.centerVariants = new[] { settings.borderSprites[4] };
            settings.tileMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/Flow/TerrainMaterials/Tile5.mat");
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        // Used both for the existing scene migration and future scene generation.
        public static void Configure(FlowContent content)
        {
            content.viewSettings = EnsureSettings();
            EditorUtility.SetDirty(content);
            if (content.kind != ContentKind.Battle) return;
            var world = content.world;
            var layout = world.GetComponent<BattleMapLayout>();
            if (layout == null) layout = world.AddComponent<BattleMapLayout>();
            layout.settings = EnsureSettings();
            layout.terrain = world.GetComponentsInChildren<Transform>(true).First(t => t.name == "MapBackground" || t.name == "ArenaTiles");
            layout.walls = world.GetComponentsInChildren<Transform>(true).First(t => t.name == "BoundaryWalls");
            layout.battleCamera = world.GetComponentInChildren<Camera>(true);
            layout.spawners = world.GetComponentsInChildren<EnemySpawner>(true);
            EditorUtility.SetDirty(layout);
        }

        public static void InstallBatch()
        {
            if (!Application.isBatchMode) throw new System.InvalidOperationException("Use a batch validation copy.");
            var scene = EditorSceneManager.OpenScene(FlowApplication.BattlePath);
            Configure(Object.FindAnyObjectByType<FlowContent>());
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
    }
}
