using System;
using System.Collections.Generic;
using SpringUp.Laboratory;
using PipelineDefinition = SpringUp.Organs.OrganDefinition;

namespace Tideflesh.Systems
{
    public sealed class RunLaboratoryEquipment : ILaboratoryEquipment
    {
        private static readonly string[] Parts = { "head", "hand", "leg", "torso" };
        private readonly RunController run;
        private readonly IReadOnlyDictionary<string, PipelineDefinition> definitions;
        public event Action Changed;
        public RunLaboratoryEquipment(RunController run, IReadOnlyDictionary<string, PipelineDefinition> definitions)
        { this.run = run; this.definitions = definitions; }
        public IReadOnlyList<LaboratoryItem> Inventory
        {
            get
            {
                var items = new List<LaboratoryItem>();
                foreach (var id in run.Inventory.UnequippedIds()) items.Add(Item(id));
                return items;
            }
        }
        public void Refresh() => Changed?.Invoke();
        public int SlotCount(LaboratoryPart part) => (int)part >= 0 && (int)part < Parts.Length
            ? Tideflesh.Systems.Inventory.SlotCounts[Parts[(int)part]] : 0;
        public LaboratoryItem GetSlot(LaboratoryPart part, int index)
        {
            if (index < 0 || index >= SlotCount(part)) return null;
            var id = run.Inventory.EquippedAt(Parts[(int)part], index + 1);
            return id == null ? null : Item(id);
        }
        private LaboratoryItem Item(string id)
        {
            var definition = run.Inventory.OrganOf(id);
            if (definitions.TryGetValue(definition.id, out var combat)) return new LaboratoryItem(id, combat);
            var part = (LaboratoryPart)Array.IndexOf(Parts, definition.part);
            return new LaboratoryItem(id, definition.id, definition.name, part,
                part == LaboratoryPart.Torso ? LaboratoryOrganKind.TorsoSpecial : LaboratoryOrganKind.Actuator,
                "尚未接入战斗效果。");
        }
        public bool TryEquip(string id, LaboratoryPart part, int index, out string error)
        {
            if (index < 0 || index >= SlotCount(part)) { error = "请选择有效槽位。"; return false; }
            if (!run.Inventory.Instances.TryGetValue(id ?? "", out var item) || !definitions.ContainsKey(item.organ_id))
            { error = "该器官尚未接入战斗。"; return false; }
            return Try(() => run.Replace(id, Parts[(int)part], index + 1), out error);
        }
        public bool TryUnequip(LaboratoryPart part, int index, out string error)
        {
            if (index < 0 || index >= SlotCount(part)) { error = "请选择有效槽位。"; return false; }
            return Try(() => run.Unequip(Parts[(int)part], index + 1), out error);
        }
        public bool TrySell(string id, out string error) => Try(() => run.Sell(id), out error);
        public bool TryDiscard(string id, out string error) => Try(() => run.Discard(id), out error);
        private bool Try(Action action, out string error)
        {
            try { action(); error = null; return true; }
            catch (Exception ex) when (ex is RunException || ex is InventoryException)
            { error = SpringUp.UI.ChineseText.Error(ex.Message); return false; }
        }
    }
}
