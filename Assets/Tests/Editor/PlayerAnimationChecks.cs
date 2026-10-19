using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpringUp.Flow;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class PlayerAnimationChecks
    {
        const string Running = "SpringUp.PlayerAnimationChecks";
        static int stage;
        static double next, deadline, sampleUntil;
        static Keyboard keyboard;
        static IDisposable pause;
        static Vector3 startPosition;
        static Sprite pausedSprite;
        static readonly HashSet<Sprite> Seen = new();

        static PlayerAnimationChecks()
        { if (SessionState.GetBool(Running, false)) EditorApplication.playModeStateChanged += State; }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            var config = Resources.Load<PlayerAnimationSettings>("PlayerAnimationSettings");
            Assert.IsNotNull(config); Assert.IsTrue(config.HasFrames);
            Assert.AreEqual(6, config.rightFrames.Length); Assert.AreEqual(6, config.leftFrames.Length);
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual("1709_" + i, config.rightFrames[i].name);
                Assert.AreEqual("1709_" + (i + 6), config.leftFrames[i].name);
                Assert.AreEqual(new Rect(i * 362, 362, 362, 362), config.rightFrames[i].rect);
                Assert.AreEqual(new Rect(i * 362, 0, 362, 362), config.leftFrames[i].rect);
            }
            Assert.AreEqual(2172, config.rightFrames[0].texture.width, "Sheet is not downscaled");
            Assert.AreEqual(FilterMode.Point, config.rightFrames[0].texture.filterMode);
            SessionState.SetString(Running + "Prefs", PlayerPrefs.GetString("SpringUp.Settings.v1", ""));
            GameSettings.RestoreDefaults();
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Running, true); EditorApplication.playModeStateChanged += State;
            EditorApplication.EnterPlaymode();
        }

        static void State(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 1; deadline = next + 100;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true; keyboard = InputSystem.AddDevice<Keyboard>();
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                Assert.Less(EditorApplication.timeSinceStartup, deadline, "Timeout at stage " + stage);
                var app = Object.FindAnyObjectByType<FlowApplication>();
                if (app == null || app.IsTransitioning || app.Content == null) return;
                var player = app.Content.player;
                var animation = player != null ? player.GetComponent<PlayerSpriteAnimator>() : null;
                switch (stage)
                {
                    case 0: app.StartNewRun(); break;
                    case 1:
                        Assert.IsNotNull(animation); Assert.IsNotNull(animation.Visual.sprite);
                        Assert.IsFalse(player.GetComponent<SpriteRenderer>().enabled, "Placeholder is hidden");
                        Assert.AreEqual(Color.white, animation.Visual.color, "Artwork retains its original colors");
                        Assert.AreEqual(new Vector2(1.35f, 1.35f), player.GetComponent<BoxCollider2D>().size, "Original collision shape retained");
                        Assert.AreEqual(1.5f, animation.Visual.bounds.size.y, .01f);
                        startPosition = player.transform.position;
                        Seen.Clear(); sampleUntil = EditorApplication.timeSinceStartup + .8;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); break;
                    case 2:
                        Seen.Add(animation.Visual.sprite);
                        if (EditorApplication.timeSinceStartup < sampleUntil) { next = EditorApplication.timeSinceStartup + .03; return; }
                        Assert.IsTrue(animation.IsMoving); Assert.IsFalse(animation.FacingLeft);
                        Assert.Greater(Seen.Count, 2, "Moving player cycles through multiple actual sprites");
                        Assert.Greater(player.transform.position.x, startPosition.x + .1f);
                        CollectionAssert.Contains(animation.Settings.rightFrames, animation.Visual.sprite);
                        Capture(app, "player-animation-battle.png");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A)); break;
                    case 3:
                        Assert.IsTrue(animation.FacingLeft);
                        CollectionAssert.Contains(animation.Settings.leftFrames, animation.Visual.sprite);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); break;
                    case 4:
                        Assert.IsTrue(animation.FacingLeft, "Vertical movement retains last horizontal facing");
                        Assert.Less(Quaternion.Angle(Quaternion.identity, animation.Visual.transform.rotation), .01f, "Artwork remains upright");
                        Assert.Greater(Vector2.Dot(player.GetComponent<PlayerMotor>().FacingDirection, Vector2.up), .9f, "Attack root still turns upward");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); break;
                    case 5:
                        Assert.IsFalse(animation.IsMoving); Assert.IsTrue(animation.FacingLeft);
                        Seen.Clear(); sampleUntil = EditorApplication.timeSinceStartup + .8; break;
                    case 6:
                        Seen.Add(animation.Visual.sprite);
                        if (EditorApplication.timeSinceStartup < sampleUntil) { next = EditorApplication.timeSinceStartup + .03; return; }
                        Assert.Greater(Seen.Count, 1, "Idle breathing cycles frames");
                        pausedSprite = animation.Visual.sprite;
                        app.ToggleInventory(); break;
                    case 7:
                        Assert.AreEqual(0, Time.timeScale);
                        Assert.AreSame(pausedSprite, animation.Visual.sprite, "Inventory pause freezes current frame");
                        app.CloseTop();
                        Assert.IsTrue(GameSettings.TryBind(GameBinding.Right, Key.L, out _));
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.L, Key.LeftShift)); break;
                    case 8:
                        Assert.IsTrue(animation.IsMoving); Assert.IsFalse(animation.FacingLeft);
                        Assert.IsTrue(player.GetComponent<PlayerMotor>().IsSprinting, "Remapped movement and sprint reach animation driver");
                        player.TakeDamage(5);
                        stage++; next = EditorApplication.timeSinceStartup + .065; return;
                    case 9:
                        Assert.IsTrue(player.GetComponent<HitFlashSkin>().IsFlashing);
                        var overlay = animation.Visual.transform.Find("__HitFlashSkin").GetComponent<SpriteRenderer>();
                        Assert.IsTrue(overlay.enabled); Assert.AreSame(animation.Visual.sprite, overlay.sprite, "Hit flash follows animated frame");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        app.Content.director.CompleteCurrentWave(); break;
                    case 10:
                        Assert.AreEqual(ContentKind.Rest, app.Content.kind);
                        Assert.IsNotNull(animation); Assert.IsTrue(animation.Visual.enabled);
                        Assert.IsFalse(player.GetComponent<SpriteRenderer>().enabled);
                        Assert.AreEqual(1, player.GetComponents<PlayerSpriteAnimator>().Length);
                        pause = app.Pause.Acquire();
                        Capture(app, "player-animation-rest.png");
                        pause.Dispose(); pause = null;
                        app.NextWave(); break;
                    case 11:
                        Assert.AreEqual(ContentKind.Battle, app.Content.kind);
                        Assert.IsNotNull(animation.Visual.sprite);
                        Assert.AreEqual(1, player.GetComponents<PlayerSpriteAnimator>().Length);
                        Debug.Log("PLAYER_ANIMATION_CHECKS_PASS: 12 mapped sprites, movement/idle/direction, upright visual and attack facing, collision, pause, remapped sprint, hit flash, battle/rest/next-wave.");
                        Finish(0); return;
                }
                stage++; next = EditorApplication.timeSinceStartup + .35;
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void Capture(FlowApplication app, string name)
        {
            var camera = app.Content.world.GetComponentInChildren<Camera>();
            var canvas = app.hud.GetComponentInParent<Canvas>();
            var mode = canvas.renderMode; var originalCamera = canvas.worldCamera; float distance = canvas.planeDistance;
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24); var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); app.BattleHud.Refresh(); Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                Directory.CreateDirectory("Logs/Presentation"); File.WriteAllBytes("Logs/Presentation/" + name, pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = originalCamera; canvas.planeDistance = distance;
                camera.targetTexture = previous; RenderTexture.active = active;
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        static void Finish(int code)
        {
            pause?.Dispose(); pause = null;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            string previous = SessionState.GetString(Running + "Prefs", "");
            if (previous.Length == 0) PlayerPrefs.DeleteKey("SpringUp.Settings.v1"); else PlayerPrefs.SetString("SpringUp.Settings.v1", previous);
            PlayerPrefs.Save(); GameSettings.Load();
            SessionState.SetBool(Running, false); EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= State; EditorApplication.Exit(code);
        }
    }
}
