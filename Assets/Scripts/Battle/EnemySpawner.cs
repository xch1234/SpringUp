using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 刷怪器：在玩家周围生成敌人，并保证不在玩家脸上冒出来。
    ///
    /// **两条生成规则**（后者是策划定的）：
    /// 1. **不许生成在玩家最近的屏幕 1/4 范围内**——设屏幕宽高为 x、y，
    ///    则距离小于 x/4、y/4 的位置一律不生成，避免"凭空出现在脸上"。
    ///    屏幕范围允许生成（类幸存者里完全允许），只要离玩家够远。
    /// 2. 生成点必须落在**可通行区域**内，且半径受"当前中心到最近边界的距离"限制，
    ///    否则玩家在角落附近时环上的点会落到边界墙外——
    ///    早期版本就因此把敌人**生成到了墙外，永远进不来**。
    ///
    /// **实例复用**：敌人经 <see cref="SimplePool"/> 取用与归还，
    /// 不再每次 <c>new GameObject</c> + <c>Destroy</c>。一局几百个敌人逐次创建
    /// 会造成 GC 抖动。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/刷怪器（EnemySpawner）")]
    public class EnemySpawner : MonoBehaviour
    {
        /// <summary>池键。灰盒敌人共用一份原型与一个池。</summary>
        private const string PoolKey = "Enemy_Greybox";

        [Tooltip("敌人 prefab。留空则用 EnemyFactory 现场造灰盒敌人（能跑，无需美术资源）。")]
        [SerializeField] private GameObject enemyPrefab;

        [Tooltip("灰盒敌人用到的精灵。留空则用 1×1 白图。")]
        [SerializeField] private Sprite enemySprite;

        [Tooltip("灰盒敌人颜色。")]
        [SerializeField] private Color enemyColor = new Color(0.85f, 0.30f, 0.35f, 1f);

        [Tooltip("以玩家为圆心的生成环半径，米。")]
        [SerializeField] private float spawnRadius = 10f;

        [Tooltip("半径抖动范围，避免所有敌人从同一个圆上出来。")]
        [SerializeField] private float radiusJitter = 3f;

        [Header("距玩家最小距离（按屏幕尺寸算）")]
        [Tooltip("最小生成距离 = 屏幕半宽 × (1/2 - 此比例)。0.25 表示不许落在屏幕 1/4 范围内。")]
        [SerializeField] private float minDistanceScreenRatio = 0.25f;

        [Tooltip("找不到相机时的兜底最小生成距离，米。")]
        [SerializeField] private float fallbackMinDistance = 5f;

        [Tooltip("反复取点仍不满足最小距离时的重试次数。")]
        [SerializeField] private int positionRetryCount = 24;

        [Header("可通行区域限制")]
        [Tooltip("把生成点夹进可通行区域，避免敌人生成在边界墙之外。")]
        [SerializeField] private bool clampInsidePlayArea = true;

        [Tooltip("夹取时给边界留的余量，米。")]
        [SerializeField] private float playAreaMargin = 1f;

        [Tooltip("可通行区域半宽，米。由场景构建器接线，须与边界墙一致。")]
        [SerializeField] private float playAreaHalfWidth = 15f;

        [Tooltip("可通行区域半高，米。由场景构建器接线，须与边界墙一致。")]
        [SerializeField] private float playAreaHalfHeight = 9f;

        [Tooltip("打印刷怪细节。")]
        [SerializeField] private bool verboseLogging;

        private float _accumulatedSpawnDelay;
        private bool _warnedMissingSprite;
        private bool _reportedPoolUsage;

        /// <summary>本波预期生成的敌人总数，由 <see cref="WaveDirector"/> 设定。</summary>
        public int PlannedSpawnCount { get; private set; }

        /// <summary>本波已生成的敌人数量。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>是否还有没生成完的敌人。</summary>
        public bool HasPendingSpawns { get { return SpawnedCount < PlannedSpawnCount; } }

        /// <summary>开始新一波：重置计数。</summary>
        public void BeginWave(int plannedCount, float waveDuration)
        {
            PlannedSpawnCount = Mathf.Max(0, plannedCount);
            SpawnedCount = 0;
            _accumulatedSpawnDelay = 0f;
        }

        /// <summary>结束本波：丢弃未使用的生成计划。</summary>
        public void EndWave()
        {
            PlannedSpawnCount = 0;
            SpawnedCount = 0;
            _accumulatedSpawnDelay = 0f;
        }

        /// <summary>
        /// 每帧推进生成。把「波时长 ÷ 目标数量」作为间隔在整波内均匀铺开，
        /// 这样一波的敌人不会全挤在开头。
        /// </summary>
        public void Tick(float deltaTime, float waveDuration)
        {
            if (!HasPendingSpawns)
                return;

            var interval = waveDuration / PlannedSpawnCount;
            _accumulatedSpawnDelay += deltaTime;

            while (_accumulatedSpawnDelay >= interval && HasPendingSpawns)
            {
                _accumulatedSpawnDelay -= interval;
                SpawnOne();
            }
        }

        /// <summary>立刻生成一个敌人。手动测试也可直接调。</summary>
        public bool SpawnOne()
        {
            var position = PickSpawnPosition();

            var enemy = RentEnemy();
            if (enemy == null)
                return false;

            enemy.transform.position = new Vector3(position.x, position.y, 0f);
            enemy.transform.rotation = Quaternion.identity;
            SpawnedCount++;

            if (verboseLogging && !_reportedPoolUsage)
            {
                _reportedPoolUsage = true;
                Debug.Log("[EnemySpawner] 首次刷怪完成。池中现有 " + SimplePool.CountInPool(PoolKey) +
                          " 个待复用实例——数字大于 0 说明复用生效，不再是每次新建。", this);
            }

            return true;
        }

        /// <summary>取一个敌人实例：优先用 prefab，否则用灰盒原型；都经池分发。</summary>
        private GameObject RentEnemy()
        {
            var prototype = enemyPrefab != null ? enemyPrefab : GetOrCreateGreyboxTemplate();
            if (prototype == null)
                return null;

            var instance = SimplePool.Rent(prototype, PoolKey);
            if (instance == null)
                return null;

            // 标记为池化对象，退场时才会归还池而不是销毁。
            var runtime = instance.GetComponent<EnemyRuntime>();
            if (runtime != null)
                runtime.MarkPooled(PoolKey);

            return instance;
        }

        /// <summary>
        /// 灰盒敌人原型：只在首次刷怪时创建一次，之后一直作为池模板复用。
        /// 创建后立即停用——否则它会以"场上敌人"的身份出现在活列表里。
        /// </summary>
        private GameObject GetOrCreateGreyboxTemplate()
        {
            if (enemySprite == null && !_warnedMissingSprite)
            {
                _warnedMissingSprite = true;
                Debug.LogWarning("[EnemySpawner] enemySprite 未赋值，将使用 GreyboxAssets 的共用方块。" +
                                 "若敌人看起来过小或不可见，请先检查这里。", this);
            }

            var template = EnemyFactory.CreateGreyboxEnemy("Enemy_Greybox", enemySprite, enemyColor);
            template.SetActive(false);
            return template;
        }

        /// <summary>
        /// 取一个合法生成点。规则见类注释：不许落在玩家最近的屏幕 1/4 范围内，
        /// 且必须落在可通行区域内。
        /// </summary>
        private Vector2 PickSpawnPosition()
        {
            var center = PlayerLocator.HasPlayer
                ? PlayerLocator.PlayerPosition
                : (Vector2)transform.position;

            var minDistance = GetMinSpawnDistance();

            for (var attempt = 0; attempt < Mathf.Max(1, positionRetryCount); attempt++)
            {
                var candidate = PickCandidateInRing(center);
                if (Vector2.Distance(candidate, center) >= minDistance)
                    return candidate;
            }

            // 兜底：所有取点都被最小距离否掉时，沿随机方向推到最小距离处，
            // 再夹进可通行区域。宁可贴边界也不要生成在玩家脸上。
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var pushed = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * minDistance;
            return ClampToPlayArea(pushed);
        }

        private Vector2 PickCandidateInRing(Vector2 center)
        {
            var maxRadius = GetMaxRadiusFrom(center);
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var radius = Mathf.Max(1f, spawnRadius + Random.Range(-radiusJitter, radiusJitter));

            if (maxRadius < float.MaxValue)
                radius = Mathf.Min(radius, Mathf.Max(0.5f, maxRadius));

            var candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            return ClampToPlayArea(candidate);
        }

        /// <summary>
        /// 最小生成距离（米）。
        /// 策划规则：屏幕宽高为 x、y 时，不许生成在距玩家 x/4、y/4 的范围内。
        /// 取两轴中更严格的那个（即屏幕半宽的一半）。
        /// </summary>
        private float GetMinSpawnDistance()
        {
            var camera = Camera.main;
            if (camera == null || !camera.orthographic)
                return fallbackMinDistance;

            var halfHeight = camera.orthographicSize;
            var halfWidth = halfHeight * camera.aspect;

            // 半宽的一半 = 全宽的 1/4；半高同理。
            var ratio = Mathf.Clamp(minDistanceScreenRatio, 0f, 0.9f);
            var limitX = halfWidth * ratio * 2f;
            var limitY = halfHeight * ratio * 2f;

            return Mathf.Max(0.5f, Mathf.Min(limitX, limitY));
        }

        /// <summary>从给定中心出发，环半径的上限（不越过可通行区域）。</summary>
        private float GetMaxRadiusFrom(Vector2 center)
        {
            if (!clampInsidePlayArea)
                return float.MaxValue;

            var availableX = Mathf.Max(0.5f, playAreaHalfWidth - playAreaMargin);
            var availableY = Mathf.Max(0.5f, playAreaHalfHeight - playAreaMargin);

            var roomX = Mathf.Max(0f, availableX - Mathf.Abs(center.x));
            var roomY = Mathf.Max(0f, availableY - Mathf.Abs(center.y));
            return Mathf.Min(roomX, roomY);
        }

        private Vector2 ClampToPlayArea(Vector2 position)
        {
            if (!clampInsidePlayArea)
                return position;

            var availableX = Mathf.Max(0.5f, playAreaHalfWidth - playAreaMargin);
            var availableY = Mathf.Max(0.5f, playAreaHalfHeight - playAreaMargin);

            return new Vector2(
                Mathf.Clamp(position.x, -availableX, availableX),
                Mathf.Clamp(position.y, -availableY, availableY));
        }
    }
}
