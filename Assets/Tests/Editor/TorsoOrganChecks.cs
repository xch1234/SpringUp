using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SpringUp.Organs;
using SpringUp.Laboratory;
using Tideflesh.Systems;
using TideBorne.Battle;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public sealed class TorsoOrganChecks
    {
        [Test] public void SevenAssetsRestrictionsAndComposition()
        {
            var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
            Assert.AreEqual(7, definitions.Count(d => d.Torso != null));
            foreach (var definition in definitions.Where(d => d.Torso != null))
            {
                var item = new LaboratoryItem("test", definition);
                Assert.IsTrue(item.CanEquip(LaboratoryPart.Torso)); Assert.IsFalse(item.CanEquip(LaboratoryPart.Head));
                Assert.IsFalse(definition.CanAttack); Assert.AreEqual(0, definition.ChargeCost);
                Assert.Throws<ArgumentException>(() => new BodyRuntime(SpringUp.Organs.BodyPart.Head, 1).SetSlot(0, new SpringUp.Organs.OrganInstance(definition)));
            }
            var loadout = new TorsoLoadout();
            loadout.Set(new[] { new TorsoSettings(damage: 1.2f, maxHealth: .6f), new TorsoSettings(damage: .8f, lifeSteal: .05f) });
            Assert.AreEqual(.96f, loadout.Damage, .0001f); Assert.AreEqual(.6f, loadout.MaxHealth); Assert.AreEqual(.05f, loadout.LifeSteal);
            loadout.Set(null); Assert.AreEqual(1, loadout.Damage); Assert.AreEqual(0, loadout.LifeSteal);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TorsoSettings(view: float.NaN));
        }
        [Test] public void DirectionalDamageUsesFacingCone()
        {
            var loadout = new TorsoLoadout(); loadout.Set(new[] { new TorsoSettings(frontDamage: 1.6f, frontHalfAngle: 45) });
            Assert.AreEqual(1.6f, loadout.FrontMultiplier(Vector2.zero, Vector2.right, Vector2.right));
            Assert.AreEqual(1.6f, loadout.FrontMultiplier(Vector2.zero, Vector2.right, Vector2.one));
            Assert.AreEqual(1, loadout.FrontMultiplier(Vector2.zero, Vector2.right, Vector2.up));
            Assert.AreEqual(1, loadout.FrontMultiplier(Vector2.zero, Vector2.right, Vector2.left));
        }
        [Test] public void StatsRecalculateWithoutStackingOrFreeHealing()
        {
            var go = new GameObject("Torso stats checks");
            try
            {
                var stats = go.AddComponent<PlayerStatsBase>(); var player = go.AddComponent<PlayerRuntime>(); player.ResetHealthToMax();
                var torso = go.AddComponent<TorsoEquipmentRuntime>(); torso.Configure(player);
                var settings = new[] { new TorsoSettings(damage: 1.2f, maxHealth: .6f, frequency: 1.2f, view: .8f) };
                torso.Apply(settings, null); torso.Apply(settings, null);
                Assert.AreEqual(60, player.MaxHealth, .001f); Assert.AreEqual(60, player.CurrentHealth, .001f);
                Assert.AreEqual(1.2f, stats.DamageMultiplier, .001f); Assert.AreEqual(1/1.2f, stats.BaseCooldown, .001f);
                torso.Apply(null, null); Assert.AreEqual(100, player.MaxHealth); Assert.AreEqual(60, player.CurrentHealth, .001f);
                Assert.AreEqual(1, stats.DamageMultiplier);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void IncomingDamageAndInvulnerability()
        {
            var go = new GameObject("Torso guard checks");
            try
            {
                go.AddComponent<PlayerStatsBase>(); var player = go.AddComponent<PlayerRuntime>(); player.ResetHealthToMax();
                var torso = go.AddComponent<TorsoEquipmentRuntime>(); torso.Configure(player);
                torso.Apply(new[] { new TorsoSettings(incomingDamage: .6f, invulnerability: .5f) }, null);
                Assert.AreEqual(6, player.ReceiveAttack(10), .001f); Assert.AreEqual(.15f, player.InvulnerabilityRemaining, .001f);
                Assert.AreEqual(0, player.ReceiveAttack(10)); player.ResetDamageProtection();
                torso.Apply(new[] { new TorsoSettings(incomingDamage: 1.2f, invulnerability: 1.5f) }, null);
                Assert.AreEqual(12, player.ReceiveAttack(10), .001f); Assert.AreEqual(.45f, player.InvulnerabilityRemaining, .001f);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void PreviewAllSevenEffectsAndRemoval()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/LaboratoryDemo.unity");
            var presenter = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LaboratoryPresenter>()).Single();
            presenter.InitializeView(); var demo = presenter.GetComponentInChildren<LaboratoryCombatDemo>(); demo.Initialize();
            var equipment = (TemporaryEquipment)presenter.source.Equipment;
            for (int p = 0; p < 4; p++) for (int s = 0; s < equipment.SlotCount((LaboratoryPart)p); s++) equipment.TryUnequip((LaboratoryPart)p, s, out _);
            var fist = equipment.Inventory.First(i => i.DefinitionId == "fist"); equipment.TryEquip(fist.InstanceId, LaboratoryPart.Head, 0, out _);
            foreach (var definition in OrganCatalog.LoadDefault().CreateDefinitions().Where(d => d.Torso != null))
            {
                var item = equipment.Inventory.First(i => i.DefinitionId == definition.Id);
                Assert.IsTrue(equipment.TryEquip(item.InstanceId, LaboratoryPart.Torso, 0, out _));
                demo.Pipeline.AutomaticExecution = false;
                Assert.AreEqual(100 * definition.Torso.MaxHealth, demo.PreviewMaxHealth, .001f);
                float hp = demo.targets[0].CurrentHealth, playerHp = demo.PreviewHealth;
                Assert.IsTrue(demo.Pipeline.TryExecuteSlot(SpringUp.Organs.BodyPart.Head, 0));
                float damage = 5 * definition.Torso.Damage * definition.Torso.FrontDamage;
                Assert.AreEqual(hp - damage, demo.targets[0].CurrentHealth, .001f, definition.Id);
                Assert.AreEqual(Mathf.Min(demo.PreviewMaxHealth, playerHp + damage * definition.Torso.LifeSteal), demo.PreviewHealth, .001f);
                demo.PreviewHurt(); float hurtHp = demo.PreviewHealth; demo.PreviewHurt(); Assert.AreEqual(hurtHp, demo.PreviewHealth);
                Canvas.ForceUpdateCanvases(); equipment.TryUnequip(LaboratoryPart.Torso, 0, out _);
                Assert.AreEqual(1, demo.PreviewTorso.Damage);
            }
        }
        [Test] public void TriggerProbabilityAndFrequencyUseTorsoFactors()
        {
            var go = new GameObject("Torso trigger factors");
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>(); pipeline.AutomaticExecution = false;
                var body = pipeline.GetBody(SpringUp.Organs.BodyPart.Head);
                var source = new SpringUp.Organs.OrganInstance(new SpringUp.Organs.OrganDefinition("source", "source", OrganType.Actuator, 5));
                body.SetSlot(0, source);
                void SetTrigger(TriggerOn kind) => body.SetSlot(1, new SpringUp.Organs.OrganInstance(new SpringUp.Organs.OrganDefinition("trigger", "trigger", OrganType.Trigger,
                    triggerOn: kind, triggerTarget: TriggerTargetKind.FollowingSlots, trigger: new TriggerSettings(kind, .3f))));
                var cause = new CastEvent("target", 5, "source", source.InstanceId);
                int count = 0; pipeline.TriggerActivated += (_, __, ___, ____) => count++;
                pipeline.TriggerFrequencyMultiplier = pipeline.TriggerProbabilityMultiplier = 1.2f;
                SetTrigger(TriggerOn.Started); pipeline.NotifyExecutionStarted(cause);
                pipeline.AdvanceTriggers(.4f); Assert.AreEqual(0, count);
                pipeline.AdvanceTriggers(.017f); Assert.AreEqual(1, count);
                SetTrigger(TriggerOn.Hit); pipeline.TriggerRandom = () => .35f;
                pipeline.PublishSignal(new TriggerSignal(TriggerOn.Hit, cause)); Assert.AreEqual(2, count);
                SetTrigger(TriggerOn.Stationary); pipeline.OriginProvider = () => Vector2.zero;
                pipeline.AdvanceTriggers(5); Assert.AreEqual(6, pipeline.TriggerProgress(SpringUp.Organs.BodyPart.Head, 1), .001f);
            }
            finally { Object.DestroyImmediate(go); }
        }
        public static void RunBatch()
        {
            try
            {
                var checks = new TorsoOrganChecks();
                foreach (var method in typeof(TorsoOrganChecks).GetMethods().Where(m => Attribute.IsDefined(m, typeof(TestAttribute)))) method.Invoke(checks, null);
                Debug.Log("[Torso] EDIT PASS: 6 groups; all 7 definitions, preview, restrictions, composition, facing, health, immunity, trigger factors and removal.");
                OrganTriggerChecks.RunBatch();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void RunPlay(SpringUpRunDriver driver)
        {
            var target = new GameObject("Torso damage target"); target.SetActive(false);
            target.transform.position = driver.player.transform.position + driver.player.transform.right;
            var data = target.AddComponent<EnemyData>(); var edit = new SerializedObject(data);
            edit.FindProperty("maxHealth").floatValue = 10000; edit.ApplyModifiedPropertiesWithoutUndo();
            var enemy = target.AddComponent<EnemyRuntime>(); target.SetActive(true);
            var motor = driver.player.GetComponent<PlayerMotor>(); var body = driver.player.GetComponent<Rigidbody2D>();
            float rotation = body.rotation; Vector2 velocity = body.linearVelocity;
            var inputField = typeof(PlayerMotor).GetField("_moveInput", BindingFlags.NonPublic | BindingFlags.Instance);
            var oldInput = inputField.GetValue(motor);
            try
            {
                driver.combat.Advance(.01f);
                driver.Torso.Apply(new[] { driver.Definitions["torso_leech"].Torso }, driver.pipeline);
                driver.player.TakeDamage(20); float hp = driver.player.CurrentHealth;
                float actual = driver.combat.ResolveDamage(new CastEvent(enemy.TargetId, 10, "test", "test"));
                Assert.AreEqual(8, actual, .001f); Assert.AreEqual(hp + .4f, driver.player.CurrentHealth, .001f);
                driver.Torso.Apply(new[] { driver.Definitions["torso_ram"].Torso }, driver.pipeline);
                actual = driver.combat.ResolveDamage(new CastEvent(enemy.TargetId, 10, "test", "test")); Assert.AreEqual(16, actual, .001f);
                target.transform.position = driver.player.transform.position - driver.player.transform.right;
                actual = driver.combat.ResolveDamage(new CastEvent(enemy.TargetId, 10, "test", "test")); Assert.AreEqual(10, actual, .001f);
                body.rotation = 0; inputField.SetValue(motor, Vector2.up); motor.SendMessage("FixedUpdate");
                Assert.AreEqual(driver.player.Stats.MoveSpeed, body.linearVelocity.magnitude, .01f);
                Assert.Greater(body.linearVelocity.x, 0, "turning limits actual movement, not only sprite rotation");
                driver.Torso.Apply(new[] { driver.Definitions["torso_overdrive"].Torso }, driver.pipeline);
                Assert.AreEqual(1.2f, driver.pipeline.TriggerProbabilityMultiplier, .001f);
                Assert.AreEqual(1.2f, driver.pipeline.TriggerFrequencyMultiplier, .001f);
            }
            finally
            {
                driver.Torso.Apply(null, driver.pipeline); driver.player.ResetHealthToMax(); driver.player.ResetDamageProtection();
                inputField.SetValue(motor, oldInput); body.rotation = rotation; body.linearVelocity = velocity;
                driver.combat.Clear(); Object.DestroyImmediate(target);
            }
            Debug.Log("[Torso] PLAY PASS: actual damage/lifesteal/front-back cone, velocity steering and trigger factors.");
        }
        public static void RunLab(SpringUpRunDriver driver)
        {
            float cameraSize = driver.battleCamera.orthographicSize;
            foreach (var definition in driver.Definitions.Values.Where(d => d.Torso != null))
            {
                string id = driver.Run.Inventory.Spawn(definition.Id); driver.Equipment.Refresh();
                try
                {
                    Assert.IsFalse(driver.Equipment.TryEquip(id, LaboratoryPart.Head, 0, out _));
                    Assert.IsTrue(driver.Equipment.TryEquip(id, LaboratoryPart.Torso, 0, out _));
                    driver.SendMessage("LateUpdate");
                    Assert.AreEqual(100 * definition.Torso.MaxHealth, driver.player.MaxHealth, .001f);
                    Assert.AreEqual(cameraSize * definition.Torso.View, driver.battleCamera.orthographicSize, .001f);
                }
                finally { driver.Equipment.TryUnequip(LaboratoryPart.Torso, 0, out _); driver.Equipment.TryDiscard(id, out _); driver.SendMessage("LateUpdate"); }
            }
            driver.player.ResetHealthToMax();
            Debug.Log("[Torso] LAB PASS: all 7 through real inventory/equipment, camera size, health cap and restore.");
        }
    }
}
