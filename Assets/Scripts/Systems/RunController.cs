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
        // Informational wave kill total; already credited, never pay this again.
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
        readonly HashSet<string> _activeOrganIds;
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
        readonly HashSet<string> rewardedLives = new HashSet<string>();
        decimal marrowFraction;
        public int WaveKillMarrow { get; private set; }
        public event Action<int> BalanceChanged;
        // A life id is a spawn snapshot, never an object reference that can be reused by a pool.
        public int CreditKill(string lifeId, int baseMarrow)
        {
            TryCreditKill(lifeId, baseMarrow, out int gained);
            return gained;
        }
        public bool TryCreditKill(string lifeId, int baseMarrow, out int gained)
        {
            gained = 0;
            if (Stage != RunPhase.Combat || string.IsNullOrEmpty(lifeId) || baseMarrow <= 0 || !rewardedLives.Add(lifeId)) return false;
            decimal exact = baseMarrow * (decimal)Inventory.MarrowGainMult() + marrowFraction;
            gained = (int)decimal.Floor(exact);
            marrowFraction = exact - gained;
            Marrow += gained;
            WaveKillMarrow += gained;
            BalanceChanged?.Invoke(Marrow);
            return true;
        }
        public int MaxWave => _maxWave;
        public int WaveDuration => _waves[Wave].duration_sec;

        public RunController(
            Dictionary<string, OrganDefinition> catalog,
            List<WaveDefinition> waves,
            Emit emit,
            Func<double> nextUnit = null,
            IEnumerable<string> activeOrganIds = null)
        {
            _catalog = catalog;
            _activeOrganIds = new HashSet<string>(activeOrganIds ?? catalog.Keys);
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
            marrowFraction = 0;
            rewardedLives.Clear();
            WaveKillMarrow = 0;
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
            var drops = ResolveDrops(cfg);
            var spawned = new List<string>();
            foreach (var oid in drops)
                spawned.Add(Inventory.Spawn(oid));
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
            var reward = new LabOpenedPayload { Drops = payloadDrops, MarrowDelta = WaveKillMarrow };
            RestOpened?.Invoke(reward);
            _emit?.Invoke("LabOpened", reward);
        }

        public bool CanStartNextWave() => Stage == RunPhase.Rest && Inventory.UnequippedWithinCap();

        public void StartNextWave()
        {
            if (!CanStartNextWave())
                throw new RunException("cannot start next wave");
            Wave += 1;
            WaveKillMarrow = 0;
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
        public ShopResult TryPurchase(string organId, int? quotedPrice = null)
        {
            if (Stage != RunPhase.Rest) return new ShopResult { Reason = "rest_only" };
            if (!_catalog.TryGetValue(organId, out var item)) return new ShopResult { Reason = "unknown_item" };
            if (Inventory.UnequippedIds().Count >= Inventory.UnequippedCapacity()) return new ShopResult { Reason = "capacity" };
            int price = quotedPrice ?? QuotePurchasePrice(organId);
            if (!PurchasePriceInBand(item.shop_price, price)) return new ShopResult { Reason = "invalid_price" };
            if (price < 0 || Marrow < price) return new ShopResult { Reason = "insufficient_marrow" };
            Inventory.Spawn(organId);
            Marrow -= price;
            EmitLoadout();
            return new ShopResult { Ok = true };
        }

        static bool PurchasePriceInBand(int listPrice, int price)
        {
            int low = EconomySwing.RoundAwayFromZero(listPrice, 0, EconomySwing.PurchaseHalfPercent);
            int high = EconomySwing.RoundAwayFromZero(listPrice, 1, EconomySwing.PurchaseHalfPercent);
            if (low > high) { int swap = low; low = high; high = swap; }
            return price >= low && price <= high;
        }

        public ShopResult TryUpgradeTorso(string nodeId) => new() { Ok = false, Reason = "not_open" };

        List<string> ResolveDrops(WaveDefinition cfg)
        {
            if (cfg.random_drop_pool == null || cfg.random_drop_pool.Length == 0)
            {
                var fixedDrops = new List<string>();
                if (cfg.guaranteed_drops != null) fixedDrops.AddRange(cfg.guaranteed_drops);
                if (cfg.optional_drops != null) fixedDrops.AddRange(cfg.optional_drops);
                return fixedDrops;
            }

            var pool = new List<string>();
            foreach (var id in cfg.random_drop_pool)
            {
                if (string.IsNullOrEmpty(id) || !_catalog.ContainsKey(id) || !_activeOrganIds.Contains(id)) continue;
                if (!pool.Contains(id)) pool.Add(id);
            }
            var count = cfg.random_drop_count > 0 ? cfg.random_drop_count : 1;
            var result = new List<string>();
            while (pool.Count > 0 && result.Count < count)
            {
                var index = RandomIndex(pool.Count);
                result.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return result;
        }

        int RandomIndex(int count)
        {
            if (count <= 1) return 0;
            var unit = _nextUnit();
            if (double.IsNaN(unit) || double.IsNegativeInfinity(unit)) unit = 0;
            if (double.IsPositiveInfinity(unit) || unit >= 1) unit = 0.999999999999;
            if (unit < 0) unit = 0;
            return Math.Min(count - 1, (int)(unit * count));
        }

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
