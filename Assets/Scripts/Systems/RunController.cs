using System;
using System.Collections.Generic;

namespace Tideflesh.Systems
{
    public sealed class RunException : Exception
    {
        public RunException(string message) : base(message) { }
    }

    public sealed class RunStartedPayload
    {
        public int Wave;
    }

    public sealed class WaveEndedPayload
    {
        public int Wave;
        public string Reason;
    }

    public sealed class LabOpenedPayload
    {
        public List<OrganInstance> Drops;
        public int MarrowDelta;
    }

    public sealed class RunEndedPayload
    {
        public string Result;
    }

    public sealed class ShopResult
    {
        public bool Ok;
        public string Reason;
    }

    public sealed class RunController
    {
        public delegate void Emit(string name, object payload);

        readonly Dictionary<string, OrganDefinition> _catalog;
        readonly Dictionary<int, WaveDefinition> _waves;
        readonly int _maxWave;
        readonly Emit _emit;

        public Inventory Inventory { get; private set; }
        public string Phase { get; private set; } = "boot";
        public int Wave { get; private set; }
        public int Marrow { get; private set; }

        public RunController(
            Dictionary<string, OrganDefinition> catalog,
            List<WaveDefinition> waves,
            Emit emit)
        {
            _catalog = catalog;
            _waves = new Dictionary<int, WaveDefinition>();
            foreach (var w in waves)
                _waves[w.wave] = w;
            _maxWave = 0;
            foreach (var key in _waves.Keys)
                if (key > _maxWave) _maxWave = key;
            _emit = emit;
            Inventory = new Inventory(catalog);
        }

        public void StartRun()
        {
            if (Phase != "boot")
                throw new RunException("run already started");
            Inventory = new Inventory(_catalog);
            Marrow = 0;
            Wave = 1;
            Inventory.SpawnEquipped("fist", "hand", 1);
            Phase = "combat";
            EmitLoadout();
            _emit("RunStarted", new RunStartedPayload { Wave = Wave });
        }

        public void CombatTimerFinished()
        {
            if (Phase != "combat")
                throw new RunException("timer only valid in combat");
            Phase = "wave_settling";
            _emit("WaveEnded", new WaveEndedPayload { Wave = Wave, Reason = "timer" });
            var cfg = _waves[Wave];
            var drops = new List<string>();
            if (cfg.guaranteed_drops != null)
                drops.AddRange(cfg.guaranteed_drops);
            if (cfg.optional_drops != null)
                drops.AddRange(cfg.optional_drops);
            var spawned = new List<string>();
            foreach (var oid in drops)
                spawned.Add(Inventory.Spawn(oid));
            var gained = (int)Math.Floor(cfg.marrow_reward * Inventory.MarrowGainMult());
            Marrow += gained;
            if (Wave >= _maxWave)
            {
                Phase = "run_won";
                _emit("RunEnded", new RunEndedPayload { Result = "won" });
                return;
            }
            Phase = "lab";
            var payloadDrops = new List<OrganInstance>();
            foreach (var iid in spawned)
            {
                var inst = Inventory.Instances[iid];
                payloadDrops.Add(new OrganInstance
                {
                    instance_id = inst.instance_id,
                    organ_id = inst.organ_id
                });
            }
            _emit("LabOpened", new LabOpenedPayload { Drops = payloadDrops, MarrowDelta = gained });
        }

        public bool CanStartNextWave() => Phase == "lab" && Inventory.UnequippedWithinCap();

        public void StartNextWave()
        {
            if (!CanStartNextWave())
                throw new RunException("cannot start next wave");
            Wave += 1;
            Phase = "combat";
            _emit("RunStarted", new RunStartedPayload { Wave = Wave });
        }

        public void Equip(string instanceId, string part, int slot)
        {
            RequireLab();
            Inventory.Equip(instanceId, part, slot);
            EmitLoadout();
        }

        public void Unequip(string part, int slot)
        {
            RequireLab();
            Inventory.Unequip(part, slot);
            EmitLoadout();
        }

        public void Sell(string instanceId)
        {
            RequireLab();
            Marrow += Inventory.Sell(instanceId);
            EmitLoadout();
        }

        public void Discard(string instanceId)
        {
            RequireLab();
            Inventory.Discard(instanceId);
            EmitLoadout();
        }

        public ShopResult TryBuy(string organId) => new() { Ok = false, Reason = "not_open" };

        public ShopResult TryUpgradeTorso(string nodeId) => new() { Ok = false, Reason = "not_open" };

        void RequireLab()
        {
            if (Phase != "lab")
                throw new RunException("lab actions only in lab");
        }

        void EmitLoadout() => _emit("LoadoutChanged", Inventory.Snapshot());
    }
}
