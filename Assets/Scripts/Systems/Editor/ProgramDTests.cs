using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Tideflesh.Systems;
using UnityEngine;

public class ProgramDTests
{
    static string OrgansPath => Path.Combine(Application.dataPath, "Data", "Organs", "organs.json");
    static string WavesPath => Path.Combine(Application.dataPath, "Data", "Waves", "waves.json");

    [Test]
    public void LoadOrgans_IndexesMvpIds()
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(OrgansPath));
        Assert.AreEqual(40, catalog.Count);
        Assert.AreEqual(5, catalog["fist"].damage);
        Assert.AreEqual(8, catalog["steel_pipe"].damage);
        Assert.AreEqual(4, catalog["rust_knife"].damage);
        Assert.AreEqual(3, catalog["slime_gland"].damage);
        Assert.AreEqual(0, catalog["collapse_body"].damage);
        Assert.AreEqual(12, catalog["tnt"].damage);
        Assert.AreEqual(3f, catalog["tnt"].radius);
        Assert.AreEqual("kill", catalog["multi_tentacle"].trigger_on);
        Assert.AreEqual("NONE", catalog["spider_nest"].trigger_target);
        Assert.AreEqual("torso_special", catalog["collection_net"].type);
        Assert.AreEqual(1.1f, catalog["collection_net"].MarrowGainMult, 0.001f);
        Assert.IsFalse(catalog.ContainsKey("slime_core"));
        Assert.IsFalse(catalog.ContainsKey("rusty_knife"));
        Assert.IsFalse(catalog.ContainsKey("many_limb_tentacle"));
    }

    [Test]
    public void LoadWaves_HasSevenEntries()
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(OrgansPath));
        var waves = Catalog.LoadWaves(File.ReadAllText(WavesPath), catalog);
        Assert.AreEqual(7, waves.Count);
        Assert.AreEqual(450, waves[0].marrow_reward);
        Assert.AreEqual("multi_tentacle", waves[0].guaranteed_drops[0]);
        Assert.AreEqual("rust_knife", waves[0].guaranteed_drops[1]);
        Assert.AreEqual(0, waves[0].optional_drops.Length);
        Assert.AreEqual(35, waves[0].random_drop_pool.Length);
        Assert.AreEqual(2, waves[0].random_drop_count);
        Assert.AreEqual(1200, waves[6].marrow_reward);
    }

    [Test]
    public void MissingOrganId_Throws()
    {
        const string json = "{\"version\":1,\"organs\":[{\"name\":\"bad\",\"part\":\"hand\",\"type\":\"executor\",\"rarity\":\"common\",\"shop_price\":0,\"icon\":\"x.png\",\"damage\":1,\"count\":1,\"radius\":0}]}";
        var ex = Assert.Throws<CatalogException>(() => Catalog.LoadOrgans(json));
        Assert.AreEqual("<unknown>", ex.OrganId);
        StringAssert.Contains("id", ex.Reason);
    }

    [Test]
    public void TriggerMissingTriggerOn_Throws()
    {
        const string json = "{\"version\":1,\"organs\":[{\"id\":\"bad_trigger\",\"name\":\"x\",\"description\":\"x\",\"part\":\"hand\",\"type\":\"trigger\",\"rarity\":\"rare\",\"shop_price\":1,\"icon\":\"x.png\",\"trigger_target\":\"NONE\",\"chance\":100}]}";
        var ex = Assert.Throws<CatalogException>(() => Catalog.LoadOrgans(json));
        Assert.AreEqual("bad_trigger", ex.OrganId);
        StringAssert.Contains("trigger_on", ex.Reason);
    }

    [Test]
    public void SpawnIdsIncrement_AndEquipMatchingPart()
    {
        var inv = NewInventory();
        var a = inv.Spawn("fist");
        var b = inv.Spawn("rust_knife");
        Assert.AreEqual("inst_1", a);
        Assert.AreEqual("inst_2", b);
        inv.Equip(a, "hand", 1);
        Assert.AreEqual(a, inv.EquippedAt("hand", 1));
        Assert.AreEqual(1, inv.UnequippedIds().Count);
    }

    [Test]
    public void EquipOrdinaryOrganInTorso_Fails()
    {
        var inv = NewInventory();
        var iid = inv.Spawn("fist");
        Assert.Throws<InventoryException>(() => inv.Equip(iid, "torso", 1));
    }

    [Test]
    public void SellRustKnife_GivesFour()
    {
        var inv = NewInventory();
        var iid = inv.Spawn("rust_knife");
        Assert.AreEqual(40, inv.Sell(iid));
    }

    [Test]
    public void StartRun_EmitsLoadoutThenRunStarted()
    {
        var rec = new Recorder();
        var run = NewRun(rec);
        run.StartRun();
        Assert.AreEqual("combat", run.Phase);
        Assert.AreEqual(1, run.Wave);
        Assert.AreEqual("inst_1", run.Inventory.EquippedAt("hand", 1));
        Assert.AreEqual("LoadoutChanged", rec.Names[0]);
        Assert.AreEqual("RunStarted", rec.Names[1]);
    }

    [Test]
    public void Wave1Timer_DrawsRandomDropsFromPool()
    {
        var low = NewRun(new Recorder(), () => 0.0);
        low.StartRun(); low.CombatTimerFinished();
        var high = NewRun(new Recorder(), () => 0.999999);
        high.StartRun(); high.CombatTimerFinished();
        Assert.AreEqual("lab", low.Phase);
        Assert.AreEqual(427, low.Marrow); // Deterministic minimum: 450 with -5% reward swing.
        CollectionAssert.AreEquivalent(new[] { "straight_projectile", "steel_pipe" }, BagOrganIds(low));
        CollectionAssert.AreEquivalent(new[] { "torso_shortguard", "torso_longguard" }, BagOrganIds(high));
        Assert.AreNotEqual(string.Join(",", BagOrganIds(low)), string.Join(",", BagOrganIds(high)));
    }

    [Test]
    public void Wave2Timer_OpensLabAgain()
    {
        var rec = new Recorder();
        var run = NewRun(rec);
        run.StartRun();
        run.CombatTimerFinished();
        run.StartNextWave();
        run.CombatTimerFinished();
        Assert.AreEqual("lab", run.Phase);
        Assert.AreEqual(1000, run.Marrow);
        var labCount = 0;
        foreach (var n in rec.Names)
            if (n == "LabOpened") labCount++;
        Assert.AreEqual(2, labCount);
    }

    [Test]
    public void Wave7Timer_WinsWithoutLab()
    {
        var rec = new Recorder();
        var run = NewRun(rec);
        run.StartRun();
        for (var w = 1; w <= 7; w++)
        {
            run.CombatTimerFinished();
            if (w < 7)
            {
                while (!run.CanStartNextWave())
                {
                    var bag = run.Inventory.UnequippedIds();
                    Assert.Greater(bag.Count, 0);
                    run.Sell(bag[0]);
                }
                run.StartNextWave();
            }
        }
        Assert.AreEqual("run_won", run.Phase);
        Assert.AreEqual("RunEnded", rec.Names[rec.Names.Count - 1]);
        var labCount = 0;
        foreach (var n in rec.Names)
            if (n == "LabOpened") labCount++;
        Assert.AreEqual(6, labCount);
    }

    [Test]
    public void OverCapacity_BlocksNextWave()
    {
        var run = NewRun(new Recorder());
        run.StartRun();
        run.CombatTimerFinished();
        for (var i = 0; i < 4; i++)
            run.Inventory.Spawn("fist");
        Assert.IsFalse(run.CanStartNextWave());
        Assert.Throws<RunException>(() => run.StartNextWave());
    }

    [Test]
    public void ShopStubs_NotOpen()
    {
        var run = NewRun(new Recorder());
        run.StartRun();
        var buy = run.TryBuy("rust_knife");
        Assert.IsFalse(buy.Ok);
        Assert.AreEqual("not_open", buy.Reason);
    }

    static Inventory NewInventory()
    {
        return new Inventory(Catalog.LoadOrgans(File.ReadAllText(OrgansPath)));
    }

    [Test]
    public void WaveReward_UsesFractionalPercent()
    {
        var run = NewRun(new Recorder(), () => 0.24);
        run.StartRun();
        run.CombatTimerFinished();
        double percent = EconomySwing.Percent(0.24, EconomySwing.MarrowHalfPercent);
        Assert.Greater(Math.Abs(percent - Math.Round(percent)), 0.2);
        Assert.AreEqual(438, run.Marrow);
    }

    [Test]
    public void PurchasePrice_UsesFractionalPercent()
    {
        var run = NewRun(new Recorder(), () => 0.24);
        double percent = EconomySwing.Percent(0.24, EconomySwing.PurchaseHalfPercent);
        Assert.Greater(Math.Abs(percent - Math.Round(percent)), 0.2);
        Assert.AreEqual(237, run.QuotePurchasePrice("collection_net"));
        Assert.AreEqual(238, EconomySwing.RoundAwayFromZero(250, 0.25, EconomySwing.PurchaseHalfPercent));
    }

    static RunController NewRun(Recorder rec, Func<double> unit = null)
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(OrgansPath));
        var waves = Catalog.LoadWaves(File.ReadAllText(WavesPath), catalog);
        return new RunController(catalog, waves, rec.Emit, unit ?? (() => 0.5));
    }

    static List<string> BagOrganIds(RunController run)
    {
        var ids = new List<string>();
        foreach (var iid in run.Inventory.UnequippedIds())
            ids.Add(run.Inventory.OrganOf(iid).id);
        return ids;
    }

    sealed class Recorder
    {
        public readonly List<(string Name, object Payload)> Events = new();
        public List<string> Names
        {
            get
            {
                var names = new List<string>();
                foreach (var e in Events) names.Add(e.Name);
                return names;
            }
        }

        public void Emit(string name, object payload) => Events.Add((name, payload));
    }
}
