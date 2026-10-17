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
            public Instance Trigger(TriggerOn kind, int slot = 0, float probability = 1)
            {
                var settings = new TriggerSettings(kind, probability);
                var organ = new Instance(new Definition("test_trigger", "trigger", OrganType.Trigger, triggerOn: kind,
                    triggerTarget: TriggerTargetKind.FollowingSlots, trigger: settings));
                Body.SetSlot(slot, organ); return organ;
            }
            public CastEvent Cause(string source = "external", CastContext chain = null)
                => new CastEvent("target", 5, "fist", source, chain ?? new CastContext(), effectCenter: Vector2.right);
            public void Signal(TriggerOn kind, CastEvent? cause = null) => Pipeline.PublishSignal(new TriggerSignal(kind, cause ?? Cause(), Vector2.right));
            public void Dispose() => Object.DestroyImmediate(Go);
        }
        [Test] public void AssetsAndAllEventConditions()
        {
            var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
            Assert.AreEqual(10, definitions.Count(d => d.Trigger != null));
            Assert.AreEqual(9, definitions.Where(d => d.Trigger != null).Select(d => d.TriggerOn).Distinct().Count());
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
                r.Trigger(TriggerOn.Hit, probability: .3f); r.Executor(1);
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
        [Test] public void MigratedTentacleKeepsWholePipeScope()
        {
            var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
            var tentacle = definitions.Single(d => d.Id == "multi_tentacle");
            Assert.AreEqual(1, tentacle.Trigger.KillThreshold);
            Assert.AreEqual(TriggerTargetKind.SelfSlots, tentacle.TriggerTarget);
            var item = new LaboratoryItem("scope", tentacle);
            Assert.AreEqual(LaboratoryTriggerTarget.SelfSlots, item.TriggerTarget);
            using (var r = new Rig())
            {
                r.Executor(0); r.Body.SetSlot(1, new Instance(tentacle)); r.Executor(2);
                var cause = r.Cause(); r.Signal(TriggerOn.Kill, cause);
                Assert.AreEqual(2, r.Attacks.Count, "Tentacle must include slots before and after itself.");
                r.Signal(TriggerOn.Kill, cause);
                Assert.AreEqual(2, r.Attacks.Count, "Same-chain guard still applies after migration.");
            }
            var count = definitions.Single(d => d.Trigger != null && d.TriggerOn == TriggerOn.Kill && d.Id != tentacle.Id);
            Assert.AreEqual(10, count.Trigger.KillThreshold);
            Assert.AreEqual(TriggerTargetKind.FollowingSlots, count.TriggerTarget);
        }
        [Test] public void ResetDuringActivationStopsRemainingWork()
        {
            using (var r = new Rig())
            {
                r.Trigger(TriggerOn.Hit); r.Executor(1); r.Trigger(TriggerOn.Ended, 2); r.Executor(3);
                r.Pipeline.AttackProduced += _ => r.Pipeline.ResetTriggers();
                r.Signal(TriggerOn.Hit); Assert.AreEqual(1, r.Attacks.Count);
            }
        }
        [Test] public void KillThresholdAndReset()
        {
            using (var r = new Rig())
            {
                r.Trigger(TriggerOn.Kill); r.Executor(1);
                for (int i = 0; i < 9; i++) r.Signal(TriggerOn.Kill);
                Assert.IsEmpty(r.Attacks); Assert.AreEqual(9, r.Pipeline.TriggerProgress(Part.Head, 0));
                r.Signal(TriggerOn.Kill); Assert.AreEqual(1, r.Attacks.Count);
                Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 0));
                r.Signal(TriggerOn.Kill); r.Pipeline.ResetTriggers();
                Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 0));
            }
        }
        [Test] public void DelayNearestPredecessorAndCancellation()
        {
            using (var r = new Rig())
            {
                var first = r.Executor(0); var previous = r.Executor(2); r.Trigger(TriggerOn.Started, 3); r.Executor(5);
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
                var previous = r.Executor(0); r.Trigger(TriggerOn.Ended, 2); r.Executor(3);
                r.Pipeline.NotifyExecutionEnded(r.Cause(previous.InstanceId), true); Assert.IsEmpty(r.Attacks);
                r.Pipeline.NotifyExecutionEnded(r.Cause()); Assert.IsEmpty(r.Attacks);
                r.Pipeline.NotifyExecutionEnded(r.Cause(previous.InstanceId)); Assert.AreEqual(1, r.Attacks.Count);
                r.Pipeline.TryExecuteSlot(Part.Head, 0); Assert.AreEqual(3, r.Attacks.Count, "instant executor starts/ends too");
            }
        }
        [Test] public void StationaryChargeIsIndependentAndMovementResets()
        {
            using (var r = new Rig())
            {
                r.Trigger(TriggerOn.Stationary); r.Executor(1);
                r.Pipeline.AdvanceTriggers(9); Assert.IsEmpty(r.Attacks);
                Assert.AreEqual(9, r.Pipeline.TriggerProgress(Part.Head, 0));
                r.Pipeline.MovingProvider = () => true; r.Pipeline.AdvanceTriggers(1);
                Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 0));
                r.Pipeline.MovingProvider = () => false; r.Pipeline.AdvanceTriggers(10);
                Assert.AreEqual(1, r.Attacks.Count); Assert.AreEqual(0, r.Pipeline.TriggerProgress(Part.Head, 0));
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
                Debug.Log("[Triggers] LOGIC PASS: 8 groups, 9 conditions, chain guards, actual health, timing, reset, critical damage.");
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
            var data = target.AddComponent<EnemyData>(); var edit = new SerializedObject(data);
            edit.FindProperty("maxHealth").floatValue = 10000; edit.ApplyModifiedPropertiesWithoutUndo();
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
                        triggerTarget: TriggerTargetKind.FollowingSlots, trigger: settings)));
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
                            driver.combat.Resolve(new CastEvent(enemy.TargetId, 20000, "test", "test"));
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
                driver.player.ResetHealthToMax(); Object.DestroyImmediate(target);
            }
            Debug.Log("[Triggers] PLAY PASS: all 9 conditions through real health, physics, kill, critical, slow and execution lifecycle.");
        }
    }
}
