using System;
using System.Linq;
using System.IO;
using NUnit.Framework;
using SpringUp.Flow;
using SpringUp.Organs;
using SpringUp.Laboratory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class SummonFollowerChecks
    {
        const string Running = "SpringUp.SummonFollowerChecks";
        static int stage;
        static double next, deadline;
        static string instance;
        static Vector2 previous;
        static Sprite pausedSprite;
        static float pausedHealth;
        static SummonExecution Summon(FlowApplication app) => app.Content.combat.Actuators.Active.OfType<SummonExecution>().Single();
        static SpriteRenderer View(FlowApplication app) => app.Content.effects.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "Summon follower");
        static SummonFollowerChecks()
        { if (SessionState.GetBool(Running, false)) EditorApplication.playModeStateChanged += State; }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            var art = OrganArtAuthoring.RefreshCatalog();
            var config = Resources.Load<SummonVisualSettings>("SummonVisualSettings");
            Assert.AreEqual(38, art.entries.Length); Assert.IsTrue(config.IsValid); Assert.AreEqual(10, config.frames.Length);
            for (int i = 0; i < 10; i++) Assert.AreEqual("summon_follower_" + i, config.frames[i].name);
            Assert.AreEqual(config.frames[0], art.Find("summon_follower"));
            Assert.AreEqual(10, AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Organs/summon_follower.png").OfType<Sprite>().Count(), "Refreshing icons preserves animation slicing");
            new PatternActuatorChecks().SporesStopAndSummonsFollowDecayAndTakeDamage();
            SummonCombatChecks.RunLogic();
            RuntimeBugfixChecks.RunLogic();
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Running, true); EditorApplication.playModeStateChanged += State;
            EditorApplication.EnterPlaymode();
        }
        static void State(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 1; deadline = next + 100;
            Application.runInBackground = true; EditorApplication.update += Tick;
        }
        static void Tick()
        {
            try
            {
                Assert.Less(EditorApplication.timeSinceStartup, deadline, "Timeout at " + stage);
                var app = Object.FindAnyObjectByType<FlowApplication>();
                if (app != null)
                    Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>().Count(l => l.isActiveAndEnabled), "Exactly one listener, including during scene transitions");
                if (EditorApplication.timeSinceStartup < next) return;
                if (app == null || app.IsTransitioning || app.Content == null) return;
                var content = app.Content;
                switch (stage)
                {
                    case 0: app.StartNewRun(); break;
                    case 1:
                        Assert.IsEmpty(content.combat.Actuators.Active.OfType<SummonExecution>());
                        content.director.CompleteCurrentWave(); break;
                    case 2:
                        Assert.AreEqual(ContentKind.Rest, content.kind);
                        content.player.transform.position = content.Laboratory.position;
                        Assert.IsTrue(app.TryOpenLaboratory());
                        instance = app.Session.Run.Inventory.Spawn("summon_follower");
                        Assert.IsEmpty(content.combat.Actuators.Active.OfType<SummonExecution>(), "Backpack item does not summon");
                        app.Session.Run.Equip(instance, "hand", 2);
                        Assert.IsEmpty(content.combat.Actuators.Active.OfType<SummonExecution>(), "Equipping waits for the summon slot");
                        var icon = OrganUiBuilder.Icon(app.uiRoot, "Summon icon check", Resources.Load<OrganArtCatalog>("OrganArtCatalog"), 0, 0, 50, false);
                        icon.Bind(new LaboratoryItem(instance, app.Configuration.Definitions["summon_follower"]));
                        Assert.IsTrue(icon.icon.enabled); Assert.AreEqual("summon_follower_0", icon.icon.sprite.name);
                        Object.DestroyImmediate(icon.gameObject); break;
                    case 3:
                        Assert.AreEqual(0, Time.timeScale);
                        Assert.IsEmpty(content.combat.Actuators.Active.OfType<SummonExecution>(), "Paused equipment cannot summon");
                        Assert.AreEqual(0, content.effects.SummonViewCount);
                        app.CloseTop();
                        AdvanceToSummon(content);
                        content.effects.SendMessage("Update");
                        Assert.AreEqual(1, content.effects.SummonViewCount); Assert.IsNotNull(View(app).sprite);
                        Assert.AreEqual(1.8f, Vector2.Distance(Summon(app).Position, content.player.transform.position), .01f, "Summon appears at the increased follow distance");
                        CheckHealthBar(app);
                        previous = Summon(app).Position;
                        content.player.transform.position += Vector3.right * 3; break;
                    case 4:
                        Assert.Greater(Summon(app).Position.x, previous.x);
                        Assert.IsTrue(View(app).flipX, "Left-facing artwork flips when following right");
                        Summon(app).TakeDamage(5);
                        CheckHealthBar(app);
                        Capture(app, "summon-healthbar.png");
                        Assert.IsTrue(content.pipeline.TryExecuteSlot(BodyPart.Hand, 1));
                        Assert.IsTrue(content.pipeline.TryExecuteSlot(BodyPart.Hand, 1));
                        Assert.AreEqual(1, content.combat.Actuators.Active.OfType<SummonExecution>().Count(), "Repeated execution does not stack followers");
                        previous = Summon(app).Position;
                        content.player.transform.position -= Vector3.right * 4; break;
                    case 5:
                        Assert.Less(Summon(app).Position.x, previous.x); Assert.IsFalse(View(app).flipX);
                        app.ToggleInventory(); pausedSprite = View(app).sprite; pausedHealth = Summon(app).Health; previous = Summon(app).Position; break;
                    case 6:
                        Assert.AreSame(pausedSprite, View(app).sprite); Assert.AreEqual(pausedHealth, Summon(app).Health);
                        Assert.AreEqual(previous, Summon(app).Position, "Pause freezes movement, animation and lifetime");
                        app.CloseTop(); content.player.transform.position = content.Laboratory.position;
                        Assert.IsTrue(app.TryOpenLaboratory());
                        app.Session.Run.Unequip("hand", 2); break;
                    case 7:
                        Assert.IsEmpty(content.combat.Actuators.Active.OfType<SummonExecution>());
                        Assert.AreEqual(0, content.effects.SummonViewCount, "Unequip removes summon and visual");
                        app.Session.Run.Equip(instance, "hand", 2);
                        Assert.IsEmpty(content.combat.Actuators.Active.OfType<SummonExecution>(), "Re-equipping also waits for the slot");
                        app.CloseTop(); AdvanceToSummon(content); break;
                    case 8:
                        SummonCombatChecks.RunPlay(content.player);
                        Capture(app, "summon-rest.png"); app.NextWave(); break;
                    case 9:
                        Assert.AreEqual(ContentKind.Battle, content.kind);
                        if (!content.combat.Actuators.Active.OfType<SummonExecution>().Any()) AdvanceToSummon(content);
                        content.effects.SendMessage("Update");
                        Assert.AreEqual(1, content.effects.SummonViewCount);
                        Assert.AreEqual(instance, Summon(app).Context.Cause.OriginInstanceId);
                        Capture(app, "summon-battle.png");
                        Summon(app).TakeDamage(Summon(app).Health); content.combat.Advance(0);
                        Assert.IsEmpty(content.combat.Actuators.Active.OfType<SummonExecution>());
                        Assert.IsTrue(content.pipeline.TryExecuteSlot(BodyPart.Hand, 1));
                        Assert.IsNotNull(Summon(app), "Expired summon can be recreated by organ execution");
                        app.AbandonRun(); break;
                    case 10:
                        Assert.AreEqual(ContentKind.MainMenu, content.kind);
                        Assert.IsFalse(Object.FindObjectsByType<SpriteRenderer>().Any(r => r.name == "Summon follower"), "No summon leaks after leaving run");
                        Debug.Log("SUMMON_FOLLOWER_CHECKS_PASS: frame icon, preserved slicing, equip/unequip, follow/flip, pause, deduplication, lifetime, scene transitions and cleanup.");
                        Finish(0); return;
                }
                stage++; next = EditorApplication.timeSinceStartup + .45;
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        static void AdvanceToSummon(FlowContent content)
        {
            var pipeline = content.pipeline;
            bool automatic = pipeline.AutomaticExecution;
            try
            {
                pipeline.AutomaticExecution = true;
                float step = pipeline.StepIntervalProvider?.Invoke(BodyPart.Hand) ?? pipeline.StepInterval;
                pipeline.AdvanceAutomatic(Mathf.Max(BodyRuntime.MinimumStepInterval, step) * 2);
            }
            finally { pipeline.AutomaticExecution = automatic; }
        }
        static void CheckHealthBar(FlowApplication app)
        {
            app.Content.effects.SendMessage("Update");
            var view = View(app);
            var fill = view.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "Summon health fill");
            var background = view.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "Summon health background");
            var settings = Resources.Load<SummonVisualSettings>("SummonVisualSettings");
            Assert.IsTrue(fill.enabled); Assert.IsTrue(background.enabled);
            Assert.Greater(fill.GetPosition(0).y, view.bounds.max.y, "Health bar stays above sprite");
            Assert.AreEqual(fill.GetPosition(0).y, fill.GetPosition(1).y, .001f, "Flipping does not rotate the bar");
            Assert.AreEqual(settings.healthBarSize.x * Summon(app).Health / Summon(app).MaxHealth,
                Vector3.Distance(fill.GetPosition(0), fill.GetPosition(1)), .001f, "Bar length tracks actual health");
            Assert.AreEqual((Color32)settings.healthBarColor, (Color32)fill.startColor);
        }
        static void Capture(FlowApplication app, string name)
        {
            var camera = app.Content.world.GetComponentInChildren<Camera>();
            var canvas = app.hud.GetComponentInParent<Canvas>();
            var mode = canvas.renderMode; var oldCamera = canvas.worldCamera; float distance = canvas.planeDistance;
            var oldTarget = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24); var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); app.BattleHud.Refresh(); Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                Directory.CreateDirectory("Logs/Presentation"); File.WriteAllBytes("Logs/Presentation/" + name, pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = oldCamera; canvas.planeDistance = distance;
                camera.targetTexture = oldTarget; RenderTexture.active = active;
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Running, false); EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= State; EditorApplication.Exit(code);
        }
    }
}
