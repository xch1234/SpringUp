using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpringUp.Organs;
using SpringUp.Laboratory;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Definition = SpringUp.Organs.OrganDefinition;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public sealed class StraightProjectileChecks
    {
        private sealed class World : IActuatorWorld
        {
            public readonly List<(string id, Vector2 position)> Targets = new List<(string, Vector2)>();
            public readonly List<CastEvent> Hits = new List<CastEvent>();
            public Action OnHit;
            public bool Sweep(Vector2 from, Vector2 to, float radius, out ActuatorHit hit)
            {
                float nearest = float.PositiveInfinity; string id = null; bool found = false;
                foreach (var target in Targets)
                    if (ProjectileGeometry.SegmentCircle(from, to, target.position, radius + .2f, out float fraction) && fraction < nearest)
                    { nearest = fraction; id = target.id; found = true; }
                hit = found ? new ActuatorHit(id, Vector2.Lerp(from, to, nearest)) : default;
                return found;
            }
            public void ApplyHit(CastEvent hit) { Hits.Add(hit); OnHit?.Invoke(); }
        }
        private static ActuatorContext Context(float speed = 8, float lifetime = 2)
            => new ActuatorContext(new CastEvent("original", 5, "straight_projectile", "instance", new CastContext()),
                BodyPart.Leg, 5, Vector2.zero, Vector2.right, new StraightProjectileSettings(speed, .12f, lifetime));

        [Test]
        public void Config_UsesSnapshotAndRejectsInvalidParameters()
        {
            var config = Object.Instantiate(AssetDatabase.LoadAssetAtPath<StraightProjectileConfig>(
                "Assets/Data/Organs/Actuators/StraightProjectileBehaviour.asset"));
            try
            {
                Assert.IsTrue(config.TryCreateDefinition(out var original, out _));
                var serialized = new SerializedObject(config);
                serialized.FindProperty("speed").floatValue = 3; serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(config.TryCreateDefinition(out var changed, out _));
                Assert.AreEqual(8, original.Projectile.Speed); Assert.AreEqual(3, changed.Projectile.Speed);
                serialized.FindProperty("lifetime").floatValue = 0; serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(config.TryCreateDefinition(out _, out _));
                Assert.Throws<ArgumentOutOfRangeException>(() => new StraightProjectileSettings(float.NaN, 1, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => new StraightProjectileSettings(1, float.PositiveInfinity, 1));
                Assert.IsFalse(ParameterisedBehaviour.TryCreateAttack(new SpringUp.Organs.OrganInstance(original), "target", out _));
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void HighSpeed_FirstHitOnly_PreservesSourceAndChain()
        {
            var world = new World(); world.Targets.Add(("far", Vector2.right * 8)); world.Targets.Add(("near", Vector2.right * 2));
            var context = Context(100);
            var execution = new StraightProjectileExecution(context, world);
            Assert.IsEmpty(world.Hits);
            execution.Tick(.1f); execution.Tick(1);
            Assert.AreEqual(1, world.Hits.Count); Assert.AreEqual("near", world.Hits[0].TargetId);
            Assert.AreSame(context.Cause.Context, world.Hits[0].Context);
            Assert.AreEqual("instance", world.Hits[0].OriginInstanceId);
            Assert.AreEqual(BodyPart.Leg, execution.Context.SourcePart);
            Assert.AreEqual(5, execution.Context.SourceSlotIndex);
            Assert.AreEqual(ActuatorEndReason.Hit, execution.EndReason);
        }

        [Test]
        public void ExpiryClipsMovement_AndCancellationIsIdempotent()
        {
            var world = new World(); world.Targets.Add(("too-far", Vector2.right * 3));
            var execution = new StraightProjectileExecution(Context(10, .1f), world);
            execution.Tick(100);
            Assert.AreEqual(1, execution.Position.x, .0001f); Assert.IsEmpty(world.Hits);
            Assert.AreEqual(ActuatorEndReason.Expired, execution.EndReason);
            var runner = new ActuatorRunner(); int cancelled = 0;
            runner.Ended += value => { Assert.AreEqual(ActuatorEndReason.Cancelled, value.EndReason); cancelled++; };
            runner.Start(new StraightProjectileActuator(), Context(), world);
            runner.Clear(); runner.Clear(); runner.Advance(1);
            Assert.AreEqual(1, cancelled); Assert.IsEmpty(runner.Active); Assert.IsEmpty(world.Hits);
        }

        [Test]
        public void DirectionDoesNotFollowTarget_OriginOverlapHits()
        {
            var world = new World(); world.Targets.Add(("moved", new Vector2(1, 2)));
            var execution = new StraightProjectileExecution(Context(), world);
            execution.Tick(.5f);
            Assert.AreEqual(0, execution.Position.y); Assert.IsEmpty(world.Hits);
            world.Targets.Clear(); world.Targets.Add(("overlap", Vector2.zero));
            execution = new StraightProjectileExecution(Context(), world); execution.Tick(.01f);
            Assert.AreEqual(1, world.Hits.Count); Assert.AreEqual(Vector2.zero, execution.Position);
        }

        [Test]
        public void KillCallbackCanCancelOtherShotsWithoutDoubleEnding()
        {
            var world = new World(); world.Targets.Add(("target", Vector2.right));
            var runner = new ActuatorRunner(); int ended = 0;
            runner.Ended += _ => ended++;
            runner.Start(new StraightProjectileActuator(), Context(), world);
            runner.Start(new StraightProjectileActuator(), Context(), world);
            world.OnHit = runner.Clear;
            runner.Advance(1);
            Assert.AreEqual(1, world.Hits.Count); Assert.AreEqual(2, ended); Assert.IsEmpty(runner.Active);
        }

        public static void RunPhysicsPlayChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("真实敌人生命周期检查必须在 Play 中运行。");
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.CreateScene("StraightProjectilePhysics-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(scene);
            try
            {
                var target = new GameObject("Enemy"); target.SetActive(false); target.transform.position = Vector2.right * 2;
                target.AddComponent<EnemyData>(); var enemy = target.AddComponent<EnemyRuntime>();
                target.AddComponent<BoxCollider2D>(); target.AddComponent<CircleCollider2D>(); target.SetActive(true);
                var wall = new GameObject("Wall"); wall.transform.position = Vector2.right;
                wall.AddComponent<BoxCollider2D>().size = new Vector2(.1f, 2);
                int hits = 0;
                var world = new BattleActuatorWorld(scene, hit => { enemy.TakeAttack(hit); hits++; });
                float health = enemy.CurrentHealth;
                var execution = new StraightProjectileExecution(Context(100), world); execution.Tick(.1f);
                Assert.AreEqual(ActuatorEndReason.Blocked, execution.EndReason); Assert.AreEqual(health, enemy.CurrentHealth);
                Object.DestroyImmediate(wall);
                execution = new StraightProjectileExecution(Context(100), world); execution.Tick(.1f); execution.Tick(.1f);
                Assert.AreEqual(1, hits); Assert.AreEqual(health - 5, enemy.CurrentHealth);
                var oldHit = Context().Hit(enemy.TargetId, enemy.Position);
                target.SetActive(false); target.SetActive(true);
                float freshHealth = enemy.CurrentHealth;
                Assert.AreEqual(0, enemy.TakeAttack(oldHit)); Assert.AreEqual(freshHealth, enemy.CurrentHealth);
            }
            finally { SceneManager.SetActiveScene(previous); SceneManager.UnloadSceneAsync(scene); }
            Debug.Log("[Straight Projectile Checks] PHYSICS PASS: wall blocking, compound collider single hit and stale target rejection.");
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在隔离 batchmode 工程运行。");
            EditorSceneManager.OpenScene("Assets/Scenes/OrganDemo.unity");
            // 一个 batch 进程完成逻辑回归，再进入已有 Play 流程，避免反复启动编译。
            var checks = new StraightProjectileChecks();
            checks.Config_UsesSnapshotAndRejectsInvalidParameters();
            checks.HighSpeed_FirstHitOnly_PreservesSourceAndChain();
            checks.ExpiryClipsMovement_AndCancellationIsIdempotent();
            checks.DirectionDoesNotFollowTarget_OriginOverlapHits();
            checks.KillCallbackCanCancelOtherShotsWithoutDoubleEnding();
            UniversalEquipmentChecks.RunBatch();
            // 这些已有测试自带资源清理，无共享 SetUp 状态。
            foreach (var type in new[] { typeof(ProgramDTests), typeof(BattleRunLinkTests), typeof(AutomaticAttackChecks) })
                foreach (var method in type.GetMethods().Where(m => Attribute.IsDefined(m, typeof(TestAttribute))))
                    method.Invoke(Activator.CreateInstance(type), null);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/LaboratoryDemo.unity", OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var presenter = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LaboratoryPresenter>()).Single();
                var demo = presenter.GetComponentInChildren<LaboratoryCombatDemo>();
                presenter.InitializeView(); demo.Initialize();
                SpringUp.LaboratoryEditor.LaboratorySettlementChecks.Run(presenter, demo);
            }
            finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
            Debug.Log("[Straight Projectile Checks] EDIT PASS: 5 projectile tests, 5 equipment tests, 51 organ groups, inventory/flow/automatic-attack tests and laboratory settlement.");
            IntegratedFlowChecks.RunBatch();
        }
    }
}
