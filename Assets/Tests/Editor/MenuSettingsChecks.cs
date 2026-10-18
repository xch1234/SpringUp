using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpringUp.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public class MenuSettingsTests
    {
        string previous;
        const string Pref = "SpringUp.Settings.v1";
        [SetUp] public void Setup() { previous = PlayerPrefs.GetString(Pref, ""); GameSettings.RestoreDefaults(); }
        [TearDown] public void Cleanup()
        { if (previous.Length == 0) PlayerPrefs.DeleteKey(Pref); else PlayerPrefs.SetString(Pref, previous); PlayerPrefs.Save(); GameSettings.Load(); }
        [Test] public void BindingsAndVolumesPersistAndRejectConflicts()
        {
            Assert.IsTrue(GameSettings.TryBind(GameBinding.Up, Key.I, out _));
            Assert.IsFalse(GameSettings.TryBind(GameBinding.Down, Key.I, out _));
            foreach (var key in new[] { Key.Escape, Key.Tab, Key.UpArrow, Key.None })
                Assert.IsFalse(GameSettings.TryBind(GameBinding.Up, key, out _));
            GameSettings.SetVolumes(0, .27f); GameSettings.Load();
            Assert.AreEqual(Key.I, GameSettings.Binding(GameBinding.Up));
            Assert.AreEqual(Key.S, GameSettings.Binding(GameBinding.Down));
            Assert.AreEqual(0, GameSettings.Music); Assert.AreEqual(.27f, GameSettings.Sfx);
            GameSettings.RestoreDefaults(); Assert.AreEqual(Key.W, GameSettings.Binding(GameBinding.Up));
        }
        [Test] public void MovementOverridesRetainArrowBackup()
        {
            var asset = Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Settings/InputSystem_Actions.inputactions"));
            try
            {
                GameSettings.TryBind(GameBinding.Up, Key.I, out _);
                GameSettings.TryBind(GameBinding.Sprint, Key.Q, out _);
                var move = asset.FindAction("Player/Move"); var sprint = asset.FindAction("Player/Sprint");
                GameSettings.ApplyMovement(move, sprint);
                Assert.IsTrue(move.bindings.Any(b => b.path == "<Keyboard>/w" && b.effectivePath == "<Keyboard>/i"));
                Assert.IsTrue(move.bindings.Any(b => b.effectivePath == "<Keyboard>/upArrow"));
                Assert.IsTrue(sprint.bindings.Any(b => b.effectivePath == "<Keyboard>/q"));
            }
            finally { Object.DestroyImmediate(asset); }
        }
        [Test] public void CorruptPreferencesRecover()
        {
            PlayerPrefs.SetString(Pref, "{\"keys\":[0],\"music\":2,\"sfx\":-1}"); GameSettings.Load();
            Assert.AreEqual(Key.W, GameSettings.Binding(GameBinding.Up)); Assert.AreEqual(1, GameSettings.Music); Assert.AreEqual(0, GameSettings.Sfx);
        }
    }

    [InitializeOnLoad]
    public static class MenuSettingsChecks
    {
        const string Running = "SpringUp.MenuChecks";
        static int stage;
        static double next, deadline;
        static Keyboard keyboard;
        static Mouse mouse;
        static MenuSettingsView settings;
        static Vector3 position;
        static MenuSettingsChecks() { if (SessionState.GetBool(Running, false)) EditorApplication.playModeStateChanged += State; }
        public static void RunBatch()
        {
            var test = new MenuSettingsTests();
            foreach (Action check in new Action[] { test.BindingsAndVolumesPersistAndRejectConflicts, test.MovementOverridesRetainArrowBackup, test.CorruptPreferencesRecover })
            { test.Setup(); try { check(); } finally { test.Cleanup(); } }
            SessionState.SetString(Running + "Prefs", PlayerPrefs.GetString("SpringUp.Settings.v1", ""));
            GameSettings.RestoreDefaults();
            Directory.CreateDirectory("Logs/Presentation");
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Running, true); EditorApplication.playModeStateChanged += State; EditorApplication.EnterPlaymode();
        }
        static void State(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 1; deadline = next + 100;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            EditorApplication.update += Tick;
        }
        static MenuSettingsView Top(FlowApplication app) => app.uiRoot.GetComponentsInChildren<MenuSettingsView>().Last(v => v.GetComponent<CanvasGroup>().alpha > 0);
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                Assert.Less(EditorApplication.timeSinceStartup, deadline, "Timeout at " + stage);
                var app = Object.FindAnyObjectByType<FlowApplication>();
                if (app == null || app.IsTransitioning || app.Content == null) return;
                switch (stage)
                {
                    case 0:
                        Assert.AreEqual(ContentKind.MainMenu, app.Content.kind);
                        CollectionAssert.AreEqual(new[] { "开始游戏", "设置", "退出" }, Top(app).buttons.Select(b => b.name).ToArray());
                        Capture(app, "menu-main", 1440, 900); Capture(app, "menu-main-wide", 1920, 1080);
                        Top(app).buttons[1].onClick.Invoke(); break;
                    case 1:
                        settings = Top(app); settings.music.value = 0; settings.sfx.value = .35f;
                        Capture(app, "menu-settings", 1440, 900); Capture(app, "menu-settings-small", 1280, 720);
                        Assert.AreEqual(0, GameSettings.Music); Assert.AreEqual(.35f, GameSettings.Sfx);
                        settings.BeginBinding((int)GameBinding.Up); break;
                    case 2:
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.I)); break;
                    case 3:
                        Assert.IsFalse(settings.IsListening); Assert.AreEqual(Key.I, GameSettings.Binding(GameBinding.Up));
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        settings.BeginBinding((int)GameBinding.Down); break;
                    case 4:
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.I)); break;
                    case 5:
                        Assert.AreEqual(Key.S, GameSettings.Binding(GameBinding.Down)); Assert.IsTrue(settings.hint.text.Contains("已用于"));
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        settings.BeginBinding((int)GameBinding.Interact); break;
                    case 6:
                        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); break;
                    case 7:
                        Assert.IsFalse(settings.IsListening); Assert.IsTrue(app.HasModal, "Right click cancels binding only");
                        InputSystem.QueueStateEvent(mouse, new MouseState()); break;
                    case 8:
                        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); break;
                    case 9:
                        Assert.IsFalse(app.HasModal); InputSystem.QueueStateEvent(mouse, new MouseState());
                        Top(app).buttons[0].onClick.Invoke(); break;
                    case 10:
                        Assert.AreEqual(ContentKind.Battle, app.Content.kind);
                        position = app.Content.player.transform.position;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.I)); break;
                    case 11:
                        Assert.Greater(app.Content.player.transform.position.y, position.y + .05f, "Rebound movement works after scene load");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        app.OpenWindow(WindowKind.Pause); break;
                    case 12:
                        Capture(app, "menu-pause", 1440, 900); Capture(app, "menu-pause-wide", 1920, 1080);
                        Top(app).buttons[1].onClick.Invoke(); break;
                    case 13:
                        Assert.AreEqual(0, Time.timeScale); app.HandleBack(); Assert.AreEqual(0, Time.timeScale);
                        Top(app).buttons[2].onClick.Invoke(); app.HandleBack(); Assert.AreEqual(0, Time.timeScale);
                        app.HandleBack(); Assert.AreEqual(1, Time.timeScale); Assert.IsTrue(app.WorldInputAllowed);
                        app.HandleBack(); Assert.IsFalse(app.HasModal, "Right click in world does not open pause");
                        GameSettings.TryBind(GameBinding.Inventory, Key.V, out _);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V)); break;
                    case 14:
                        Assert.IsTrue(app.HasModal, "Rebound inventory key");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); app.HandleBack(); Assert.IsFalse(app.HasModal);
                        GameSettings.TryBind(GameBinding.Interact, Key.J, out _);
                        app.Content.director.CompleteCurrentWave(); break;
                    case 15:
                        Assert.AreEqual(ContentKind.Rest, app.Content.kind);
                        app.Content.player.transform.position = app.Content.Laboratory.position;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J)); break;
                    case 16:
                        Assert.IsTrue(app.CanModifyEquipment, "Rebound interaction opens laboratory");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        var backpack = app.uiRoot.GetComponentInChildren<SpringUp.Laboratory.BackpackPresenter>();
                        var slot = backpack.equipmentSlots.Concat(backpack.inventorySlots).First(s => s.Item != null);
                        string id = slot.Item.InstanceId;
                        backpack.BeginDrag(slot, new UnityEngine.EventSystems.PointerEventData(app.eventSystem));
                        Assert.IsTrue(backpack.IsDragging); app.HandleBack();
                        Assert.IsFalse(backpack.IsDragging); Assert.IsTrue(app.HasModal); Assert.AreEqual(id, slot.Item.InstanceId);
                        app.HandleBack(); Assert.IsFalse(app.HasModal); app.AbandonRun(); break;
                    case 17:
                        Assert.AreEqual(ContentKind.MainMenu, app.Content.kind);
                        app.OpenWindow(WindowKind.Settings); Assert.AreEqual("I", GameSettings.Display(GameBinding.Up));
                        Top(app).buttons[0].onClick.Invoke(); Assert.AreEqual(Key.W, GameSettings.Binding(GameBinding.Up));
                        Assert.IsNull(app.Session, "Restore defaults does not create a run"); app.HandleBack();
                        Debug.Log("MENU_SETTINGS_CHECKS_PASS: preferences, keyboard capture/conflicts/cancel, movement, inventory, nested pause, right click, scene persistence, layout.");
                        Finish(0); return;
                }
                stage++; next = EditorApplication.timeSinceStartup + .3;
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        static void Capture(FlowApplication app, string name, int width, int height)
        {
            var canvas = app.hud.GetComponentInParent<Canvas>(); var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldScale = canvas.scaleFactor;
            var camera = new GameObject("Menu capture").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10000); camera.orthographic = true; camera.orthographicSize = 450;
            camera.farClipPlane = 100; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .035f, .05f);
            var rt = new RenderTexture(width, height, 24); var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
                canvas.scaleFactor = Mathf.Min(width / 1440f, height / 900f);
                foreach (var text in canvas.GetComponentsInChildren<Text>()) text.SetAllDirty();
                Canvas.ForceUpdateCanvases(); foreach (var view in app.uiRoot.GetComponentsInChildren<MenuSettingsView>()) view.Fit(); Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = rt;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes("Logs/Presentation/" + name + ".png", image.EncodeToPNG()); Object.DestroyImmediate(image);
                foreach (var view in app.uiRoot.GetComponentsInChildren<MenuSettingsView>().Where(v => v.GetComponent<CanvasGroup>().alpha > 0))
                    foreach (var control in view.GetComponentsInChildren<Selectable>())
                    {
                        var corners = new Vector3[4]; ((RectTransform)control.transform).GetWorldCorners(corners);
                        foreach (var corner in corners)
                        { var p = camera.WorldToViewportPoint(corner); Assert.IsTrue(p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1, control.name + " stays on screen"); }
                    }
            }
            finally
            { RenderTexture.active = previous; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.scaleFactor = oldScale; Object.DestroyImmediate(camera.gameObject); rt.Release(); Object.DestroyImmediate(rt); }
        }
        static void Finish(int code)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
            string previous = SessionState.GetString(Running + "Prefs", "");
            if (previous.Length == 0) PlayerPrefs.DeleteKey("SpringUp.Settings.v1"); else PlayerPrefs.SetString("SpringUp.Settings.v1", previous);
            PlayerPrefs.Save(); GameSettings.Load();
            SessionState.SetBool(Running, false); EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= State; EditorApplication.Exit(code);
        }
    }
}
