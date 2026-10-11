using System.Collections.Generic;
using NUnit.Framework;
using SpringUp.Organs;
using Tideflesh.Systems;

public class BattleRunLinkTests
{
    [Test]
    public void WaveTimer_OpensLab_AndNextWaveAdvances()
    {
        var link = NewLink(waves: 2);
        link.Begin();
        link.NotifyWaveTimerFinished(1);

        Assert.AreEqual("lab", link.Run.Phase);
        Assert.AreEqual(10, link.Run.Marrow);
        Assert.IsTrue(link.TryStartNextWave(out var next));
        Assert.AreEqual(2, next);
        Assert.AreEqual("combat", link.Run.Phase);
        Assert.AreEqual(2, link.Run.Wave);
    }

    [Test]
    public void OverCapacity_KeepsLabWave()
    {
        var link = NewLink(waves: 2);
        link.Begin();
        link.NotifyWaveTimerFinished(1);
        for (var i = 0; i < 5; i++)
            link.Run.Inventory.Spawn("fist");

        Assert.IsFalse(link.TryStartNextWave(out var next));
        Assert.AreEqual(0, next);
        Assert.AreEqual("lab", link.Run.Phase);
        Assert.AreEqual(1, link.Run.Wave);
    }

    [Test]
    public void MismatchedWave_Throws()
    {
        var link = NewLink(waves: 2);
        link.Begin();
        var ex = Assert.Throws<RunException>(() => link.NotifyWaveTimerFinished(2));
        StringAssert.Contains("wave mismatch", ex.Message);
        Assert.AreEqual("combat", link.Run.Phase);
    }

    [Test]
    public void LastWave_WinsWithoutAnotherLab()
    {
        var link = NewLink(waves: 7);
        link.Begin();
        for (var wave = 1; wave <= 7; wave++)
        {
            link.NotifyWaveTimerFinished(wave);
            if (wave < 7)
            {
                Assert.AreEqual("lab", link.Run.Phase);
                Assert.IsTrue(link.TryStartNextWave(out _));
            }
        }

        Assert.AreEqual("run_won", link.Run.Phase);
        Assert.IsFalse(link.TryStartNextWave(out _));
    }

    [Test]
    public void Apply_WritesHandSlotsFromInventory()
    {
        var inventory = NewInventory();
        inventory.SpawnEquipped("fist", "hand", 1);
        inventory.SpawnEquipped("rust_knife", "hand", 2);
        var hand = new BodyRuntime(BodyPart.Hand, 1f);
        var fist = Pipe("fist");
        hand.SetSlot(3, new SpringUp.Organs.OrganInstance(fist));

        var skipped = HandPipelineLoadout.Apply(hand, inventory, new Dictionary<string, SpringUp.Organs.OrganDefinition>
        {
            ["fist"] = fist,
            ["rust_knife"] = Pipe("rust_knife")
        });

        Assert.AreEqual(0, skipped);
        Assert.AreEqual("fist", hand.GetSlot(0).Definition.Id);
        Assert.AreEqual("rust_knife", hand.GetSlot(1).Definition.Id);
        Assert.IsNull(hand.GetSlot(2));
        Assert.IsNull(hand.GetSlot(3));
    }

    [Test]
    public void Apply_SkipsOrganMissingFromPipeline()
    {
        var inventory = NewInventory();
        inventory.SpawnEquipped("spider_nest", "hand", 1);
        var hand = new BodyRuntime(BodyPart.Hand, 1f);

        var skipped = HandPipelineLoadout.Apply(
            hand,
            inventory,
            new Dictionary<string, SpringUp.Organs.OrganDefinition>
            {
                ["fist"] = Pipe("fist")
            });

        Assert.AreEqual(1, skipped);
        Assert.IsNull(hand.GetSlot(0));
    }

    static void IgnoreSignal(string name, object payload) { }

    static BattleRunLink NewLink(int waves)
    {
        var list = new List<WaveDefinition>();
        for (var i = 1; i <= waves; i++)
        {
            list.Add(new WaveDefinition
            {
                wave = i,
                duration_sec = 60,
                guaranteed_drops = i == 1 ? new[] { "rust_knife" } : new string[0],
                optional_drops = new string[0],
                marrow_reward = 10
            });
        }

        return new BattleRunLink(new RunController(CatalogOf(), list, IgnoreSignal));
    }

    static Inventory NewInventory() => new Inventory(CatalogOf());

    static Dictionary<string, Tideflesh.Systems.OrganDefinition> CatalogOf()
    {
        return new Dictionary<string, Tideflesh.Systems.OrganDefinition>
        {
            ["fist"] = new Tideflesh.Systems.OrganDefinition { id = "fist", part = "hand", type = "executor" },
            ["rust_knife"] = new Tideflesh.Systems.OrganDefinition { id = "rust_knife", part = "hand", type = "executor", shop_price = 8 },
            ["spider_nest"] = new Tideflesh.Systems.OrganDefinition { id = "spider_nest", part = "hand", type = "trigger" }
        };
    }

    static SpringUp.Organs.OrganDefinition Pipe(string id) =>
        new SpringUp.Organs.OrganDefinition(id, id, BodyPart.Hand, OrganType.Actuator, 1f);
}
