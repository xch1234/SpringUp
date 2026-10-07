using System;
using System.Collections.Generic;

namespace Tideflesh.Systems
{
    public sealed class InventoryException : Exception
    {
        public InventoryException(string message) : base(message) { }
    }

    public sealed class OrganInstance
    {
        public string instance_id;
        public string organ_id;
    }

    public sealed class InventorySnapshot
    {
        public Dictionary<string, Dictionary<int, string>> Equipped = new();
        public List<OrganInstance> Unequipped = new();
    }

    public sealed class Inventory
    {
        public static readonly Dictionary<string, int> SlotCounts = new()
        {
            { "head", 6 }, { "hand", 6 }, { "leg", 6 }, { "torso", 2 }
        };

        const int DefaultUnequippedCap = 5;

        public Dictionary<string, OrganDefinition> Catalog { get; }
        public Dictionary<string, OrganInstance> Instances { get; } = new();

        readonly List<string> _unequipped = new();
        readonly Dictionary<string, string[]> _equipped = new();
        int _next = 1;

        public Inventory(Dictionary<string, OrganDefinition> catalog)
        {
            Catalog = catalog;
            foreach (var kv in SlotCounts)
                _equipped[kv.Key] = new string[kv.Value];
        }

        public List<string> UnequippedIds() => new(_unequipped);

        public string Spawn(string organId)
        {
            if (!Catalog.ContainsKey(organId))
                throw new InventoryException($"unknown organ {organId}");
            var iid = $"inst_{_next++}";
            Instances[iid] = new OrganInstance { instance_id = iid, organ_id = organId };
            _unequipped.Add(iid);
            return iid;
        }

        public string SpawnEquipped(string organId, string part, int slot)
        {
            var iid = Spawn(organId);
            try
            {
                if (OrganOf(iid).part != part)
                    throw new InventoryException("part mismatch");
                _unequipped.Remove(iid);
                Place(iid, part, slot);
            }
            catch (InventoryException)
            {
                _unequipped.Remove(iid);
                Instances.Remove(iid);
                throw;
            }
            return iid;
        }

        public OrganDefinition OrganOf(string instanceId)
        {
            if (!Instances.TryGetValue(instanceId, out var inst))
                throw new InventoryException($"unknown instance {instanceId}");
            return Catalog[inst.organ_id];
        }

        public string EquippedAt(string part, int slot)
        {
            var idx = SlotIndex(part, slot);
            return _equipped[part][idx];
        }

        public int UnequippedCapacity()
        {
            var cap = DefaultUnequippedCap;
            foreach (var organ in EquippedOrgans())
            {
                if (organ.type == "torso_special")
                    cap += organ.UnequippedCapacityDelta;
            }
            return cap;
        }

        public float MarrowGainMult()
        {
            var mult = 1f;
            foreach (var organ in EquippedOrgans())
            {
                if (organ.type == "torso_special")
                    mult *= organ.MarrowGainMult;
            }
            return mult;
        }

        public bool UnequippedWithinCap() => _unequipped.Count <= UnequippedCapacity();

        public void Equip(string instanceId, string part, int slot)
        {
            if (!_unequipped.Contains(instanceId))
                throw new InventoryException("equip only from unequipped");
            if (OrganOf(instanceId).part != part)
                throw new InventoryException("part mismatch");
            Place(instanceId, part, slot);
            _unequipped.Remove(instanceId);
        }

        public string Unequip(string part, int slot)
        {
            var idx = SlotIndex(part, slot);
            var iid = _equipped[part][idx];
            if (iid == null)
                throw new InventoryException("empty slot");
            _equipped[part][idx] = null;
            _unequipped.Add(iid);
            return iid;
        }

        public int Sell(string instanceId)
        {
            var organ = OrganOf(instanceId);
            Remove(instanceId);
            return organ.shop_price / 2;
        }

        public void Discard(string instanceId) => Remove(instanceId);

        public InventorySnapshot Snapshot()
        {
            var snap = new InventorySnapshot();
            foreach (var kv in _equipped)
            {
                Dictionary<int, string> filled = null;
                for (var i = 0; i < kv.Value.Length; i++)
                {
                    if (kv.Value[i] == null) continue;
                    filled ??= new Dictionary<int, string>();
                    filled[i + 1] = kv.Value[i];
                }
                if (filled != null)
                    snap.Equipped[kv.Key] = filled;
            }
            foreach (var iid in _unequipped)
            {
                var inst = Instances[iid];
                snap.Unequipped.Add(new OrganInstance
                {
                    instance_id = inst.instance_id,
                    organ_id = inst.organ_id
                });
            }
            return snap;
        }

        int SlotIndex(string part, int slot)
        {
            if (!_equipped.ContainsKey(part))
                throw new InventoryException("invalid part");
            if (slot < 1 || slot > _equipped[part].Length)
                throw new InventoryException("invalid slot");
            return slot - 1;
        }

        void Place(string instanceId, string part, int slot)
        {
            var idx = SlotIndex(part, slot);
            if (_equipped[part][idx] != null)
                throw new InventoryException("slot occupied");
            _equipped[part][idx] = instanceId;
        }

        void Remove(string instanceId)
        {
            if (_unequipped.Remove(instanceId))
            {
                Instances.Remove(instanceId);
                return;
            }
            var found = false;
            foreach (var kv in _equipped)
            {
                for (var i = 0; i < kv.Value.Length; i++)
                {
                    if (kv.Value[i] != instanceId) continue;
                    kv.Value[i] = null;
                    found = true;
                }
            }
            if (!found)
                throw new InventoryException($"unknown instance {instanceId}");
            Instances.Remove(instanceId);
        }

        List<OrganDefinition> EquippedOrgans()
        {
            var organs = new List<OrganDefinition>();
            foreach (var slots in _equipped.Values)
            {
                foreach (var iid in slots)
                {
                    if (iid != null)
                        organs.Add(OrganOf(iid));
                }
            }
            return organs;
        }
    }
}
