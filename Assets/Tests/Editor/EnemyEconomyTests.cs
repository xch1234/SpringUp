using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class EnemyEconomyTests
{
    static RunController NewRun()
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(Path.Combine(Application.dataPath, "Data/Organs/organs.json")));
        return new RunController(catalog, Catalog.LoadWaves(File.ReadAllText(Path.Combine(Application.dataPath, "Data/Waves/waves.json")), catalog), null, () => 0);
    }
    [Test] public void PrefabsUseMigratedAssetsAndWaveSevenKeepsDeadline()
    {
        float[] health = { 20, 14, 20, 24, 40, 16, 99999 };
        int[] rewards = { 30, 30, 40, 50, 80, 60, 0 };
        foreach (EnemyArchetype type in Enum.GetValues(typeof(EnemyArchetype)))
        {
            var def = EnemyCatalog.Load().Get(type);
            var prefab = EnemyPrefabLibrary.Get(type);
            Assert.AreSame(def, prefab.GetComponent<EnemyData>().Definition);
            Assert.AreEqual(health[(int)type], def.maxHealth);
            Assert.AreEqual(rewards[(int)type], def.baseMarrow);
            Assert.AreEqual(type != EnemyArchetype.Dummy, def.allowRegularSpawn);
        }
        CollectionAssert.DoesNotContain(EnemyCatalog.Load().Spawnable(), EnemyArchetype.Dummy);
        Assert.AreEqual(50, WaveSpawnSettings.Load().SpawnDuration(6, 60));
        Assert.AreEqual(60, WaveSpawnSettings.Load().SpawnDuration(7, 60));
    }
    [Test] public void FractionalKillsAreImmediateDeduplicatedAndDoNotPayAgainAtWaveEnd()
    {
        var run = NewRun(); run.StartRun();
        run.Inventory.SpawnEquipped("collection_net", "torso", 1);
        for (int i = 0; i < 10; i++) run.CreditKill("life" + i, 1);
        Assert.AreEqual(11, run.Marrow);
        Assert.AreEqual(0, run.CreditKill("life0", 500));
        Assert.AreEqual(0, run.CreditKill("dummy", 0));
        run.CombatTimerFinished();
        Assert.AreEqual(11, run.Marrow);
        Assert.Greater(run.Inventory.UnequippedIds().Count, 0);
        Assert.AreEqual(0, run.CreditKill("outside-combat", 500));
        var id = run.Inventory.UnequippedIds()[0];
        int price = run.Inventory.SalePrice(id); run.Sell(id);
        Assert.AreEqual(11 + price, run.Marrow);
        var fresh = NewRun(); fresh.StartRun(); fresh.CreditKill("life0", 1);
        Assert.AreEqual(1, fresh.Marrow);
    }
    [UnityTest] public IEnumerator RealDeathPoolReuseClearAndVisualExpiry()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        var run = NewRun(); run.StartRun();
        using (var binding = new KillMarrowBinding(run))
        {
            var prefab = EnemyPrefabLibrary.Get(EnemyArchetype.Normal);
            var go = SimplePool.Rent(prefab, "economy-test");
            var enemy = go.GetComponent<EnemyRuntime>(); enemy.MarkPooled("economy-test");
            string firstLife = enemy.TargetId;
            Action<EnemyEvents.DamagedInfo> reenter = info => {
                Assert.AreEqual(30, run.Marrow, "Reward precedes gameplay callback reentry");
                enemy.TakeDamage(999, "repeat", Vector2.zero);
            };
            EnemyEvents.Damaged += reenter;
            enemy.TakeDamage(999, "test", Vector2.zero);
            EnemyEvents.Damaged -= reenter;
            Assert.AreEqual(30, run.Marrow, "Paid synchronously before visual expiry");
            enemy.TakeDamage(999, "inactive", Vector2.zero);
            Assert.AreEqual(30, run.Marrow);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<MarrowDeathVisual>(FindObjectsSortMode.None).Length);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<MarrowDeathVisual>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(30, run.Marrow);
            go = SimplePool.Rent(prefab, "economy-test"); enemy = go.GetComponent<EnemyRuntime>();
            Assert.AreNotEqual(firstLife, enemy.TargetId);
            Assert.AreEqual(enemy.MaxHealth, enemy.CurrentHealth);
            enemy.TakeDamage(999, "second", Vector2.zero);
            Assert.AreEqual(60, run.Marrow);
            go = SimplePool.Rent(prefab, "economy-test"); enemy = go.GetComponent<EnemyRuntime>();
            enemy.RemoveFromBattlefield();
            Assert.AreEqual(60, run.Marrow);
            var dummy = SimplePool.Rent(EnemyPrefabLibrary.Get(EnemyArchetype.Dummy), "dummy-test").GetComponent<EnemyRuntime>();
            dummy.MarkPooled("dummy-test"); dummy.TakeDamage(100000, "test", Vector2.zero);
            Assert.AreEqual(60, run.Marrow);
            EnemyRuntime.ClearBattlefield();
            yield return null;
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<MarrowDeathVisual>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(60, run.Marrow);
            SimplePool.Clear();
        }
        yield return new ExitPlayMode();
    }
}
