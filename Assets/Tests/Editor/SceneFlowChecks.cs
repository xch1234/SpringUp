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
        [Test] public void ExpandedEconomyCoversEveryImplementedPricedOrgan()
        {
            var config = Config();
            var eligible = config.Definitions.Keys.Where(id => config.Organs[id].shop_price > 0).ToArray();
            Assert.AreEqual(35, eligible.Length);
            foreach (var wave in config.Waves.Where(w => w.wave < 7))
            {
                CollectionAssert.AreEquivalent(eligible, wave.random_drop_pool);
                Assert.AreEqual(1, wave.random_drop_count);
            }
            Assert.IsEmpty(config.Waves.Last().random_drop_pool);
            var run = new RunController(config.Organs, config.Waves, null, () => 0, config.Definitions.Keys);
            CollectionAssert.AreEquivalent(eligible, run.SelectShopOffers(100));
            Assert.IsEmpty(run.SelectShopOffers(0));
            var low = run.SelectShopOffers(4);
            var high = new RunController(config.Organs, config.Waves, null, () => 1, config.Definitions.Keys).SelectShopOffers(4);
            Assert.AreEqual(4, low.Distinct().Count());
            Assert.AreEqual(4, high.Distinct().Count());
            Assert.AreNotEqual(string.Join(",", low), string.Join(",", high));
            Assert.IsFalse(high.Contains("rust_knife"), "No fixed knife slot.");
        }

        [Test] public void PaidShopRefreshIsAtomicAndPersists()
        {
            void Check(bool condition, string message) { Assert.IsTrue(condition, message); }
            var catalog = new System.Collections.Generic.Dictionary<string, Tideflesh.Systems.OrganDefinition>();
            foreach (var id in new[] { "fist", "one", "two", "three", "four", "five" })
                catalog.Add(id, new Tideflesh.Systems.OrganDefinition { id = id, name = id, part = "any", type = "executor", shop_price = id == "fist" ? 0 : 10 });
            var waves = new System.Collections.Generic.List<WaveDefinition>();
            for (int i = 1; i <= 3; i++) waves.Add(new WaveDefinition { wave = i, duration_sec = 60,
                guaranteed_drops = Array.Empty<string>(), optional_drops = Array.Empty<string>(), marrow_reward = 450 });
            double random = .5;
            var run = new RunController(catalog, waves, null, () => random);
            Check(!run.TryRefreshShop().Ok && run.Marrow == 0 && run.ShopRefreshCount == 0, "Boot cannot refresh.");
            run.StartRun();
            Check(!run.TryRefreshShop().Ok, "Combat cannot refresh.");
            run.CombatTimerFinished();
            int balance = run.Marrow;
            var offers = run.GetShopOffers();
            Check(offers.Count == 4 && offers.Select(x => x.OrganId).Distinct().Count() == 4, "Four unique offers.");
            Check(ReferenceEquals(offers, run.GetShopOffers()) && run.Marrow == balance, "Reopening keeps offers and quotes for free.");
            var first = offers[0];
            Check(run.TryPurchaseShopOffer(first).Ok, "Purchase a current offer.");
            int afterPurchase = run.Marrow;
            Check(!run.TryPurchaseShopOffer(first).Ok && run.Marrow == afterPurchase, "Sold offer cannot charge twice.");
            Check(run.GetShopOffers()[0].Purchased, "Sold flag survives reopening.");
            var inventoryIds = run.Inventory.Instances.Keys.ToArray();
            random = 0;
            Check(run.ShopRefreshPrice == 30 && run.TryRefreshShop().Ok, "First refresh costs 30.");
            Check(run.Marrow == afterPurchase - 30 && run.ShopRefreshCount == 1 && run.ShopRefreshPrice == 50, "Fee and count commit once.");
            var refreshed = run.GetShopOffers();
            Check(!ReferenceEquals(offers, refreshed) && refreshed.All(x => !x.Purchased), "Refresh creates fresh stock.");
            Check(inventoryIds.SequenceEqual(run.Inventory.Instances.Keys), "Refresh preserves owned organs.");
            balance = run.Marrow;
            Check(!run.TryPurchaseShopOffer(offers[1]).Ok && run.Marrow == balance, "Stale pre-refresh offer cannot be bought.");
            Check(run.TryRefreshShop().Ok && run.Marrow == balance - 50 && run.ShopRefreshPrice == 70, "Second refresh costs 50.");
            while (run.Marrow >= run.ShopRefreshPrice) Check(run.TryRefreshShop().Ok, "Affordable refresh succeeds.");
            balance = run.Marrow; int count = run.ShopRefreshCount;
            offers = run.GetShopOffers();
            Check(!run.TryRefreshShop().Ok && run.Marrow == balance && run.ShopRefreshCount == count
                && ReferenceEquals(offers, run.GetShopOffers()), "Unaffordable refresh is atomic.");
            run.StartNextWave();
            Check(run.ShopRefreshCount == 0 && run.ShopRefreshPrice == 30 && !run.TryRefreshShop().Ok, "Next wave resets price, combat stays blocked.");
            run.CombatTimerFinished();
            Check(!ReferenceEquals(offers, run.GetShopOffers()), "Next rest has new stock.");
            var custom = new RunController(catalog, waves, null, () => .5, shopRefreshBasePrice: 7, shopRefreshPriceIncrement: 3, shopOfferCount: 2);
            custom.StartRun(); custom.CombatTimerFinished();
            Check(custom.GetShopOffers().Count == 2 && custom.ShopRefreshPrice == 7, "Custom settings used.");
            Check(custom.TryRefreshShop().Ok && custom.ShopRefreshPrice == 10, "Custom increment used.");
            // Unlimited inventory, exact refresh balance, and empty candidate pool.
            var exact = new RunController(catalog, waves, null, () => .5, shopRefreshBasePrice: 440);
            exact.StartRun(); exact.CombatTimerFinished();
            var exactOffer = exact.GetShopOffers()[0];
            for (int i = 0; i < 200; i++) exact.Inventory.Spawn("one");
            Check(exact.TryPurchaseShopOffer(exactOffer).Ok && exactOffer.Purchased && exact.Marrow == 440, "Large inventory permits purchasing and charges once.");
            Check(exact.TryRefreshShop().Ok && exact.Marrow == 0, "Large inventory can refresh with exact funds.");
            var empty = new RunController(catalog, waves, null, () => .5, new[] { "fist" });
            empty.StartRun(); empty.CombatTimerFinished();
            Check(!empty.TryRefreshShop().Ok && empty.Marrow == 450 && empty.ShopRefreshCount == 0, "Empty stock never charges.");
            var fresh = new RunController(catalog, waves, null);
            Check(fresh.ShopRefreshCount == 0 && fresh.ShopRefreshPrice == 30, "New run resets refresh count.");
            var expensive = new RunController(catalog, waves, null, () => .5, shopRefreshBasePrice: 1, shopRefreshPriceIncrement: int.MaxValue);
            expensive.StartRun(); expensive.CombatTimerFinished();
            Check(expensive.TryRefreshShop().Ok && expensive.ShopRefreshPrice == int.MaxValue, "Large prices never overflow negative.");
            Check(!expensive.TryRefreshShop().Ok, "Huge price is unaffordable.");
        }

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
            run.CreditKill("purchase-test", 500);
            run.CombatTimerFinished();
            int reward = run.Marrow;
            Assert.Throws<RunException>(() => run.CombatTimerFinished());
            Assert.AreEqual(reward, run.Marrow);
            for (int i = 0; i < 200; i++) run.Inventory.Spawn("fist");
            Assert.IsTrue(run.CanStartNextWave());
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
        static float health;
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
            core.PaidShopRefreshIsAtomicAndPersists();
            core.ExpandedEconomyCoversEveryImplementedPricedOrgan();
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
                        Require(content.pipeline.AutomaticExecution, "Automatic organ attacks enabled");
                        FlowPresentationChecks.CaptureFlowUI(app, "battle-equipment-merged.png");
                        Require(!run.TryPurchase("fist").Ok, "Battle purchase rejected");
                        bool rejected = false;
                        try { run.Unequip("hand", 1); } catch (RunException) { rejected = true; }
                        Require(rejected, "Battle equipment locked");
                        app.ToggleInventory();
                        var battleBag = Object.FindFirstObjectByType<FlowLaboratoryWindow>();
                        Require(battleBag != null && battleBag.backpack.ReadOnly && !battleBag.discardButton.interactable, "Battle inventory read-only");
                        Require(battleBag.sellButton == null || !battleBag.sellButton.gameObject.activeSelf, "No inventory selling");
                        app.HandleEscape(); Require(app.WorldInputAllowed, "Inventory Esc returns directly to game");
                        app.ToggleInventory(); app.ToggleInventory(); Require(app.WorldInputAllowed, "Inventory toggle closes");
                        app.OpenWindow(WindowKind.Pause); app.ToggleInventory();
                        Require(Object.FindObjectsByType<FlowLaboratoryWindow>(FindObjectsSortMode.None).All(x => !x.gameObject.activeInHierarchy), "Pause blocks inventory");
                        app.HandleEscape();
                        var target = new GameObject("Battle attack verification"); target.SetActive(false);
                        target.transform.position = content.player.transform.position + Vector3.right;
                        target.AddComponent<EnemyData>(); target.AddComponent<EnemyRuntime>(); target.SetActive(true);
                        Require(content.pipeline.TryExecuteSlot(BodyPart.Hand, 0), "Battle organ connected before transition");
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
                        if (content.dummy.CurrentHealth == content.dummy.MaxHealth) return;
                        Require(content.pipeline.AutomaticExecution, "Rest automatically tests the equipped build");
                        app.OpenWindow(WindowKind.Inventory);
                        Require(Time.timeScale == 0 && !app.WorldInputAllowed, "Inventory pauses world execution");
                        knife = run.Inventory.UnequippedIds().FirstOrDefault(id => run.Inventory.OrganOf(id).id == "rust_knife");
                        if (knife == null) { run.Inventory.Spawn("rust_knife"); knife = run.Inventory.UnequippedIds().Single(id => run.Inventory.OrganOf(id).id == "rust_knife"); }
                        var lab = Object.FindFirstObjectByType<FlowLaboratoryWindow>();
                        Require(lab != null && lab.backpack != null, "Real laboratory view without demo bootstrap");
                        int beforeSample = run.Inventory.Instances.Count;
                        StraightProjectileSampleMenu.Give();
                        Require(run.Inventory.Instances.Count == beforeSample + 1, "Sample menu supports new rest scene");
                        Require(lab.backpack.pageLabel.text.Contains(run.Inventory.UnequippedIds().Count + " 件"), "Open inventory refreshes after sample menu");
                        run.Discard(run.Inventory.UnequippedIds().Last(id => run.Inventory.OrganOf(id).id == "straight_projectile"));
                        FlowPresentationChecks.CaptureFlowUI(app, "inventory-zh.png");
                        Require(Object.FindFirstObjectByType<SpringUp.Laboratory.LaboratoryCombatDemo>() == null, "No internal combat preview");
                        lab.backpack.ChangePage(-100);
                        var sourceSlot = lab.backpack.inventorySlots.First(slot => slot.Item?.InstanceId == knife);
                        var targetSlot = lab.backpack.equipmentSlots.First(slot => slot.part == SpringUp.Laboratory.LaboratoryPart.Hand && slot.index == 0);
                        lab.backpack.BeginDrag(sourceSlot, new UnityEngine.EventSystems.PointerEventData(app.eventSystem));
                        Require(lab.backpack.CanDrop(targetSlot), "Compatible slot accepts dragged organ");
                        lab.backpack.Drop(targetSlot);
                        Require(lab.backpack.pageLabel.text.Contains(run.Inventory.UnequippedIds().Count + " 件"), "Inventory refreshes after equip");
                        Require(content.combat.ActiveBlackHoles == 0 && content.combat.Actuators.Active.Count == 0, "Equipment clears old execution");
                        Require(content.pipeline.GetBody(BodyPart.Hand).GetSlot(0).InstanceId == knife, "Rest pipeline updated immediately");
                        app.CloseTop();
                        content.dummy.TakeDamage(100000, "test", content.dummy.Position);
                        Require(content.dummy.IsAlive && run.Marrow == reward && run.Stage == RunPhase.Rest, "Dummy cannot reward or end wave");
                        content.ResetDummy();
                        Require(content.dummy.CurrentHealth == content.dummy.MaxHealth, "Reset training target");
                        // Direct invocation checks the new loadout; later stages verify real automatic Update.
                        Require(content.pipeline.TryExecuteSlot(BodyPart.Hand, 0), "Changed build attacks");
                        Require(Mathf.Approximately(content.dummy.MaxHealth - content.dummy.CurrentHealth, app.Configuration.Definitions["rust_knife"].Damage), "Changed build damage");
                        CheckMergedOrgans(app);
                        health = content.player.CurrentHealth;
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
                        var buyButtons = buttons.Where(b => b.name.StartsWith("购买 ")).ToArray();
                        Require(buyButtons.Length == 4, "Shop exposes four rotating offers");
                        var buy = buyButtons.First(b => b.interactable);
                        var boughtOrgan = app.Configuration.Organs.Values.Single(o => buy.name.StartsWith("购买 " + o.name + "（"));
                        int marrowBefore = run.Marrow, inventoryBefore = run.Inventory.Instances.Count;
                        buy.onClick.Invoke();
                        int listPrice = boughtOrgan.shop_price;
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
                        var reroll = buttons.Single(b => b.name == "刷新商品");
                        int refreshPrice = run.ShopRefreshPrice;
                        marrowBefore = run.Marrow;
                        reroll.onClick.Invoke();
                        Require(run.Marrow == marrowBefore - refreshPrice && run.ShopRefreshCount == 1, "Paid refresh deducts once");
                        Require(reroll.GetComponentInChildren<UnityEngine.UI.Text>().text.Contains(run.ShopRefreshPrice.ToString()), "Refresh button shows next price");
                        var preservedStock = run.GetShopOffers();
                        app.CloseTop();
                        Require(app.TryOpenShop() && ReferenceEquals(preservedStock, run.GetShopOffers()) && run.ShopRefreshCount == 1,
                            "Reopening shop preserves stock and refresh price");
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
                        app.CloseTop();
                        Require(Time.timeScale == 0, "Closing nested settings preserves pause");
                        app.OpenWindow(WindowKind.Settings);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Escape));
                        stage++; next = EditorApplication.timeSinceStartup + .2; break;
                    case 5:
                        Require(Time.timeScale == 0, "Esc closes settings before pause");
                        app.HandleEscape(); Require(Time.timeScale == 1, "Pause closes next");
                        InputSystem.RemoveDevice(keyboard); keyboard = null;
                        for (int i = 0; i < 20; i++) run.Inventory.Spawn("fist");
                        Require(run.CanStartNextWave(), "Large inventory permits transition");
                        app.NextWave(); app.NextWave(); stage++; break;
                    case 6:
                        Require(run.Wave == 2 && content.director.CurrentWave == 2, "Repeated next request starts only wave 2");
                        Require(content.player.CurrentHealth == health, "Health preserved into next battle");
                        Require(content.pipeline.GetBody(BodyPart.Hand).GetSlot(0).InstanceId == knife, "Equipment carried to battle");
                        Require(content.pipeline.GetBody(BodyPart.Head).GetSlot(0).Definition.Id == "straight_projectile"
                            && content.pipeline.GetBody(BodyPart.Leg).GetSlot(0).Definition.Id == "poison_fog", "Head and leg equipment carried across scenes");
                        Require(Mathf.Approximately(content.Torso.Loadout.MaxHealth, .6f)
                            && Mathf.Approximately(content.Torso.Loadout.View, 1.2f), "Torso modifiers restored in next scene");
                        Require(content.pipeline.AutomaticExecution, "Next wave resumes automatic attacks");
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
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        stage = 19; next = EditorApplication.timeSinceStartup + 2.5; break;
                    case 19:
                        Require(keyboardTarget != null && keyboardTarget.CurrentHealth < keyboardTarget.MaxHealth,
                            "No attack key pressed: real automatic Update deals damage");
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
        static void CheckMergedOrgans(FlowApplication app)
        {
            var content = app.Content; var run = app.Session.Run;
            Require(app.Configuration.Definitions.Count == 36, "All A organ definitions available in Bootstrap");
            void Equip(string id, string part, int slot) { var instance = run.Inventory.Spawn(id); run.Equip(instance, part, slot); }
            Equip("straight_projectile", "head", 1);
            Equip("poison_fog", "leg", 1);
            float beforeMax = content.player.MaxHealth;
            Equip("torso_overdrive", "torso", 1);
            Require(Mathf.Approximately(content.player.MaxHealth, beforeMax * .6f), "Torso applies immediately without attacking");
            Equip("torso_wideview", "torso", 2);
            Require(content.pipeline.TryExecuteSlot(BodyPart.Head, 0), "Projectile starts in additive scene");
            Require(content.pipeline.TryExecuteSlot(BodyPart.Leg, 0), "Self-area effect starts in additive scene");
            Require(content.combat.Actuators.Active.Count == 2, "Both effects connect to the current scene runner");
            Require(content.CombatReport != null && content.Equipment.GetSlot(SpringUp.Laboratory.LaboratoryPart.Leg, 0) != null,
                "HUD and report share current inventory");
            var old = content.combat.Actuators.Active.ToArray();
            Equip("multi_tentacle", "head", 2);
            Require(old.All(e => e.IsFinished) && content.combat.Actuators.Active.Count == 0, "Rest equipment change cancels old effects");
            float lost = content.dummy.TakeDamage(100000, "actual-loss-check", content.dummy.Position);
            Require(content.dummy.CurrentHealth == 1 && lost <= content.dummy.MaxHealth - 1, "Training dummy reports actual loss");
            Require(content.dummy.TakeDamage(10, "actual-loss-check", content.dummy.Position) == 0, "No phantom damage at immortal floor");
            content.ResetDummy();
            Debug.Log("[Main + A Integration] PASS: 36 configs, cross-part loadout, immediate torso, effect cancellation, HUD/report and actual dummy damage.");
        }
        static void Finish(int code)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= StateChanged; EditorApplication.Exit(code);
        }
    }
}
