using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpringUp.Flow;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public sealed class BattleTuningChecks
    {
        [TestCase(0, .5f)] [TestCase(19, .5f)] [TestCase(20, .7f)]
        [TestCase(49, .7f)] [TestCase(50, 1f)] [TestCase(69, 1f)] [TestCase(70, 2f)]
        public void SpawnBandsAndSoftBudget(int occupied, float expected)
        {
            var mix = EnemyWaveMixSettings.CreateDefaultInstance();
            try
            {
                mix.ForWave(4).aliveCap = 100;
                Assert.AreEqual(expected, mix.IntervalMultiplier(4, occupied, 0, 24));
                Assert.AreEqual(2, mix.IntervalMultiplier(4, occupied, 24, 24));
                Assert.AreEqual(24, mix.SlowAfterCount(4, 50));
                Assert.AreEqual(28, mix.SlowAfterCount(7, 60));
                mix.ForWave(4).slowAfterCount = 10;
                Assert.AreEqual(10, mix.SlowAfterCount(4, 50));
                mix.dynamicSpawning = false;
                Assert.AreEqual(1, mix.IntervalMultiplier(4, occupied, 24, 24));
            }
            finally { Object.DestroyImmediate(mix); }
        }

        [Test] public void KillRewardsFloorIndividuallyAndLeaveWaveRewardUnchanged()
        {
            var catalog = new Dictionary<string, Tideflesh.Systems.OrganDefinition> {
                ["fist"] = new Tideflesh.Systems.OrganDefinition { id="fist", type="executor", shop_price=10 }
            };
            var waves = new List<WaveDefinition> {
                new WaveDefinition { wave=1, duration_sec=60, marrow_reward=50, kill_organ_chance=0 },
                new WaveDefinition { wave=2, duration_sec=60, marrow_reward=60, kill_organ_chance=0 }
            };
            var run = new RunController(catalog, waves, null); run.StartRun();
            Assert.AreEqual(2, run.ResolveKill("a", 5, true).Marrow);
            Assert.AreEqual(2, run.ResolveKill("b", 5, true).Marrow);
            var zero = run.ResolveKill("c", 1, true);
            Assert.IsTrue(zero.Accepted); Assert.IsFalse(zero.HasMarrow);
            Assert.IsFalse(run.ResolveKill("a", 5, true).Accepted);
            Assert.AreEqual(4, run.Marrow);
            run.CombatTimerFinished(); Assert.AreEqual(54, run.Marrow);
        }

        [Test] public void RestZonesAndBothSceneSpeeds()
        {
            foreach (string path in new[] { FlowApplication.RestPath, FlowApplication.BattlePath })
            {
                EditorSceneManager.OpenScene(path);
                var content = Object.FindAnyObjectByType<FlowContent>();
                Assert.AreEqual(7.5f, content.player.Stats.MoveSpeed, .0001f);
                var motor = content.player.GetComponent<PlayerMotor>();
                float multiplier = new SerializedObject(motor).FindProperty("sprintMultiplier").floatValue;
                Assert.AreEqual(10, content.player.Stats.MoveSpeed * multiplier, .0001f);
                if (content.kind != ContentKind.Rest) continue;
                content.world.SetActive(true); content.EnsureMerchant();
                var ship = content.world.transform.Find("ShipBackground").GetComponent<SpriteRenderer>();
                var bounds = ship.sprite.bounds;
                void At(Vector2 uv) => content.player.transform.position = ship.transform.TransformPoint(
                    new Vector3(bounds.min.x + uv.x * bounds.size.x, bounds.min.y + uv.y * bounds.size.y, 0));
                var settings = content.InteractionSettings;
                At(settings.trainingArea.center); Assert.IsTrue(content.IsInTrainingArea());
                At(settings.shopArea.center); Assert.IsFalse(content.IsInTrainingArea()); Assert.IsTrue(content.IsNearCounter(content.Merchant));
                At(settings.laboratoryArea.center); Assert.IsFalse(content.IsInTrainingArea()); Assert.IsTrue(content.IsNearCounter(content.Laboratory));
                At(new Vector2(settings.trainingArea.xMax + .001f, settings.trainingArea.center.y));
                Assert.IsFalse(content.IsInTrainingArea());
                ship.transform.position += Vector3.right * 5; ship.transform.localScale *= 1.3f;
                At(settings.trainingArea.center); Assert.IsTrue(content.IsInTrainingArea());
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        [UnityTest] public IEnumerator ReservationsCapFallbackCutoffAndWaveRestart()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            yield return new EnterPlayMode();
            yield return null;
            Assert.IsTrue(Application.isPlaying);
            yield return ExerciseReservations();
            yield return new ExitPlayMode();
        }
        static IEnumerator ExerciseReservations()
        {
            Time.timeScale = 1;
            var go = new GameObject("Spawn budget test");
            var spawner = go.AddComponent<EnemySpawner>();
            var mix = EnemyWaveMixSettings.CreateDefaultInstance();
            mix.ForWave(1).aliveCap = 1; mix.ForWave(1).slowAfterCount = 1;
            typeof(EnemySpawner).GetField("_mix", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(spawner, mix);
            spawner.BeginWave(1, 60);
            int born = 0;
            Assert.IsTrue(spawner.TryScheduleSpawn(Vector2.zero, () => { born++; return true; }));
            Assert.AreEqual(1, spawner.PendingSpawnCount);
            Assert.IsFalse(spawner.TryScheduleSpawn(Vector2.zero, () => { born++; return true; }));
            float until = Time.time + 1.3f;
            while (Time.time < until) yield return null;
            Assert.AreEqual(1, born); Assert.AreEqual(0, spawner.PendingSpawnCount);
            Assert.AreEqual(2, spawner.CurrentIntervalMultiplier);
            Assert.IsTrue(spawner.CanScheduleSpawn, "Soft budget still allows fallback spawns");
            spawner.TryScheduleSpawn(Vector2.zero, () => { born++; return true; });
            spawner.EndWave(); spawner.BeginWave(1, 60);
            until = Time.time + 1.3f;
            while (Time.time < until) yield return null;
            Assert.AreEqual(1, born, "Old wave callbacks must not enter a new wave");
            Assert.AreEqual(0, spawner.PendingSpawnCount);
            var enemy = Object.Instantiate(EnemyPrefabLibrary.Get(EnemyArchetype.Normal));
            Assert.IsFalse(spawner.CanScheduleSpawn, "Live enemies share the pending-spawn cap");
            spawner.Tick(10, 50);
            Object.Destroy(enemy); yield return null;
            spawner.Tick(.1f, 50);
            Assert.AreEqual(0, spawner.PendingSpawnCount, "Time blocked at cap must not cause a catch-up burst");
            spawner.Tick(50, 50);
            Assert.IsFalse(spawner.TryScheduleSpawn(Vector2.zero, () => true));
            Object.Destroy(go); Object.Destroy(mix);
            yield return null;
        }
    }
}
