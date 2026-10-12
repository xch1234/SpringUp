using System;
using SpringUp.Organs;
using UnityEngine;
namespace SpringUp.Laboratory
{
    public sealed class LaboratoryDemoBootstrap : MonoBehaviour
    {
        [Min(1)] public int torsoSlotCount = 2;
        public OrganCatalog catalog;
        public string ConfigurationError { get; private set; }
        private ILaboratoryEquipment equipment;
        public void Bind(ILaboratoryEquipment value)
        {
            equipment = value ?? throw new ArgumentNullException(nameof(value));
            ConfigurationError = null;
        }
        public ILaboratoryEquipment Equipment { get { EnsureInitialized(); return equipment; } }
        public void EnsureInitialized()
        {
            if (equipment != null) return;
            var samples = new TemporaryEquipment(Mathf.Max(1, torsoSlotCount));
            equipment = samples;
            try
            {
                var definitions = (catalog != null ? catalog : OrganCatalog.LoadDefault()).CreateDefinitions();
                foreach (var definition in definitions)
                    for (int copy = 0; copy < 2; copy++)
                    {
                        var item = new LaboratoryItem(Guid.NewGuid().ToString("N"), definition);
                        samples.AddSample(item);
                        if (copy == 0 && (definition.Id == "fist" || definition.Id == "multi_tentacle"))
                            equipment.TryEquip(item.InstanceId, LaboratoryPart.Hand, definition.Id == "fist" ? 0 : 3, out _);
                    }
            }
            catch (InvalidOperationException error)
            {
                ConfigurationError = error.Message;
                Debug.LogError("实验室配置错误：" + ConfigurationError, this);
            }
            foreach (var part in new[] { LaboratoryPart.Head, LaboratoryPart.Leg, LaboratoryPart.Torso })
                for (int i = 0; i < 3; i++)
                    samples.AddSample(new LaboratoryItem(Guid.NewGuid().ToString("N"), "placeholder_" + part + i,
                        "装配占位", part, part == LaboratoryPart.Torso ? LaboratoryOrganKind.TorsoSpecial : LaboratoryOrganKind.Actuator,
                        "无真实配置，仅验证装卸与容量；不产生战斗效果。"));
        }
        public void SyncTorsoCapacity()
        {
            EnsureInitialized(); torsoSlotCount = Mathf.Max(1, torsoSlotCount);
            if (equipment is TemporaryEquipment samples) samples.ResizeTorso(torsoSlotCount);
        }
        private void Update()
        {
            if (equipment is TemporaryEquipment && equipment.SlotCount(LaboratoryPart.Torso) != Mathf.Max(1, torsoSlotCount)) SyncTorsoCapacity();
        }
    }
}
