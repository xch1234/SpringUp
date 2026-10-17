using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tideflesh.Systems;

public class RewardEconomyTests
{
    static RunController Run(Func<double> random = null, double chance = .005, string[] pool = null, IEnumerable<string> active = null)
    {
        var catalog = new Dictionary<string, OrganDefinition>
        {
            ["fist"] = new OrganDefinition { id = "fist", type = "executor", shop_price = 10 },
            ["drop"] = new OrganDefinition { id = "drop", type = "executor", shop_price = 20 },
            ["inactive"] = new OrganDefinition { id = "inactive", type = "executor" },
            ["bonus"] = new OrganDefinition { id = "bonus", type = "torso_special", modifiers = new OrganModifiers { marrow_gain_mult = 1.1f } }
        };
        var waves = new List<WaveDefinition>();
        for (int i = 1; i <= 7; i++) waves.Add(new WaveDefinition {
            wave = i, duration_sec = 60, marrow_reward = i <= 6 ? 40 + i * 10 : 999,
            kill_organ_chance = chance, random_drop_count = 99,
            random_drop_pool = i <= 6 ? pool ?? new[] { "drop" } : Array.Empty<string>(),
            guaranteed_drops = new[] { "fist", "fist" }, optional_drops = new[] { "fist" }
        });
        var run = new RunController(catalog, waves, null, random ?? (() => .5), active);
        run.StartRun(); return run;
    }
    [Test] public void WaveRewardsAreAdditionalExactlyOneAndSeventhUnchanged()
    {
        var run = Run(); int expected = 0;
        for (int wave = 1; wave <= 7; wave++)
        {
            run.ResolveKill("wave" + wave, 30, true); expected += 30;
            run.CombatTimerFinished();
            if (wave <= 6) expected += 40 + wave * 10;
            Assert.AreEqual(expected, run.Marrow);
            Assert.AreEqual(Math.Min(wave, 6), run.Inventory.UnequippedIds().Count);
            Assert.Throws<RunException>(() => run.CombatTimerFinished());
            Assert.AreEqual(expected, run.Marrow);
            if (wave < 7) run.StartNextWave();
        }
        Assert.AreEqual(RunPhase.Won, run.Stage);
    }
    [Test] public void MultiplierFractionsAreSharedWithWaveBonus()
    {
        var run = Run(); run.Inventory.SpawnEquipped("bonus", "torso", 1);
        for (int i = 0; i < 10; i++) run.ResolveKill(i.ToString(), 1, true);
        Assert.AreEqual(11, run.Marrow);
        LabOpenedPayload payload = null; run.RestOpened += value => payload = value;
        run.CombatTimerFinished();
        Assert.AreEqual(66, run.Marrow); Assert.AreEqual(55, payload.WaveEndMarrow);
        Assert.AreEqual(11, payload.MarrowDelta);
        var fresh = Run(); fresh.ResolveKill("0", 1, true); Assert.AreEqual(1, fresh.Marrow);
    }
    [Test] public void IndependentRollsBoundaryDuplicatesAndInventoryInstances()
    {
        var rolls = new Queue<double>(new[] { .00499, .1, .005, 0, .9 });
        int consumed = 0;
        var run = Run(() => { consumed++; return rolls.Dequeue(); });
        run.Inventory.SpawnEquipped("bonus", "torso", 1); // Must not change the chance boundary.
        int loadoutSignals = 0; run.LoadoutChanged += _ => loadoutSignals++;
        var first = run.ResolveKill("a", 30, true);
        Assert.NotNull(first.OrganInstanceId);
        Assert.IsFalse(run.ResolveKill("a", 30, true).Accepted);
        Assert.IsFalse(run.ResolveKill("dummy", 30, false).Accepted);
        Assert.AreEqual(2, consumed);
        Assert.IsNull(run.ResolveKill("b", 30, true).OrganInstanceId, "Exactly 0.5% boundary fails");
        var second = run.ResolveKill("c", 30, true);
        Assert.NotNull(second.OrganInstanceId); Assert.AreNotEqual(first.OrganInstanceId, second.OrganInstanceId);
        Assert.AreEqual("drop", run.Inventory.OrganOf(first.OrganInstanceId).id);
        Assert.AreEqual("drop", run.Inventory.OrganOf(second.OrganInstanceId).id);
        Assert.AreEqual(0, loadoutSignals, "Reward must not reset attack timing or equip organs");
        Assert.AreEqual("fist", run.Inventory.OrganOf(run.Inventory.EquippedAt("hand", 1)).id);
        Assert.AreEqual(99, run.Marrow); Assert.AreEqual(0, rolls.Count);
    }
    [Test] public void EmptyOrInvalidPoolsSkipSafelyAndZeroMarrowCanStillDropOrgan()
    {
        var run = Run(() => 0, 1, new[] { "missing", "inactive", "drop", "drop" }, new[] { "fist", "drop" });
        var reward = run.ResolveKill("summoned-normal", 0, true);
        Assert.IsTrue(reward.Accepted); Assert.IsFalse(reward.HasMarrow);
        Assert.AreEqual("drop", run.Inventory.OrganOf(reward.OrganInstanceId).id);
        var empty = Run(() => 0, 1, Array.Empty<string>());
        Assert.IsNull(empty.ResolveKill("kill", 30, true).OrganInstanceId);
        empty.CombatTimerFinished(); Assert.AreEqual(80, empty.Marrow); Assert.AreEqual(0, empty.Inventory.UnequippedIds().Count);
        var invalid = Run(() => 0, 1, new[] { "missing", "inactive", null }, new[] { "fist" });
        Assert.IsNull(invalid.ResolveKill("kill", 30, true).OrganInstanceId);
        invalid.CombatTimerFinished(); Assert.AreEqual(0, invalid.Inventory.UnequippedIds().Count);
    }
    [Test] public void UnlimitedBagKeepsSlotAndPhaseRulesAndTransactions()
    {
        var run = Run(); run.ResolveKill("fund", 1000, true);
        for (int i = 0; i < 500; i++) run.Inventory.Spawn("drop");
        Assert.IsNull(run.Inventory.UnequippedCapacity()); Assert.IsTrue(run.Inventory.UnequippedWithinCap());
        Assert.Throws<RunException>(() => run.Unequip("hand", 1));
        Assert.IsFalse(run.TryPurchase("drop", 20).Ok);
        run.CombatTimerFinished(); run.Unequip("hand", 1);
        Assert.IsTrue(run.TryPurchase("drop", 20).Ok);
        int before = run.Marrow; var id = run.Inventory.UnequippedIds()[0]; run.Sell(id);
        Assert.AreEqual(before + 10, run.Marrow);
        Assert.Throws<InventoryException>(() => run.Inventory.Equip(run.Inventory.UnequippedIds()[0], "hand", 7));
        Assert.Throws<InventoryException>(() => run.Inventory.Equip(run.Inventory.UnequippedIds()[0], "torso", 1));
        Assert.IsTrue(run.CanStartNextWave()); run.StartNextWave(); Assert.AreEqual(2, run.Wave);
    }
    [Test] public void ExcludedAndEndedKillsDoNotConsumeRandomAndSeventhPoolIsEmpty()
    {
        int rolls = 0; var run = Run(() => { rolls++; return 0; }, 1);
        run.ResolveKill("dummy", 30, false); Assert.AreEqual(0, rolls);
        for (int i = 1; i <= 6; i++) { run.CombatTimerFinished(); run.StartNextWave(); }
        int count = run.Inventory.Instances.Count;
        Assert.IsNull(run.ResolveKill("seventh", 30, true).OrganInstanceId);
        Assert.AreEqual(count, run.Inventory.Instances.Count);
        int money = run.Marrow; run.LoseRun(); int before = rolls;
        Assert.IsFalse(run.ResolveKill("after-exit", 30, true).Accepted);
        Assert.AreEqual(before, rolls); Assert.AreEqual(money, run.Marrow);
    }
}
