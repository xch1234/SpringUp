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
        Assert.AreEqual(11, catalog.Count);
        Assert.AreEqual(5, catalog["fist"].damage);
        Assert.AreEqual(8, catalog["steel_pipe"].damage);
        Assert.AreEqual(4, catalog["rusty_knife"].damage);
        Assert.AreEqual(3, catalog["slime_gland"].damage);
        Assert.AreEqual(0, catalog["collapse_body"].damage);
        Assert.AreEqual(12, catalog["tnt"].damage);
        Assert.AreEqual(3f, catalog["tnt"].radius);
        Assert.AreEqual("kill", catalog["many_limb_tentacle"].trigger_on);
        Assert.AreEqual("NONE", catalog["spider_nest"].trigger_target);
        Assert.AreEqual("torso_special", catalog["collection_net"].type);
        Assert.AreEqual(1.1f, catalog["collection_net"].MarrowGainMult, 0.001f);
        Assert.IsFalse(catalog.ContainsKey("slime_core"));
    }

    [Test]
    public void LoadWaves_HasSevenEntries()
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(OrgansPath));
        var waves = Catalog.LoadWaves(File.ReadAllText(WavesPath), catalog);
        Assert.AreEqual(7, waves.Count);
        Assert.AreEqual(45, waves[0].marrow_reward);
        Assert.AreEqual("many_limb_tentacle", waves[0].guaranteed_drops[0]);
        Assert.AreEqual("rusty_knife", waves[0].guaranteed_drops[1]);
        Assert.AreEqual(0, waves[0].optional_drops.Length);
        Assert.AreEqual(120, waves[6].marrow_reward);
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
        var b = inv.Spawn("rusty_knife");
        Assert.AreEqual("inst_1", a);
        Assert.AreEqual("inst_2", b);
        inv.Equip(a, "hand", 1);
        Assert.AreEqual(a, inv.EquippedAt("hand", 1));
        Assert.AreEqual(1, inv.UnequippedIds().Count);
    }

    [Test]
    public void EquipWrongPart_Fails()
    {
        var inv = NewInventory();
        var iid = inv.Spawn("fist");
        Assert.Throws<InventoryException>(() => inv.Equip(iid, "head", 1));
    }

    [Test]
    public void SellRustyKnife_GivesFour()
    {
        var inv = NewInventory();
        var iid = inv.Spawn("rusty_knife");
        Assert.AreEqual(4, inv.Sell(iid));
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
    public void Wave1Timer_OpensLabWithBothDrops()
    {
        var rec = new Recorder();
        var run = NewRun(rec);
        run.StartRun();
        run.CombatTimerFinished();
        Assert.AreEqual("lab", run.Phase);
        Assert.AreEqual(45, run.Marrow);
        CollectionAssert.AreEquivalent(
            new[] { "many_limb_tentacle", "rusty_knife" },
            BagOrganIds(run));
        Assert.AreEqual("WaveEnded", rec.Events[rec.Events.Count - 2].Name);
        Assert.AreEqual("LabOpened", rec.Events[rec.Events.Count - 1].Name);
        var lab = (LabOpenedPayload)rec.Events[rec.Events.Count - 1].Payload;
        Assert.AreEqual(45, lab.MarrowDelta);
        Assert.AreEqual(2, lab.Drops.Count);
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
        Assert.AreEqual(100, run.Marrow);
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
        var buy = run.TryBuy("rusty_knife");
        Assert.IsFalse(buy.Ok);
        Assert.AreEqual("not_open", buy.Reason);
    }

    static Inventory NewInventory()
    {
        return new Inventory(Catalog.LoadOrgans(File.ReadAllText(OrgansPath)));
    }

    static RunController NewRun(Recorder rec)
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(OrgansPath));
        var waves = Catalog.LoadWaves(File.ReadAllText(WavesPath), catalog);
        return new RunController(catalog, waves, rec.Emit);
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
