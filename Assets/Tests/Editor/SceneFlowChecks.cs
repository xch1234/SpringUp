using System;
using System.Linq;
using NUnit.Framework;
using SpringUp.Flow;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public class SceneFlowCoreTests
    {
        RunConfiguration Config() => new RunConfiguration(
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Organs/organs.json").text,
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Waves/waves.json").text, OrganCatalog.LoadDefault());
        [Test] public void NestedPauseLeasesAreIdempotent()
        {
            using var service = new PauseService();
            var a = service.Acquire(); var b = service.Acquire();
            a.Dispose(); a.Dispose(); Assert.AreEqual(0, Time.timeScale);
            b.Dispose(); Assert.AreEqual(1, Time.timeScale);
            var stale = service.Acquire(); service.Dispose();
            using var nextService = new PauseService();
            using var active = nextService.Acquire();
            stale.Dispose(); Assert.AreEqual(0, Time.timeScale, "Old service token cannot unpause a new owner");
        }
        [Test] public void RewardsOverflowButPurchaseAndNextWaveAreAtomic()
        {
            var run = Config().CreateRun(); run.StartRun();
            Assert.Throws<RunException>(() => run.Unequip("hand", 1));
            run.CombatTimerFinished();
            int reward = run.Marrow;
            Assert.Throws<RunException>(() => run.CombatTimerFinished());
            Assert.AreEqual(reward, run.Marrow);
            while (run.Inventory.UnequippedWithinCap()) run.Inventory.Spawn("fist");
            int count = run.Inventory.Instances.Count;
            Assert.IsFalse(run.TryPurchase("rust_knife").Ok);
            Assert.AreEqual(reward, run.Marrow); Assert.AreEqual(count, run.Inventory.Instances.Count);
            Assert.Throws<RunException>(() => run.StartNextWave());
            while (!run.CanStartNextWave()) run.Discard(run.Inventory.UnequippedIds()[0]);
            run.Discard(run.Inventory.UnequippedIds()[0]);
            Assert.IsTrue(run.TryPurchase("rust_knife").Ok);
            int paid = reward - run.Marrow;
            int list = run.Inventory.Catalog["rust_knife"].shop_price;
            int low = EconomySwing.RoundAwayFromZero(list, 0, EconomySwing.PurchaseHalfPercent);
            int high = EconomySwing.RoundAwayFromZero(list, 1, EconomySwing.PurchaseHalfPercent);
            Assert.That(paid, Is.InRange(Math.Min(low, high), Math.Max(low, high)));
        }
    }

    [InitializeOnLoad]
    public static class SceneFlowChecks
    {
        const string Key = "SpringUp.SceneFlow.Checks";
        static int stage;
        static bool shortcutsChecked;
        static double next, deadline;
        static float health, charge;
        static string knife;
        static Keyboard keyboard;
        static Vector3 position;
        static GameObject pausedEnemy;
        static Projectile pausedProjectile;
        static EnemyRuntime keyboardTarget;
        static int reward, wave;
        static SceneFlowChecks()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += StateChanged;
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch validation copy");
            System.IO.Directory.CreateDirectory("Logs/Presentation");
            var core = new SceneFlowCoreTests();
            core.NestedPauseLeasesAreIdempotent();
            core.RewardsOverflowButPurchaseAndNextWaveAreAtomic();
            Debug.Log("[Scene Flow Checks] CORE PASS: pause ownership, capacity and purchase transactions.");
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Key, true);
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.EnterPlaymode();
        }
        static void StateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; shortcutsChecked = false; next = EditorApplication.timeSinceStartup + 1; deadline = next + 120;
            EditorApplication.update += Tick;
        }
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("[Scene Flow Checks] " + message); }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout at stage " + stage);
                var app = Object.FindFirstObjectByType<FlowApplication>();
                if (app == null || app.IsTransitioning || app.Content == null) return;
                var content = app.Content;
                var run = app.Session?.Run;
                Require(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "One EventSystem");
                Require(Object.FindFirstObjectByType<BattleRunDriver>() == null, "No legacy automatic driver");
                Require(UnityEngine.SceneManagement.SceneManager.sceneCount == 2, "One bootstrap + one content scene");
                Require(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(x => x.isActiveAndEnabled) == 1, "One audio listener");
                switch (stage)
                {
                    case 0:
                        Require(content.kind == ContentKind.MainMenu, "Independent main menu");
                        Require(app.uiRoot.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text == "开始新局"), "Chinese main menu");
                        FlowPresentationChecks.CaptureFlowUI(app, "main-menu-zh.png");
                        app.StartNewRun(); app.StartNewRun(); stage++; break;
                    case 1:
                        if (!shortcutsChecked)
                        {
                            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                            keyboard = InputSystem.AddDevice<Keyboard>();
                            InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.B));
                            stage = 20; next = EditorApplication.timeSinceStartup + .15; break;
                        }
                        ArenaBoundaryChecks.Run(content);
                        Require(run.Wave == 1 && content.director.CurrentWave == 1, "One initial wave");
                        Require(content.Attack.Charge == content.Attack.Capacity, "Battle charge filled");
                        Require(!run.TryPurchase("fist").Ok, "Battle purchase rejected");
                        bool rejected = false;
                        try { run.Unequip("hand", 1); } catch (RunException) { rejected = true; }
                        Require(rejected, "Battle equipment locked");
                        app.ToggleInventory();
                        var battleBag = Object.FindFirstObjectByType<FlowLaboratoryWindow>();
                        Require(battleBag != null && battleBag.presenter.ReadOnly && !battleBag.discardButton.interactable, "Battle inventory read-only");
                        Require(battleBag.sellButton == null || !battleBag.sellButton.gameObject.activeSelf, "No inventory selling");
                        app.HandleEscape(); Require(app.WorldInputAllowed, "Inventory Esc returns directly to game");
                        app.ToggleInventory(); app.ToggleInventory(); Require(app.WorldInputAllowed, "Inventory toggle closes");
                        app.OpenWindow(WindowKind.Pause); app.ToggleInventory();
                        Require(Object.FindObjectsByType<FlowLaboratoryWindow>(FindObjectsSortMode.None).All(x => !x.gameObject.activeInHierarchy), "Pause blocks inventory");
                        app.HandleEscape();
                        var target = new GameObject("Battle attack verification"); target.SetActive(false);
                        target.transform.position = content.player.transform.position + Vector3.right;
                        target.AddComponent<EnemyData>(); target.AddComponent<EnemyRuntime>(); target.SetActive(true);
                        Require(content.Attack.TryAttack(), "Battle attack before transition");
                        Require(content.Attack.Charge < content.Attack.Capacity, "Battle charge spent");
                        content.player.TakeDamage(17); health = content.player.CurrentHealth;
                        content.director.CompleteCurrentWave();
                        reward = run.Marrow; content.director.CompleteCurrentWave();
                        Require(run.Marrow == reward, "Duplicate completion rewards once");
                        stage++; break;
                    case 20:
                        Require(Time.timeScale == 0 && app.HasModal, "B opens combat inventory");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Tab));
                        stage++; next = EditorApplication.timeSinceStartup + .15; break;
                    case 21:
                        Require(app.WorldInputAllowed, "Tab closes B-opened inventory");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        stage++; next = EditorApplication.timeSinceStartup + .1; break;
                    case 22:
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Tab));
                        stage++; next = EditorApplication.timeSinceStartup + .15; break;
                    case 23:
                        Require(Time.timeScale == 0, "Tab opens inventory");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Escape));
                        stage++; next = EditorApplication.timeSinceStartup + .15; break;
                    case 24:
                        Require(app.WorldInputAllowed && !app.HasModal, "Inventory Esc returns directly to game");
                        InputSystem.RemoveDevice(keyboard); keyboard = null; shortcutsChecked = true; stage = 1; break;
                    case 2:
                        ArenaBoundaryChecks.Run(content);
                        Require(content.kind == ContentKind.Rest && content.player.CurrentHealth == health, "Health preserved entering rest");
                        Require(content.director == null && EnemyRuntime.ActiveCount == 1, "Only training dummy in rest, count=" + EnemyRuntime.ActiveCount);
                        Require(content.Attack.Charge < content.Attack.Capacity, "Rest does not refill charge");
                        charge = content.Attack.Charge;
                        Require(content.Attack.TryAttack(), "Rest uses real charge attack");
                        Require(content.dummy.CurrentHealth < content.dummy.MaxHealth && content.Attack.Charge < charge, "Real damage and charge spend");
                        app.OpenWindow(WindowKind.Inventory);
                        Require(Time.timeScale == 0 && !app.WorldInputAllowed && !content.Attack.TryAttack(), "Inventory pauses and gates attack");
                        knife = run.Inventory.UnequippedIds().Single(id => run.Inventory.OrganOf(id).id == "rust_knife");
                        var lab = Object.FindFirstObjectByType<FlowLaboratoryWindow>();
                        Require(lab != null && lab.presenter.source == null, "Real laboratory view without demo bootstrap");
                        FlowPresentationChecks.CaptureFlowUI(app, "inventory-zh.png");
                        Require(Object.FindFirstObjectByType<SpringUp.Laboratory.LaboratoryCombatDemo>() == null, "No internal combat preview");
                        lab.presenter.SelectInventory(knife); lab.presenter.SelectSlot(1, 0);
                        Require(lab.presenter.installButton.interactable, "Select item and slot enables installation");
                        lab.presenter.installButton.onClick.Invoke();
                        Require(lab.presenter.inventoryRoot.childCount == run.Inventory.UnequippedIds().Count, "Inventory refreshes after equip");
                        Require(!content.Attack.IsAttacking && content.combat.ActiveBlackHoles == 0, "Equipment clears old execution");
                        Require(content.pipeline.GetBody(BodyPart.Hand).GetSlot(0).InstanceId == knife, "Rest pipeline updated immediately");
                        app.CloseTop();
                        content.dummy.TakeDamage(100000, "test", content.dummy.Position);
                        Require(content.dummy.IsAlive && run.Marrow == reward && run.Stage == RunPhase.Rest, "Dummy cannot reward or end wave");
                        content.ResetDummy();
                        Require(content.dummy.CurrentHealth == content.dummy.MaxHealth, "Reset training target");
                        // A newly equipped loadout still respects the previous round cooldown.
                        content.Attack.BeginWave();
                        Require(content.Attack.TryAttack(), "Changed build attacks");
                        Require(Mathf.Approximately(content.dummy.MaxHealth - content.dummy.CurrentHealth, app.Configuration.Definitions["rust_knife"].Damage), "Changed build damage");
                        app.OpenWindow(WindowKind.Pause); app.OpenWindow(WindowKind.Settings);
                        app.HandleEscape(); Require(Time.timeScale == 0, "Closing nested settings preserves pause");
                        app.HandleEscape(); Require(Time.timeScale == 1 && app.WorldInputAllowed, "Closing pause restores input");
                        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                        Application.runInBackground = true;
                        keyboard = InputSystem.AddDevice<Keyboard>();
                        position = content.player.transform.position;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.D));
                        stage++; next = EditorApplication.timeSinceStartup + .5; break;
                    case 3:
                        Require(content.player.transform.position.x > position.x + .1f, "Rest keyboard movement");
                        content.player.transform.position = content.Merchant.position + Vector3.right * 5;
                        Require(!app.TryOpenShop(), "Merchant rejects out of range");
                        content.player.transform.position = content.Merchant.position;
                        Require(app.TryOpenShop(), "Merchant shop entry");
                        var buttons = app.uiRoot.GetComponentsInChildren<UnityEngine.UI.Button>();
                        var buy = buttons.Single(b => b.name.StartsWith("购买 "));
                        int marrowBefore = run.Marrow, inventoryBefore = run.Inventory.Instances.Count;
                        buy.onClick.Invoke();
                        int listPrice = app.Configuration.Organs["rust_knife"].shop_price;
                        int paid = marrowBefore - run.Marrow;
                        int low = EconomySwing.RoundAwayFromZero(listPrice, 0, EconomySwing.PurchaseHalfPercent);
                        int high = EconomySwing.RoundAwayFromZero(listPrice, 1, EconomySwing.PurchaseHalfPercent);
                        Require(paid >= Math.Min(low, high) && paid <= Math.Max(low, high) && run.Inventory.Instances.Count == inventoryBefore + 1, "Shop purchase refresh");
                        var sell = buttons.Single(b => b.name == "出售所选物品");
                        Require(!sell.interactable, "Selling requires selection");
                        var saleId = run.Inventory.UnequippedIds().Last();
                        int price = run.Inventory.SalePrice(saleId);
                        var choice = app.uiRoot.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name.EndsWith("#" + saleId));
                        choice.onClick.Invoke(); Require(sell.interactable, "Selection enables selling");
                        marrowBefore = run.Marrow; sell.onClick.Invoke();
                        Require(run.Marrow == marrowBefore + price && !run.Inventory.Instances.ContainsKey(saleId), "Sale uses inventory price and removes instance");
                        sell.onClick.Invoke(); Require(run.Marrow == marrowBefore + price, "Repeat sale cannot reward twice");
                        FlowPresentationChecks.CaptureFlowUI(app, "shop-zh.png");
                        position = content.player.transform.position;
                        pausedEnemy = new GameObject("Ready enemy during pause"); pausedEnemy.SetActive(false);
                        pausedEnemy.transform.position = position;
                        pausedEnemy.AddComponent<EnemyData>(); pausedEnemy.AddComponent<EnemyRuntime>();
                        var readyAttack = pausedEnemy.AddComponent<EnemyAttack>();
                        var readySettings = new SerializedObject(readyAttack);
                        readySettings.FindProperty("initialDelay").floatValue = 0;
                        readySettings.FindProperty("useProjectiles").boolValue = false;
                        readySettings.ApplyModifiedPropertiesWithoutUndo();
                        pausedEnemy.SetActive(true);
                        pausedProjectile = Projectile.Spawn(position, content.player.transform, Vector2.right, 1, 10,
                            "paused-test", .1f, Color.white, null);
                        stage++; next = EditorApplication.timeSinceStartup + .3; break;
                    case 4:
                        Require(Vector3.Distance(position, content.player.transform.position) < .01f, "Shop blocks movement");
                        Require(content.player.CurrentHealth == health, "Ready enemies and overlapping projectiles cannot damage during pause");
                        pausedEnemy.SetActive(false); Object.Destroy(pausedEnemy);
                        pausedProjectile.gameObject.SetActive(false); Object.Destroy(pausedProjectile.gameObject);
                        app.CloseTop(); InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        app.OpenWindow(WindowKind.Pause); app.OpenWindow(WindowKind.Settings);
                        content.Attack.ToggleRebind();
                        app.CloseTop();
                        Require(!content.Attack.IsRebinding && Time.timeScale == 0, "Closing settings cancels its binding capture");
                        app.OpenWindow(WindowKind.Settings);
                        content.Attack.ToggleRebind();
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Escape));
                        stage++; next = EditorApplication.timeSinceStartup + .2; break;
                    case 5:
                        Require(!content.Attack.IsRebinding && Time.timeScale == 0, "Esc cancels binding only");
                        app.HandleEscape(); Require(Time.timeScale == 0, "Settings closes before pause");
                        app.HandleEscape(); Require(Time.timeScale == 1, "Pause closes next");
                        InputSystem.RemoveDevice(keyboard); keyboard = null;
                        while (run.Inventory.UnequippedWithinCap()) run.Inventory.Spawn("fist");
                        app.NextWave(); Require(!app.IsTransitioning && run.Stage == RunPhase.Rest, "Overflow blocks transition");
                        while (!run.CanStartNextWave()) run.Discard(run.Inventory.UnequippedIds().Last());
                        app.NextWave(); app.NextWave(); stage++; break;
                    case 6:
                        Require(run.Wave == 2 && content.director.CurrentWave == 2, "Repeated next request starts only wave 2");
                        Require(content.player.CurrentHealth == health, "Health preserved into next battle");
                        Require(content.pipeline.GetBody(BodyPart.Hand).GetSlot(0).InstanceId == knife, "Equipment carried to battle");
                        Require(content.Attack.Charge == content.Attack.Capacity, "Next wave fills charge");
                        stage++; goto case 7;
                    case 7:
                        if (run.Stage == RunPhase.Won)
                        {
                            Require(run.Wave == 7 && Time.timeScale == 0, "Victory after seven waves");
                            app.StartNewRun(); stage = 9; break;
                        }
                        if (run.Stage == RunPhase.Rest)
                        {
                            while (!run.CanStartNextWave()) run.Discard(run.Inventory.UnequippedIds()[0]);
                            app.NextWave(); break;
                        }
                        wave = run.Wave;
                        content.director.CompleteCurrentWave(); reward = run.Marrow;
                        content.director.CompleteCurrentWave(); Require(run.Marrow == reward, "Repeated reward protected wave " + wave);
                        break;
                    case 9:
                        Require(run.Wave == 1 && run.Marrow == 0 && run.Inventory.Instances.Count == 1, "Fresh session");
                        Require(content.player.CurrentHealth == content.player.MaxHealth, "Fresh life");
                        keyboard = InputSystem.AddDevice<Keyboard>();
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        var keyTargetObject = new GameObject("Keyboard attack target"); keyTargetObject.SetActive(false);
                        keyTargetObject.transform.position = content.player.transform.position + Vector3.right;
                        keyTargetObject.AddComponent<EnemyData>(); keyboardTarget = keyTargetObject.AddComponent<EnemyRuntime>();
                        keyTargetObject.SetActive(true);
                        stage = 18; next = EditorApplication.timeSinceStartup + .3; break;
                    case 18:
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(content.Attack.AttackKey));
                        stage = 19; next = EditorApplication.timeSinceStartup + .4; break;
                    case 19:
                        Require(keyboardTarget.CurrentHealth < keyboardTarget.MaxHealth && content.Attack.Charge < content.Attack.Capacity,
                            "Real keyboard attack deals damage and spends charge");
                        InputSystem.RemoveDevice(keyboard); keyboard = null; keyboardTarget.RemoveFromBattlefield();
                        content.player.TakeDamage(content.player.MaxHealth);
                        Require(run.Stage == RunPhase.Lost && Time.timeScale == 0, "Defeat settlement");
                        content.director.CompleteCurrentWave(); Require(run.Marrow == 0, "No reward after death");
                        app.RequestMainMenu(); app.HandleEscape();
                        Require(Time.timeScale == 0 && app.Session != null, "Cancel abandon preserves settlement");
                        app.AbandonRun(); stage = 10; break;
                    case 10:
                        Require(content.kind == ContentKind.MainMenu && Time.timeScale == 1 && app.Session == null, "Menu clears pauses and run");
                        app.StartNewRun(); stage++; break;
                    case 11:
                        app.OpenWindow(WindowKind.Pause); app.RequestMainMenu(); app.AbandonRun(); stage++; break;
                    case 12:
                        Require(Time.timeScale == 1 && EnemyRuntime.ActiveCount == 0, "Paused abandon cleans enemies and time");
                        app.StartNewRun(); stage++; break;
                    case 13:
                        Require(app.WorldInputAllowed && run.Wave == 1, "New game after paused abandon");
                        app.Switch("Assets/Scenes/Flow/Missing.unity"); stage++; break;
                    case 14:
                        Require(!app.IsTransitioning && Time.timeScale == 0 && !app.loading.activeSelf, "Failed transition has usable error window");
                        app.AbandonRun(); stage++; break;
                    case 15:
                        Require(content.kind == ContentKind.MainMenu && Time.timeScale == 1, "Failure recovery to menu");
                        app.StartNewRun(); stage++; break;
                    case 16:
                        content.director.CompleteCurrentWave(); stage++; break;
                    case 17:
                        Require(content.kind == ContentKind.Rest, "Rest death setup");
                        content.player.TakeDamage(content.player.MaxHealth);
                        Require(run.Stage == RunPhase.Lost && Time.timeScale == 0, "Death in rest also settles the run");
                        Debug.Log("[Scene Flow Checks] PLAY PASS: seven waves, victory/defeat, data persistence, duplicate requests, capacity, real rest attacks, changed loadout, keyboard movement, nested pause/Esc, failure recovery, static cleanup.");
                        Finish(0); break;
                }
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        static void Finish(int code)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= StateChanged; EditorApplication.Exit(code);
        }
    }
}
