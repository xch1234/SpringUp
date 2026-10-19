using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SpringUp.Organs;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class ProjectileVisualChecks
    {
        const string Running = "SpringUp.ProjectileVisualChecks";
        static ProjectileVisualChecks()
        { if (SessionState.GetBool(Running, false)) EditorApplication.playModeStateChanged += State; }
        public static void RunBatch()
        {
            var settings = ProjectileVisualAuthoring.EnsureAsset();
            Assert.AreEqual(1, settings.orbit.frames.Length); Assert.AreEqual(5, settings.flying.frames.Length);
            var regression = new PatternActuatorChecks();
            regression.CurvesFollowOwnerAndHomingLosesTargetWithoutRetargeting();
            regression.TripleHitsIndependentlyAndMissileExplodesExactlyOnce();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Running, true); EditorApplication.playModeStateChanged += State;
            EditorApplication.EnterPlaymode();
        }
        static void State(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.update += Run;
        }
        sealed class World : IEffectWorld
        {
            public bool RemoveLower;
            public Vector2 Owner;
            public Vector2? OwnerPosition => Owner;
            public IReadOnlyList<AttackTarget> Targets => Array.Empty<AttackTarget>();
            public bool Sweep(Vector2 a, Vector2 b, float radius, out ActuatorHit hit)
            { hit = new ActuatorHit("target", b); return RemoveLower && b.y < -.01f; }
            public void ApplyHit(CastEvent hit) { }
            public float Damage(CastEvent hit) => 0;
            public float Heal(float amount, CastEvent? cause = null) => 0;
            public IDisposable Slow(string target, string source, float multiplier, CastEvent? cause = null) => null;
            public void NotifyDebuff(CastEvent cause) { }
            public bool SweepWall(Vector2 a, Vector2 b, float radius, out ActuatorHit hit) { hit = default; return false; }
            public IReadOnlyList<AttackTarget> BeamTargets(Vector2 a, Vector2 b, float radius) => Targets;
        }
        static ActuatorContext Context(StraightProjectileSettings settings) => new ActuatorContext(
            new CastEvent("target", 5, "visual-test", "instance", new CastContext()), BodyPart.Hand, 0, Vector2.zero, Vector2.right, settings);
        static void Render(BattleOrganEffects effects) => typeof(BattleOrganEffects).GetMethod("RenderProjectiles", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(effects, null);
        static ProjectileSpriteView[] Active(GameObject go) => go.GetComponentsInChildren<ProjectileSpriteView>();
        static void Run()
        {
            EditorApplication.update -= Run;
            try
            {
                var settings = Resources.Load<ProjectileVisualSettings>("ProjectileVisualSettings");
                var go = new GameObject("Projectile integration");
                var combat = go.AddComponent<BattleOrganCombat>(); combat.enabled = false;
                go.GetComponent<OrganPipelineController>().AutomaticExecution = false;
                var effects = go.AddComponent<BattleOrganEffects>();
                var world = new World(); var actuator = new StraightProjectileActuator();
                var configs = new StraightProjectileSettings[] { new StraightProjectileSettings(8, .12f, 4),
                    new PatternActuatorSettings(ActuatorPattern.Orbit), new PatternActuatorSettings(ActuatorPattern.Spiral),
                    new PatternActuatorSettings(ActuatorPattern.Homing), new PatternActuatorSettings(ActuatorPattern.Triple) };
                foreach (var config in configs) combat.Actuators.Start(actuator, Context(config), world);
                Render(effects); Assert.AreEqual(7, Active(go).Length, "All five player projectile types use sprites, including three individual shots");
                Assert.IsEmpty(go.GetComponentsInChildren<LineRenderer>(), "No duplicate placeholder rings for textured projectiles");
                var orbit = Active(go).Single(v => v.Renderer.sprite == settings.orbit.frames[0]);
                Assert.AreEqual(1, Vector2.Distance(orbit.transform.position, world.Owner), .001f);
                world.Owner = new Vector2(2, 3); combat.Actuators.Advance(.1f); Render(effects);
                Assert.AreEqual(1, Vector2.Distance(orbit.transform.position, world.Owner), .001f, "Orbit art follows the actual moving owner");
                combat.Actuators.Clear(); Render(effects); Assert.IsEmpty(Active(go), "Finished shots return to the pool");
                combat.Actuators.Start(actuator, Context(new PatternActuatorSettings(ActuatorPattern.Triple)), world);
                Render(effects); var tripleViews = Active(go).OrderBy(v => v.transform.eulerAngles.z).ToArray();
                Assert.AreEqual(3, tripleViews.Length); Assert.AreEqual(3, tripleViews.Select(v => Mathf.Round(v.transform.eulerAngles.z)).Distinct().Count());
                var triple = (TripleProjectileExecution)combat.Actuators.Active.Single();
                world.RemoveLower = true; combat.Actuators.Advance(.1f); Render(effects);
                var visuals = new List<EffectVisual>(); triple.GetVisuals(visuals);
                CollectionAssert.AreEqual(new[] { 1, 2 }, visuals.Select(v => v.Index), "Surviving shots retain stable visual identities");
                Assert.AreEqual(2, Active(go).Length);
                combat.Actuators.Clear(); Render(effects);
                world.RemoveLower = false;
                combat.Actuators.Start(actuator, Context(new PatternActuatorSettings(ActuatorPattern.Fog)), world);
                Render(effects); Assert.IsEmpty(Active(go)); Assert.AreEqual(1, go.GetComponentsInChildren<LineRenderer>().Length, "Unrelated effects retain their own visuals");
                combat.Actuators.Clear(); Render(effects); effects.Clear(); go.SetActive(false); Object.Destroy(go);

                var sample = new GameObject("Animation test").AddComponent<ProjectileSpriteView>(); sample.ResetView();
                sample.Present(settings.flying, Vector2.zero, Vector2.left, .12f, 0);
                Assert.AreEqual(180, sample.transform.eulerAngles.z, .01f);
                var first = sample.Renderer.sprite;
                sample.Present(settings.flying, Vector2.zero, Vector2.left, .12f, 1.01f / settings.flying.framesPerSecond);
                Assert.AreEqual(settings.flying.frames[1], sample.Renderer.sprite);
                Time.timeScale = 0; var paused = sample.Renderer.sprite;
                sample.Present(settings.flying, Vector2.zero, Vector2.left, .12f, 0);
                Assert.AreEqual(paused, sample.Renderer.sprite); Time.timeScale = 1;
                sample.ResetView(); sample.Present(settings.flying, Vector2.zero, Vector2.right, .12f, 0);
                Assert.AreEqual(first, sample.Renderer.sprite, "Pooled view resets animation");
                sample.gameObject.SetActive(false); Object.Destroy(sample.gameObject);
                Capture(settings);
                Debug.Log("PROJECTILE_VISUAL_CHECKS_PASS: sprite imports, all five projectile types, orbit follows owner, animation, directions, pause, pooling, stable triple identities, fallback effects, collision regressions.");
                Finish(0);
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        static void Capture(ProjectileVisualSettings settings)
        {
            var root = new GameObject("Projectile samples");
            for (int i = 0; i < 6; i++)
            {
                var go = new GameObject(i == 0 ? "Orbit" : "Flying frame " + (i - 1)); go.transform.SetParent(root.transform);
                var view = go.AddComponent<ProjectileSpriteView>(); view.ResetView();
                var style = i == 0 ? settings.orbit : settings.flying;
                var position = new Vector2((i % 3 - 1) * 1.6f, i < 3 ? .7f : -.7f);
                view.Present(style, position, Vector2.right, .22f, 0);
                if (i > 0) view.Present(style, position, Vector2.right, .22f, (i - 1 + .01f) / style.framesPerSecond);
            }
            var camera = new GameObject("Capture", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(-.2f, 0, -10); camera.orthographic = true; camera.orthographicSize = 1.8f;
            camera.backgroundColor = new Color(.035f, .07f, .11f); camera.clearFlags = CameraClearFlags.SolidColor;
            var target = new RenderTexture(1280, 720, 24); var active = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Logs/Presentation"); File.WriteAllBytes("Logs/Presentation/player-projectiles.png", pixels.EncodeToPNG());
            RenderTexture.active = active; camera.targetTexture = null;
            Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(camera.gameObject); Object.Destroy(root);
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Running, false); EditorApplication.playModeStateChanged -= State; EditorApplication.Exit(code);
        }
    }
}
