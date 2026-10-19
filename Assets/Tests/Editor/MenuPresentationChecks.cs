using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpringUp.Flow;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class MenuPresentationChecks
    {
        const string Running = "SpringUp.MenuPresentationChecks";
        static int stage;
        static double next, deadline;
        static float previousAlpha;
        static MenuPresentationChecks()
        { if (SessionState.GetBool(Running, false)) EditorApplication.playModeStateChanged += State; }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            MenuUiBuilder.RebuildSettings();
            var settings = MenuPresentationSettingsEditor.EnsureAsset();
            Assert.IsNotNull(settings.cover.sprite); Assert.IsNotNull(settings.background.sprite);
            Assert.IsNotNull(settings.defeat.sprite); Assert.IsNotNull(settings.victory.sprite);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CaptureSettings();
            Capture(false, false, 1280, 720); Capture(false, false, 1024, 768);
            Capture(false, false, 900, 1200);
            Capture(true, false, 1280, 720); Capture(true, true, 1280, 720);
            Capture(true, true, 1024, 768);
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Running, true); EditorApplication.playModeStateChanged += State;
            EditorApplication.EnterPlaymode();
        }
        static void Capture(bool settlement, bool won, int width, int height)
        {
            var camera = new GameObject("Capture camera").AddComponent<Camera>();
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            var font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 32);
            var flow = MenuPresentationView.Create(canvas.transform, settlement, font);
            var view = flow.GetComponent<MenuPresentationView>();
            var viewport = canvas.gameObject.AddComponent<GameViewport>(); viewport.Initialize(canvas);
            foreach (var layer in canvas.GetComponentsInChildren<Canvas>()) layer.worldCamera = camera;
            var worldCamera = new GameObject("World camera").AddComponent<Camera>(); worldCamera.enabled = false;
            viewport.Register(worldCamera);
            var settings = MenuPresentationSettings.Load();
            if (settlement) view.BindSettlement(won, () => { });
            var target = new RenderTexture(width, height, 24); var active = RenderTexture.active;
            camera.targetTexture = target; camera.Render();
            canvas.scaleFactor = Mathf.Min(width / GameViewport.ReferenceSize.x, height / GameViewport.ReferenceSize.y);
            viewport.Refresh(width, height); Canvas.ForceUpdateCanvases(); view.Refresh(); Canvas.ForceUpdateCanvases();
            camera.Render(); // Newly visible bars need a render to receive Canvas graphic depth in EditMode.
            Assert.AreEqual(GameViewport.Aspect, viewport.Content.rect.width / viewport.Content.rect.height, .001f);
            Assert.AreEqual(GameViewport.Aspect, worldCamera.aspect, .001f);
            Assert.AreEqual(viewport.Viewport, worldCamera.rect);
            Assert.AreEqual(GameViewport.Aspect, viewport.Viewport.width * width / (viewport.Viewport.height * height), .001f);
            if (!settlement)
            {
                int start = 0, options = 0, quit = 0;
                var menu = flow.GetComponent<MenuSettingsView>();
                menu.BindMenu(() => start++, () => options++, () => quit++);
                foreach (var button in menu.buttons) button.onClick.Invoke();
                Assert.AreEqual(1, start); Assert.AreEqual(1, options); Assert.AreEqual(1, quit);
                // Button centers remain inside the image's authored pixel coordinates at either aspect ratio.
                var art = flow.transform.Find("Reference layout/Artwork").GetComponent<Image>();
                foreach (var button in menu.buttons)
                {
                    var screen = RectTransformUtility.WorldToScreenPoint(camera, button.transform.position);
                    Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(art.rectTransform, screen, camera));
                }
                Assert.Greater(menu.buttons[0].transform.position.y, menu.buttons[2].transform.position.y);
                Assert.Greater(menu.buttons[2].transform.position.y, menu.buttons[1].transform.position.y);
            }
            else
            {
                var art = flow.transform.Find("Reference layout/Artwork").GetComponent<Image>();
                Assert.AreEqual(won ? settings.victory.sprite : settings.defeat.sprite, art.sprite);
                float old = settings.background.opacity; settings.background.opacity = .42f; view.Refresh();
                Assert.AreEqual(.42f * settings.background.tint.a, flow.transform.Find("Background").GetComponent<Image>().color.a, .001f);
                settings.background.opacity = old; view.Refresh();
                var events = new GameObject("Events", typeof(EventSystem)).GetComponent<EventSystem>();
                var pointer = new PointerEventData(events);
                foreach (var point in new[] { new Vector2(2, 2), new Vector2(width - 2, height - 2), new Vector2(width / 2, height / 2), new Vector2(width / 2, height / 5) })
                {
                    pointer.position = point; var hits = new System.Collections.Generic.List<RaycastResult>();
                    // BaseRaycaster registration requires PlayMode. Invoke modules explicitly in this EditMode capture.
                    foreach (var raycaster in canvas.GetComponentsInChildren<GraphicRaycaster>().OrderByDescending(r => r.GetComponent<Canvas>().sortingOrder))
                        raycaster.Raycast(pointer, hits);
                    Assert.IsNotEmpty(hits, $"Raycast at {point}, viewport {viewport.Viewport}, resolution {width}x{height}");
                    bool inside = viewport.Viewport.Contains(new Vector2(point.x / width, point.y / height));
                    if (inside) Assert.AreEqual(flow.gameObject, hits[0].gameObject, "Anywhere inside game hits return surface");
                    else Assert.AreEqual("Letterbox bars", hits[0].gameObject.transform.parent.name, "Black bars intercept clicks");
                }
                Object.DestroyImmediate(events.gameObject);
            }
            camera.Render(); RenderTexture.active = target;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            if (Mathf.Abs(width / (float)height - GameViewport.Aspect) > .001f)
            { var corner = pixels.GetPixel(1, 1); Assert.Less(corner.r + corner.g + corner.b, .01f, "Letterbox pixels are black"); }
            Directory.CreateDirectory("Logs/MenuPresentation");
            File.WriteAllBytes($"Logs/MenuPresentation/{(settlement ? won ? "victory" : "defeat" : "menu")}-{width}.png", pixels.EncodeToPNG());
            RenderTexture.active = active; camera.targetTexture = null;
            Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(canvas.gameObject);
            Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(font);
            Object.DestroyImmediate(worldCamera.gameObject);
        }
        static void State(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 1; deadline = next + 90;
            Application.runInBackground = true; EditorApplication.update += Tick;
        }
        static MenuPresentationView Settlement(FlowApplication app) => app.uiRoot.GetComponentsInChildren<MenuPresentationView>().Single(v => v.name == "Settlement artwork");
        static void CheckRunHud(FlowApplication app, bool rest)
        {
            app.BattleHud.Refresh();
            Assert.IsFalse(app.BattleHud.HelpText.gameObject.activeInHierarchy, "Lower-left controls hint is hidden");
            var panel = app.uiRoot.Find("Debug actions"); Assert.IsNotNull(panel);
            var settings = app.BattleHud.Settings; Assert.IsFalse(settings.showDebugPanel);
            app.SendMessage("RefreshContentActionsVisibility"); Assert.IsFalse(panel.gameObject.activeSelf);
            if (rest) Assert.IsTrue(app.uiRoot.Find("Next wave").gameObject.activeSelf);
            settings.showDebugPanel = true; app.SendMessage("RefreshContentActionsVisibility");
            Assert.IsTrue(panel.gameObject.activeSelf, "Debug toggle takes effect without restarting");
            if (rest) Assert.IsFalse(app.uiRoot.Find("Next wave").gameObject.activeSelf);
            settings.showDebugPanel = false; app.SendMessage("RefreshContentActionsVisibility");
            Assert.IsFalse(panel.gameObject.activeSelf);
        }
        static void CaptureSettings()
        {
            var camera = new GameObject("Settings capture").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .04f, .07f);
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            var flow = Object.Instantiate(Resources.Load<FlowWindow>("Menus/Settings"), canvas.transform);
            var view = flow.GetComponent<MenuSettingsView>();
            var font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 30);
            foreach (var text in flow.GetComponentsInChildren<Text>()) text.font = font;
            for (int i = 0; i < view.bindings.Length; i++) view.bindings[i].GetComponentInChildren<Text>().text = GameSettings.Display((GameBinding)i);
            var target = new RenderTexture(1024, 768, 24); var active = RenderTexture.active;
            camera.targetTexture = target; canvas.scaleFactor = 1024 / 1448f;
            camera.Render(); Canvas.ForceUpdateCanvases(); view.Fit(); Canvas.ForceUpdateCanvases(); camera.Render();
            foreach (var text in flow.GetComponentsInChildren<Text>())
            {
                Assert.LessOrEqual(text.preferredHeight, text.rectTransform.rect.height + 1, "Text fits: " + text.name);
                var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                { var p = camera.WorldToViewportPoint(corner); Assert.IsTrue(p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1, "Settings stays on screen"); }
            }
            RenderTexture.active = target; var pixels = new Texture2D(1024, 768, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Logs/MenuPresentation"); File.WriteAllBytes("Logs/MenuPresentation/settings-large.png", pixels.EncodeToPNG());
            RenderTexture.active = active; camera.targetTexture = null;
            Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(canvas.gameObject);
            Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(font);
        }
        static void Click(MenuPresentationView view, FlowApplication app)
        {
            var e = new PointerEventData(app.eventSystem) { button = PointerEventData.InputButton.Left };
            view.OnPointerDown(e); view.OnPointerClick(e);
        }
        static void Tick()
        {
            try
            {
                Assert.Less(EditorApplication.timeSinceStartup, deadline, "Timeout at " + stage);
                if (EditorApplication.timeSinceStartup < next) return;
                var app = Object.FindAnyObjectByType<FlowApplication>();
                if (app == null || app.IsTransitioning || app.Content == null) return;
                Assert.AreEqual(GameViewport.Aspect, app.Viewport.Content.rect.width / app.Viewport.Content.rect.height, .001f, "UI remains 4:3 in every scene");
                Assert.AreEqual(GameViewport.Aspect, app.menuCamera.aspect, .001f);
                if (app.Content.world != null)
                    foreach (var worldCamera in app.Content.world.GetComponentsInChildren<Camera>(true))
                    { Assert.AreEqual(GameViewport.Aspect, worldCamera.aspect, .001f); Assert.AreEqual(app.Viewport.Viewport, worldCamera.rect); }
                switch (stage)
                {
                    case 0:
                        Assert.AreEqual(ContentKind.MainMenu, app.Content.kind);
                        app.uiRoot.GetComponentInChildren<MenuSettingsView>().buttons[1].onClick.Invoke();
                        Assert.IsTrue(app.HasModal); app.CloseTop(); app.StartNewRun(); break;
                    case 1:
                        CheckRunHud(app, false);
                        app.Content.player.TakeDamage(app.Content.player.MaxHealth);
                        var view = Settlement(app); Click(view, app);
                        Assert.IsFalse(app.IsTransitioning, "Opening click is ignored");
                        previousAlpha = view.GetComponentInChildren<Text>().GetComponentInParent<CanvasGroup>().alpha;
                        break;
                    case 2:
                        Assert.AreEqual(0, Time.timeScale);
                        Assert.AreNotEqual(previousAlpha, Settlement(app).GetComponentInChildren<Text>().GetComponentInParent<CanvasGroup>().alpha, "Hint animates while paused");
                        // Release without a new press must not dismiss (held input at death).
                        Settlement(app).OnPointerClick(new PointerEventData(app.eventSystem));
                        Assert.IsFalse(app.IsTransitioning);
                        Click(Settlement(app), app); break;
                    case 3:
                        Assert.AreEqual(ContentKind.MainMenu, app.Content.kind); Assert.IsNull(app.Session);
                        Assert.IsFalse(app.HasModal); Assert.AreEqual(1, Time.timeScale);
                        app.StartNewRun(); break;
                    case 4:
                        var run = app.Session.Run;
                        for (int i = 0; i < 100 && run.Stage != RunPhase.Won; i++)
                        { if (run.CanStartNextWave()) run.StartNextWave(); run.CombatTimerFinished(); }
                        Assert.AreEqual(RunPhase.Won, run.Stage); app.ResolveRunPhase(); break;
                    case 5:
                        Assert.AreEqual(MenuPresentationSettings.Load().victory.sprite, Settlement(app).transform.Find("Reference layout/Artwork").GetComponent<Image>().sprite);
                        Click(Settlement(app), app); break;
                    case 6:
                        Assert.AreEqual(ContentKind.MainMenu, app.Content.kind); Assert.IsNull(app.Session);
                        Assert.IsFalse(app.HasModal); Assert.AreEqual(1, Time.timeScale);
                        app.StartNewRun(); break;
                    case 7:
                        app.Content.director.CompleteCurrentWave(); break;
                    case 8:
                        Assert.AreEqual(ContentKind.Rest, app.Content.kind); CheckRunHud(app, true);
                        app.uiRoot.Find("Next wave").GetComponent<Button>().onClick.Invoke(); break;
                    case 9:
                        Assert.AreEqual(ContentKind.Battle, app.Content.kind); CheckRunHud(app, false); app.AbandonRun(); break;
                    case 10:
                        Assert.AreEqual(ContentKind.MainMenu, app.Content.kind); Assert.IsNull(app.Session);
                        Debug.Log("MENU_PRESENTATION_CHECKS_PASS: larger settings layout, hidden controls hint, live debug toggle in battle/rest, standalone next-wave progression, fixed 4:3 UI and cameras, artwork, callbacks, paused blink, loss/win return to title.");
                        Finish(0); return;
                }
                stage++; next = EditorApplication.timeSinceStartup + .65;
            }
            catch (Exception e) { Debug.LogException(e); Finish(1); }
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Running, false); EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= State; EditorApplication.Exit(code);
        }
    }
}
