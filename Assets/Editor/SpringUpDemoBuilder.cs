using System;
using System.Linq;
using SpringUp.Laboratory;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class SpringUpDemoBuilder
    {
        public const string ScenePath = "Assets/Scenes/SpringUpDemo.unity";
        [MenuItem("Tools/SpringUp/Build Unified Demo")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止 Play。");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (System.IO.File.Exists(ScenePath)) throw new InvalidOperationException("统一场景已存在，请直接打开。生成器不会覆盖手工修改。");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Battle_Greybox.unity");
            EditorSceneManager.SaveScene(scene, ScenePath);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var debug in root.GetComponentsInChildren<DevDamageField>(true)) Object.DestroyImmediate(debug);
                foreach (var debug in root.GetComponentsInChildren<BattleDebugHud>(true)) Object.DestroyImmediate(debug);
                foreach (var debug in root.GetComponentsInChildren<WaveHud>(true)) Object.DestroyImmediate(debug);
            }
            var labScene = EditorSceneManager.OpenScene("Assets/Scenes/LaboratoryDemo.unity", OpenSceneMode.Additive);
            foreach (var root in labScene.GetRootGameObjects())
                if (root.GetComponent<Camera>() == null) SceneManager.MoveGameObjectToScene(root, scene);
            EditorSceneManager.CloseScene(labScene, true);
            SceneManager.SetActiveScene(scene);
            var driver = new GameObject("SpringUp Run").AddComponent<SpringUpRunDriver>();
            driver.director = Object.FindFirstObjectByType<WaveDirector>();
            driver.player = Object.FindFirstObjectByType<PlayerRuntime>();
            var motor = driver.player.GetComponent<PlayerMotor>();
            if (motor == null) motor = driver.player.gameObject.AddComponent<PlayerMotor>();
            var input = new SerializedObject(motor);
            var actions = AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/InputSystem_Actions.inputactions")
                .OfType<InputActionReference>().ToArray();
            input.FindProperty("moveAction").objectReferenceValue = actions.First(a => a.action?.name == "Move");
            input.FindProperty("sprintAction").objectReferenceValue = actions.First(a => a.action?.name == "Sprint");
            input.ApplyModifiedPropertiesWithoutUndo();
            var stats = new SerializedObject(driver.player.Stats);
            stats.FindProperty("baseCooldown").floatValue = .15f;
            stats.FindProperty("baseCycleDuration").floatValue = 2f;
            stats.ApplyModifiedPropertiesWithoutUndo();
            for (int i = 0; i < PlayerRuntime.PartCount; i++) driver.player.SetPartRate(i, 1f);
            driver.battleCamera = Camera.main;
            driver.pipeline = driver.gameObject.AddComponent<OrganPipelineController>();
            driver.combat = driver.gameObject.AddComponent<BattleOrganCombat>();
            driver.gameObject.AddComponent<BattleOrganEffects>();
            driver.GetComponent<BattleOrganTargeting>().AttackOrigin = driver.player.transform;
            driver.laboratorySource = Object.FindFirstObjectByType<LaboratoryDemoBootstrap>();
            driver.presenter = Object.FindFirstObjectByType<LaboratoryPresenter>();
            driver.laboratoryRoot = driver.presenter.gameObject;
            foreach (var text in driver.laboratoryRoot.GetComponentsInChildren<Text>(true))
                if (text.text == "装备与战斗测试") text.text = "实验室";
            driver.presenter.GetComponentInChildren<LaboratoryCombatDemo>(true).restartButton.GetComponentInChildren<Text>().text = "重置预览";
            driver.laboratoryRoot.SetActive(false);
            driver.organsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Organs/organs.json");
            driver.wavesJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Waves/waves.json");
            driver.organCatalog = AssetDatabase.LoadAssetAtPath<OrganCatalog>("Assets/Resources/OrganCatalog.asset");
            var wave = new SerializedObject(driver.director);
            wave.FindProperty("autoStart").boolValue = false;
            wave.FindProperty("intervalBetweenWaves").floatValue = 0;
            wave.ApplyModifiedPropertiesWithoutUndo();

            var canvasGo = new GameObject("Run UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.GetComponent<Canvas>().sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            driver.runStatus = Label(canvasGo.transform, "Run status", font, new Vector2(24, -6), new Vector2(1100, 32), true);
            var footer = new GameObject("Settlement", typeof(RectTransform), typeof(Image));
            footer.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)footer.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.right;
            rect.pivot = Vector2.zero; rect.sizeDelta = new Vector2(0, 90);
            footer.GetComponent<Image>().color = new Color(.08f, .1f, .12f, .98f);
            driver.rewardText = Label(footer.transform, "Rewards", font, new Vector2(24, -4), new Vector2(970, 30), true);
            driver.capacityText = Label(footer.transform, "Capacity", font, new Vector2(24, -42), new Vector2(420, 30), true);
            driver.errorText = Label(canvasGo.transform, "Error", font, new Vector2(24, -42), new Vector2(1300, 32), true);
            driver.nextButton = Button(footer.transform, "下一波", font, 1220, 170);
            driver.sellButton = Button(footer.transform, "出售", font, 900, 140);
            driver.discardButton = Button(footer.transform, "丢弃", font, 1060, 140);
            driver.restartButton = Button(footer.transform, "重新开始", font, 1220, 170);
            footer.SetActive(false);
            // 为流程状态和结算条留空间，原实验室布局保持完整比例。
            var labCanvas = driver.laboratoryRoot.GetComponent<CanvasScaler>();
            labCanvas.referenceResolution = new Vector2(1440, 1020);
            var panel = driver.laboratoryRoot.transform.GetChild(0) as RectTransform;
            if (panel != null) panel.anchoredPosition += new Vector2(0, 28);
            driver.gameObject.AddComponent<RunUIFont>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[SpringUp Demo] Built " + ScenePath);
        }

        private static Text Label(Transform parent, string name, Font font, Vector2 position, Vector2 size, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, top ? 1 : 0);
            rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size;
            var label = go.GetComponent<Text>(); label.font = font; label.fontSize = 20;
            label.color = Color.white; label.raycastTarget = false; return label;
        }
        private static Button Button(Transform parent, string caption, Font font, float x, float width)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(x, 14); rect.sizeDelta = new Vector2(width, 40);
            go.GetComponent<Image>().color = new Color(.18f, .36f, .3f, 1);
            var label = Label(go.transform, "Label", font, Vector2.zero, new Vector2(width, 40), true);
            label.alignment = TextAnchor.MiddleCenter; label.text = caption;
            return go.GetComponent<Button>();
        }
    }
}
