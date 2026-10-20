using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 产兵：按间隔在自己的位置周围持续生成指定敌人。兵工厂用它不断产生普通敌人。
    ///
    /// **为什么不做成"兵工厂专用"**：产兵是通用能力（未来的巢穴、召唤物、Boss 分阶段
    /// 都可能要），做成一个带"产出哪种敌人"配置的通用组件，比写死成兵工厂更省事。
    ///
    /// **产出的敌人也走对象池**：与 <see cref="EnemySpawner"/> 同一套
    /// <see cref="SimplePool"/>，不额外新建实例。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/产兵（EnemyProducer）")]
    public class EnemyProducer : MonoBehaviour
    {
        private EnemyArchetype productArchetype => GetComponent<EnemyData>().Definition.productArchetype;
        private float produceInterval => GetComponent<EnemyData>().Definition.produceInterval;
        private float spawnJitter => GetComponent<EnemyData>().Definition.spawnJitter;
        private int maxAliveProducts => GetComponent<EnemyData>().Definition.maxAliveProducts;

        [Tooltip("产出时在 Console 打一行，便于肉眼验收。")]
        [SerializeField] private bool logProductions = true;

        private float _cooldownRemaining;
        private EnemyRuntime _self;
        private int _producedCount;
        private EnemySpawner _spawner;
        private EnemySpawner Spawner
        {
            get
            {
                if (_spawner == null) _spawner = EnemySpawner.ForScene(gameObject.scene);
                return _spawner;
            }
        }

        /// <summary>产出间隔，秒。</summary>
        public float ProduceInterval { get { return produceInterval; } }

        /// <summary>产出物上限。</summary>
        public int MaxAliveProducts { get { return maxAliveProducts; } }

        /// <summary>产出哪种敌人。</summary>
        public EnemyArchetype ProductArchetype
        {
            get { return productArchetype; }
        }

        /// <summary>已产出的总数，供诊断使用。</summary>
        public int ProducedCount { get { return _producedCount; } }

        private void Awake()
        {
            _self = GetComponent<EnemyRuntime>();
            _cooldownRemaining = produceInterval;
        }

        private void OnEnable() { _cooldownRemaining = produceInterval; _producedCount = 0; }

        private void Update()
        {
            // A ready cooldown / overlapping projectile must not resolve while the world is paused.
            if (Time.timeScale <= 0f) return;
            // 兵工厂自己死了就不该继续产兵。
            if (_self != null && !_self.IsAlive)
                return;

            var spawner = Spawner;
            if (spawner != null && !spawner.CanScheduleSpawn) { _cooldownRemaining = produceInterval; return; }
            _cooldownRemaining -= Time.deltaTime / (spawner != null ? spawner.CurrentIntervalMultiplier : 1f);
            if (_cooldownRemaining > 0f)
                return;

            _cooldownRemaining = produceInterval;
            TryProduce();
        }

        /// <summary>产出一个敌人。达到上限或找不到原型时返回 null。</summary>
        public GameObject TryProduce()
        {
            if (!isActiveAndEnabled || !GetComponent<EnemyData>().Definition.hasProducer || (_self != null && !_self.IsAlive)) return null;
            if (EnemyRuntime.ActiveCount >= maxAliveProducts + 1)
            {
                // +1 是把自己算进去：上限说的是"产出物"数量，自身不该占用额度。
                if (logProductions)
                    Debug.Log("[EnemyProducer] 场上敌人已达上限 " + maxAliveProducts + "，暂停产出。");
                return null;
            }

            var prototype = EnemyPrefabLibrary.Get(productArchetype);
            if (prototype == null)
            {
                Debug.LogWarning("[EnemyProducer] 找不到 " + productArchetype + " 的 prefab，无法产出。");
                return null;
            }

            var offset = spawnJitter > 0.01f
                ? Random.insideUnitCircle * spawnJitter
                : Vector2.zero;
            var origin = (Vector2)transform.position + offset;
            var player = PlayerLocator.Player;
            var map = player != null ? player.GetComponentInParent<BattleMapLayout>() : null;
            if (map != null) origin = map.ClampInside(origin);

            var spawner = Spawner;
            if (spawner != null)
            {
                string producerLife = _self != null ? _self.TargetId : null;
                spawner.TryScheduleSpawn(origin, () =>
                {
                    if (this == null || !isActiveAndEnabled || _self == null || !_self.IsAlive || _self.TargetId != producerLife) return false;
                    var product = SpawnAt(prototype, origin);
                    if (product == null) return false;
                    _producedCount++;
                    return true;
                });
                return null;
            }
            _producedCount++;

            if (logProductions)
            {
                Debug.Log("[EnemyProducer] 产出第 " + _producedCount + " 个 " + productArchetype +
                          "（生成水花播完后出现），场上现有 " + EnemyRuntime.ActiveCount + " 个敌人。");
            }

            // 与主刷怪一致：先放水花，播完再把敌人放出来；产兵者若在水花期间死了，就不再产出。
            if (EnemySpawnEffect.TryPlay(origin, () =>
            {
                if (_self == null || _self.IsAlive)
                    SpawnAt(prototype, origin);
            }))
                return null;

            return SpawnAt(prototype, origin);
        }

        /// <summary>真正把产出物放到场上。水花播完后由回调调用。</summary>
        private GameObject SpawnAt(GameObject prototype, Vector2 origin)
        {
            var instance = SimplePool.Rent(prototype, prototype.name);
            if (instance == null)
                return null;

            var runtime = instance.GetComponent<EnemyRuntime>();
            if (runtime != null)
                runtime.MarkPooled(prototype.name);

            Spawner?.InitializeSpawnedEnemy(instance);
            instance.transform.position = new Vector3(origin.x, origin.y, 0f);
            instance.transform.rotation = Quaternion.identity;
            return instance;
        }
    }
}
