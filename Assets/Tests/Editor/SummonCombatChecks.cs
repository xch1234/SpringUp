using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using OrganInstance = SpringUp.Organs.OrganInstance;
using OrganDefinition = SpringUp.Organs.OrganDefinition;
using Part = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public static class SummonCombatChecks
    {
        public static void RunLogic()
        {
            SummonsWaitForTheirSlot(0);
            SummonsWaitForTheirSlot(2);
            var triggers = new OrganTriggerChecks();
            triggers.ProbabilityAndChainGuard();
            triggers.DelayAdjacentPredecessorAndCancellation();
            triggers.AttackConditionsIgnoreOtherSlotsBodiesAndSameDefinitionInstances();
            triggers.ChainedBindingsExecuteOnlyTheirOwnSuccessors();
            triggers.ResetDuringActivationStopsRemainingWork();
            triggers.AutomaticLoopSkipsTriggerAndBoundExecutorInBothTimingModes();
            triggers.BindingsDoNotSkipEmptySlotsOrWrapAtBoundary();
            Debug.Log("SUMMON_TRIGGER_REGRESSION_PASS");
        }
        public static void RunTimingBatch()
        {
            try
            {
                RunLogic();
                Debug.Log("SUMMON_TIMING_CHECKS_PASS");
                UnityEditor.EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                UnityEditor.EditorApplication.Exit(1);
            }
        }
        [TestCase(0f)]
        [TestCase(2f)]
        public static void SummonsWaitForTheirSlot(float cycleDuration)
        {
            var root = new GameObject("Summon timing checks");
            try
            {
                var pipeline = root.AddComponent<OrganPipelineController>();
                var combat = root.AddComponent<BattleOrganCombat>();
                // Edit Mode does not invoke the runtime components' lifecycle callbacks.
                InvokeEditModeLifecycle(root, "OnEnable");
                root.GetComponent<BattleOrganTargeting>().AttackOrigin = root.transform;
                pipeline.StepInterval = .5f;
                pipeline.CycleDurationProvider = _ => cycleDuration;
                var body = pipeline.GetBody(Part.Hand);
                var first = new OrganInstance(new OrganDefinition("first", "first", OrganType.Actuator, 1));
                var organ = new OrganInstance(new OrganDefinition("summon", "summon", OrganType.Actuator, 5,
                    projectile: new PatternActuatorSettings(ActuatorPattern.Summon, healthDecay: 0)));
                body.SetSlot(0, first); body.SetSlot(1, organ);
                Assert.IsEmpty(combat.Actuators.Active, "Equipping cannot start an execution");
                pipeline.AutomaticExecution = false;
                pipeline.AdvanceAutomatic(10);
                Assert.IsEmpty(combat.Actuators.Active, "Disabled automatic execution cannot summon");
                pipeline.AutomaticExecution = true;
                pipeline.AdvanceAutomatic(.5f);
                Assert.AreEqual(0, body.LastExecutedSlot);
                Assert.IsEmpty(combat.Actuators.Active, "Earlier slot must execute before summon");
                pipeline.AdvanceAutomatic(.49f);
                Assert.IsEmpty(combat.Actuators.Active, "Summon must wait the full slot interval");
                pipeline.AdvanceAutomatic(.01f);
                var summon = combat.Actuators.Active.OfType<SummonExecution>().Single();
                Assert.AreEqual(organ.InstanceId, summon.Context.Cause.OriginInstanceId);
                float roundDuration = cycleDuration > 0 ? cycleDuration : 1;
                pipeline.AdvanceAutomatic(roundDuration);
                Assert.AreSame(summon, combat.Actuators.Active.OfType<SummonExecution>().Single(),
                    "A living summon is not replaced or duplicated on later turns");
                summon.TakeDamage(summon.Health);
                combat.Actuators.Advance(0);
                Assert.IsEmpty(combat.Actuators.Active);
                pipeline.AdvanceAutomatic(roundDuration - .01f);
                Assert.IsEmpty(combat.Actuators.Active, "Death must wait for the next summon turn");
                pipeline.AdvanceAutomatic(.01f);
                Assert.AreNotSame(summon, combat.Actuators.Active.OfType<SummonExecution>().Single());

                combat.Clear();
                body.SetSlot(1, new OrganInstance(new OrganDefinition("hit", "hit", OrganType.Trigger,
                    triggerOn: TriggerOn.Hit, trigger: new TriggerSettings(TriggerOn.Hit, 1))));
                body.SetSlot(2, organ); body.ResetTiming();
                pipeline.AdvanceAutomatic(10);
                Assert.IsEmpty(combat.Actuators.Active, "Trigger-bound summons skip the automatic loop");
                pipeline.PublishSignal(new TriggerSignal(TriggerOn.Hit,
                    new CastEvent("target", 1, first.Definition.Id, first.InstanceId)));
                Assert.AreEqual(organ.InstanceId,
                    combat.Actuators.Active.OfType<SummonExecution>().Single().Context.Cause.OriginInstanceId);
            }
            finally
            {
                InvokeEditModeLifecycle(root, "OnDisable");
                Object.DestroyImmediate(root);
            }
        }
        static void InvokeEditModeLifecycle(GameObject root, string method)
        {
            if (Application.isPlaying) return;
            foreach (var component in root.GetComponents<MonoBehaviour>())
                component.GetType().GetMethod(method,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(component, null);
        }
        public static void RunPlay(PlayerRuntime player)
        {
            var root = new GameObject("Summon combat checks");
            SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
            var pipeline = root.AddComponent<OrganPipelineController>(); pipeline.AutomaticExecution = false;
            var combat = root.AddComponent<BattleOrganCombat>();
            var enemyObject = new GameObject("Summon contact target"); enemyObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(enemyObject, player.gameObject.scene);
            var data = enemyObject.AddComponent<EnemyData>();
            var enemyData = Object.Instantiate(EnemyCatalog.Load().Get(EnemyArchetype.Normal));
            enemyData.maxHealth = 1000; enemyData.attackPower = 3; enemyData.hasContactDamage = false;
            data.Configure(enemyData);
            var enemy = enemyObject.AddComponent<EnemyRuntime>();
            enemyObject.AddComponent<CircleCollider2D>().radius = .2f;
            enemyObject.AddComponent<BoxCollider2D>().size = Vector2.one * .2f;
            var emitted = new List<ActuatorContext>(); pipeline.ActuatorProduced += emitted.Add;
            var body = pipeline.GetBody(Part.Head);
            var summonOrgan = new OrganInstance(new OrganDefinition("summon_test", "summon", OrganType.Actuator, 5,
                projectile: new PatternActuatorSettings(ActuatorPattern.Summon, speed:100, radius: .35f, health:100, healthDecay:0, followDistance:3)));
            body.SetSlot(0, summonOrgan);
            void SetTrigger(TriggerOn kind, int slot = 1)
                => body.SetSlot(slot, new OrganInstance(new OrganDefinition("trigger" + slot, "trigger", OrganType.Trigger,
                    triggerOn:kind, trigger:new TriggerSettings(kind, 1))));
            SetTrigger(TriggerOn.Hit);
            body.SetSlot(2, new OrganInstance(new OrganDefinition("shot", "shot", OrganType.Actuator, 1,
                projectile:new StraightProjectileSettings(8, .12f, 4), targetingRange:1)));
            // A delayed successor must follow the actor's current position too.
            SetTrigger(TriggerOn.Started, 3);
            body.SetSlot(4, new OrganInstance(new OrganDefinition("delayed", "delayed", OrganType.Actuator, 1,
                projectile:new StraightProjectileSettings(8, .12f, 4), targetingRange:1)));
            Vector3 playerPosition = player.transform.position;
            float oldScale = Time.timeScale;
            var bullets = new List<GameObject>();
            int playerHits = 0;
            Action<EnemyEvents.PlayerHurtInfo> hurt = _ => playerHits++;
            EnemyEvents.PlayerHurt += hurt;
            try
            {
                Time.timeScale = 1;
                player.transform.position = new Vector3(50,50,0);
                Assert.IsEmpty(combat.Actuators.Active.OfType<SummonExecution>(), "Equipping does not summon");
                Assert.IsTrue(pipeline.TryExecuteSlot(Part.Head, 0));
                var summon = combat.Actuators.Active.OfType<SummonExecution>().Single();
                enemyObject.transform.position = summon.Position + Vector2.up * .1f; enemyObject.SetActive(true);
                emitted.Clear(); float enemyHealth = enemy.CurrentHealth;
                combat.Advance(.02f);
                Assert.Less(enemy.CurrentHealth, enemyHealth, "Contact damages enemy once despite compound colliders");
                Assert.AreEqual(97, summon.Health, .001f);
                Assert.AreEqual(1, emitted.Count(c => c.Cause.OriginDefinitionId == "shot"));
                var shot = emitted.Single(c => c.Cause.OriginDefinitionId == "shot");
                Assert.AreEqual(summon.Position, shot.SpawnPosition, "Range and firing origin belong to summon, not distant player");
                Assert.NotNull(shot.Cause.Context.ActorPosition);
                Assert.Greater(pipeline.PendingTriggerCount, 0);
                player.transform.position += Vector3.right;
                summon.Tick(.1f);
                enemyObject.transform.position = summon.Position + Vector2.up * .1f;
                pipeline.AdvanceTriggers(5);
                Assert.AreEqual(summon.Position, emitted.Single(c => c.Cause.OriginDefinitionId == "delayed").SpawnPosition);
                // Contact cooldown has not elapsed; next collision starts a fresh trigger chain.
                emitted.Clear(); float hp = summon.Health;
                summon.Tick(.02f); Assert.AreEqual(hp, summon.Health, .001f);
                summon.Tick(.5f);
                Assert.AreEqual(hp - 3, summon.Health, .001f);
                Assert.AreEqual(1, emitted.Count(c => c.Cause.OriginDefinitionId == "shot"));
                SetTrigger(TriggerOn.Hurt);
                emitted.Clear(); pipeline.NotifyOwner(TriggerOn.Hurt);
                Assert.IsEmpty(emitted, "Player hurt cannot drive summon-bound hurt trigger");
                summon.TakeDamage(2);
                Assert.AreEqual(1, emitted.Count(c => c.Cause.OriginDefinitionId == "shot"));
                Assert.AreEqual(summon.Position, emitted[0].SpawnPosition);
                enemyObject.SetActive(false);
                emitted.Clear(); float beforeDecay = summon.Health;
                summon.Tick(.1f); Assert.AreEqual(beforeDecay, summon.Health, "Zero decay is supported"); Assert.IsEmpty(emitted);
                // Place the player behind the summon on the same swept segment.
                player.transform.position = (Vector3)summon.Position + Vector3.right * 3;
                Projectile Bullet(Vector2 from, Vector2 direction)
                {
                    var go = new GameObject("Summon bullet test"); bullets.Add(go);
                    SceneManager.MoveGameObjectToScene(go, player.gameObject.scene);
                    go.transform.position = from; go.AddComponent<SpriteRenderer>();
                    var bullet = go.AddComponent<Projectile>(); bullet.Initialize(direction, 100, 4, .1f, "test", Color.white);
                    return bullet;
                }
                var first = Bullet(summon.Position - Vector2.right * 2, Vector2.right);
                float beforeBullet = summon.Health;
                Time.timeScale = 0; first.Advance(.1f);
                Assert.AreEqual(beforeBullet, summon.Health); Assert.IsTrue(first.gameObject.activeSelf);
                Time.timeScale = 1; first.Advance(.1f);
                Assert.AreEqual(beforeBullet - 4, summon.Health); Assert.AreEqual(0, playerHits);
                Assert.IsFalse(first.gameObject.activeSelf);
                // Reverse direction: the player must be hit first and summon left untouched.
                var second = Bullet((Vector2)player.transform.position + Vector2.right, Vector2.left);
                second.Advance(.1f);
                Assert.AreEqual(1, playerHits); Assert.AreEqual(beforeBullet - 4, summon.Health);
                // Death cancels a pending cast without falling back to player origin.
                pipeline.NotifyExecutionStarted(new CastEvent(shot.Cause.TargetId, 1, shot.Cause.OriginDefinitionId,
                    shot.Cause.OriginInstanceId, summon.NewActorContext()));
                Assert.Greater(pipeline.PendingTriggerCount, 0);
                emitted.Clear();
                var actor = summon.NewActorContext();
                summon.TakeDamage(1000); Assert.IsTrue(summon.IsFinished); Assert.IsNull(actor.ActorPosition());
                Assert.IsNull(BattleOrganCombat.SweepSummons(root.scene, summon.Position-Vector2.right, summon.Position+Vector2.right, .1f, out _));
                pipeline.AdvanceTriggers(5);
                Assert.IsEmpty(emitted, "No delayed cast after actor death");
                // Natural health decay does not emit a damage event.
                var decaySettings = new PatternActuatorSettings(ActuatorPattern.Summon, health:20, healthDecay:2);
                var decay = new SummonExecution(new ActuatorContext(new CastEvent("owner", 0, "decay", "decay", allowZeroDamage:true),
                    Part.Head, 0, player.transform.position, Vector2.right, decaySettings), new BattleActuatorWorld(root.scene, _ => { }));
                int hurtCount = 0; decay.Damaged += (_, __) => hurtCount++;
                decay.Tick(1); Assert.AreEqual(18, decay.Health, .002f); Assert.AreEqual(0, hurtCount);
                decay.TakeDamage(1); Assert.AreEqual(1, hurtCount);
                decay.Cancel();
                combat.Advance(0); Assert.IsEmpty(combat.Actuators.Active.OfType<SummonExecution>());
                Debug.Log("SUMMON_COMBAT_CHECKS_PASS: contact/cooldown, actor targeting, hit/hurt chains, delayed origin, pause, bullet interception, nearest hit, death.");
            }
            finally
            {
                Time.timeScale = oldScale; player.transform.position = playerPosition;
                EnemyEvents.PlayerHurt -= hurt;
                Object.DestroyImmediate(root); Object.DestroyImmediate(enemyObject); Object.DestroyImmediate(enemyData);
                foreach (var go in bullets) if (go != null) Object.DestroyImmediate(go);
            }
        }
    }
}
