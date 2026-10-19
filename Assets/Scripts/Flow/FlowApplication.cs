using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Organs;
using TideBorne.Audio;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SpringUp.Flow
{
    public enum WindowKind { Pause, Inventory, Shop, Settings, Confirm, Settlement, Failure, Laboratory, Notice }
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
        public RewardFlightSettings rewardFlightSettings;
        public RewardFlightPresenter RewardFlights { get; private set; }
        public FlowWindow windowPrefab;
        public FlowWindow laboratoryPrefab;
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
        GameObject dimmer;
        Text controlsHint;
        public bool HasModal => windows.Count > 0;
        public bool CanModifyEquipment => !IsTransitioning && Session?.Run.Stage == RunPhase.Rest
            && Content != null && Content.IsNearCounter(Content.Laboratory)
            && windows.Count == 1 && windows[0].Kind == WindowKind.Laboratory;
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
            GameSettings.Load();
            GameSettings.Changed += RefreshControlsHint;
            RewardFlights = RewardFlightPresenter.Create(uiRoot, rewardFlightSettings, Pause);
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            if (uiFont != null)
                foreach (var text in hud.GetComponentInParent<Canvas>().GetComponentsInChildren<Text>(true)) text.font = uiFont;
            // Bootstrap stays loaded; every content scene is additive.
        }
        void Start()
        {
            try { Configuration = new RunConfiguration(organsJson.text, wavesJson.text, organCatalog); Switch(MenuPath); }
            catch (Exception error) { ShowFailure(error); }
        }
        static string ContentTitle(ContentKind kind) => kind == ContentKind.Battle ? "战斗" : kind == ContentKind.Rest ? "休整" : "主菜单";
        static string WindowTitle(WindowKind kind) => kind switch
        {
            WindowKind.Inventory => "背包 · 仅查看", WindowKind.Laboratory => "实验室 · 器官装配", WindowKind.Shop => "商店",
            WindowKind.Settings => "设置", WindowKind.Confirm => "确认放弃",
            WindowKind.Settlement => "本局结算", WindowKind.Failure => "加载失败", _ => "暂停"
        };
        void Update()
        {
            if (Pause == null) return;
            var settings = windows.Count > 0 ? windows[windows.Count - 1].View.GetComponent<MenuSettingsView>() : null;
            if (settings != null && settings.IsListening) { settings.CaptureKey(); return; }
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) HandleEscape();
            else if (Mouse.current?.rightButton.wasPressedThisFrame == true) HandleBack();
            else if (GameSettings.Pressed(GameBinding.Inventory) || Keyboard.current?.tabKey.wasPressedThisFrame == true) ToggleInventory();
            if (Content?.player != null && Session != null)
            {
                var run = Session.Run;
                hud.text = $"{ContentTitle(Content.kind)}  波次 {run.Wave}/{run.MaxWave}  生命 {Content.player.CurrentHealth:0}/{Content.player.MaxHealth:0}"
                    + $"  骨髓 {run.Marrow}"
                    + $"  器官自动攻击  {GameSettings.InventoryHint}：背包  Esc：菜单";
                if (CanInteractWithShop()) hud.text += $"  {GameSettings.Display(GameBinding.Interact)}：商店";
                else if (CanInteractWithLaboratory()) hud.text += $"  {GameSettings.Display(GameBinding.Interact)}：实验室装配";
                if (Content.dummy != null) hud.text += $"  木桩生命 {Content.dummy.CurrentHealth:0}";
            }
            else hud.text = "";
        }
        public void ToggleInventory()
        {
            if (IsTransitioning) return;
            if (windows.Count == 1 && (windows[0].Kind == WindowKind.Inventory || windows[0].Kind == WindowKind.Laboratory)) { CloseTop(); return; }
            if (WorldInputAllowed && windows.Count == 0) OpenWindow(WindowKind.Inventory);
        }
        public bool CanInteractWithShop() => WorldInputAllowed && !HasModal && Session?.Run.Stage == RunPhase.Rest
            && Content.IsNearCounter(Content.Merchant);
        public bool CanInteractWithLaboratory() => WorldInputAllowed && !HasModal && Session?.Run.Stage == RunPhase.Rest
            && Content.IsNearCounter(Content.Laboratory);
        public bool TryInteract() => CanInteractWithLaboratory() ? TryOpenLaboratory() : TryOpenShop();
        public bool TryOpenLaboratory()
        {
            if (!CanInteractWithLaboratory()) return false;
            OpenWindow(WindowKind.Laboratory); return true;
        }
        public bool TryOpenShop()
        {
            if (!CanInteractWithShop()) return false;
            OpenWindow(WindowKind.Shop); return true;
        }
        bool CancelDrag()
        {
            if (windows.Count == 0) return false;
            var backpack = windows[windows.Count - 1].View.GetComponentInChildren<SpringUp.Laboratory.BackpackPresenter>();
            if (backpack == null || !backpack.IsDragging) return false;
            backpack.CancelDrag(); return true;
        }
        public void HandleBack()
        {
            if (IsTransitioning || CancelDrag() || windows.Count == 0) return;
            var settings = windows[windows.Count - 1].View.GetComponent<MenuSettingsView>();
            if (settings != null && settings.IsListening) { settings.CancelBinding(); return; }
            var kind = windows[windows.Count - 1].Kind;
            if (kind != WindowKind.Settlement && kind != WindowKind.Failure) CloseTop();
        }
        public void HandleEscape()
        {
            if (IsTransitioning || CancelDrag()) return;
            if (windows.Count > 0 && windows[windows.Count - 1].View.TryGetComponent<MenuSettingsView>(out var settings) && settings.IsListening)
            { settings.CancelBinding(); return; }
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
            RewardFlights?.Clear();
            Session = new RunSession(Configuration);
            var current = Session;
            current.Run.EquipmentAccessPolicy = () => Session == current && CanModifyEquipment;
            Switch(BattlePath);
        }
        public void ShowKillReward(KillRewardResult reward, Vector2 deathPosition)
        {
            if (RewardFlights == null) return;
            var camera = Camera.main;
            if (reward.HasMarrow) RewardFlights.Spawn(deathPosition, camera, false);
            if (reward.OrganInstanceId != null) RewardFlights.Spawn(deathPosition, camera, true);
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
            Content?.Stop(); RewardFlights?.Clear(); Session = null; Switch(MenuPath);
        }
        public void Switch(string path)
        {
            if (IsTransitioning) return;
            if (path == MenuPath) RewardFlights?.Clear();
            retryPath = path;
            IsTransitioning = true;
            StartCoroutine(GuardTransition(path));
        }
        IEnumerator GuardTransition(string path)
        {
            var lease = Pause.Acquire(pauseRewardAnimations: false);
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
            // 场景切换完成后切 BGM：战斗用战场曲，标题/休整用休整曲。
            GameAudio.Instance?.SetBattle(Content.kind == ContentKind.Battle);
            ShowContentActions();
        }
        void ShowFailure(Exception error)
        {
            Debug.LogException(error);
            ClearWindows();
            var view = Push(WindowKind.Failure, "加载失败");
            view.AddText("加载暂未完成，请重试或返回主菜单。详细原因见控制台。");
            view.AddButton("重试", () => {
                if (Configuration == null)
                {
                    try { Configuration = new RunConfiguration(organsJson.text, wavesJson.text, organCatalog); }
                    catch (Exception e) { ShowFailure(e); return; }
                }
                Switch(retryPath ?? MenuPath);
            });
            view.AddButton("返回主菜单", RequestMainMenu);
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
            actions = CreateMenuView("MainMenu");
            actions.GetComponent<MenuSettingsView>().BindMenu(StartNewRun, () => OpenWindow(WindowKind.Settings), QuitGame);
            FocusFirst(actions);
        }
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        FlowWindow CreateMenuView(string name)
        {
            var view = Instantiate(Resources.Load<FlowWindow>("Menus/" + name), uiRoot);
            if (uiFont != null) foreach (var text in view.GetComponentsInChildren<Text>(true)) text.font = uiFont;
            return view;
        }
        void RefreshControlsHint()
        {
            if (controlsHint != null) controlsHint.text = $"{GameSettings.InventoryHint}：查看背包\n靠近 STORE 按 {GameSettings.Display(GameBinding.Interact)}：商店\n靠近 LAB 按 {GameSettings.Display(GameBinding.Interact)}：器官装配";
        }
        void ShowContentActions()
        {
            ClearActions();
            if (Content.kind == ContentKind.MainMenu) return;
            actions = CreateView(); actions.title.text = ContentTitle(Content.kind);
            // Small non-modal corner panel; only windows acquire pause leases.
            var rect = (RectTransform)actions.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0); rect.anchoredPosition = new Vector2(-12, 12); rect.sizeDelta = new Vector2(310, 280);
            actions.AddButton("暂停［Esc］", () => OpenWindow(WindowKind.Pause));
            if (Content.kind == ContentKind.Rest)
            {
                controlsHint = actions.AddText(""); RefreshControlsHint();
                actions.AddButton("重置木桩", () => Content.ResetDummy());
                actions.AddButton("进入下一波", NextWave);
            }
            else
            {
                actions.AddButton("测试：结束本波", () => { if (WorldInputAllowed) Content.director.CompleteCurrentWave(); });
                actions.AddButton("测试：战败结算", () => { if (WorldInputAllowed) Content.player.TakeDamage(Content.player.MaxHealth); });
                actions.AddButton("测试：战斗换装限制", () => {
                    try { Session.Run.Unequip("hand", 1); }
                    catch (RunException) { OpenNotice("战斗期间无法更换装备，请在休整时操作。"); }
                });
            }
        }
        void OpenNotice(string text)
        {
            var view = Push(WindowKind.Notice, "提示"); view.AddText(text);
            view.AddButton("关闭", CloseTop); FocusFirst(view);
        }
        public void OpenWindow(WindowKind kind)
        {
            if (IsTransitioning) return;
            if (kind == WindowKind.Inventory && (HasModal || Session == null ||
                (Session.Run.Stage != RunPhase.Rest && Session.Run.Stage != RunPhase.Combat))) return;
            if (kind == WindowKind.Shop && !CanInteractWithShop()) return;
            if (kind == WindowKind.Laboratory && !CanInteractWithLaboratory()) return;
            if (windows.Any(w => w.Kind == kind)) return;
            var view = Push(kind, kind == WindowKind.Pause ? $"暂停 · {ContentTitle(Content.kind)}" : WindowTitle(kind));
            switch (kind)
            {
                case WindowKind.Pause:
                    view.GetComponent<MenuSettingsView>().BindMenu(CloseTop, () => OpenWindow(WindowKind.Settings), RequestMainMenu);
                    break;
                case WindowKind.Settings:
                    view.GetComponent<MenuSettingsView>().BindSettings(CloseTop); break;
                case WindowKind.Confirm:
                    view.GetComponent<MenuSettingsView>().BindConfirmation(CloseTop, AbandonRun); break;
                case WindowKind.Inventory:
                case WindowKind.Laboratory:
                    view.GetComponent<FlowLaboratoryWindow>().Bind(this);
                    break;
                case WindowKind.Shop:
                    view.GetComponent<FlowShopWindow>().Bind(this); break;
                case WindowKind.Settlement:
                    view.AddText(Session.Run.Stage == RunPhase.Won ? "挑战成功！" : "挑战失败");
                    view.AddText($"到达第 {Session.Run.Wave} 波，持有骨髓 {Session.Run.Marrow}");
                    view.AddButton("开始新局", StartNewRun);
                    view.AddButton("主菜单", RequestMainMenu); break;
            }
            view.gameObject.SetActive(true);
            FocusFirst(view);
        }
        FlowWindow Push(WindowKind kind, string title)
        {
            if (actions != null) actions.group.interactable = false;
            if (windows.Count > 0) windows[windows.Count - 1].View.group.interactable = false;
            var focus = eventSystem.currentSelectedGameObject;
            EnsureDimmer(); dimmer.SetActive(true); dimmer.transform.SetAsLastSibling();
            var view = kind == WindowKind.Inventory || kind == WindowKind.Laboratory ? Instantiate(laboratoryPrefab, uiRoot)
                : kind == WindowKind.Pause || kind == WindowKind.Settings || kind == WindowKind.Confirm || kind == WindowKind.Shop ? CreateMenuView(kind.ToString()) : CreateView();
            if (view.title != null) view.title.text = title;
            windows.Add(new WindowEntry { Kind = kind, View = view, Lease = Pause.Acquire(), Focus = focus });
            RefreshWindowVisibility();
            return view;
        }
        public void CloseTop()
        {
            if (windows.Count == 0) return;
            var last = windows[windows.Count - 1]; windows.RemoveAt(windows.Count - 1);
            if (last.View != null) { last.View.gameObject.SetActive(false); Destroy(last.View.gameObject); }
            last.Lease.Dispose();
            RefreshWindowVisibility();
            if (windows.Count > 0 && windows[windows.Count - 1].View != null) windows[windows.Count - 1].View.group.interactable = true;
            else if (actions != null) actions.group.interactable = true;
            if (eventSystem == null) return;
            if (last.Focus != null && last.Focus.activeInHierarchy) eventSystem.SetSelectedGameObject(last.Focus);
            else if (windows.Count > 0) FocusFirst(windows[windows.Count - 1].View);
            else if (actions != null) FocusFirst(actions);
        }
        void EnsureDimmer()
        {
            if (dimmer != null) return;
            dimmer = new GameObject("Modal dimmer", typeof(RectTransform), typeof(Image));
            dimmer.transform.SetParent(uiRoot, false);
            var rect = (RectTransform)dimmer.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            dimmer.GetComponent<Image>().color = new Color(0, 0, 0, .6f);
        }
        void RefreshWindowVisibility()
        {
            if (dimmer != null) dimmer.SetActive(windows.Count > 0);
            for (int i = 0; i < windows.Count; i++)
            {
                var group = windows[i].View.group; bool top = i == windows.Count - 1;
                group.alpha = top ? 1 : 0; group.interactable = top; group.blocksRaycasts = top;
            }
            if (windows.Count > 0 && dimmer != null)
            { dimmer.transform.SetAsLastSibling(); windows[windows.Count - 1].View.transform.SetAsLastSibling(); }
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
            GameSettings.Changed -= RefreshControlsHint;
            Content?.Stop(); ClearWindows(); Pause.Dispose(); IsActive = false;
            if (dimmer != null) Destroy(dimmer);
            if (uiFont != null) Destroy(uiFont);
            if (!quitting && contentScene.IsValid() && contentScene.isLoaded) SceneManager.UnloadSceneAsync(contentScene);
        }
    }
}
