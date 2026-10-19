using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpringUp.Organs;
using SpringUp.Laboratory;
using Tideflesh.Systems;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Definition = SpringUp.Organs.OrganDefinition;
using Instance = SpringUp.Organs.OrganInstance;
using Part = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public sealed class OrganTriggerChecks
    {
        private sealed class Rig : IDisposable
        {
            public readonly GameObject Go = new GameObject("Trigger checks");
            public readonly OrganPipelineController Pipeline;
            public readonly List<CastEvent> Attacks = new List<CastEvent>();
            public BodyRuntime Body => Pipeline.GetBody(Part.Head);
            public Rig()
            {
                Pipeline = Go.AddComponent<OrganPipelineController>(); Pipeline.AutomaticExecution = false;
                Pipeline.TargetSelector = () => "target"; Pipeline.TargetValidator = _ => true;
                Pipeline.TargetSnapshotProvider = id => new AttackTarget(id, Vector2.right, true);
                Pipeline.OriginProvider = () => Vector2.zero; Pipeline.TriggerRandom = () => 0;
                Pipeline.AttackProduced += attack => Attacks.Add(attack);
            }
            public Instance Executor(int slot)
            { var organ = new Instance(new Definition("fist", "fist", OrganType.Actuator, 5)); Body.SetSlot(slot, organ); return organ; }
            public Instance Trigger(TriggerOn kind, int slot = 1, float probability = 1)
            {
                var settings = new TriggerSettings(kind, probability);
                var organ = new Instance(new Definition("test_trigger", "trigger", OrganType.Trigger, triggerOn: kind,
                    triggerTarget: TriggerTargetKind.NextSlot, trigger: settings));
                Body.SetSlot(slot, organ); return organ;
            }
            public CastEvent Cause(string source = null, CastContext chain = null)
                => new CastEvent("target", 5, "fist", source ?? Body.GetSlot(0)?.InstanceId ?? "external", chain ?? new CastContext(), effectCenter: Vector2.right);
            public void Signal(TriggerOn kind, CastEvent? cause = null) => Pipeline.PublishSignal(new TriggerSignal(kind, cause ?? Cause(), Vector2.right));
            public void Dispose() => Object.DestroyImmediate(Go);
        }
        [Test] public void AssetsAndAllEventConditions()
        {
            var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
            Assert.AreEqual(8, definitions.Count(d => d.Trigger != null));
            Assert.AreEqual(7, definitions.Where(d => d.Trigger != null).Select(d => d.TriggerOn).Distinct().Count());
            foreach (var kind in new[] { TriggerOn.Hit, TriggerOn.Hurt, TriggerOn.Heal, TriggerOn.Debuff, TriggerOn.Critical })
                using (var r = new Rig())
                {
                    r.Executor(0); r.Trigger(kind, 1); r.Executor(2);
                    r.Pipeline.GetBody(Part.Leg).SetSlot(0, new Instance(new Definition("other", "other", OrganType.Actuator, 10)));
                    r.Signal(kind); Assert.AreEqual(1, r.Attacks.Count, kind.ToString()); Assert.AreEqual(1, r.Attacks[0].ChainDepth);
                    r.Signal(TriggerOn.Stationary); Assert.AreEqual(1, r.Attacks.Count);
                }
            Assert.Throws<ArgumentOutOfRangeException>(() => new TriggerSettings(TriggerOn.Hit, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TriggerSettings(TriggerOn.Kill, killThreshold: 0));
        }
        [Test] public void ProbabilityAndChainGuard()
        {
            using (var r = new Rig())
            {
                r.Executor(0); r.Trigger(TriggerOn.Hit, probability: .3f); r.Executor(2);
                r.Pipeline.TriggerRandom = () => .3f; r.Signal(TriggerOn.Hit); Assert.IsEmpty(r.Attacks);
                r.Pipeline.TriggerRandom = () => .299f;
                var cause = r.Cause(); r.Signal(TriggerOn.Hit, cause); r.Signal(TriggerOn.Hit, cause);
                Assert.AreEqual(1, r.Attacks.Count);
                r.Pipeline.AttackProduced += attack => r.Signal(TriggerOn.Hit, attack);
                r.Signal(TriggerOn.Hit); Assert.AreEqual(2, r.Attacks.Count, "same-chain reentry is bounded");
                r.Signal(TriggerOn.Hit, r.Cause(chain: new CastContext(CastContext.MaxChainDepth)));
                Assert.AreEqual(2, r.Attacks.Count);
            }
        }
        [Test] public void AllTriggerAssetsUseSingleSuccessorScope()
        {
            var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
            Assert.IsTrue(definitions.Where(d => d.Type == OrganType.Trigger).All(d => d.TriggerTarget == TriggerTargetKind.NextSlot));
            var tentacle = definitions.Single(d => d.Id == "multi_tentacle");
            Assert.AreEqual(1, tentacle.Trigger.KillThreshold);
            Assert.AreEqual(TriggerTargetKind.NextSlot, tentacle.TriggerTarget);
            var item = new LaboratoryItem("scope", tentacle);
            Assert.AreEqual(LaboratoryTriggerTarget.NextSlot, item.TriggerTarget);
            using (var r = new Rig())
            {
                r.Executor(0); r.Body.SetSlot(1, new Instance(tentacle)); r.Executor(2);
                var cause = r.Cause(); r.Signal(TriggerOn.Kill, cause);
                Assert.AreEqual(1, r.Attacks.Count, "Tentacle must execute only the adjacent successor.");
                r.Signal(TriggerOn.Kill, cause);
                Assert.AreEqual(1, r.Attacks.Count, "Same-chain guard still applies after migration.");
            }
            var count = definitions.Single(d => d.Trigger != null && d.TriggerOn == TriggerOn.Kill && d.Id != tentacle.Id);
            Assert.AreEqual(10, count.Trigger.KillThreshold);
            Assert.AreEqual(TriggerTargetKind.NextSlot, count.TriggerTarget);
        }
        [Test] public void ResetDuringActivationStopsRemainingWork()
        {
            using (var r = new Rig())
            {
                r.Executor(0); r.Trigger(TriggerOn.Hit); r.Executor(2); r.Trigger(TriggerOn.Ended, 3); r.Executor(4);
                r.Pipeline.AttackProduced += _ => r.Pipeline.ResetTriggers();
                r.Signal(TriggerOn.Hit); Assert.AreEqual(1, r.Attacks.Count);
            }
        }
        [Test] public void KillThresholdAndReset()
        {
            using (var r = new Rig())
            {
                r.Executor(0); r.Trigger(TriggerOn.Kill); r.Executor(2);
                for (int i = 0; i < 9; i++) r.Signal(TriggerOn.Kill);
                Assert.IsEmpty(r.Attacks); Assert.AreEqual(9, r.Pipeline.TriggerProgress(Part.Head, 1));
                r.Signal(TriggerOn.Kill); Assert.AreEqual(1, r.Attacks.Count);
                Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 1));
                r.Signal(TriggerOn.Kill); r.Pipeline.ResetTriggers();
                Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 1));
            }
        }
        [Test] public void DelayAdjacentPredecessorAndCancellation()
        {
            using (var r = new Rig())
            {
                var first = r.Executor(0); var previous = r.Executor(2); r.Trigger(TriggerOn.Started, 3); r.Executor(4);
                r.Signal(TriggerOn.Started, r.Cause(first.InstanceId)); Assert.AreEqual(0, r.Pipeline.PendingTriggerCount);
                r.Signal(TriggerOn.Started, r.Cause(previous.InstanceId));
                r.Pipeline.AdvanceTriggers(.49f); Assert.IsEmpty(r.Attacks);
                r.Pipeline.AdvanceTriggers(.01f); Assert.AreEqual(1, r.Attacks.Count);
                r.Signal(TriggerOn.Started, r.Cause(previous.InstanceId));
                var trigger = r.Body.GetSlot(3); r.Body.SetSlot(3, null); r.Body.SetSlot(3, trigger);
                r.Pipeline.AdvanceTriggers(1); Assert.AreEqual(1, r.Attacks.Count);
                r.Signal(TriggerOn.Started, r.Cause(previous.InstanceId)); r.Pipeline.ResetTriggers();
                r.Pipeline.AdvanceTriggers(1); Assert.AreEqual(1, r.Attacks.Count);
            }
        }
        [Test] public void EndOnlyNormalPredecessorCompletion()
        {
            using (var r = new Rig())
            {
                var previous = r.Executor(0); r.Trigger(TriggerOn.Ended, 1); r.Executor(2);
                r.Pipeline.NotifyExecutionEnded(r.Cause(previous.InstanceId), true); Assert.IsEmpty(r.Attacks);
                r.Pipeline.NotifyExecutionEnded(r.Cause("external")); Assert.IsEmpty(r.Attacks);
                r.Pipeline.NotifyExecutionEnded(r.Cause(previous.InstanceId)); Assert.AreEqual(1, r.Attacks.Count);
                r.Pipeline.TryExecuteSlot(Part.Head, 0); Assert.AreEqual(3, r.Attacks.Count, "instant executor starts/ends too");
            }
        }
        [Test] public void StationaryChargeIsIndependentAndMovementResets()
        {
            using (var r = new Rig())
            {
                r.Trigger(TriggerOn.Stationary); r.Executor(2);
                r.Pipeline.AdvanceTriggers(9); Assert.IsEmpty(r.Attacks);
                Assert.AreEqual(9, r.Pipeline.TriggerProgress(Part.Head, 1));
                r.Pipeline.MovingProvider = () => true; r.Pipeline.AdvanceTriggers(1);
                Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 1));
                r.Pipeline.MovingProvider = () => false; r.Pipeline.AdvanceTriggers(10);
                Assert.AreEqual(1, r.Attacks.Count); Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 1));
            }
        }
        [Test] public void AutomaticLoopSkipsTriggerAndBoundExecutorInBothTimingModes()
        {
            foreach (float period in new[] { 0f, 1f })
                using (var r = new Rig())
                {
                    r.Executor(0); r.Trigger(TriggerOn.Hit); r.Executor(2);
                    r.Executor(3); r.Executor(4); r.Executor(5);
                    var slots = new List<int>();
                    r.Pipeline.SlotExecuted += (_, slot, __) => slots.Add(slot);
                    r.Pipeline.AutomaticExecution = true;
                    r.Pipeline.StepInterval = .125f;
                    r.Pipeline.CycleDurationProvider = _ => period;
                    r.Pipeline.AdvanceAutomatic(period > 0 ? 1.5f : 1f);
                    CollectionAssert.AreEqual(new[] { 0, 3, 4, 5, 0, 3, 4, 5 }, slots);
                    Assert.AreEqual(8, r.Attacks.Count);
                    Assert.IsFalse(r.Pipeline.TryExecuteSlot(Part.Head, 2));
                    var source = r.Cause(); r.Signal(TriggerOn.Hit, source);
                    Assert.AreEqual(9, r.Attacks.Count);
                    Assert.AreEqual(r.Body.GetSlot(2).InstanceId, r.Attacks[8].OriginInstanceId);
                    Assert.AreEqual(source.ChainId, r.Attacks[8].ChainId);
                }
        }
        [Test] public void AttackConditionsIgnoreOtherSlotsBodiesAndSameDefinitionInstances()
        {
            foreach (var kind in new[] { TriggerOn.Hit, TriggerOn.Kill, TriggerOn.Debuff, TriggerOn.Critical, TriggerOn.Started, TriggerOn.Ended })
                using (var r = new Rig())
                {
                    r.Executor(0); r.Trigger(kind); r.Executor(2); var other = r.Executor(3);
                    var otherBody = new Instance(r.Body.GetSlot(0).Definition);
                    r.Pipeline.GetBody(Part.Leg).SetSlot(0, otherBody);
                    for (int i = 0; i < 10; i++)
                    {
                        r.Signal(kind, r.Cause(other.InstanceId));
                        r.Signal(kind, r.Cause(otherBody.InstanceId));
                        r.Signal(kind, r.Cause(r.Body.GetSlot(2).InstanceId));
                    }
                    r.Pipeline.AdvanceTriggers(1);
                    Assert.IsEmpty(r.Attacks, kind.ToString());
                    Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 1));
                    for (int i = 0; i < (kind == TriggerOn.Kill ? 10 : 1); i++) r.Signal(kind);
                    r.Pipeline.AdvanceTriggers(1);
                    Assert.AreEqual(1, r.Attacks.Count, kind.ToString());
                    Assert.AreEqual(r.Body.GetSlot(2).InstanceId, r.Attacks[0].OriginInstanceId);
                }
        }
        [Test] public void ChainedBindingsExecuteOnlyTheirOwnSuccessors()
        {
            using (var r = new Rig())
            {
                var first = r.Executor(0); r.Trigger(TriggerOn.Hit); var second = r.Executor(2);
                r.Trigger(TriggerOn.Ended, 3); var third = r.Executor(4); r.Executor(5);
                r.Signal(TriggerOn.Hit);
                CollectionAssert.AreEqual(new[] { second.InstanceId, third.InstanceId }, r.Attacks.Select(a => a.OriginInstanceId));
                CollectionAssert.AreEqual(new[] { 1, 2 }, r.Attacks.Select(a => a.ChainDepth));
                Assert.AreEqual(r.Attacks[0].ChainId, r.Attacks[1].ChainId);
                r.Attacks.Clear(); r.Body.Tick(2);
                CollectionAssert.AreEqual(new[] { first.InstanceId, r.Body.GetSlot(5).InstanceId }, r.Attacks.Select(a => a.OriginInstanceId));
            }
        }
        [Test] public void BindingsDoNotSkipEmptySlotsOrWrapAtBoundary()
        {
            using (var r = new Rig())
            {
                var first = r.Executor(0); r.Trigger(TriggerOn.Hit, 2); r.Executor(3);
                r.Signal(TriggerOn.Hit); Assert.IsEmpty(r.Attacks, "An empty predecessor breaks the source binding.");
                r.Body.SetSlot(2, null); r.Trigger(TriggerOn.Hit, 1); r.Executor(4);
                r.Signal(TriggerOn.Hit); Assert.IsEmpty(r.Attacks, "An empty successor must not forward to slot 4 or 5.");
                r.Body.SetSlot(1, null); r.Trigger(TriggerOn.Hit, 5);
                r.Signal(TriggerOn.Hit, r.Cause(r.Body.GetSlot(4).InstanceId));
                Assert.IsEmpty(r.Attacks, "Slot 6 does not bind slot 1.");
                Assert.IsTrue(r.Body.IsAutomaticSlot(0));
            }
        }
        [Test] public void RemovingTriggerRestoresSuccessorAndCancelsPendingWork()
        {
            using (var r = new Rig())
            {
                r.Executor(0); r.Trigger(TriggerOn.Started); var successor = r.Executor(2);
                r.Signal(TriggerOn.Started); Assert.AreEqual(1, r.Pipeline.PendingTriggerCount);
                r.Body.SetSlot(1, null); r.Pipeline.AdvanceTriggers(1);
                Assert.IsEmpty(r.Attacks); Assert.AreEqual(0, r.Pipeline.PendingTriggerCount);
                Assert.IsTrue(r.Pipeline.TryExecuteSlot(Part.Head, 2));
                Assert.AreEqual(successor.InstanceId, r.Attacks[0].OriginInstanceId);
            }
        }
        [Test] public void FullyBoundPipeDoesNotBankAutomaticTime()
        {
            foreach (float period in new[] { 0f, 1f })
                using (var r = new Rig())
                {
                    r.Trigger(TriggerOn.Stationary, 0); r.Executor(1);
                    r.Body.CycleDuration = period; r.Body.Tick(100);
                    Assert.IsEmpty(r.Attacks);
                    r.Body.SetSlot(0, null); r.Body.Tick(.5f); Assert.IsEmpty(r.Attacks);
                    r.Body.Tick(.5f); Assert.AreEqual(1, r.Attacks.Count);
                }
        }
        [Test] public void LoadoutChangeDuringActivationCannotFireStaleSuccessor()
        {
            using (var r = new Rig())
            {
                r.Executor(0); r.Trigger(TriggerOn.Hit); r.Executor(2);
                r.Pipeline.TriggerActivated += (_, __, ___, ____) => r.Body.SetSlot(2, null);
                r.Signal(TriggerOn.Hit); Assert.IsEmpty(r.Attacks);
            }
        }
        [Test] public void ActualHealthAndCriticalDamage()
        {
            var go = new GameObject("Health trigger checks");
            try
            {
                go.AddComponent<PlayerStatsBase>(); var player = go.AddComponent<PlayerRuntime>(); player.ResetHealthToMax();
                int hurt = 0, heal = 0; CastContext received = null;
                player.PlayerDamaged += _ => hurt++;
                player.PlayerHealed += (_, cause) => { heal++; received = cause?.Context; };
                player.Heal(5); player.TakeDamage(0); Assert.AreEqual(0, heal + hurt);
                player.TakeDamage(5); Assert.AreEqual(1, hurt);
                var cause = new CastEvent("enemy", 5, "vampire", "vampire-instance");
                Assert.AreEqual(5, player.Heal(10, cause)); Assert.AreEqual(1, heal); Assert.AreSame(cause.Context, received);
                Assert.AreEqual(0, player.Heal(float.NaN));
                var critical = CriticalDamage.Roll(cause, 1, 2, .99f, out bool applied);
                Assert.IsTrue(applied); Assert.AreEqual(10, critical.Damage); Assert.AreSame(cause.Context, critical.Context);
                CriticalDamage.Roll(cause, 0, 2, 0, out applied); Assert.IsFalse(applied);
            }
            finally { Object.DestroyImmediate(go); }
        }
        public static void RunBatch()
        {
            try
            {
                var checks = new OrganTriggerChecks();
                foreach (var method in typeof(OrganTriggerChecks).GetMethods().Where(m => Attribute.IsDefined(m, typeof(TestAttribute)))) method.Invoke(checks, null);
                CheckPreview();
                Debug.Log("[Triggers] LOGIC PASS: adjacent binding, automatic exclusion, source identity, conditions, chain guards, health, timing and reset.");
                PatternActuatorChecks.RunBatch();
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
        private static void CheckPreview()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/LaboratoryDemo.unity");
            var presenter = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LaboratoryPresenter>()).Single();
            presenter.InitializeView(); var demo = presenter.GetComponentInChildren<LaboratoryCombatDemo>(); demo.Initialize();
            var equipment = (TemporaryEquipment)presenter.source.Equipment;
            foreach (var definition in OrganCatalog.LoadDefault().CreateDefinitions().Where(d => d.Trigger != null))
            {
                for (int p = 0; p < 4; p++) for (int s = 0; s < equipment.SlotCount((LaboratoryPart)p); s++) equipment.TryUnequip((LaboratoryPart)p, s, out _);
                void Equip(string id, int slot)
                { var item = equipment.Inventory.First(i => i.DefinitionId == id); Assert.IsTrue(equipment.TryEquip(item.InstanceId, LaboratoryPart.Head, slot, out _)); }
                Equip("fist", 0); Equip(definition.Id, 1); Equip("straight_projectile", 2);
                demo.Pipeline.AutomaticExecution = false; demo.Pipeline.TriggerRandom = () => 0;
                if (definition.TriggerOn == TriggerOn.Hurt) demo.PreviewHurt();
                else if (definition.TriggerOn == TriggerOn.Heal) demo.PreviewHeal();
                else if (definition.TriggerOn == TriggerOn.Stationary) demo.Advance(10);
                else if (definition.TriggerOn == TriggerOn.Kill)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        demo.Pipeline.AttackProduced += KillTarget;
                        demo.Pipeline.TryExecuteSlot(Part.Head, 0);
                        demo.Pipeline.AttackProduced -= KillTarget;
                        demo.Advance(2);
                    }
                    void KillTarget(CastEvent hit) { demo.targets.First(t => t.TargetId == hit.TargetId).TryApplyAttack(new CastEvent(hit.TargetId, 1000, hit.OriginDefinitionId, hit.OriginInstanceId, hit.Context)); }
                }
                else if (definition.TriggerOn == TriggerOn.Debuff)
                {
                    equipment.TryUnequip(LaboratoryPart.Head, 0, out _); Equip("octopus_sucker", 0);
                    demo.Pipeline.AutomaticExecution = false; demo.Pipeline.TriggerRandom = () => 0;
                    demo.Pipeline.TryExecuteSlot(Part.Head, 0); demo.Advance(.2f);
                }
                else
                {
                    if (definition.TriggerOn == TriggerOn.Critical) demo.previewCriticalChance = 1;
                    demo.Pipeline.TryExecuteSlot(Part.Head, 0);
                    if (definition.TriggerOn == TriggerOn.Started) demo.Advance(.5f);
                }
                Assert.Greater(demo.TriggerCount, 0, definition.Id);
                Canvas.ForceUpdateCanvases(); demo.Restart(); Assert.AreEqual(0, demo.Pipeline.PendingTriggerCount);
            }
            Debug.Log("[Triggers] PREVIEW PASS: all 10 equip, activate and reset; actual preview health/critical/debuff/kill/lifecycle wiring.");
        }
        public static void RunPlay(SpringUpRunDriver driver)
        {
            EnemyRuntime.ClearBattlefield();
            var target = new GameObject("Trigger integration target"); target.SetActive(false);
            target.transform.position = driver.player.transform.position + Vector3.right * .6f;
            var data = target.AddComponent<EnemyData>();
            var enemyDefinition = Object.Instantiate(EnemyCatalog.Load().Get(EnemyArchetype.Normal));
            enemyDefinition.maxHealth = 10000; enemyDefinition.hasContactDamage = false;
            data.Configure(enemyDefinition);
            var enemy = target.AddComponent<EnemyRuntime>(); target.AddComponent<CircleCollider2D>().radius = .2f; target.SetActive(true);
            var body = driver.pipeline.GetBody(Part.Head); var old = Enumerable.Range(0, 6).Select(body.GetSlot).ToArray();
            var stats = new SerializedObject(driver.player.Stats); float oldCrit = stats.FindProperty("critChance").floatValue;
            try
            {
                driver.combat.Advance(.01f);
                foreach (var kind in Enum.GetValues(typeof(TriggerOn)).Cast<TriggerOn>().OrderBy(k => k == TriggerOn.Kill ? 1 : 0))
                {
                    driver.combat.Clear(); for (int i = 0; i < 6; i++) body.SetSlot(i, null);
                    var settings = new TriggerSettings(kind, 1, killThreshold: 1);
                    body.SetSlot(1, new Instance(new Definition("test", "test", OrganType.Trigger, triggerOn: kind,
                        triggerTarget: TriggerTargetKind.NextSlot, trigger: settings)));
                    body.SetSlot(2, new Instance(driver.Definitions["fist"]));
                    body.SetSlot(0, new Instance(driver.Definitions[kind == TriggerOn.Debuff ? "octopus_sucker" : "straight_projectile"]));
                    int activated = 0;
                    void Count(BodyRuntime b, int s, Instance o, CastContext c) { if (b.Part == Part.Head) activated++; }
                    driver.pipeline.TriggerActivated += Count;
                    try
                    {
                        stats.Update(); stats.FindProperty("critChance").floatValue = kind == TriggerOn.Critical ? 1 : 0; stats.ApplyModifiedPropertiesWithoutUndo();
                        if (kind == TriggerOn.Hurt) driver.player.TakeDamage(1);
                        else if (kind == TriggerOn.Heal) { driver.player.TakeDamage(2); driver.player.Heal(1); }
                        else if (kind == TriggerOn.Stationary) { driver.pipeline.MovingProvider = () => false; driver.combat.Advance(10); }
                        else if (kind == TriggerOn.Kill)
                            driver.combat.Resolve(new CastEvent(enemy.TargetId, 20000, body.GetSlot(0).Definition.Id, body.GetSlot(0).InstanceId));
                        else { Assert.IsTrue(driver.pipeline.TryExecuteSlot(Part.Head, 0), "Start " + kind); driver.combat.Advance(.6f); }
                        Assert.Greater(activated, 0, "Play " + kind);
                        if (kind == TriggerOn.Started)
                        {
                            Assert.IsTrue(driver.pipeline.TryExecuteSlot(Part.Head, 0));
                            Assert.Greater(driver.pipeline.PendingTriggerCount, 0);
                            driver.pipeline.enabled = false;
                            Assert.AreEqual(0, driver.pipeline.PendingTriggerCount);
                            driver.pipeline.enabled = true;
                        }
                    }
                    finally { driver.pipeline.TriggerActivated -= Count; }
                }
            }
            finally
            {
                driver.combat.Clear(); driver.pipeline.MovingProvider = null;
                for (int i = 0; i < 6; i++) body.SetSlot(i, old[i]);
                stats.Update(); stats.FindProperty("critChance").floatValue = oldCrit; stats.ApplyModifiedPropertiesWithoutUndo();
                driver.player.ResetHealthToMax(); Object.DestroyImmediate(target); Object.DestroyImmediate(enemyDefinition);
            }
            Debug.Log("[Triggers] PLAY PASS: all 9 conditions through real health, physics, kill, critical, slow and execution lifecycle.");
        }
    }
}
