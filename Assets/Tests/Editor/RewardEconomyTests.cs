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
    [Test] public void FixedBagRejectsOverflowAndAllowsAtomicReplacementAndSale()
    {
        var run = Run(); run.ResolveKill("fund", 1000, true);
        while (!run.Inventory.IsBackpackFull) run.Inventory.Spawn("drop");
        Assert.AreEqual(14, run.Inventory.UnequippedCapacity());
        int instances = run.Inventory.Instances.Count;
        Assert.Throws<InventoryException>(() => run.Inventory.Spawn("drop"));
        Assert.AreEqual(instances, run.Inventory.Instances.Count);
        run.CombatTimerFinished();
        string equipped = run.Inventory.EquippedAt("hand", 1);
        Assert.Throws<InventoryException>(() => run.Unequip("hand", 1));
        Assert.AreEqual(equipped, run.Inventory.EquippedAt("hand", 1));
        var offer = run.GetShopOffers()[0]; int balance = run.Marrow;
        Assert.AreEqual("capacity", run.TryPurchaseShopOffer(offer).Reason);
        Assert.IsFalse(offer.Purchased); Assert.AreEqual(balance, run.Marrow);
        string replacement = run.Inventory.UnequippedIds()[0];
        run.Replace(replacement, "hand", 1);
        Assert.AreEqual(replacement, run.Inventory.EquippedAt("hand", 1));
        Assert.Contains(equipped, run.Inventory.UnequippedIds());
        Assert.AreEqual(14, run.Inventory.UnequippedIds().Count);
        run.MoveOrSwap(replacement, "hand", 1, "head", 1);
        Assert.AreEqual(replacement, run.Inventory.EquippedAt("head", 1));
        Assert.Throws<InventoryException>(() => run.Inventory.SpawnEquipped("drop", "head", 1));
        run.Inventory.SpawnEquipped("drop", "hand", 1);
        Assert.AreEqual(14, run.Inventory.UnequippedIds().Count, "Direct equipped spawn does not use a bag slot");
        run.Sell(equipped);
        Assert.IsTrue(run.TryPurchaseShopOffer(offer).Ok);
        Assert.AreEqual(14, run.Inventory.UnequippedIds().Count);
        Assert.IsTrue(run.CanStartNextWave()); run.StartNextWave(); Assert.AreEqual(2, run.Wave);
    }
    [Test] public void FullBagRewardsAutoSellAtExactSalePriceWithoutGainMultiplier()
    {
        var run = Run(() => 0, 1);
        run.Inventory.SpawnEquipped("bonus", "torso", 1);
        while (!run.Inventory.IsBackpackFull) run.Inventory.Spawn("drop");
        int signals = 0; run.LoadoutChanged += _ => signals++;
        var reward = run.ResolveKill("full", 30, true);
        Assert.IsNull(reward.OrganInstanceId, "No organ flight for an automatic sale");
        Assert.AreEqual(43, reward.Marrow, "33 kill marrow plus exactly 10 sale marrow");
        Assert.AreEqual(43, run.Marrow); Assert.AreEqual(43, run.WaveKillMarrow);
        Assert.IsFalse(run.ResolveKill("full", 30, true).Accepted);
        LabOpenedPayload settlement = null; run.RestOpened += value => settlement = value;
        run.CombatTimerFinished();
        Assert.AreEqual(108, run.Marrow, "55 wave marrow plus exactly 10 sale marrow");
        Assert.AreEqual(65, settlement.WaveEndMarrow); Assert.IsEmpty(settlement.Drops);
        Assert.AreEqual(14, run.Inventory.UnequippedIds().Count); Assert.AreEqual(0, signals);
        Assert.IsTrue(run.CanStartNextWave());
    }
    [Test] public void FourteenthRewardFitsAndNextRewardSellsEvenWithZeroKillMarrow()
    {
        var run = Run(() => 0, 1);
        for (int i = 0; i < 13; i++) run.Inventory.Spawn("drop");
        var last = run.ResolveKill("last-slot", 0, true);
        Assert.NotNull(last.OrganInstanceId); Assert.IsFalse(last.HasMarrow);
        var overflow = run.ResolveKill("overflow", 0, true);
        Assert.IsNull(overflow.OrganInstanceId); Assert.IsTrue(overflow.HasMarrow);
        Assert.AreEqual(10, overflow.Marrow); Assert.AreEqual(10, run.Marrow);
        Assert.AreEqual(14, run.Inventory.UnequippedIds().Count);
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
