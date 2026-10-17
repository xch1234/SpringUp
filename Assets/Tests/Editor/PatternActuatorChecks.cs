using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpringUp.Organs;
using SpringUp.Laboratory;
using Tideflesh.Systems;
using TideBorne.Battle;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using BodyPart = SpringUp.Organs.BodyPart;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public sealed class PatternActuatorChecks
    {
        private sealed class World : IEffectWorld
        {
            public Vector2? Owner = Vector2.zero;
            public Vector2? OwnerPosition => Owner;
            public readonly Dictionary<string, Vector2> Positions = new Dictionary<string, Vector2>();
            public readonly Dictionary<string, float> Health = new Dictionary<string, float>();
            public readonly Dictionary<string, float> Slows = new Dictionary<string, float>();
            public readonly List<CastEvent> Hits = new List<CastEvent>();
            public float Healing;
            public float? Wall;
            public void Add(string id, Vector2 position, float health = 100) { Positions[id] = position; Health[id] = health; }
            public IReadOnlyList<AttackTarget> Targets => Positions.Select(p => new AttackTarget(p.Key, p.Value, Health[p.Key] > 0)).ToArray();
            public bool Sweep(Vector2 from, Vector2 to, float radius, out ActuatorHit hit)
            {
                float nearest = float.PositiveInfinity; string id = null;
                foreach (var target in Targets)
                    if (target.IsAlive && ProjectileGeometry.SegmentCircle(from, to, target.Position, radius + .2f, out float t) && t < nearest)
                    { nearest = t; id = target.Id; }
                if (SweepWall(from, to, radius, out var wall))
                {
                    float t = (to-from).sqrMagnitude < .000001f ? 0 : Vector2.Distance(from, wall.Position) / Vector2.Distance(from, to);
                    if (t <= nearest) { nearest = t; id = null; }
                }
                hit = float.IsPositiveInfinity(nearest) ? default : new ActuatorHit(id, Vector2.Lerp(from, to, nearest));
                return !float.IsPositiveInfinity(nearest);
            }
            public bool SweepWall(Vector2 from, Vector2 to, float radius, out ActuatorHit hit)
            {
                hit = default;
                if (!Wall.HasValue || from.x > Wall.Value || to.x < Wall.Value-radius) return false;
                float t = Mathf.Abs(to.x-from.x) < .000001f ? 0 : Mathf.Clamp01((Wall.Value-radius-from.x)/(to.x-from.x));
                hit = new ActuatorHit(null, Vector2.Lerp(from, to, t)); return true;
            }
            public IReadOnlyList<AttackTarget> BeamTargets(Vector2 from, Vector2 to, float radius)
                => Targets.Where(t => t.IsAlive && ProjectileGeometry.SegmentCircle(from, to, t.Position, radius+.2f, out _)).ToArray();
            public float Damage(CastEvent hit)
            {
                if (!Health.TryGetValue(hit.TargetId, out float health) || health <= 0) return 0;
                float actual = Mathf.Min(health, hit.Damage); Health[hit.TargetId] -= actual; Hits.Add(hit); return actual;
            }
            public void ApplyHit(CastEvent hit) => Damage(hit);
            public void NotifyDebuff(CastEvent cause) { }
            public float Heal(float amount, CastEvent? cause = null) { Healing += amount; return amount; }
            private sealed class Lease : IDisposable
            {
                private Action release;
                public Lease(Action release) { this.release = release; }
                public void Dispose() { var action = release; release = null; action?.Invoke(); }
            }
            public IDisposable Slow(string targetId, string sourceId, float multiplier, CastEvent? cause = null)
            { Slows[sourceId] = multiplier; return new Lease(() => Slows.Remove(sourceId)); }
        }
        private static ActuatorContext Context(PatternActuatorSettings settings, float damage = 5)
            => new ActuatorContext(new CastEvent("a", damage, "pattern", "instance", new CastContext(), allowZeroDamage: damage == 0),
                BodyPart.Leg, 5, Vector2.zero, Vector2.right, settings);
        private static IActuatorExecution Create(World world, PatternActuatorSettings settings, float damage = 5)
            => EffectExecutionFactory.Create(Context(settings, damage), world);

        [Test] public void MixedEffectsStressExpiresAndCancelsCleanly()
        {
            var world = new World();
            var runner = new ActuatorRunner();
            var actuator = new StraightProjectileActuator();
            int started = 0, ended = 0;
            runner.Started += _ => started++; runner.Ended += _ => ended++;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 32; i++)
                foreach (ActuatorPattern pattern in Enum.GetValues(typeof(ActuatorPattern)))
                    runner.Start(actuator, Context(new PatternActuatorSettings(pattern)), world);
            for (int frame = 0; frame < 650; frame++) runner.Advance(.02f);
            Assert.AreEqual(384, started); Assert.AreEqual(started, ended);
            Assert.AreEqual(0, runner.Active.Count); Assert.AreEqual(0, world.Slows.Count);
            for (int i = 0; i < 256; i++) runner.Start(actuator, Context(new PatternActuatorSettings(ActuatorPattern.Fog)), world);
            runner.Clear(); runner.Clear();
            Assert.AreEqual(started, ended); Assert.AreEqual(0, runner.Active.Count);
            Debug.Log("[Actuator Stress] PASS: 384 mixed effects expire, 256 cancel; simulated world, " + watch.ElapsedMilliseconds + " ms (not a rendered frame benchmark).");
        }

        [Test] public void AssetsCoverAllThirteenAndValidateSnapshots()
        {
            var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
            Assert.AreEqual(36, definitions.Count);
            Assert.AreEqual(13, definitions.Count(d => d.Projectile != null));
            Assert.AreEqual(12, definitions.Select(d => d.Projectile).OfType<PatternActuatorSettings>().Select(p => p.Pattern).Distinct().Count());
            var summon = definitions.Single(d => d.Id == "summon_follower");
            Assert.AreEqual(0, summon.Damage); Assert.IsTrue(summon.CanAttack);
            Assert.Throws<ArgumentOutOfRangeException>(() => new PatternActuatorSettings(ActuatorPattern.Fog, interval: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PatternActuatorSettings(ActuatorPattern.Orbit, angularSpeed: float.NaN));
            Assert.Throws<ArgumentException>(() => new PatternActuatorSettings(ActuatorPattern.Vampire, lifeSteal: 2));
        }

        [Test] public void CurvesFollowOwnerAndHomingLosesTargetWithoutRetargeting()
        {
            var world = new World();
            var orbit = Create(world, new PatternActuatorSettings(ActuatorPattern.Orbit)); orbit.Tick(.25f);
            Assert.AreEqual(Mathf.Sqrt(.5f), orbit.Position.x, .002f); Assert.AreEqual(Mathf.Sqrt(.5f), orbit.Position.y, .002f);
            world.Owner = Vector2.right * 2; orbit.Tick(.02f);
            Assert.AreEqual(1, Vector2.Distance(orbit.Position, world.Owner.Value), .002f);
            var spiral = Create(world, new PatternActuatorSettings(ActuatorPattern.Spiral, speed: 3, orbitRadius: .5f, angularSpeed: 360)); spiral.Tick(.25f);
            Assert.AreEqual(.25f, spiral.Position.x, .002f); Assert.AreEqual(.5f, spiral.Position.y, .002f);
            world.Add("a", new Vector2(3,2));
            var homing = Create(world, new PatternActuatorSettings(ActuatorPattern.Homing, speed: 2)); homing.Tick(.1f);
            Assert.Greater(homing.Position.y, 0);
            world.Health["a"] = 0; Vector2 before = homing.Position;
            homing.Tick(.1f); Vector2 first = homing.Position - before; before = homing.Position;
            world.Add("b", new Vector2(-5,-5)); homing.Tick(.1f);
            Assert.Less(Vector2.Distance(first, homing.Position-before), .001f);
            world.Owner = null; orbit.Tick(.02f); Assert.AreEqual(ActuatorEndReason.Cancelled, orbit.EndReason);
        }

        [Test] public void TripleHitsIndependentlyAndMissileExplodesExactlyOnce()
        {
            var world = new World(); world.Add("a", Vector2.right*2);
            world.Add("b", new Vector2(Mathf.Cos(Mathf.PI/6), Mathf.Sin(Mathf.PI/6))*2);
            world.Add("c", new Vector2(Mathf.Cos(Mathf.PI/6), -Mathf.Sin(Mathf.PI/6))*2);
            var triple = Create(world, new PatternActuatorSettings(ActuatorPattern.Triple)); triple.Tick(1); triple.Tick(1);
            Assert.AreEqual(3, world.Hits.Count); Assert.IsTrue(triple.IsFinished);
            Assert.IsTrue(world.Health.Values.All(h => h == 95));
            world = new World(); world.Add("a", Vector2.right, 20); world.Add("b", new Vector2(1,.5f), 20);
            var missile = Create(world, new PatternActuatorSettings(ActuatorPattern.Missile, explosionDamage:6)); missile.Tick(1); missile.Tick(1);
            Assert.AreEqual(9, world.Health["a"]); Assert.AreEqual(14, world.Health["b"]); Assert.AreEqual(3, world.Hits.Count);
            Assert.AreEqual(1, world.Hits.Select(h => h.ChainId).Distinct().Count());
        }

        [Test] public void BeamsPierceOnceRespectWallsAndWaitForCharge()
        {
            var world = new World(); world.Add("a", Vector2.right); world.Add("b", Vector2.right*3); world.Add("c", new Vector2(2,1));
            var beam = Create(world, new PatternActuatorSettings(ActuatorPattern.Laser)); beam.Tick(1);
            Assert.AreEqual(2, world.Hits.Count); Assert.AreEqual(100, world.Health["c"]);
            world.Hits.Clear(); world.Wall = 2;
            var charged = Create(world, new PatternActuatorSettings(ActuatorPattern.ChargedLaser));
            charged.Tick(.99f); Assert.IsEmpty(world.Hits); world.Owner = new Vector2(9,9);
            charged.Tick(.02f); charged.Tick(1); Assert.AreEqual(1, world.Hits.Count); Assert.AreEqual("a", world.Hits[0].TargetId);
        }

        [Test] public void FogTicksAtOriginSuckerReleasesOnlyOwnSlowAndVampireUsesActualDamage()
        {
            var world = new World(); world.Add("a", Vector2.right*.5f); world.Add("b", Vector2.right*4);
            var fog = Create(world, new PatternActuatorSettings(ActuatorPattern.Fog, lifetime:1.1f));
            world.Owner = new Vector2(8,8); fog.Tick(1.05f);
            Assert.AreEqual(Vector2.zero, fog.Position); Assert.AreEqual(3, world.Hits.Count); Assert.AreEqual(100, world.Health["b"]);
            fog.Cancel(); fog.Tick(1); Assert.AreEqual(3, world.Hits.Count);
            world = new World(); world.Add("a", Vector2.right*.3f);
            var sucker1 = Create(world, new PatternActuatorSettings(ActuatorPattern.Sucker));
            var sucker2 = Create(world, new PatternActuatorSettings(ActuatorPattern.Sucker));
            sucker1.Tick(.02f); sucker2.Tick(.02f); Assert.AreEqual(2, world.Slows.Count);
            sucker1.Cancel(); sucker1.Cancel(); Assert.AreEqual(1, world.Slows.Count);
            world.Health["a"] = 0; sucker2.Tick(.02f); Assert.IsEmpty(world.Slows);
            world = new World(); world.Add("a", Vector2.right*.5f, 2);
            var needle = Create(world, new PatternActuatorSettings(ActuatorPattern.Vampire, lifeSteal:.25f), 6);
            needle.Tick(.2f); needle.Tick(.2f); Assert.AreEqual(.5f, world.Healing, .0001f);
        }

        [Test] public void SporesStopAndSummonsFollowDecayAndTakeDamage()
        {
            var world = new World();
            var spore = Create(world, new PatternActuatorSettings(ActuatorPattern.Spore, speed:1)); spore.Tick(2);
            Assert.AreEqual(1, spore.Position.x, .002f); spore.Tick(.5f); Assert.AreEqual(1, spore.Position.x, .002f);
            world.Add("a", Vector2.right); spore.Tick(.02f); Assert.IsTrue(spore.IsFinished); Assert.AreEqual(1, world.Hits.Count);
            world = new World();
            var summon = (SummonExecution)Create(world, new PatternActuatorSettings(ActuatorPattern.Summon), 0);
            summon.Tick(2); Assert.AreEqual(16, summon.Health, .002f); Assert.IsEmpty(world.Hits);
            world.Owner = Vector2.right*3; summon.Tick(.5f); Assert.Greater(summon.Position.x, 0);
            summon.TakeDamage(summon.Health); Assert.IsTrue(summon.IsFinished); Assert.AreEqual(0, summon.Health, .002f);
            summon = (SummonExecution)Create(world, new PatternActuatorSettings(ActuatorPattern.Summon), 0);
            summon.Tick(11); Assert.IsTrue(summon.IsFinished); Assert.Less(summon.Health, .002f);
        }

        [Test] public void AllPatternsCancelAndSelfEffectsWorkWithoutEnemy()
        {
            var world = new World(); world.Add("a", new Vector2(.5f,0));
            var runner = new ActuatorRunner();
            foreach (ActuatorPattern pattern in Enum.GetValues(typeof(ActuatorPattern)))
                runner.Start(new StraightProjectileActuator(), Context(new PatternActuatorSettings(pattern), pattern == ActuatorPattern.Summon ? 0 : 5), world);
            runner.Advance(.02f); runner.Clear(); int count = world.Hits.Count; float healing = world.Healing;
            runner.Advance(10); Assert.IsEmpty(runner.Active); Assert.IsEmpty(world.Slows);
            Assert.AreEqual(count, world.Hits.Count); Assert.AreEqual(healing, world.Healing);
            var go = new GameObject("Self effect test");
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>(); pipeline.OriginProvider = () => Vector2.zero;
                pipeline.TargetSelector = () => null;
                int emitted = 0; pipeline.ActuatorProduced += _ => emitted++;
                var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
                foreach (var id in new[] { "orbit_projectile", "poison_fog", "summon_follower" })
                {
                    pipeline.GetBody(BodyPart.Head).SetSlot(0, new SpringUp.Organs.OrganInstance(definitions.Single(d => d.Id == id)));
                    Assert.IsTrue(pipeline.TryExecuteSlot(BodyPart.Head, 0));
                }
                Assert.AreEqual(3, emitted);
                pipeline.GetBody(BodyPart.Head).ResetTiming();
                pipeline.StepInterval = .1f;
                pipeline.AdvanceAutomatic(.1f);
                Assert.AreEqual(4, emitted, "Self effects automatically execute without enemies.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void CheckEveryPreview()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/LaboratoryDemo.unity", OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var presenter = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LaboratoryPresenter>()).Single();
                presenter.InitializeView(); var demo = presenter.GetComponentInChildren<LaboratoryCombatDemo>(); demo.Initialize();
                var equipment = (TemporaryEquipment)presenter.source.Equipment;
                foreach (var definition in OrganCatalog.LoadDefault().CreateDefinitions().Where(d => d.Projectile != null))
                {
                    for (int part = 0; part < 4; part++) for (int slot = 0; slot < equipment.SlotCount((LaboratoryPart)part); slot++) equipment.TryUnequip((LaboratoryPart)part,slot,out _);
                    var item = equipment.Inventory.First(i => i.DefinitionId == definition.Id);
                    Assert.IsTrue(equipment.TryEquip(item.InstanceId,LaboratoryPart.Head,0,out _));
                    Assert.IsTrue(demo.Pipeline.TryExecuteSlot(BodyPart.Head,0), definition.Id);
                    demo.Advance(.02f); Canvas.ForceUpdateCanvases(); demo.Advance(.3f); Canvas.ForceUpdateCanvases(); demo.Restart();
                    Assert.IsEmpty(demo.Actuators.Active, definition.Id);
                }
            }
            finally { EditorSceneManager.CloseScene(scene,true); SceneManager.SetActiveScene(previous); }
            Debug.Log("[All Actuators] PREVIEW PASS: all 13 definitions equip, start, update, render and reset.");
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在隔离工程运行。");
            EditorSceneManager.OpenScene("Assets/Scenes/OrganDemo.unity");
            var errors = new List<string>();
            void Log(string condition, string trace, LogType type)
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition); }
            Application.logMessageReceived += Log;
            try
            {
                var checks = new PatternActuatorChecks();
                foreach (var method in typeof(PatternActuatorChecks).GetMethods().Where(m => Attribute.IsDefined(m,typeof(TestAttribute)))) method.Invoke(checks,null);
                CheckEveryPreview();
                Assert.IsEmpty(errors, "执行器/预览产生异常日志：" + string.Join("\n",errors));
            }
            finally { Application.logMessageReceived -= Log; }
            Debug.Log("[All Actuators] LOGIC PASS: 7 behavior groups covering all 12 added patterns and shared cleanup.");
            StraightProjectileChecks.RunBatch();
        }

        public static void RunPlay(SpringUpRunDriver driver)
        {
            EnemyRuntime.ClearBattlefield();
            var target = new GameObject("All actuator integration target"); target.SetActive(false);
            target.transform.position = driver.player.transform.position + Vector3.right * .6f;
            var data = target.AddComponent<EnemyData>(); var edit = new SerializedObject(data);
            edit.FindProperty("maxHealth").floatValue = 1000; edit.ApplyModifiedPropertiesWithoutUndo();
            var enemy = target.AddComponent<EnemyRuntime>(); target.AddComponent<CircleCollider2D>().radius = .2f;
            target.AddComponent<BoxCollider2D>().size = Vector2.one * .4f; target.SetActive(true);
            var body = driver.pipeline.GetBody(BodyPart.Head); var previous = body.GetSlot(0);
            try
            {
                foreach (var definition in driver.Definitions.Values.Where(d => d.Projectile != null))
                {
                    body.SetSlot(0,new SpringUp.Organs.OrganInstance(definition));
                    Assert.IsTrue(driver.pipeline.TryExecuteSlot(BodyPart.Head,0), definition.Id);
                    driver.combat.Advance(.02f);
                    foreach (var execution in driver.combat.Actuators.Active)
                    {
                        var visuals = new List<EffectVisual>();
                        if (execution is IEffectVisuals effect) effect.GetVisuals(visuals);
                        Assert.IsTrue(CastEvent.IsFinite(execution.Position));
                    }
                    driver.combat.Clear(); Assert.IsEmpty(driver.combat.Actuators.Active);
                }
                driver.player.TakeDamage(10);
                float hp = driver.player.CurrentHealth;
                body.SetSlot(0,new SpringUp.Organs.OrganInstance(driver.Definitions["vampire_needle"]));
                Assert.IsTrue(driver.pipeline.TryExecuteSlot(BodyPart.Head,0)); driver.combat.Advance(.2f);
                Assert.AreEqual(Mathf.Min(driver.player.MaxHealth,hp+1.5f),driver.player.CurrentHealth,.002f);
                driver.combat.Clear();
                body.SetSlot(0,new SpringUp.Organs.OrganInstance(driver.Definitions["octopus_sucker"]));
                Assert.IsTrue(driver.pipeline.TryExecuteSlot(BodyPart.Head,0)); driver.combat.Advance(.2f);
                Assert.AreEqual(.5f,enemy.OrganSpeedMultiplier,.001f);
                enemy.OrganSpeedMultiplier = .8f;
                Assert.AreEqual(.4f,enemy.OrganSpeedMultiplier,.001f);
                driver.combat.Actuators.Clear(); Assert.AreEqual(.8f,enemy.OrganSpeedMultiplier,.001f);
                float health = enemy.CurrentHealth;
                body.SetSlot(0,new SpringUp.Organs.OrganInstance(driver.Definitions["laser"]));
                Assert.IsTrue(driver.pipeline.TryExecuteSlot(BodyPart.Head,0)); driver.combat.Advance(.3f);
                Assert.AreEqual(health-8,enemy.CurrentHealth,.001f);
            }
            finally
            {
                driver.combat.Clear(); body.SetSlot(0,previous); driver.player.ResetHealthToMax();
                Object.DestroyImmediate(target);
            }
            Debug.Log("[All Actuators] PLAY PASS: 13 pipeline starts, real lifesteal, independent slow cleanup, compound-collider beam single damage.");
        }

        public static void CapturePreview(SpringUpRunDriver driver, LaboratoryCombatDemo demo)
        {
            string[] definitions = { "poison_fog", "homing_projectile", "charged_laser", "summon_follower" };
            var instances = new List<string>();
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    string id = driver.Run.Inventory.Spawn(definitions[i]); instances.Add(id);
                    driver.Equipment.Refresh();
                    Assert.IsTrue(driver.Equipment.TryEquip(id,LaboratoryPart.Head,i+1,out _));
                }
                for (int i = 0; i <= definitions.Length; i++) Assert.IsTrue(demo.Pipeline.TryExecuteSlot(BodyPart.Head,i));
                demo.Advance(.02f); Canvas.ForceUpdateCanvases();
                IntegratedVisualChecks.Capture("all_actuator_preview",1440,900);
            }
            finally
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    driver.Equipment.TryUnequip(LaboratoryPart.Head,i+1,out _);
                    driver.Equipment.TryDiscard(instances[i],out _);
                }
            }
        }
    }
}
