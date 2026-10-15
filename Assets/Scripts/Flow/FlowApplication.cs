using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SpringUp.Flow
{
    public enum WindowKind { Pause, Inventory, Shop, Settings, Confirm, Settlement, Failure }
    [DefaultExecutionOrder(-1000)]
    public sealed class FlowApplication : MonoBehaviour
    {
        public const string MenuPath = "Assets/Scenes/Flow/MainMenu.unity";
        public const string BattlePath = "Assets/Scenes/Flow/Battle.unity";
        public const string RestPath = "Assets/Scenes/Flow/Rest.unity";
        public static bool IsActive { get; private set; }
        public TextAsset organsJson, wavesJson;
        public OrganCatalog organCatalog;
        public Transform uiRoot;
        public FlowWindow windowPrefab;
        public GameObject loading;
        public Camera menuCamera;
        public Text hud;
        public EventSystem eventSystem;
        public RunConfiguration Configuration { get; private set; }
        public RunSession Session { get; private set; }
        public FlowContent Content { get; private set; }
        public PauseService Pause { get; private set; }
        public bool IsTransitioning { get; private set; }
        public bool WorldInputAllowed => !IsTransitioning && !Pause.IsPaused && Content != null
            && Content.kind != ContentKind.MainMenu && Session != null
            && (Session.Run.Stage == RunPhase.Combat || Session.Run.Stage == RunPhase.Rest);
        readonly List<WindowEntry> windows = new();
        Scene contentScene;
        string retryPath;
        bool quitting;
        Font uiFont;
        sealed class WindowEntry
        {
            public WindowKind Kind;
            public FlowWindow View;
            public IDisposable Lease;
            public GameObject Focus;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => IsActive = false;
        void Awake()
        {
            if (IsActive) { Destroy(gameObject); return; }
            IsActive = true;
            Pause = new PauseService();
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            if (uiFont != null) hud.font = uiFont;
            // Bootstrap stays loaded; every content scene is additive.
        }
        void Start()
        {
            try { Configuration = new RunConfiguration(organsJson.text, wavesJson.text, organCatalog); Switch(MenuPath); }
            catch (Exception error) { ShowFailure(error); }
        }
        void Update()
        {
            if (Pause == null) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) HandleEscape();
            if (Content?.player != null && Session != null)
            {
                var run = Session.Run;
                hud.text = $"{Content.kind}  Wave {run.Wave}/{run.MaxWave}  HP {Content.player.CurrentHealth:0}/{Content.player.MaxHealth:0}"
                    + $"  Marrow {run.Marrow}  Charge {Content.Attack.Charge:0}/{Content.Attack.Capacity:0}"
                    + $"  Attack [{Content.Attack.KeyLabel}]  Esc: menu";
                if (Content.dummy != null) hud.text += $"  Dummy HP {Content.dummy.CurrentHealth:0}";
            }
            else hud.text = "SpringUp - scene flow prototype";
        }
        public void HandleEscape()
        {
            if (IsTransitioning) return;
            if (Content?.Attack?.IsRebinding == true) { Content.Attack.ToggleRebind(); return; }
            if (windows.Count != 0)
            {
                var kind = windows[windows.Count - 1].Kind;
                if (kind != WindowKind.Settlement && kind != WindowKind.Failure) CloseTop();
                return;
            }
            if (Content != null && Content.kind != ContentKind.MainMenu) OpenWindow(WindowKind.Pause);
        }
        public void StartNewRun()
        {
            if (IsTransitioning) return;
            if (Configuration == null)
            {
                try { Configuration = new RunConfiguration(organsJson.text, wavesJson.text, organCatalog); }
                catch (Exception error) { ShowFailure(error); return; }
            }
            // Stop old content before replacing the session; its last snapshot belongs to the old run.
            Content?.Stop();
            Session = new RunSession(Configuration);
            Switch(BattlePath);
        }
        public void NextWave()
        {
            if (IsTransitioning || Session?.Run.CanStartNextWave() != true) return;
            Session.Run.StartNextWave();
            Switch(BattlePath);
        }
        public void ResolveRunPhase()
        {
            if (IsTransitioning || Session == null) return;
            if (Session.Run.Stage == RunPhase.Rest) Switch(RestPath);
            else if (Session.Run.Stage == RunPhase.Won || Session.Run.Stage == RunPhase.Lost)
            {
                Content.Stop();
                menuCamera.gameObject.SetActive(true);
                ClearWindows();
                OpenWindow(WindowKind.Settlement);
            }
        }
        public void RequestMainMenu() => OpenWindow(WindowKind.Confirm);
        public void AbandonRun()
        {
            if (IsTransitioning) return;
            Content?.Stop(); Session = null; Switch(MenuPath);
        }
        public void Switch(string path)
        {
            if (IsTransitioning) return;
            retryPath = path;
            IsTransitioning = true;
            StartCoroutine(GuardTransition(path));
        }
        IEnumerator GuardTransition(string path)
        {
            var lease = Pause.Acquire();
            loading.SetActive(true);
            ClearWindows();
            var routine = Transition(path);
            Exception failure = null;
            while (true)
            {
                object next = null; bool more = false;
                try { more = routine.MoveNext(); if (more) next = routine.Current; }
                catch (Exception error) { failure = error; }
                if (failure != null || !more) break;
                yield return next;
            }
            (routine as IDisposable)?.Dispose();
            IsTransitioning = false;
            loading.SetActive(false);
            lease.Dispose();
            if (failure != null)
            {
                Content?.Stop(); menuCamera.gameObject.SetActive(true);
                ShowFailure(failure);
            }
            else if (Content.kind == ContentKind.MainMenu) ShowMainMenu();
        }
        IEnumerator Transition(string path)
        {
            if (!Application.CanStreamedLevelBeLoaded(path)) throw new InvalidOperationException("Scene not in build settings: " + path);
            Content?.Stop(); Content = null;
            // Despawn without kill rewards, then discard pool and cached player references.
            EnemyRuntime.ClearBattlefield();
            SimplePool.Clear(); PlayerLocator.Reset();
            if (contentScene.IsValid() && contentScene.isLoaded)
            {
                SceneManager.SetActiveScene(gameObject.scene);
                yield return SceneManager.UnloadSceneAsync(contentScene);
            }
            menuCamera.gameObject.SetActive(true);
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            contentScene = SceneManager.GetSceneByPath(path);
            if (!contentScene.IsValid() || !contentScene.isLoaded) throw new InvalidOperationException("Scene did not load");
            SceneManager.SetActiveScene(contentScene);
            Content = contentScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FlowContent>(true)).Single();
            menuCamera.gameObject.SetActive(Content.kind == ContentKind.MainMenu);
            Content.Initialize(this, Session);
            Content.Begin();
            ShowContentActions();
        }
        void ShowFailure(Exception error)
        {
            Debug.LogException(error);
            ClearWindows();
            var view = Push(WindowKind.Failure, "Loading failed");
            view.AddText(error.Message);
            view.AddButton("Retry", () => {
                if (Configuration == null)
                {
                    try { Configuration = new RunConfiguration(organsJson.text, wavesJson.text, organCatalog); }
                    catch (Exception e) { ShowFailure(e); return; }
                }
                Switch(retryPath ?? MenuPath);
            });
            view.AddButton("Return to main menu", RequestMainMenu);
            FocusFirst(view);
        }
        FlowWindow CreateView()
        {
            var view = Instantiate(windowPrefab, uiRoot);
            if (uiFont != null) view.title.font = uiFont;
            return view;
        }
        FlowWindow actions;
        void ClearActions() { if (actions != null) { actions.gameObject.SetActive(false); Destroy(actions.gameObject); } actions = null; }
        void ShowMainMenu()
        {
            ClearActions();
            actions = CreateView(); actions.title.text = "SpringUp";
            actions.AddText("New game / no saves");
            actions.AddButton("New game", StartNewRun);
            FocusFirst(actions);
        }
        void ShowContentActions()
        {
            ClearActions();
            if (Content.kind == ContentKind.MainMenu) return;
            actions = CreateView(); actions.title.text = Content.kind.ToString();
            // Small non-modal corner panel; only windows acquire pause leases.
            var rect = (RectTransform)actions.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0); rect.anchoredPosition = new Vector2(-12, 12); rect.sizeDelta = new Vector2(310, 280);
            actions.AddButton("Pause [Esc]", () => OpenWindow(WindowKind.Pause));
            if (Content.kind == ContentKind.Rest)
            {
                actions.AddButton("Inventory", () => OpenWindow(WindowKind.Inventory));
                actions.AddButton("Shop", () => OpenWindow(WindowKind.Shop));
                actions.AddButton("Reset dummy", () => Content.ResetDummy());
                actions.AddButton("Next wave", NextWave);
            }
            else
            {
                actions.AddButton("Debug: finish wave", () => { if (WorldInputAllowed) Content.director.CompleteCurrentWave(); });
                actions.AddButton("Debug: defeat", () => { if (WorldInputAllowed) Content.player.TakeDamage(Content.player.MaxHealth); });
                actions.AddButton("Verify equipment lock", () => {
                    try { Session.Run.Unequip("hand", 1); }
                    catch (RunException) { OpenNotice("Equipment is locked during battle."); }
                });
            }
        }
        void OpenNotice(string text)
        {
            var view = Push(WindowKind.Settings, "Notice"); view.AddText(text);
            view.AddButton("Close", CloseTop); FocusFirst(view);
        }
        public void OpenWindow(WindowKind kind)
        {
            if (IsTransitioning) return;
            if ((kind == WindowKind.Inventory || kind == WindowKind.Shop) && Session?.Run.Stage != RunPhase.Rest) return;
            if (windows.Any(w => w.Kind == kind)) return;
            var view = Push(kind, kind == WindowKind.Pause ? $"Pause - {Content.kind}" : kind.ToString());
            switch (kind)
            {
                case WindowKind.Pause:
                    view.AddButton("Resume", CloseTop);
                    if (Session?.Run.Stage == RunPhase.Rest)
                    {
                        view.AddButton("Inventory", () => OpenWindow(WindowKind.Inventory));
                        view.AddButton("Shop", () => OpenWindow(WindowKind.Shop));
                    }
                    view.AddButton("Settings", () => OpenWindow(WindowKind.Settings));
                    view.AddButton("Main menu (abandon run)", RequestMainMenu);
                    break;
                case WindowKind.Settings:
                    view.AddText("Settings placeholder");
                    view.AddButton("Rebind attack (Esc cancels)", () => Content?.Attack.ToggleRebind());
                    view.AddButton("Close", CloseTop); break;
                case WindowKind.Confirm:
                    view.AddText("Abandon this run? Progress will be lost.");
                    view.AddButton("Cancel", CloseTop); view.AddButton("Abandon run", AbandonRun); break;
                case WindowKind.Inventory: PopulateInventory(view); break;
                case WindowKind.Shop:
                    var item = Configuration.Organs["rust_knife"];
                    view.AddText($"Fixed test item: {item.name}, cost {item.shop_price}. Marrow {Session.Run.Marrow}");
                    view.AddButton("Buy", () => {
                        var result = Session.Run.TryPurchase("rust_knife");
                        if (result.Ok) { CloseTop(); OpenWindow(WindowKind.Shop); }
                        OpenNotice(result.Ok ? "Purchased." : result.Reason);
                    });
                    view.AddButton("Close", CloseTop); break;
                case WindowKind.Settlement:
                    view.AddText(Session.Run.Stage == RunPhase.Won ? "Victory!" : "Defeat");
                    view.AddText($"Wave {Session.Run.Wave}, marrow {Session.Run.Marrow}");
                    view.AddButton("New game", StartNewRun);
                    view.AddButton("Main menu", RequestMainMenu); break;
            }
            FocusFirst(view);
        }
        void PopulateInventory(FlowWindow view)
        {
            var run = Session.Run;
            view.AddText($"Bag {run.Inventory.UnequippedIds().Count}/{run.Inventory.UnequippedCapacity()} - overflow blocks next wave");
            view.AddButton("Close", CloseTop);
            // Six hand slots expose real replacement/removal; other body parts remain future UI.
            for (int slot = 1; slot <= 6; slot++)
            {
                int target = slot; var id = run.Inventory.EquippedAt("hand", slot);
                view.AddButton($"Hand {slot}: {(id == null ? "empty" : run.Inventory.OrganOf(id).name)} / unequip",
                    () => EquipmentOperation(() => run.Unequip("hand", target)));
            }
            foreach (var id in run.Inventory.UnequippedIds())
            {
                var captured = id; var item = run.Inventory.OrganOf(id);
                view.AddText(item.name);
                for (int slot = 1; slot <= 6; slot++)
                {
                    int target = slot;
                    view.AddButton("Equip / replace hand " + slot, () => EquipmentOperation(() => run.Replace(captured, "hand", target)));
                }
                view.AddButton("Sell", () => EquipmentOperation(() => run.Sell(captured)));
                view.AddButton("Discard", () => EquipmentOperation(() => run.Discard(captured)));
            }
        }
        void EquipmentOperation(Action operation)
        {
            try { operation(); CloseTop(); OpenWindow(WindowKind.Inventory); }
            catch (Exception error) when (error is InventoryException || error is RunException) { OpenNotice(error.Message); }
        }
        FlowWindow Push(WindowKind kind, string title)
        {
            if (actions != null) actions.group.interactable = false;
            if (windows.Count > 0) windows[windows.Count - 1].View.group.interactable = false;
            var focus = eventSystem.currentSelectedGameObject;
            var view = CreateView(); view.title.text = title;
            windows.Add(new WindowEntry { Kind = kind, View = view, Lease = Pause.Acquire(), Focus = focus });
            return view;
        }
        public void CloseTop()
        {
            if (windows.Count == 0) return;
            var last = windows[windows.Count - 1]; windows.RemoveAt(windows.Count - 1);
            if (last.Kind == WindowKind.Settings && Content?.Attack?.IsRebinding == true) Content.Attack.ToggleRebind();
            if (last.View != null) { last.View.gameObject.SetActive(false); Destroy(last.View.gameObject); }
            last.Lease.Dispose();
            if (windows.Count > 0 && windows[windows.Count - 1].View != null) windows[windows.Count - 1].View.group.interactable = true;
            else if (actions != null) actions.group.interactable = true;
            if (eventSystem == null) return;
            if (last.Focus != null && last.Focus.activeInHierarchy) eventSystem.SetSelectedGameObject(last.Focus);
            else if (windows.Count > 0) FocusFirst(windows[windows.Count - 1].View);
            else if (actions != null) FocusFirst(actions);
        }
        void ClearWindows() { while (windows.Count > 0) CloseTop(); ClearActions(); }
        void FocusFirst(FlowWindow view)
        {
            if (view == null || eventSystem == null) return;
            var button = view.GetComponentInChildren<Button>();
            eventSystem.SetSelectedGameObject(button != null ? button.gameObject : null);
        }
        void OnApplicationQuit() => quitting = true;
        void OnDestroy()
        {
            if (Pause == null) return;
            Content?.Stop(); ClearWindows(); Pause.Dispose(); IsActive = false;
            if (uiFont != null) Destroy(uiFont);
            if (!quitting && contentScene.IsValid() && contentScene.isLoaded) SceneManager.UnloadSceneAsync(contentScene);
        }
    }
}
