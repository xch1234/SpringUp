using System;
using System.Linq;
using SpringUp.Flow;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class FlowSceneBuilder
    {
        public const string BootstrapPath = "Assets/Scenes/Flow/Bootstrap.unity";
        const string PrefabPath = "Assets/Prefabs/Flow/Window.prefab";
        [MenuItem("Tools/SpringUp/Flow/Open Bootstrap")]
        public static void Open() { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(BootstrapPath); }
        [MenuItem("Tools/SpringUp/Flow/Generate Missing Scenes")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play mode first");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            System.IO.Directory.CreateDirectory("Assets/Scenes/Flow");
            System.IO.Directory.CreateDirectory("Assets/Prefabs/Flow");
            if (!System.IO.File.Exists(PrefabPath)) BuildWindow();
            if (!System.IO.File.Exists(FlowApplication.BattlePath)) BuildWorld(ContentKind.Battle, FlowApplication.BattlePath);
            if (!System.IO.File.Exists(FlowApplication.RestPath)) BuildWorld(ContentKind.Rest, FlowApplication.RestPath);
            if (!System.IO.File.Exists(FlowApplication.MenuPath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("MainMenu Entry").AddComponent<FlowContent>().kind = ContentKind.MainMenu;
                EditorSceneManager.SaveScene(scene, FlowApplication.MenuPath);
            }
            if (!System.IO.File.Exists(BootstrapPath)) BuildBootstrap();
            var paths = new[] { BootstrapPath, FlowApplication.MenuPath, FlowApplication.BattlePath, FlowApplication.RestPath };
            EditorBuildSettings.scenes = paths.Select(p => new EditorBuildSettingsScene(p, true))
                .Concat(EditorBuildSettings.scenes.Where(s => !paths.Contains(s.path))).ToArray();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[Flow Build] PASS");
        }
        // Narrow migration: preserve layout customizations while removing the obsolete transaction control.
        public static void UpgradeInputShop()
        {
            var root = PrefabUtility.LoadPrefabContents(FlowPresentationRepair.LabPath);
            try
            {
                var binding = root.GetComponent<FlowLaboratoryWindow>();
                if (binding.sellButton != null) Object.DestroyImmediate(binding.sellButton.gameObject);
                binding.sellButton = null;
                binding.closeButton.GetComponentInChildren<Text>().text = "关闭背包 · 返回游戏 [Esc / B / Tab]";
                PrefabUtility.SaveAsPrefabAsset(root, FlowPresentationRepair.LabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var scene = EditorSceneManager.OpenScene(FlowApplication.RestPath);
            var entry = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FlowContent>(true)).Single();
            entry.EnsureMerchant();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        static void BuildWorld(ContentKind kind, string path)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Battle_Greybox.unity");
            // Save a separate copy before modifying; source demo never changes.
            EditorSceneManager.SaveScene(scene, path);
            foreach (var enemy in Object.FindObjectsByType<EnemyRuntime>(FindObjectsInactive.Include))
                Object.DestroyImmediate(enemy.gameObject);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component is DevDamageField || component is BattleDebugHud || component is WaveHud
                        || component is EnemyCountLogger || component is BattleRunDriver)
                        Object.DestroyImmediate(component);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var events in root.GetComponentsInChildren<EventSystem>(true))
                    Object.DestroyImmediate(events.gameObject);
            var player = Object.FindAnyObjectByType<PlayerRuntime>();
            var motor = player.GetComponent<PlayerMotor>() ?? player.gameObject.AddComponent<PlayerMotor>();
            var inputs = AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/InputSystem_Actions.inputactions").OfType<InputActionReference>().ToArray();
            var motorData = new SerializedObject(motor);
            motorData.FindProperty("moveAction").objectReferenceValue = inputs.First(a => a.action?.name == "Move");
            motorData.FindProperty("sprintAction").objectReferenceValue = inputs.First(a => a.action?.name == "Sprint");
            motorData.ApplyModifiedPropertiesWithoutUndo();
            var stats = new SerializedObject(player.Stats);
            stats.FindProperty("baseCooldown").floatValue = .15f;
            stats.FindProperty("baseCycleDuration").floatValue = 2;
            stats.ApplyModifiedPropertiesWithoutUndo();
            var director = Object.FindAnyObjectByType<WaveDirector>();
            director.ConfigureExternal(7, 60);
            if (kind == ContentKind.Rest)
            {
                Object.DestroyImmediate(director); director = null;
                foreach (var spawner in Object.FindObjectsByType<EnemySpawner>())
                    Object.DestroyImmediate(spawner);
            }
            var pipeline = new GameObject("Player organ execution").AddComponent<OrganPipelineController>();
            pipeline.AutomaticExecution = false;
            var combat = pipeline.gameObject.AddComponent<BattleOrganCombat>();
            pipeline.GetComponent<BattleOrganTargeting>().AttackOrigin = player.transform;
            var effects = pipeline.gameObject.AddComponent<BattleOrganEffects>();
            EnemyRuntime dummy = null;
            if (kind == ContentKind.Rest)
            {
                var go = new GameObject("Training dummy"); go.SetActive(false);
                go.transform.position = player.transform.position + Vector3.right * 2;
                var data = go.AddComponent<EnemyData>();
                data.Configure(EnemyCatalog.Load().trainingDummy);
                dummy = go.AddComponent<EnemyRuntime>();
                var source = player.GetComponentInChildren<SpriteRenderer>();
                var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = source.sprite; renderer.color = Color.yellow;
                go.AddComponent<CircleCollider2D>().radius = .4f;
                go.SetActive(true);
            }
            var roots = scene.GetRootGameObjects();
            var world = new GameObject("World (explicitly activated by entry)");
            foreach (var root in roots) root.transform.SetParent(world.transform, true);
            var entry = new GameObject("Content Entry").AddComponent<FlowContent>();
            entry.kind = kind; entry.world = world; entry.player = player; entry.director = director;
            entry.pipeline = pipeline; entry.combat = combat; entry.effects = effects; entry.dummy = dummy;
            entry.EnsureMerchant();
            BattleMapAuthoring.Configure(entry);
            world.SetActive(false);
            EditorSceneManager.SaveScene(scene, path);
        }
        static GameObject UI(string name, Transform parent, params Type[] components)
        {
            var go = new GameObject(name, new[] { typeof(RectTransform) }.Concat(components).ToArray());
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }
        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        static Text Label(string name, Transform parent, string caption)
        {
            var go = UI(name, parent, typeof(Text)); var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 18; text.color = Color.white; text.text = caption;
            return text;
        }
        static void BuildWindow()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = UI("Flow Window", null, typeof(Image), typeof(CanvasGroup), typeof(FlowWindow));
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(660, 640);
            go.GetComponent<Image>().color = new Color(.045f, .07f, .09f, .98f);
            var view = go.GetComponent<FlowWindow>(); view.group = go.GetComponent<CanvasGroup>();
            var title = Label("Title", go.transform, "窗口");
            var tr = (RectTransform)title.transform; tr.anchorMin = new Vector2(0, 1); tr.anchorMax = Vector2.one;
            tr.pivot = new Vector2(.5f, 1); tr.anchoredPosition = new Vector2(0, -10); tr.sizeDelta = new Vector2(-24, 36);
            title.fontSize = 24; title.alignment = TextAnchor.MiddleCenter; view.title = title;
            var scroll = UI("Scroll", go.transform, typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            var sr = scroll.GetComponent<ScrollRect>(); Stretch((RectTransform)scroll.transform);
            ((RectTransform)scroll.transform).offsetMin = new Vector2(14, 14);
            ((RectTransform)scroll.transform).offsetMax = new Vector2(-14, -54);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, .05f);
            var body = UI("Body", scroll.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var br = (RectTransform)body.transform; br.anchorMin = new Vector2(0, 1); br.anchorMax = Vector2.one;
            br.pivot = new Vector2(.5f, 1); br.sizeDelta = Vector2.zero;
            var layout = body.GetComponent<VerticalLayoutGroup>(); layout.spacing = 6;
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = br; sr.viewport = (RectTransform)scroll.transform; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
            view.body = body.transform;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        }
        static void BuildBootstrap()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = new GameObject("Flow Bootstrap").AddComponent<FlowApplication>();
            app.rewardFlightSettings = AssetDatabase.LoadAssetAtPath<RewardFlightSettings>("Assets/Data/Rewards/RewardFlightSettings.asset");
            app.organsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Organs/organs.json");
            app.wavesJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Waves/waves.json");
            app.organCatalog = AssetDatabase.LoadAssetAtPath<OrganCatalog>("Assets/Resources/OrganCatalog.asset");
            app.laboratoryPrefab = AssetDatabase.LoadAssetAtPath<FlowWindow>(FlowPresentationRepair.LabPath);
            app.windowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<FlowWindow>();
            var camera = new GameObject("Menu Camera", typeof(Camera), typeof(AudioListener));
            app.menuCamera = camera.GetComponent<Camera>(); app.menuCamera.clearFlags = CameraClearFlags.SolidColor;
            app.menuCamera.backgroundColor = new Color(.03f, .045f, .065f);
            var canvas = UI("Global UI", null, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            app.uiRoot = UI("Windows", canvas.transform).transform; Stretch((RectTransform)app.uiRoot);
            app.hud = Label("Status", canvas.transform, "春生");
            var rect = (RectTransform)app.hud.transform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = new Vector2(-24, 60);
            app.hud.raycastTarget = false;
            app.loading = UI("Loading", canvas.transform, typeof(Image)); Stretch((RectTransform)app.loading.transform);
            app.loading.GetComponent<Image>().color = new Color(0, 0, 0, .9f);
            var label = Label("Loading title", app.loading.transform, "正在加载……");
            Stretch((RectTransform)label.transform); label.alignment = TextAnchor.MiddleCenter;
            app.loading.SetActive(false);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            app.eventSystem = events.GetComponent<EventSystem>();
            EditorSceneManager.SaveScene(scene, BootstrapPath);
        }
    }
}
