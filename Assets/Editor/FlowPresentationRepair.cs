using System;
using System.Linq;
using SpringUp.Flow;
using SpringUp.Laboratory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class FlowPresentationRepair
    {
        public const string MapPath = "Assets/Prefabs/Flow/ArenaTiles.prefab";
        public const string LabPath = "Assets/Prefabs/Flow/LaboratoryWindow.prefab";
        public static void RepairAndVerify()
        {
            Run();
            FlowPresentationChecks.Run();
            SceneFlowChecks.RunBatch();
        }
        // Density updates preserve all existing scene edits and laboratory prefab customization.
        public static void UpdateTerrainAndVerify()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var map = CreateMap();
            PrefabUtility.SaveAsPrefabAsset(map, MapPath);
            Object.DestroyImmediate(map);
            AssetDatabase.SaveAssets();
            FlowPresentationChecks.Run();
            SceneFlowChecks.RunBatch();
        }
        [MenuItem("Tools/SpringUp/Scene View/Hide UI for Map Editing")]
        public static void HideUI() { Tools.visibleLayers &= ~(1 << LayerMask.NameToLayer("UI")); SetCanvasVisibility(true); }
        [MenuItem("Tools/SpringUp/Scene View/Show UI")]
        public static void ShowUI() { Tools.visibleLayers |= 1 << LayerMask.NameToLayer("UI"); SetCanvasVisibility(false); }
        static void SetCanvasVisibility(bool hide)
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (hide) SceneVisibilityManager.instance.Hide(canvas.gameObject, true);
                else SceneVisibilityManager.instance.Show(canvas.gameObject, true);
            foreach (var events in Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (hide) SceneVisibilityManager.instance.Hide(events.gameObject, true);
                else SceneVisibilityManager.instance.Show(events.gameObject, true);
            SceneView.RepaintAll();
        }
        static void LayerUI(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerMask.NameToLayer("UI");
        }
        public static GameObject CreateMap()
        {
            const int count = 40;
            const float cell = 72f / count;
            var sprites = Enumerable.Range(1, 9).Select(i => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/bk1440-s/{i}.png")).ToArray();
            if (sprites.Any(s => s == null)) throw new InvalidOperationException("Missing terrain sprite");
            // Keep a stable texture binding per tile type, including SRP batch/editor captures.
            System.IO.Directory.CreateDirectory("Assets/Prefabs/Flow/TerrainMaterials");
            var materials = new Material[9];
            var template = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");
            for (int i = 0; i < materials.Length; i++)
            {
                string path = $"Assets/Prefabs/Flow/TerrainMaterials/Tile{i + 1}.mat";
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (materials[i] == null) { materials[i] = new Material(template); AssetDatabase.CreateAsset(materials[i], path); }
                materials[i].mainTexture = sprites[i].texture;
                EditorUtility.SetDirty(materials[i]);
            }
            var root = new GameObject("MapBackground");
            for (int y = 0; y < count; y++) for (int x = 0; x < count; x++)
            {
                int row = y == count - 1 ? 0 : y == 0 ? 2 : 1;
                int col = x == 0 ? 0 : x == count - 1 ? 2 : 1;
                var sprite = sprites[row * 3 + col];
                if (sprite.rect.width != sprite.rect.height) throw new InvalidOperationException("Terrain tiles must be square");
                var tile = new GameObject($"Tile_{x:00}_{y:00}");
                tile.transform.SetParent(root.transform, false);
                tile.transform.localPosition = new Vector3((x + .5f) * cell - 36, (y + .5f) * cell - 36, 0);
                // Uniform scaling preserves the source pixel aspect; never stretch an edge strip.
                float scale = cell * sprite.pixelsPerUnit / sprite.rect.width;
                tile.transform.localScale = new Vector3(scale, scale, 1);
                var renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite; renderer.sortingOrder = -100;
                // Match the existing arena's unlit material; terrain must not depend on scene lights.
                renderer.sharedMaterial = materials[row * 3 + col];
            }
            return root;
        }
        // Explicit migration: preserve other scene objects and the hand-authored demo layouts.
        [MenuItem("Tools/SpringUp/Flow/Repair Terrain and Import Laboratory")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play first");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var map = CreateMap(); PrefabUtility.SaveAsPrefabAsset(map, MapPath); Object.DestroyImmediate(map);
            foreach (string path in new[] { "Assets/Scenes/Battle_Greybox.unity", FlowApplication.BattlePath, FlowApplication.RestPath })
            {
                var scene = EditorSceneManager.OpenScene(path);
                var old = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == "MapBackground" || t.name == "ArenaTiles");
                var parent = old.parent; var position = old.localPosition;
                Object.DestroyImmediate(old.gameObject);
                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MapPath), scene);
                replacement.name = "MapBackground";
                replacement.transform.SetParent(parent, false); replacement.transform.localPosition = position;
                LayerUI(scene); EditorSceneManager.SaveScene(scene);
            }
            BuildLaboratory();
            var bootstrap = EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            var app = bootstrap.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FlowApplication>(true)).Single();
            app.laboratoryPrefab = AssetDatabase.LoadAssetAtPath<FlowWindow>(LabPath);
            app.uiRoot.GetComponentInParent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            LayerUI(bootstrap);
            EditorUtility.SetDirty(app); EditorSceneManager.SaveScene(bootstrap);
            AssetDatabase.SaveAssets();
            Debug.Log("[Presentation Repair] PASS: three maps tiled 40x40 and laboratory prefab bound.");
        }
        public static void BuildLaboratory()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SpringUpDemo.unity");
            var original = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LaboratoryPresenter>(true)).Single();
            var root = Object.Instantiate(original.gameObject); root.name = "Laboratory Window"; root.SetActive(false);
            var presenter = root.GetComponent<LaboratoryPresenter>(); presenter.source = null;
            Object.DestroyImmediate(root.GetComponent<GraphicRaycaster>());
            Object.DestroyImmediate(root.GetComponent<CanvasScaler>());
            Object.DestroyImmediate(root.GetComponent<Canvas>());
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one; rect.anchoredPosition3D = Vector3.zero; rect.sizeDelta = new Vector2(1440, 900);
            var layout = (RectTransform)root.transform.GetChild(0); layout.anchoredPosition = Vector2.zero;
            var preview = root.GetComponentInChildren<LaboratoryCombatDemo>(true);
            var panel = preview.transform;
            // The preview script and targets are not dependencies of the production inventory.
            foreach (Transform child in panel.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            Object.DestroyImmediate(preview);
            var view = root.AddComponent<FlowWindow>();
            view.group = root.AddComponent<CanvasGroup>(); view.body = panel;
            view.title = layout.GetComponentsInChildren<Text>(true).First(t => t.gameObject.name == "Title");
            view.title.text = "实验室 · 装备";
            var column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(20,20,20,20); column.spacing = 16;
            column.childControlHeight = true; column.childControlWidth = true; column.childForceExpandHeight = false;
            var binding = root.AddComponent<FlowLaboratoryWindow>(); binding.presenter = presenter;
            binding.status = view.AddText(""); binding.status.GetComponent<LayoutElement>().preferredHeight = 410;
            binding.closeButton = view.AddButton("关闭背包 · 返回游戏 [Esc / B / Tab]", () => {});

            binding.discardButton = view.AddButton("丢弃选中物品", () => {});
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerMask.NameToLayer("UI");
            PrefabUtility.SaveAsPrefabAsset(root, LabPath);
            Object.DestroyImmediate(root);
        }
    }
}
