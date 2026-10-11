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
        [Tooltip("产出哪种敌人。")]
        [SerializeField] private EnemyArchetype productArchetype = EnemyArchetype.Normal;

        [Tooltip("产出间隔，秒。")]
        [SerializeField] private float produceInterval = 3f;

        [Tooltip("产出点相对自身的位置抖动半径，米。0 表示就在自身位置生成。")]
        [SerializeField] private float spawnJitter = 1.5f;

        [Tooltip("同时存在的产出物上限。超过就暂停产出，避免无限堆积拖垮性能。")]
        [SerializeField] private int maxAliveProducts = 12;

        [Tooltip("产出时在 Console 打一行，便于肉眼验收。")]
        [SerializeField] private bool logProductions = true;

        private float _cooldownRemaining;
        private EnemyRuntime _self;
        private int _producedCount;

        /// <summary>产出间隔，秒。</summary>
        public float ProduceInterval { get { return produceInterval; } set { produceInterval = Mathf.Max(0.1f, value); } }

        /// <summary>产出物上限。</summary>
        public int MaxAliveProducts { get { return maxAliveProducts; } set { maxAliveProducts = Mathf.Max(0, value); } }

        /// <summary>产出哪种敌人。</summary>
        public EnemyArchetype ProductArchetype
        {
            get { return productArchetype; }
            set { productArchetype = value; }
        }

        /// <summary>已产出的总数，供诊断使用。</summary>
        public int ProducedCount { get { return _producedCount; } }

        private void Awake()
        {
            _self = GetComponent<EnemyRuntime>();
            _cooldownRemaining = produceInterval;
        }

        private void Update()
        {
            // 兵工厂自己死了就不该继续产兵。
            if (_self != null && !_self.IsAlive)
                return;

            _cooldownRemaining -= Time.deltaTime;
            if (_cooldownRemaining > 0f)
                return;

            _cooldownRemaining = produceInterval;
            TryProduce();
        }

        /// <summary>产出一个敌人。达到上限或找不到原型时返回 null。</summary>
        public GameObject TryProduce()
        {
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

            var instance = SimplePool.Rent(prototype, prototype.name);
            if (instance == null)
                return null;

            var runtime = instance.GetComponent<EnemyRuntime>();
            if (runtime != null)
                runtime.MarkPooled(prototype.name);

            var offset = spawnJitter > 0.01f
                ? Random.insideUnitCircle * spawnJitter
                : Vector2.zero;
            var origin = (Vector2)transform.position + offset;

            instance.transform.position = new Vector3(origin.x, origin.y, 0f);
            instance.transform.rotation = Quaternion.identity;
            _producedCount++;

            if (logProductions)
            {
                Debug.Log("[EnemyProducer] 产出第 " + _producedCount + " 个 " + productArchetype +
                          "，场上现有 " + EnemyRuntime.ActiveCount + " 个敌人。");
            }

            return instance;
        }
    }
}
