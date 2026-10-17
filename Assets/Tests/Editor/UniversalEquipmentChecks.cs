using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpringUp.Organs;
using SpringUp.Laboratory;
using Tideflesh.Systems;
using UnityEngine;
using Definition = SpringUp.Organs.OrganDefinition;
using Instance = SpringUp.Organs.OrganInstance;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public sealed class UniversalEquipmentChecks
    {
        private static Dictionary<string, Tideflesh.Systems.OrganDefinition> CatalogData()
            => Catalog.LoadOrgans(File.ReadAllText(Path.Combine(Application.dataPath, "Data/Organs/organs.json")));

        [Test]
        public void Inventory_AllParts_IdentityAndTorsoRestrictions()
        {
            var inventory = new Inventory(CatalogData());
            string id = inventory.Spawn("fist"), torso = inventory.Spawn("collection_net");
            foreach (string part in new[] { "head", "hand", "leg" })
            {
                inventory.Equip(id, part, 6);
                Assert.AreEqual(id, inventory.EquippedAt(part, 6));
                Assert.Throws<InventoryException>(() => inventory.Equip(id, "head", 1));
                Assert.Throws<InventoryException>(() => inventory.Replace(torso, part, 6));
                Assert.AreEqual(id, inventory.EquippedAt(part, 6));
                Assert.AreEqual(id, inventory.Unequip(part, 6));
            }
            Assert.Throws<InventoryException>(() => inventory.Equip(id, "torso", 1));
            inventory.Equip(torso, "torso", 1);
            Assert.AreEqual(2, inventory.Instances.Count);
            Assert.AreEqual(id, inventory.UnequippedIds()[0]);
        }

        [Test]
        public void Pipeline_CrossPartDuplicatesAreRejectedById()
        {
            var go = new GameObject("Universal equipment check");
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>();
                var definition = new Definition("test", "test", OrganType.Actuator, 5);
                var head = pipeline.GetBody(BodyPart.Head);
                var leg = pipeline.GetBody(BodyPart.Leg);
                head.SetSlot(0, new Instance(definition, "shared-id"));
                Assert.Throws<ArgumentException>(() => leg.SetSlot(5, new Instance(definition, "shared-id")));
                Assert.Throws<ArgumentException>(() => head.SetSlot(1, new Instance(definition, "shared-id")));
                Assert.IsNull(leg.GetSlot(5));
                head.SetSlot(0, null);
                leg.SetSlot(5, new Instance(definition, "shared-id"));
                Assert.AreEqual("shared-id", leg.GetSlot(5).InstanceId);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Loadout_MovesFromLegToHead_AndPreservesRuntimeInstance()
        {
            var go = new GameObject("Loadout check");
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>();
                var inventory = new Inventory(CatalogData());
                string legId = inventory.SpawnEquipped("fist", "leg", 6);
                string handId = inventory.SpawnEquipped("fist", "hand", 1);
                var definitions = new Dictionary<string, Definition>
                { ["fist"] = new Definition("fist", "拳头", OrganType.Actuator, 5) };
                Assert.AreEqual(0, PipelineLoadout.Apply(pipeline, inventory, definitions));
                var original = pipeline.GetBody(BodyPart.Leg).GetSlot(5);
                inventory.Unequip("leg", 6); inventory.Equip(legId, "head", 1);
                PipelineLoadout.Apply(pipeline, inventory, definitions);
                Assert.AreSame(original, pipeline.GetBody(BodyPart.Head).GetSlot(0));
                Assert.IsNull(pipeline.GetBody(BodyPart.Leg).GetSlot(5));
                Assert.AreEqual(handId, pipeline.GetBody(BodyPart.Hand).GetSlot(0).InstanceId);
                Assert.AreSame(original.Definition, pipeline.GetBody(BodyPart.Hand).GetSlot(0).Definition);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Trigger_ExecutesAdjacentSuccessorInItsActualBody()
        {
            foreach (var part in new[] { BodyPart.Head, BodyPart.Hand, BodyPart.Leg })
            {
                var go = new GameObject("Trigger location check");
                try
                {
                    var pipeline = go.AddComponent<OrganPipelineController>();
                    pipeline.TargetSelector = () => "target";
                    pipeline.GetBody(part).SetSlot(0, new Instance(new Definition("fist", "拳头", OrganType.Actuator, 5)));
                    pipeline.GetBody(part).SetSlot(1, new Instance(new Definition("tentacle", "触手", OrganType.Trigger)));
                    pipeline.GetBody(part).SetSlot(2, new Instance(new Definition("next", "next", OrganType.Actuator, 5)));
                    int attacks = 0, slots = 0;
                    pipeline.AttackProduced += _ => attacks++;
                    pipeline.TriggeredSlotExecuted += (body, slot, organ, chain) =>
                    { Assert.AreEqual(part, body.Part); slots++; };
                    var cause = new CastEvent("target", 5, "fist", pipeline.GetBody(part).GetSlot(0).InstanceId, new CastContext());
                    pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, cause));
                    pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, cause));
                    Assert.AreEqual(1, attacks);
                    Assert.AreEqual(1, slots);
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        [Test]
        public void FormalEquipment_CombatBlocked_LabAllowsCrossPart()
        {
            var run = new RunController(CatalogData(), new List<WaveDefinition>
            {
                new WaveDefinition { wave = 1, duration_sec = 60, guaranteed_drops = new[] { "rust_knife" }, optional_drops = Array.Empty<string>() },
                new WaveDefinition { wave = 2, duration_sec = 60, guaranteed_drops = Array.Empty<string>(), optional_drops = Array.Empty<string>() }
            }, (_, __) => { });
            var definitions = new Dictionary<string, Definition>
            {
                ["fist"] = new Definition("fist", "拳头", OrganType.Actuator, 5),
                ["rust_knife"] = new Definition("rust_knife", "锈刀", OrganType.Actuator, 1)
            };
            var equipment = new RunLaboratoryEquipment(run, definitions);
            run.StartRun();
            string id = run.Inventory.Spawn("rust_knife");
            Assert.IsFalse(equipment.TryEquip(id, LaboratoryPart.Head, 0, out _));
            Assert.IsFalse(equipment.TryUnequip(LaboratoryPart.Hand, 0, out _));
            run.CombatTimerFinished();
            Assert.IsTrue(equipment.TryEquip(id, LaboratoryPart.Head, 0, out _));
            Assert.IsTrue(equipment.TryUnequip(LaboratoryPart.Head, 0, out _));
            Assert.IsTrue(equipment.TryEquip(id, LaboratoryPart.Leg, 5, out _));
            Assert.AreEqual(id, run.Inventory.EquippedAt("leg", 6));
        }

        public static void RunBatch()
        {
            var checks = new UniversalEquipmentChecks();
            checks.Inventory_AllParts_IdentityAndTorsoRestrictions();
            checks.Pipeline_CrossPartDuplicatesAreRejectedById();
            checks.Loadout_MovesFromLegToHead_AndPreservesRuntimeInstance();
            checks.Trigger_ExecutesAdjacentSuccessorInItsActualBody();
            checks.FormalEquipment_CombatBlocked_LabAllowsCrossPart();
            SpringUp.LaboratoryEditor.TemporaryEquipmentChecks.Run();
            OrganPipelineStep1Checks.Run();
            OrganConsolidationChecks.Run();
            Debug.Log("[Universal Equipment Checks] PASS: cross-part inventory, unique identity, loadout, trigger, phase guards and organ regression.");
        }
    }
}
