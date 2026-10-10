using System;
using System.IO;
using System.Linq;
using SpringUp.Laboratory;
using SpringUp.Organs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.LaboratoryEditor
{
    public static class LaboratoryViewChecks
    {
        [MenuItem("Tools/SpringUp/Check Laboratory UI")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止 Play。");
            if (SceneManager.GetSceneByPath(LaboratorySceneMenu.ScenePath).isLoaded)
                throw new InvalidOperationException("请先关闭实验室场景，避免影响未保存的编辑。");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(LaboratorySceneMenu.ScenePath, OpenSceneMode.Additive);
            try
            {
                var presenter = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LaboratoryPresenter>()).Single();
                var demo = presenter.GetComponentInChildren<LaboratoryCombatDemo>();
                Require(presenter.GetComponentsInChildren<Outline>().Length == 0, "左侧不含装备高亮组件。");
                Require(presenter.GetComponentsInChildren<OrganPipelineDemo>().Length == 0, "不挂载自动装入固定装备的演示组件。");
                foreach (var card in presenter.GetComponentsInChildren<OrganCardView>())
                    Require(PrefabUtility.IsPartOfPrefabInstance(card), "保存的卡片保持 Prefab 关联。");
                presenter.InitializeView(); demo.Initialize();
                TemporaryEquipmentChecks.Run();
                var equipment = (TemporaryEquipment)presenter.source.Equipment;
                LaboratorySettlementChecks.Run(presenter, demo);
                Directory.CreateDirectory("Logs/LaboratoryRebuild");
                // 最长常驻状态：六个独立黑洞，叠加三种状态与完整最近事件。
                for (int slot = 0; slot < 6; slot++) equipment.TryUnequip(LaboratoryPart.Hand, slot, out _);
                var collapse = OrganCatalog.LoadDefault().CreateDefinitions().Single(d => d.Id == "collapse_body");
                for (int slot = 0; slot < 6; slot++)
                {
                    var item = new LaboratoryItem("visual_hole_" + slot, collapse); equipment.AddSample(item);
                    equipment.TryEquip(item.InstanceId, LaboratoryPart.Hand, slot, out _);
                }
                demo.Pipeline.GetBody(BodyPart.Hand).Tick(6);
                foreach (var definition in OrganCatalog.LoadDefault().CreateDefinitions().Where(d => d.Status != null))
                    for (int target = 0; target < 2; target++)
                        demo.StatusAt(target).TryApply(new CastEvent(demo.targets[target].TargetId, definition.Damage,
                            definition.Id, "visual", new CastContext(), definition.Status), 0);
                demo.Advance(0);
                presenter.SelectInventory(equipment.Inventory.First(x => x.DefinitionId == "slime_gland").InstanceId);
                Capture(presenter, equipment.Inventory.Count, 1440, 900, "Logs/LaboratoryRebuild/demo.png");
                Capture(presenter, equipment.Inventory.Count, 1280, 720, "Logs/LaboratoryRebuild/demo-720.png");
                for (int i = 0; i < 30; i++) equipment.AddSample(new LaboratoryItem("extra" + i, OrganCatalog.LoadDefault().CreateDefinitions()[0]));
                Capture(presenter, equipment.Inventory.Count, 1024, 768, "Logs/LaboratoryRebuild/inventory-overflow.png");
                File.WriteAllText("Logs/LaboratoryRebuild/result.txt", "通过：装备事务、A 的伤害与击杀链、选择独立、重置、空装备与仅触手、重新绑定、Prefab 关联及背包溢出。Play 检查单独运行。");
                Debug.Log("[Laboratory Rebuild Checks] PASS");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        private static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("[Laboratory Rebuild] " + message);
        }
        private static void Capture(LaboratoryPresenter presenter, int inventoryCount, int width, int height, string path)
        {
            var cameraObject = new GameObject("Temporary UI verification camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(15, 22, 31, 255);
            camera.orthographic = true; camera.orthographicSize = height / 2f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            var texture = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            Canvas canvas = presenter.GetComponent<Canvas>();
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            var originalMode = canvas.renderMode;
            var originalCamera = canvas.worldCamera;
            float originalDistance = canvas.planeDistance;
            float originalScale = canvas.scaleFactor;
            bool originalScalerEnabled = scaler.enabled;
            var scroll = presenter.inventoryRoot.GetComponentInParent<ScrollRect>();
            Vector2 originalPosition = scroll.normalizedPosition;
            try
            {
                camera.targetTexture = texture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 10;
                scaler.enabled = false;
                canvas.scaleFactor = Mathf.Min(width / 1440f, height / 900f);
                Canvas.ForceUpdateCanvases();
                Require(presenter.inventoryRoot.childCount == inventoryCount, "全部库存都有对应卡片。");
                var demo = presenter.GetComponentInChildren<LaboratoryCombatDemo>();
                foreach (var text in new[] { presenter.selectionText, demo.statusText, demo.eventText })
                    Require(text.preferredHeight <= text.rectTransform.rect.height + 1, "文字不能垂直溢出：" + text.name);
                // 超出容量的卡片会被裁剪；检查视口边界及滚动后首尾卡片是否完整可见。
                var corners = new Vector3[4];
                scroll.viewport.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 point = camera.WorldToViewportPoint(corner);
                    Require(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1, "背包视口不能超出屏幕。");
                }
                if (inventoryCount > 0)
                {
                    scroll.verticalNormalizedPosition = 1;
                    Canvas.ForceUpdateCanvases();
                    RequireVisible(scroll.viewport, presenter.inventoryRoot.GetChild(0));
                    scroll.verticalNormalizedPosition = 0;
                    Canvas.ForceUpdateCanvases();
                    RequireVisible(scroll.viewport, presenter.inventoryRoot.GetChild(inventoryCount - 1));
                }
                scroll.normalizedPosition = originalPosition;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                scroll.normalizedPosition = originalPosition;
                canvas.renderMode = originalMode; canvas.worldCamera = originalCamera;
                canvas.planeDistance = originalDistance; canvas.scaleFactor = originalScale;
                scaler.enabled = originalScalerEnabled;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
        private static void RequireVisible(RectTransform viewport, Transform item)
        {
            var corners = new Vector3[4];
            ((RectTransform)item).GetWorldCorners(corners);
            Rect bounds = viewport.rect;
            foreach (Vector3 corner in corners)
            {
                Vector3 local = viewport.InverseTransformPoint(corner);
                Require(local.x >= bounds.xMin - 1 && local.x <= bounds.xMax + 1 &&
                    local.y >= bounds.yMin - 1 && local.y <= bounds.yMax + 1,
                    "滚动后首尾卡片必须完整可见。");
            }
        }
    }
}
