using System;
using System.Linq;
using SpringUp.Flow;
using SpringUp.Laboratory;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    // Run in an isolated project with -executeMethod ...CounterAccessChecks.RunBatch (no -quit).
    [InitializeOnLoad]
    public static class CounterAccessChecks
    {
        const string Key = "SpringUp.CounterAccessChecks";
        static int stage;
        static double next, deadline;
        static Keyboard keyboard;
        static string spare, equipped;

        static CounterAccessChecks()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += StateChanged;
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            System.IO.Directory.CreateDirectory("Logs/Presentation");
            RestInteractionSettingsEditor.EnsureAsset();
            var scene = EditorSceneManager.OpenScene(FlowApplication.RestPath);
            var content = Object.FindAnyObjectByType<FlowContent>();
            content.EnsureMerchant();
            CheckPositions(content);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Key, true);
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.EnterPlaymode();
        }

        static void StateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 1; deadline = next + 120;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
            keyboard = InputSystem.AddDevice<Keyboard>();
            EditorApplication.update += Tick;
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Counter Access Checks] " + message); }

        static void CheckPositions(FlowContent content)
        {
            var ship = content.world.transform.Find("ShipBackground").GetComponent<SpriteRenderer>();
            var bounds = ship.sprite.bounds;
            foreach (var counter in new[] { content.Merchant, content.Laboratory })
            {
                var local = ship.transform.InverseTransformPoint(counter.position);
                float x = Mathf.InverseLerp(bounds.min.x, bounds.max.x, local.x);
                float y = Mathf.InverseLerp(bounds.min.y, bounds.max.y, local.y);
                Require(x > .63f && x < .66f, "Interaction point sits on deck to the left of the artwork");
                Require(counter == content.Merchant ? y > .58f && y < .62f : y > .36f && y < .40f,
                    "Interaction point matches its painted counter");
                Require(counter.GetComponent<SpriteRenderer>() == null || !counter.GetComponent<SpriteRenderer>().enabled,
                    "Cyan placeholder hidden");
                Require(counter.GetComponentInChildren<TextMesh>(true).font != null, "Counter hint has a renderable font");
            }
            var settings = content.InteractionSettings;
            Require(!settings.shopArea.Overlaps(settings.laboratoryArea), "Painted counter interaction rectangles do not overlap");
            var playerPosition = content.player.transform.position;
            var shipPosition = ship.transform.position; var shipScale = ship.transform.localScale;
            bool worldActive = content.world.activeSelf; content.world.SetActive(true);
            Vector3 At(Vector2 uv) => ship.transform.TransformPoint(new Vector3(bounds.min.x + bounds.size.x * uv.x, bounds.min.y + bounds.size.y * uv.y, 0));
            try
            {
                foreach (var counter in new[] { content.Merchant, content.Laboratory })
                {
                    var area = counter == content.Merchant ? settings.shopArea : settings.laboratoryArea;
                    foreach (var uv in new[] { area.center, area.min + Vector2.one * .001f, area.max - Vector2.one * .001f })
                    {
                        content.player.transform.position = At(uv);
                        Require(content.IsNearCounter(counter), "Counter art interior and corner approaches are interactive: " + counter.name + " " + uv);
                        Require(!content.IsNearCounter(counter == content.Merchant ? content.Laboratory : content.Merchant), "Only the intended counter opens");
                    }
                    foreach (var uv in new[] { new Vector2(area.xMin - .001f, area.center.y), new Vector2(area.xMax + .001f, area.center.y),
                        new Vector2(area.center.x, area.yMin - .001f), new Vector2(area.center.x, area.yMax + .001f) })
                    { content.player.transform.position = At(uv); Require(!content.IsNearCounter(counter), "Outside each edge cannot interact"); }
                }
                content.player.transform.position = At(new Vector2(.75f, (settings.shopArea.yMin + settings.laboratoryArea.yMax) * .5f));
                Require(!content.IsNearCounter(content.Merchant) && !content.IsNearCounter(content.Laboratory), "Corridor does not trigger either room");
                ship.transform.position += new Vector3(3, -2, 0); ship.transform.localScale *= 1.3f;
                content.player.transform.position = At(settings.shopArea.center);
                Require(content.IsNearCounter(content.Merchant), "Zone follows translated and scaled artwork");
            }
            finally
            {
                content.player.transform.position = playerPosition; ship.transform.position = shipPosition; ship.transform.localScale = shipScale;
                content.world.SetActive(worldActive);
            }
            var merchant = content.Merchant; var laboratory = content.Laboratory;
            content.EnsureMerchant();
            Require(content.Merchant == merchant && content.Laboratory == laboratory, "Counter setup is idempotent");
        }

        static void Locked(RunController run)
        {
            int events = 0, marrow = run.Marrow, count = run.Inventory.Instances.Count;
            void Changed(InventorySnapshot _) => events++;
            run.LoadoutChanged += Changed;
            try
            {
                foreach (Action action in new Action[] {
                    () => run.Equip(spare, "head", 1), () => run.Replace(spare, "hand", 1),
                    () => run.Unequip("hand", 1), () => run.MoveOrSwap(equipped, "hand", 1, "leg", 1),
                    () => run.Discard(equipped), () => run.Sell(equipped) })
                {
                    bool rejected = false;
                    try { action(); } catch (RunException) { rejected = true; }
                    Require(rejected, "All equipment mutations must reject outside the laboratory");
                }
                Require(events == 0 && marrow == run.Marrow && count == run.Inventory.Instances.Count
                    && run.Inventory.EquippedAt("hand", 1) == equipped && run.Inventory.UnequippedIds().Contains(spare),
                    "Rejected operations preserve loadout, inventory and money without events");
            }
            finally { run.LoadoutChanged -= Changed; }
        }

        static FlowLaboratoryWindow Bag() => Object.FindAnyObjectByType<FlowLaboratoryWindow>();
        static void Press(UnityEngine.InputSystem.Key key) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        static void Release() => InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout at stage " + stage);
                var app = Object.FindAnyObjectByType<FlowApplication>();
                if (app == null || app.IsTransitioning || app.Content == null) return;
                var content = app.Content; var run = app.Session?.Run;
                switch (stage)
                {
                    case 0:
                        app.StartNewRun(); break;
                    case 1:
                        equipped = run.Inventory.EquippedAt("hand", 1);
                        spare = run.Inventory.Spawn("rust_knife");
                        // Stock/prices are randomized; fund the trade check independently of the wave reward.
                        run.CreditKill("counter-access-shop-budget", 1000);
                        Locked(run);
                        Require(!app.TryOpenLaboratory(), "Battle rejects laboratory entry");
                        app.ToggleInventory(); Require(Bag().backpack.ReadOnly, "Battle bag is read-only");
                        app.CloseTop(); content.director.CompleteCurrentWave(); break;
                    case 2:
                        CheckPositions(content);
                        content.player.transform.position = Vector3.zero;
                        Require(!app.TryInteract() && !app.HasModal, "Empty deck has no interaction");
                        Locked(run);
                        content.player.transform.position = content.Merchant.position;
                        Require(!app.CanInteractWithLaboratory(), "Store cannot open laboratory");
                        content.player.transform.position = content.Laboratory.position;
                        Require(!app.CanInteractWithShop(), "Laboratory cannot open store");
                        Press(UnityEngine.InputSystem.Key.B); break;
                    case 3:
                        Require(Bag().backpack.ReadOnly && !Bag().discardButton.interactable, "B at LAB still opens read-only bag");
                        Require(Bag().status != null && Bag().status.text.Contains("仅可查看"), "Bag visibly explains the restriction");
                        Locked(run);
                        var source = Bag().backpack.inventorySlots.First(s => s.Item?.InstanceId == spare);
                        Bag().backpack.BeginDrag(source, new PointerEventData(app.eventSystem));
                        Require(!Bag().backpack.IsDragging, "Read-only bag cannot start a drag");
                        FlowPresentationChecks.CaptureFlowUI(app, "counter_read_only_bag.png");
                        app.CloseTop(); Release(); break;
                    case 4:
                        CaptureCounters(content);
                        Press(UnityEngine.InputSystem.Key.E); break;
                    case 5:
                        Require(app.CanModifyEquipment && !Bag().backpack.ReadOnly, "E at LAB opens editable laboratory");
                        Require(Bag().status.text.Contains("实验室"), "Laboratory shows its editing status");
                        Require(Time.timeScale == 0 && !app.WorldInputAllowed, "Laboratory pauses world");
                        app.OpenWindow(WindowKind.Settings); Locked(run); app.CloseTop();
                        var bag = Bag().backpack;
                        var pointer = new PointerEventData(app.eventSystem);
                        bag.BeginDrag(bag.inventorySlots.First(s => s.Item?.InstanceId == spare), pointer);
                        bag.Drop(bag.equipmentSlots.First(s => s.part == LaboratoryPart.Hand && s.index == 0));
                        Require(run.Inventory.EquippedAt("hand", 1) == spare, "Real UI drag replaces equipment");
                        run.MoveOrSwap(spare, "hand", 1, "head", 1);
                        run.Unequip("head", 1); run.Equip(equipped, "hand", 1);
                        run.Equip(spare, "head", 1);
                        run.MoveOrSwap(equipped, "hand", 1, "head", 1);
                        Require(run.Inventory.EquippedAt("head", 1) == equipped && run.Inventory.EquippedAt("hand", 1) == spare,
                            "Laboratory supports swapping occupied slots");
                        run.MoveOrSwap(equipped, "head", 1, "hand", 1); run.Unequip("head", 1);
                        FlowPresentationChecks.CaptureFlowUI(app, "counter_laboratory.png");
                        bag.BeginDrag(bag.inventorySlots.First(s => s.Item?.InstanceId == spare), pointer);
                        content.player.transform.position = Vector3.zero;
                        bag.Drop(bag.equipmentSlots.First(s => s.part == LaboratoryPart.Hand && s.index == 0));
                        Locked(run); Release(); break;
                    case 6:
                        Require(Bag().backpack.ReadOnly && !Bag().backpack.IsDragging, "Leaving LAB revokes UI access and cancels drag");
                        app.CloseTop();
                        content.player.transform.position = content.Laboratory.position;
                        Require(app.TryOpenLaboratory(), "Laboratory reopens");
                        var backpack = Bag().backpack;
                        backpack.BeginDrag(backpack.inventorySlots.First(s => s.Item?.InstanceId == spare), new PointerEventData(app.eventSystem));
                        app.CloseTop(); Require(!backpack.IsDragging, "Closing laboratory cancels pending drag");
                        Locked(run);
                        content.player.transform.position = content.Merchant.position;
                        Press(UnityEngine.InputSystem.Key.E); break;
                    case 7:
                        Require(app.HasModal && Bag() == null && !app.CanModifyEquipment, "E at STORE opens shop only");
                        Locked(run);
                        var sale = run.Inventory.Spawn("rust_knife");
                        int money = run.Marrow, price = run.Inventory.SalePrice(sale);
                        run.Sell(sale); Require(run.Marrow == money + price, "Shop still sells unequipped organs");
                        var offer = run.GetShopOffers().First(o => !o.Purchased && o.Price <= run.Marrow);
                        Require(run.TryPurchaseShopOffer(offer).Ok, "Shop purchase still works");
                        app.CloseTop(); Release();
                        content.player.transform.position = content.Laboratory.position;
                        app.TryOpenLaboratory(); run.Replace(spare, "hand", 1);
                        app.CloseTop(); app.NextWave(); break;
                    case 8:
                        Require(content.kind == ContentKind.Battle && run.Inventory.EquippedAt("hand", 1) == spare,
                            "Laboratory loadout persists into next battle");
                        string old = equipped; equipped = spare; spare = old;
                        Locked(run);
                        Debug.Log("[Counter Access Checks] PASS: saved counter placement, B/E input, read-only bag, equip/replace/swap/unequip, stale drag, close revocation, shop trade, next-wave persistence.");
                        Finish(0); return;
                }
                stage++; next = EditorApplication.timeSinceStartup + .3;
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void Finish(int result)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= StateChanged;
            EditorApplication.Exit(result);
        }

        static void CaptureCounters(FlowContent content)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = content.world.GetComponentInChildren<Camera>();
            var position = camera.transform.position; float size = camera.orthographicSize;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = new RenderTexture(1280, 900, 24);
            var pixels = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = new Vector3(9, 0, -10); camera.orthographicSize = 10;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes("Logs/Presentation/counter_positions.png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.transform.position = position; camera.orthographicSize = size;
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
