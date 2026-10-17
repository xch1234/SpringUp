using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    public static class BattleSceneSyncChecks
    {
        public static void CheckScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var driver = roots.SelectMany(g => g.GetComponentsInChildren<SpringUpRunDriver>(true)).Single();
            Assert.AreEqual(9, roots.Single(g => g.name == "MapBackground").GetComponentsInChildren<SpriteRenderer>().Length);
            Assert.AreEqual(4, roots.Single(g => g.name == "BoundaryWalls").GetComponentsInChildren<BoxCollider2D>().Length);
            Assert.IsFalse(roots.Any(g => g.name == "Ground"));
            Assert.AreEqual(new Vector3(12, 0, 0), roots.Single(g => g.name == "TrainingDummy").transform.position);
            Assert.AreEqual(10.125f, driver.battleCamera.orthographicSize);
            Assert.AreSame(driver.player.transform, driver.battleCamera.GetComponent<CameraFollow>().Target);
            Assert.AreEqual(10, driver.player.Stats.MoveSpeed);
            Assert.AreEqual(.15f, driver.player.Stats.BaseCooldown);
            Assert.AreEqual(2, driver.player.Stats.BaseCycleDuration);
            Assert.AreEqual(RigidbodyType2D.Kinematic, driver.player.GetComponent<Rigidbody2D>().bodyType);
            Assert.AreEqual(BattleLayers.Resolve(BattleLayers.Player), driver.player.gameObject.layer);
            Assert.IsFalse(new SerializedObject(driver.director).FindProperty("autoStart").boolValue);
            Assert.NotNull(driver.presenter); Assert.NotNull(driver.laboratoryRoot); Assert.NotNull(driver.pipeline);
            Assert.AreEqual(0, roots.SelectMany(g => g.GetComponentsInChildren<DevDamageField>(true)).Count());
            Assert.AreEqual(36, new SerializedObject(driver.director.GetComponent<EnemySpawner>()).FindProperty("playAreaHalfWidth").floatValue);
            foreach (var root in roots)
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    Assert.IsNotNull(component, "Missing script in merged scene");
            Debug.Log("[Battle Scene Sync] EDIT PASS: map, walls, dummy, camera, player, spawner and retained run wiring.");
        }

        public static void RunPlay(SpringUpRunDriver driver)
        {
            var player = driver.player; var motor = player.GetComponent<PlayerMotor>();
            var body = player.GetComponent<Rigidbody2D>(); var collider = player.GetComponent<BoxCollider2D>();
            var position = body.position; float rotation = body.rotation;
            var interpolation = body.interpolation;
            var input = typeof(PlayerMotor).GetField("_moveInput", BindingFlags.Instance | BindingFlags.NonPublic);
            var previousInput = input.GetValue(motor);
            var mode = Physics2D.simulationMode;
            var follow = driver.battleCamera.GetComponent<CameraFollow>();
            var cameraPosition = follow.transform.position;
            try
            {
                Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(BattleLayers.Resolve(BattleLayers.Player), BattleLayers.Resolve(BattleLayers.Enemy)));
                Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(BattleLayers.Resolve(BattleLayers.Player), BattleLayers.Resolve(BattleLayers.Wall)));
                Physics2D.simulationMode = SimulationMode2D.Script; body.interpolation = RigidbodyInterpolation2D.None;
                foreach (var direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down, Vector2.one.normalized })
                {
                    body.position = direction * 34; body.rotation = 0; Physics2D.SyncTransforms();
                    input.SetValue(motor, direction);
                    for (int i = 0; i < 170; i++) { motor.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    Physics2D.SyncTransforms(); var bounds = collider.bounds;
                    Assert.LessOrEqual(bounds.max.x, 36.05f); Assert.GreaterOrEqual(bounds.min.x, -36.05f);
                    Assert.LessOrEqual(bounds.max.y, 36.05f); Assert.GreaterOrEqual(bounds.min.y, -36.05f);
                }
                body.position = new Vector2(35, 0); body.rotation = 0; Physics2D.SyncTransforms();
                input.SetValue(motor, Vector2.one.normalized);
                for (int i = 0; i < 40; i++) { motor.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                Assert.Greater(body.position.y, 2, "Movement can slide along a wall.");
                body.position = new Vector2(35, 35); player.transform.position = new Vector3(35, 35, 0); Physics2D.SyncTransforms(); follow.SendMessage("LateUpdate");
                Assert.IsTrue(follow.IsBoundedByMap);
                Assert.LessOrEqual(follow.transform.position.x + driver.battleCamera.orthographicSize * driver.battleCamera.aspect, 36.01f);
                Assert.LessOrEqual(follow.transform.position.y + driver.battleCamera.orthographicSize, 36.01f);
                var dummy = player.gameObject.scene.GetRootGameObjects().Single(g => g.name == "TrainingDummy");
                var dummyPosition = dummy.transform.position;
                body.position = new Vector2(10, 0); body.rotation = 0; player.transform.SetPositionAndRotation(new Vector3(10, 0, 0), Quaternion.identity); Physics2D.SyncTransforms();
                var enemy = dummy.GetComponent<EnemyRuntime>();
                float health = enemy.CurrentHealth;
                Assert.IsTrue(driver.pipeline.TryExecuteSlot(SpringUp.Organs.BodyPart.Hand, 0), "dummy=" + dummy.transform.position + " player=" + player.transform.position + " target=" + driver.pipeline.TargetSelector?.Invoke() + " health=" + health);
                Assert.Less(enemy.CurrentHealth, health, "The synced dummy can receive organ damage.");
                input.SetValue(motor, Vector2.right);
                for (int i = 0; i < 30; i++) { motor.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                Assert.Greater(body.position.x, 14, "Player passes through dummy.");
                Assert.Less(Vector3.Distance(dummy.transform.position, dummyPosition), .01f, "Player does not push dummy.");
            }
            finally
            {
                Physics2D.simulationMode = mode; input.SetValue(motor, previousInput);
                body.position = position; body.rotation = rotation; body.linearVelocity = Vector2.zero;
                player.transform.SetPositionAndRotation(new Vector3(position.x, position.y, player.transform.position.z), Quaternion.Euler(0, 0, rotation));
                Physics2D.SyncTransforms(); body.interpolation = interpolation; follow.transform.position = cameraPosition;
                player.ResetHealthToMax(); player.ResetDamageProtection();
            }
            Debug.Log("[Battle Scene Sync] PLAY PASS: runtime collision rules, four walls/corner, sliding, camera bounds, dummy damage and pass-through.");
        }
    }
}
