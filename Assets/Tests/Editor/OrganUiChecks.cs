using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpringUp.Flow;
using SpringUp.Laboratory;
using SpringUp.Organs;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public sealed class OrganUiChecks
    {
        [UnityTest] public IEnumerator RuntimeRaycastsDragAndPausedPulse()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var canvas = new GameObject("Runtime UI", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var events = new GameObject("Runtime events", typeof(EventSystem));
            var lab = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BackpackPresenter>(FlowPresentationRepair.LabPath), canvas.transform);
            var config = Configuration(); var run = config.CreateRun(); run.StartRun(); run.CombatTimerFinished();
            string knife = run.Inventory.Spawn("rust_knife");
            var equipment = new RunLaboratoryEquipment(run, config.Definitions);
            Action<InventorySnapshot> refresh = _ => equipment.Refresh(); run.LoadoutChanged += refresh;
            try
            {
                lab.Bind(equipment); lab.gameObject.SetActive(true); yield return null;
                var source = lab.inventorySlots.First(s => s.Item?.InstanceId == knife);
                var target = lab.equipmentSlots.First(s => s.part == LaboratoryPart.Hand && s.index == 0);
                var pointer = new PointerEventData(events.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left };
                var corners = new Vector3[4]; ((RectTransform)target.transform).GetWorldCorners(corners);
                pointer.position = (corners[0] + corners[2]) * .5f;
                ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.beginDragHandler);
                Assert.IsTrue(lab.IsDragging);
                var hits = new List<RaycastResult>(); events.GetComponent<EventSystem>().RaycastAll(pointer, hits);
                Assert.IsNotEmpty(hits); Assert.AreEqual(target.gameObject, hits[0].gameObject, "Ghost and overlays must not intercept drops");
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.dropHandler);
                ExecuteEvents.Execute(source.gameObject, pointer, ExecuteEvents.endDragHandler);
                Assert.AreEqual(knife, run.Inventory.EquippedAt("hand", 1)); Assert.IsFalse(lab.IsDragging);
                Time.timeScale = 0; target.view.Pulse(); Assert.IsTrue(target.view.flash.enabled);
                yield return new WaitForSecondsRealtime(.5f); Assert.IsFalse(target.view.flash.enabled, "Flash expires while world paused");
                lab.BeginDrag(target, pointer); Assert.IsTrue(lab.IsDragging);
                lab.gameObject.SetActive(false); Assert.IsFalse(lab.IsDragging, "Closing cancels drag through actual OnDisable");
                lab.Bind(equipment); lab.gameObject.SetActive(true); lab.Select(knife); Assert.AreEqual(knife, lab.SelectedInstanceId);
            }
            finally
            {
                Time.timeScale = 1; run.LoadoutChanged -= refresh;
                Object.Destroy(canvas.gameObject); Object.Destroy(events);
            }
            yield return null;
            yield return new ExitPlayMode();
        }

        static RunConfiguration Configuration() => new RunConfiguration(
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Organs/organs.json").text,
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Waves/waves.json").text, OrganCatalog.LoadDefault());

        [Test] public void MoveSwapIsAtomicAndPhaseGuarded()
        {
            var run = Configuration().CreateRun(); run.StartRun();
            string fist = run.Inventory.EquippedAt("hand", 1);
            Assert.Throws<RunException>(() => run.MoveOrSwap(fist, "hand", 1, "head", 1));
            Assert.AreEqual(fist, run.Inventory.EquippedAt("hand", 1));
            run.CombatTimerFinished();
            string knife = run.Inventory.Spawn("rust_knife"), torso = run.Inventory.Spawn("torso_overdrive");
            run.Replace(knife, "head", 1); run.Replace(torso, "torso", 1);
            int events = 0; run.LoadoutChanged += _ => events++;
            run.MoveOrSwap(fist, "hand", 1, "head", 1);
            Assert.AreEqual(1, events); Assert.AreEqual(fist, run.Inventory.EquippedAt("head", 1));
            Assert.AreEqual(knife, run.Inventory.EquippedAt("hand", 1));
            Assert.Throws<InventoryException>(() => run.MoveOrSwap(fist, "head", 1, "torso", 1));
            Assert.AreEqual(1, events); Assert.AreEqual(torso, run.Inventory.EquippedAt("torso", 1));
            Assert.AreEqual(fist, run.Inventory.EquippedAt("head", 1));
            Assert.Throws<InventoryException>(() => run.MoveOrSwap(knife, "head", 1, "leg", 1));
            run.MoveOrSwap(fist, "head", 1, "leg", 6);
            Assert.IsNull(run.Inventory.EquippedAt("head", 1)); Assert.AreEqual(fist, run.Inventory.EquippedAt("leg", 6));
            run.MoveOrSwap(fist, "leg", 6, "leg", 6);
            Assert.AreEqual(fist, run.Inventory.EquippedAt("leg", 6));
            var snapshot = run.Inventory.Snapshot();
            var ids = snapshot.Equipped.Values.SelectMany(x => x.Values).Concat(snapshot.Unequipped.Select(x => x.instance_id)).ToArray();
            Assert.AreEqual(run.Inventory.Instances.Count, ids.Length); Assert.AreEqual(ids.Length, ids.Distinct().Count());
        }

        [Test] public void DragReplaceSwapUnloadCancelAndFixedInventory()
        {
            var config = Configuration(); var run = config.CreateRun(); run.StartRun(); run.CombatTimerFinished();
            string knife = run.Inventory.Spawn("rust_knife");
            while (run.Inventory.UnequippedIds().Count < Inventory.BackpackCapacity - 1) run.Inventory.Spawn("fist");
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BackpackPresenter>(FlowPresentationRepair.LabPath));
            var events = new GameObject("UI events", typeof(EventSystem));
            var equipment = new RunLaboratoryEquipment(run, config.Definitions);
            Action<InventorySnapshot> refresh = _ => equipment.Refresh(); run.LoadoutChanged += refresh;
            try
            {
                root.Bind(equipment); root.gameObject.SetActive(true);
                var pointer = new PointerEventData(events.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left };
                var source = root.inventorySlots.First(s => s.Item?.InstanceId == knife);
                var hand = root.equipmentSlots.First(s => s.part == LaboratoryPart.Hand && s.index == 0);
                string fist = hand.Item.InstanceId;
                root.BeginDrag(source, pointer); Assert.IsTrue(root.IsDragging); root.Drop(hand);
                Assert.AreEqual(knife, run.Inventory.EquippedAt("hand", 1)); Assert.Contains(fist, run.Inventory.UnequippedIds());
                Assert.AreEqual(knife, root.SelectedInstanceId); StringAssert.Contains("锈刀", root.detail.text);
                var head = root.equipmentSlots.First(s => s.part == LaboratoryPart.Head && s.index == 0);
                root.BeginDrag(hand, pointer); root.Drop(head);
                Assert.AreEqual(knife, run.Inventory.EquippedAt("head", 1)); Assert.IsNull(run.Inventory.EquippedAt("hand", 1));
                var torso = root.equipmentSlots.First(s => s.part == LaboratoryPart.Torso);
                root.BeginDrag(head, pointer); Assert.IsFalse(root.CanDrop(torso)); root.Drop(torso);
                Assert.AreEqual(knife, run.Inventory.EquippedAt("head", 1)); Assert.IsFalse(root.IsDragging);
                root.BeginDrag(head, pointer); root.CancelDrag(); Assert.AreEqual(knife, run.Inventory.EquippedAt("head", 1));
                root.BeginDrag(head, pointer); root.DropToInventory();
                Assert.IsNull(run.Inventory.EquippedAt("head", 1)); Assert.Contains(knife, run.Inventory.UnequippedIds());
                Assert.IsTrue(root.inventorySlots.Any(s => s.Item?.InstanceId == knife), "Unloaded item is visible in the fixed backpack");
                root.Select(knife); root.Refresh(); Assert.AreEqual(knife, root.SelectedInstanceId);
                Assert.AreEqual(Inventory.BackpackCapacity, root.inventorySlots.Length);
                Assert.IsFalse(root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Previous page" || t.name == "Next page" || t.name == "Page"));
                root.ReadOnly = true; root.BeginDrag(root.inventorySlots.First(s => s.Item != null), pointer); Assert.IsFalse(root.IsDragging);
                root.ReadOnly = false; root.BeginDrag(root.inventorySlots.First(s => s.Item != null), pointer);
                root.Refresh(); Assert.IsFalse(root.IsDragging);
                root.BeginDrag(root.inventorySlots.First(s => s.Item != null), pointer); root.CancelDrag();
                Assert.IsFalse(root.IsDragging); root.gameObject.SetActive(true); root.Bind(equipment); root.Bind(equipment);
                root.Select(knife); run.Discard(knife); Assert.IsNull(root.SelectedInstanceId);
            }
            finally { run.LoadoutChanged -= refresh; Object.DestroyImmediate(root.gameObject); Object.DestroyImmediate(events); }
        }

        [Test] public void ActualActivationAndIsolatedPreviewSharePulses()
        {
            var go = new GameObject("Isolated preview pipeline"); var art = AssetDatabase.LoadAssetAtPath<OrganArtCatalog>(OrganUiBuilder.ArtPath);
            var view = OrganUiBuilder.Icon(go.transform, "Preview icon", art, 0, 0, 64, false);
            float previousTime = Time.timeScale;
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>(); pipeline.AutomaticExecution = false;
                var def = new SpringUp.Organs.OrganDefinition("fist", "拳头", OrganType.Actuator, 5);
                var instance = new SpringUp.Organs.OrganInstance(def); pipeline.GetBody(BodyPart.Hand).SetSlot(0, instance);
                view.Bind(new LaboratoryItem("preview", def)); view.SetSelected(true); view.SetDropHint(true);
                int count = 0;
                using (var binding = new OrganPulseBinding(pipeline, (part, index) => { count++; view.Pulse(); }))
                {
                    pipeline.TargetSelector = () => null;
                    Assert.IsFalse(pipeline.TryExecuteSlot(BodyPart.Hand, 0)); Assert.AreEqual(0, count);
                    pipeline.TargetSelector = () => "target"; Time.timeScale = 0;
                    Assert.IsTrue(pipeline.TryExecuteSlot(BodyPart.Hand, 0)); Assert.AreEqual(1, count);
                    Assert.IsTrue(view.flash.enabled); Assert.IsTrue(view.selection.enabled); Assert.IsTrue(view.dropHint.enabled);
                }
                pipeline.TryExecuteSlot(BodyPart.Hand, 0); Assert.AreEqual(1, count, "Disposed adapter unsubscribes");
                pipeline.GetBody(BodyPart.Hand).SetSlot(1, new SpringUp.Organs.OrganInstance(
                    new SpringUp.Organs.OrganDefinition("trigger", "触发器", OrganType.Trigger)));
                pipeline.GetBody(BodyPart.Hand).SetSlot(2, new SpringUp.Organs.OrganInstance(def));
                var pulses = new List<int>();
                using (var binding = new OrganPulseBinding(pipeline, (part, index) => pulses.Add(index)))
                    pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, new CastEvent("target", 5, def.Id, instance.InstanceId)));
                CollectionAssert.AreEqual(new[] { 1, 2 }, pulses, "Trigger and successfully activated successor both flash");
                view.Bind(new LaboratoryItem("missing", new SpringUp.Organs.OrganDefinition("missing", "未绘制器官", OrganType.Actuator)));
                Assert.IsFalse(view.icon.enabled); Assert.AreEqual("未绘制器官", view.fallback.text); Assert.IsFalse(view.flash.enabled);
                Assert.AreEqual(7, art.entries.Length); foreach (var item in art.entries) Assert.IsNotNull(item.sprite);
            }
            finally { Time.timeScale = previousTime; Object.DestroyImmediate(go); }
        }

        public static void BuildAndCheck() { OrganUiBuilder.Build(); RunBatch(); }
        public static void RunBatch()
        {
            try
            {
                var checks = new OrganUiChecks();
                checks.MoveSwapIsAtomicAndPhaseGuarded();
                checks.DragReplaceSwapUnloadCancelAndFixedInventory();
                checks.ActualActivationAndIsolatedPreviewSharePulses();
                new OrganCombatReportChecks().BoundedHistoryAttributionAndRebinding();
                Capture();
                Debug.Log("[Organ UI] PASS: atomic transactions, read-only, drag/drop, cancellation, selection, fixed inventory, missing art, reusable paused pulses and existing combat report.");
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvas = new GameObject("UI verification canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            var camera = new GameObject("UI camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -100); camera.orthographic = true; camera.orthographicSize = 450;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .09f, .11f);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            var config = Configuration(); var run = config.CreateRun(); run.StartRun(); run.CombatTimerFinished();
            foreach (var id in OrganTestFactory.Ids.Take(8)) run.Inventory.Spawn(id);
            run.Replace(run.Inventory.Spawn("multi_tentacle"), "hand", 2);
            run.Replace(run.Inventory.Spawn("tnt"), "hand", 3);
            while (!run.Inventory.IsBackpackFull) run.Inventory.Spawn("trigger_hit");
            var equipment = new RunLaboratoryEquipment(run, config.Definitions);
            var lab = Object.Instantiate(AssetDatabase.LoadAssetAtPath<FlowLaboratoryWindow>(FlowPresentationRepair.LabPath), canvas.transform);
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 24);
            foreach (var text in lab.GetComponentsInChildren<Text>(true)) text.font = font;
            lab.backpack.Bind(equipment); lab.gameObject.SetActive(true);
            lab.backpack.Select(run.Inventory.EquippedAt("hand", 2));
            lab.status.text = "休整期 · 拖动调整装备\n背包打开期间世界暂停；关闭后可攻击休整区木桩测试构筑。";
            Directory.CreateDirectory("Logs/OrganUI");
            foreach (var size in new[] { new Vector2Int(1440, 900), new Vector2Int(1280, 720), new Vector2Int(1024, 768) })
            {
                canvas.scaleFactor = Mathf.Min(size.x / 1440f, size.y / 900f);
                Render(camera, size.x, size.y, "backpack-" + size.x, () =>
                {
                    foreach (var rect in new[] { (RectTransform)lab.closeButton.transform, (RectTransform)lab.backpack.inventorySlots[13].transform, (RectTransform)lab.backpack.detail.transform })
                    {
                        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                        foreach (var corner in corners)
                        { var point = camera.WorldToViewportPoint(corner); Assert.That(point.x, Is.InRange(0f, 1f)); Assert.That(point.y, Is.InRange(0f, 1f)); }
                    }
                });
            }
            lab.gameObject.SetActive(false); canvas.scaleFactor = 1;
            var hud = RunEquipmentHud.Create(canvas.transform, font); hud.Bind(equipment);
            hud.slots.First(s => s.part == LaboratoryPart.Hand && s.index == 1).view.Pulse();
            Render(camera, 1440, 900, "hud");
            Object.DestroyImmediate(canvas.gameObject); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(font);
        }
        static void Render(Camera camera, int width, int height, string name, Action check = null)
        {
            var texture = new RenderTexture(width, height, 24); var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = texture; Canvas.ForceUpdateCanvases(); camera.Render();
                check?.Invoke(); RenderTexture.active = texture; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                File.WriteAllBytes("Logs/OrganUI/" + name + ".png", pixels.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(texture); Object.DestroyImmediate(pixels); }
        }
    }
}
