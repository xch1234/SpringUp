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
        public bool HasModal => windows.Count > 0;
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
            WindowKind.Inventory => "实验室 · 装备", WindowKind.Shop => "商店",
            WindowKind.Settings => "设置", WindowKind.Confirm => "确认放弃",
            WindowKind.Settlement => "本局结算", WindowKind.Failure => "加载失败", _ => "暂停"
        };
        void Update()
        {
            if (Pause == null) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) HandleEscape();
            else if (Keyboard.current?.bKey.wasPressedThisFrame == true || Keyboard.current?.tabKey.wasPressedThisFrame == true) ToggleInventory();
            if (Content?.player != null && Session != null)
            {
                var run = Session.Run;
                hud.text = $"{ContentTitle(Content.kind)}  波次 {run.Wave}/{run.MaxWave}  生命 {Content.player.CurrentHealth:0}/{Content.player.MaxHealth:0}"
                    + $"  骨髓 {run.Marrow}"
                    + "  器官自动攻击  B/Tab：背包  Esc：菜单";
                if (CanInteractWithShop()) hud.text += "  E：商店";
                if (Content.dummy != null) hud.text += $"  木桩生命 {Content.dummy.CurrentHealth:0}";
            }
            else hud.text = "春生 · 流程验证";
        }
        public void ToggleInventory()
        {
            if (IsTransitioning) return;
            if (windows.Count == 1 && windows[0].Kind == WindowKind.Inventory) { CloseTop(); return; }
            if (WorldInputAllowed && windows.Count == 0) OpenWindow(WindowKind.Inventory);
        }
        public bool CanInteractWithShop() => WorldInputAllowed && !HasModal && Session?.Run.Stage == RunPhase.Rest
            && Content.kind == ContentKind.Rest && Content.Merchant != null
            && Vector2.Distance(Content.player.transform.position, Content.Merchant.position) <= 2f;
        public bool TryOpenShop()
        {
            if (!CanInteractWithShop()) return false;
            OpenWindow(WindowKind.Shop); return true;
        }
        public void HandleEscape()
        {
            if (IsTransitioning) return;
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
            actions = CreateView(); actions.title.text = "春生";
            actions.AddText("开始新局 · 暂不支持存档");
            actions.AddButton("开始新局", StartNewRun);
            FocusFirst(actions);
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
                actions.AddText("B / Tab：背包 · 靠近商人 E：商店");
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
            var view = Push(WindowKind.Settings, "提示"); view.AddText(text);
            view.AddButton("关闭", CloseTop); FocusFirst(view);
        }
        public void OpenWindow(WindowKind kind)
        {
            if (IsTransitioning) return;
            if (kind == WindowKind.Inventory && (HasModal || Session == null ||
                (Session.Run.Stage != RunPhase.Rest && Session.Run.Stage != RunPhase.Combat))) return;
            if (kind == WindowKind.Shop && !CanInteractWithShop()) return;
            if (windows.Any(w => w.Kind == kind)) return;
            var view = Push(kind, kind == WindowKind.Pause ? $"暂停 · {ContentTitle(Content.kind)}" : WindowTitle(kind));
            switch (kind)
            {
                case WindowKind.Pause:
                    view.AddButton("继续游戏", CloseTop);
                    view.AddButton("设置", () => OpenWindow(WindowKind.Settings));
                    view.AddButton("返回主菜单（放弃本局）", RequestMainMenu);
                    break;
                case WindowKind.Settings:
                    view.AddText("器官按管道节拍自动攻击，无需攻击按键或玩家充能。\nB / Tab：背包；Esc：返回。\n移动与疾跑沿用 Player/Move、Player/Sprint 输入设置。");
                    view.AddButton("关闭", CloseTop); break;
                case WindowKind.Confirm:
                    view.AddText("确定放弃本局吗？当前进度不会保存。");
                    view.AddButton("取消", CloseTop); view.AddButton("放弃本局", AbandonRun); break;
                case WindowKind.Inventory:
                    view.GetComponent<FlowLaboratoryWindow>().Bind(this);
                    break;
                case WindowKind.Shop:
                    BuildShop(view); break;
                case WindowKind.Settlement:
                    view.AddText(Session.Run.Stage == RunPhase.Won ? "挑战成功！" : "挑战失败");
                    view.AddText($"到达第 {Session.Run.Wave} 波，持有骨髓 {Session.Run.Marrow}");
                    view.AddButton("开始新局", StartNewRun);
                    view.AddButton("主菜单", RequestMainMenu); break;
            }
            view.gameObject.SetActive(true);
            FocusFirst(view);
        }
        void BuildShop(FlowWindow view)
        {
            var run = Session.Run;
            var balance = view.AddText("");
            var message = view.AddText("本波商品保留，付费可刷新；每次刷新费用递增，下波重置。仅可出售未装备物品。");
            var selection = view.AddText("未选择物品");
            string selected = null;
            var choices = new List<Button>();
            var offers = run.GetShopOffers();
            var offerButtons = new Button[offers.Count];
            Button sell = null, reroll = null;
            Action refresh = null;
            Action refreshOffers = null;
            refreshOffers = () => {
                offers = run.GetShopOffers();
                for (var i = 0; i < offerButtons.Length; i++)
                {
                    var offer = offers[i];
                    var organ = Configuration.Organs[offer.OrganId];
                    var button = offerButtons[i];
                    button.interactable = !offer.Purchased
                        && run.Marrow >= offer.Price;
                    button.GetComponentInChildren<Text>().text = offer.Purchased
                        ? $"已购买 {organ.name}"
                        : $"购买 {organ.name}（{offer.Price} 骨髓）";
                    button.name = button.GetComponentInChildren<Text>().text;
                }
                if (reroll != null)
                {
                    reroll.interactable = offers.Count > 0 && run.Marrow >= run.ShopRefreshPrice;
                    reroll.GetComponentInChildren<Text>().text = $"刷新商品（{run.ShopRefreshPrice} 骨髓）";
                }
            };
            refresh = () => {
                balance.text = $"骨髓 {run.Marrow} · 背包 {run.Inventory.UnequippedIds().Count}/{run.Inventory.UnequippedCapacity()} · 本波已刷新 {run.ShopRefreshCount} 次";
                selected = null; selection.text = run.Inventory.UnequippedIds().Count == 0 ? "空库存：没有可出售物品" : "请选择出售物品";
                if (sell != null) sell.interactable = false;
                foreach (var button in choices) { button.gameObject.SetActive(false); Destroy(button.gameObject); }
                choices.Clear();
                foreach (var id in run.Inventory.UnequippedIds())
                {
                    string captured = id;
                    var organ = run.Inventory.OrganOf(id);
                    choices.Add(view.AddButton(organ.name + " #" + id, () => {
                        if (!run.Inventory.UnequippedIds().Contains(captured)) { refresh(); return; }
                        selected = captured; selection.text = $"{organ.name} · 售价 {run.Inventory.SalePrice(captured)} 骨髓";
                        sell.interactable = true;
                    }));
                }
                refreshOffers();
            };
            for (var i = 0; i < offers.Count; i++)
            {
                var offerIndex = i;
                offerButtons[i] = view.AddButton("商品", () => {
                    var offer = run.GetShopOffers()[offerIndex];
                    var result = run.TryPurchaseShopOffer(offer);
                    message.text = result.Ok ? $"购买成功：{Configuration.Organs[offer.OrganId].name}" : SpringUp.UI.ChineseText.Error(result.Reason);
                    refresh();
                });
            }
            reroll = view.AddButton("刷新商品", () => {
                int price = run.ShopRefreshPrice;
                var result = run.TryRefreshShop();
                message.text = result.Ok ? $"已刷新商品，花费 {price} 骨髓。" : SpringUp.UI.ChineseText.Error(result.Reason);
                refresh();
            });
            sell = view.AddButton("出售所选物品", () => {
                if (selected == null || !run.Inventory.UnequippedIds().Contains(selected)) { message.text = "请重新选择有效物品"; refresh(); return; }
                var equipment = new RunLaboratoryEquipment(run, Configuration.Definitions);
                message.text = equipment.TrySell(selected, out var error) ? "出售成功" : error; refresh();
            });
            view.AddButton("关闭", CloseTop); refresh();
        }

        FlowWindow Push(WindowKind kind, string title)
        {
            if (actions != null) actions.group.interactable = false;
            if (windows.Count > 0) windows[windows.Count - 1].View.group.interactable = false;
            var focus = eventSystem.currentSelectedGameObject;
            var view = kind == WindowKind.Inventory ? Instantiate(laboratoryPrefab, uiRoot) : CreateView();
            if (view.title != null) view.title.text = title;
            windows.Add(new WindowEntry { Kind = kind, View = view, Lease = Pause.Acquire(), Focus = focus });
            return view;
        }
        public void CloseTop()
        {
            if (windows.Count == 0) return;
            var last = windows[windows.Count - 1]; windows.RemoveAt(windows.Count - 1);
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
