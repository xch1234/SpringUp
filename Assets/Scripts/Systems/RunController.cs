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

    public enum RunPhase { Boot, Combat, Settling, Rest, Won, Lost }

    public sealed class RunController
    {
        public delegate void Emit(string name, object payload);

        static readonly Random SharedRandom = new Random();

        readonly Dictionary<string, OrganDefinition> _catalog;
        readonly Dictionary<int, WaveDefinition> _waves;
        readonly int _maxWave;
        readonly Emit _emit;
        readonly Func<double> _nextUnit;

        public Inventory Inventory { get; private set; }
        public RunPhase Stage { get; private set; } = RunPhase.Boot;
        // Compatibility adapter for existing demos; new consumers use Stage/events.
        public string Phase => Stage switch { RunPhase.Boot => "boot", RunPhase.Combat => "combat",
            RunPhase.Settling => "wave_settling", RunPhase.Rest => "lab", RunPhase.Won => "run_won", _ => "run_lost" };
        public event Action<InventorySnapshot> LoadoutChanged;
        public event Action<int> WaveStarted;
        public event Action<int> WaveFinished;
        public event Action<LabOpenedPayload> RestOpened;
        public event Action<RunPhase> Ended;
        public int Wave { get; private set; }
        public int Marrow { get; private set; }
        public int MaxWave => _maxWave;
        public int WaveDuration => _waves[Wave].duration_sec;

        public RunController(
            Dictionary<string, OrganDefinition> catalog,
            List<WaveDefinition> waves,
            Emit emit,
            Func<double> nextUnit = null)
        {
            _catalog = catalog;
            _waves = new Dictionary<int, WaveDefinition>();
            foreach (var w in waves)
                _waves[w.wave] = w;
            _maxWave = 0;
            foreach (var key in _waves.Keys)
                if (key > _maxWave) _maxWave = key;
            _emit = emit;
            _nextUnit = nextUnit ?? NextSharedUnit;
            Inventory = new Inventory(catalog);
        }

        static double NextSharedUnit()
        {
            lock (SharedRandom)
                return SharedRandom.NextDouble();
        }

        public void StartRun()
        {
            if (Stage != RunPhase.Boot)
                throw new RunException("run already started");
            Inventory = new Inventory(_catalog);
            Marrow = 0;
            Wave = 1;
            Inventory.SpawnEquipped("fist", "hand", 1);
            Stage = RunPhase.Combat;
            EmitLoadout();
            WaveStarted?.Invoke(Wave);
            _emit?.Invoke("RunStarted", new RunStartedPayload { Wave = Wave });
        }

        public void CombatTimerFinished()
        {
            if (Stage != RunPhase.Combat)
                throw new RunException("timer only valid in combat");
            Stage = RunPhase.Settling;
            WaveFinished?.Invoke(Wave);
            _emit?.Invoke("WaveEnded", new WaveEndedPayload { Wave = Wave, Reason = "timer" });
            var cfg = _waves[Wave];
            var drops = new List<string>();
            if (cfg.guaranteed_drops != null)
                drops.AddRange(cfg.guaranteed_drops);
            if (cfg.optional_drops != null)
                drops.AddRange(cfg.optional_drops);
            var spawned = new List<string>();
            foreach (var oid in drops)
                spawned.Add(Inventory.Spawn(oid));
            var gained = EconomySwing.Floor(
                cfg.marrow_reward * (double)Inventory.MarrowGainMult(),
                _nextUnit(),
                EconomySwing.MarrowHalfPercent);
            Marrow += gained;
            if (Wave >= _maxWave)
            {
                Stage = RunPhase.Won;
                Ended?.Invoke(Stage);
                _emit?.Invoke("RunEnded", new RunEndedPayload { Result = "won" });
                return;
            }
            Stage = RunPhase.Rest;
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
            var reward = new LabOpenedPayload { Drops = payloadDrops, MarrowDelta = gained };
            RestOpened?.Invoke(reward);
            _emit?.Invoke("LabOpened", reward);
        }

        public bool CanStartNextWave() => Stage == RunPhase.Rest && Inventory.UnequippedWithinCap();

        public void StartNextWave()
        {
            if (!CanStartNextWave())
                throw new RunException("cannot start next wave");
            Wave += 1;
            Stage = RunPhase.Combat;
            WaveStarted?.Invoke(Wave);
            _emit?.Invoke("RunStarted", new RunStartedPayload { Wave = Wave });
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

        public void Replace(string instanceId, string part, int slot)
        {
            RequireLab();
            Inventory.Replace(instanceId, part, slot);
            EmitLoadout();
        }

        public void LoseRun()
        {
            if (Stage != RunPhase.Combat && Stage != RunPhase.Rest) return;
            Stage = RunPhase.Lost;
            Ended?.Invoke(Stage);
            _emit?.Invoke("RunEnded", new RunEndedPayload { Result = "lost" });
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

        public int QuotePurchasePrice(string organId)
        {
            if (!_catalog.TryGetValue(organId, out var organ))
                throw new RunException("unknown organ");
            return EconomySwing.RoundAwayFromZero(
                organ.shop_price, _nextUnit(), EconomySwing.PurchaseHalfPercent);
        }

        public ShopResult TryBuy(string organId) => new() { Ok = false, Reason = "not_open" };

        // Opt-in transaction for the new flow. Keep the legacy shop placeholder compatible.
        public ShopResult TryPurchase(string organId)
        {
            if (Stage != RunPhase.Rest) return new ShopResult { Reason = "rest_only" };
            if (!_catalog.TryGetValue(organId, out var item)) return new ShopResult { Reason = "unknown_item" };
            if (Inventory.UnequippedIds().Count >= Inventory.UnequippedCapacity()) return new ShopResult { Reason = "capacity" };
            if (item.shop_price < 0 || Marrow < item.shop_price) return new ShopResult { Reason = "insufficient_marrow" };
            Inventory.Spawn(organId);
            Marrow -= item.shop_price;
            EmitLoadout();
            return new ShopResult { Ok = true };
        }

        public ShopResult TryUpgradeTorso(string nodeId) => new() { Ok = false, Reason = "not_open" };

        void RequireLab()
        {
            if (Stage != RunPhase.Rest)
                throw new RunException("lab actions only in lab");
        }

        void EmitLoadout()
        {
            var snapshot = Inventory.Snapshot();
            LoadoutChanged?.Invoke(snapshot);
            _emit?.Invoke("LoadoutChanged", snapshot);
        }
    }
}
