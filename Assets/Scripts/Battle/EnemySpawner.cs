using System;
using UnityEngine;
using Random = UnityEngine.Random;

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
        /// <summary>找不到 Resources/Enemies 时，灰盒兜底共用的池键。</summary>
        private const string FallbackPoolKey = "Enemy_Greybox";

        [Tooltip("兜底 prefab。Resources/Enemies 里有设计好的怪物时不会用到它。")]
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
        [SerializeField] private Vector2 playAreaCenter;

        public void ConfigurePlayArea(Vector2 center, Vector2 halfExtents)
        {
            playAreaCenter = center;
            playAreaHalfWidth = halfExtents.x; playAreaHalfHeight = halfExtents.y;
            clampInsidePlayArea = true;
        }

        [Tooltip("打印刷怪细节。")]
        [SerializeField] private bool verboseLogging;

        private float _accumulatedSpawnDelay;
        private bool _warnedMissingSprite;
        private bool _reportedPoolUsage;

        /// <summary>本波是否仍在刷怪。水花延迟期间波次结束的话，回调里就不再放敌人出来。</summary>
        private bool _waveActive;
        private int _generation;
        private int _pendingSpawns;
        private float _spawnDuration;
        public int PendingSpawnCount => _pendingSpawns;
        public int SlowAfterCount { get; private set; }
        public int AliveCap => Mathf.Max(1, _mix?.ForWave(_waveNumber)?.aliveCap ?? 16);
        public int OccupiedCount
        {
            get
            {
                int count = _pendingSpawns;
                foreach (var enemy in EnemyRuntime.ActiveEnemies)
                    if (enemy != null && enemy.IsAlive && !enemy.TrainingTarget && enemy.gameObject.scene == gameObject.scene) count++;
                return count;
            }
        }
        public float CurrentIntervalMultiplier => _mix != null
            ? _mix.IntervalMultiplier(_waveNumber, OccupiedCount, SpawnedCount + _pendingSpawns, SlowAfterCount) : 1;
        public float CurrentSpawnInterval => SpawnIntervalSeconds * CurrentIntervalMultiplier;
        public bool CanScheduleSpawn => _waveActive && _spawnElapsed < _spawnDuration
            && (_mix == null || !_mix.dynamicSpawning || OccupiedCount < AliveCap);
        public static EnemySpawner ForScene(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var candidate in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == scene) return candidate;
            return null;
        }
        void OnDisable() => EndWave();

        private int _waveNumber;
        private float _spawnElapsed;
        private EnemyWaveMixSettings _mix;

        /// <summary>当前波次的刷怪间隔，秒。</summary>
        public float SpawnIntervalSeconds
        {
            get { return _mix != null ? Mathf.Max(0.1f, _mix.spawnIntervalSeconds) : 3f; }
        }

        /// <summary>本波已经放出来的敌人数量（诊断用）。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>开始新一波：记下波次号、取权重表、重置计时。</summary>
        public void BeginWave(int waveNumber, float waveDuration)
        {
            _waveActive = true;
            _generation++;
            _pendingSpawns = 0;
            _waveNumber = waveNumber;
            SpawnedCount = 0;
            _spawnElapsed = 0f;
            _accumulatedSpawnDelay = 0f;

            if (_mix == null)
                _mix = EnemyWaveMixSettings.Load() ?? EnemyWaveMixSettings.CreateDefaultInstance();
            _spawnDuration = WaveSpawnSettings.Load().SpawnDuration(waveNumber, waveDuration);
            SlowAfterCount = _mix.SlowAfterCount(waveNumber, _spawnDuration);
        }

        /// <summary>结束本波：停止后续刷怪。</summary>
        public void EndWave()
        {
            _waveActive = false;
            _generation++;
            _pendingSpawns = 0;
            SpawnedCount = 0;
            _accumulatedSpawnDelay = 0f;
        }

        /// <summary>
        /// 每帧推进生成：按当前场上占用比例调整间隔后生成，出哪种按当前波次的权重随机。
        /// <paramref name="spawnDuration"/> 之后不再出怪（前六波 50 秒，第七波整段 60 秒）。
        /// </summary>
        public void Tick(float deltaTime, float spawnDuration)
        {
            if (!_waveActive)
                return;

            if (deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _spawnDuration = spawnDuration;
            _spawnElapsed += deltaTime;
            if (!CanScheduleSpawn)
            {
                _accumulatedSpawnDelay = 0; // No catch-up burst after reaching the cap/cutoff.
                return;
            }
            _accumulatedSpawnDelay += deltaTime;
            if (_accumulatedSpawnDelay >= CurrentSpawnInterval)
            {
                _accumulatedSpawnDelay = 0;
                SpawnOne();
            }
        }

        /// <summary>统一预约出生名额：主刷怪器和兵工厂共享上限及累计额度。</summary>
        public bool TryScheduleSpawn(Vector2 position, Func<bool> spawn)
        {
            if (!CanScheduleSpawn || spawn == null) return false;
            int generation = _generation;
            _pendingSpawns++;
            void Complete()
            {
                if (this == null || generation != _generation) return;
                _pendingSpawns--;
                if (_waveActive && spawn()) SpawnedCount++;
            }
            if (!EnemySpawnEffect.TryPlay(position, Complete)) Complete();
            return true;
        }

        /// <summary>立刻预约一个敌人，仍遵守上限与生成截止时间。</summary>
        public bool SpawnOne()
        {
            var position = PickSpawnPosition();
            return TryScheduleSpawn(position, () => SpawnAt(position));
        }

        /// <summary>真正把敌人放到场上。水花播完后由回调调用。</summary>
        private bool SpawnAt(Vector2 position)
        {
            var enemy = RentEnemy(out var poolKey);
            if (enemy == null)
                return false;

            enemy.transform.position = new Vector3(position.x, position.y, 0f);
            enemy.transform.rotation = Quaternion.identity;

            if (verboseLogging && !_reportedPoolUsage)
            {
                _reportedPoolUsage = true;
                Debug.Log("[EnemySpawner] 首次刷怪完成。池中现有 " + SimplePool.CountInPool(poolKey) +
                          " 个待复用实例——数字大于 0 说明复用生效，不再是每次新建。", this);
            }

            return true;
        }

        /// <summary>参与权重抽取的类型顺序。只影响随机时的遍历次序，不影响概率。</summary>
        private static readonly EnemyArchetype[] MixOrder =
        {
            EnemyArchetype.Normal,
            EnemyArchetype.Fast,
            EnemyArchetype.RangedKeeper,
            EnemyArchetype.RangedChaser,
            EnemyArchetype.Medic,
            EnemyArchetype.Arsenal
        };

        /// <summary>
        /// 取一个敌人实例。类型按当前波次的权重随机；兵工厂 / 医疗兵达到同时存活上限时排除。
        /// 没有权重表（灰盒场景）或 Resources 里没有任何设计 prefab 时退回灰盒原型。
        /// </summary>
        private GameObject RentEnemy(out string poolKey)
        {
            poolKey = FallbackPoolKey;

            if (_mix != null)
            {
                if (!TryPickArchetype(out var archetype))
                    return null;    // 该类型都被上限挡住：这一拍不刷

                var prototype = EnemyPrefabLibrary.Get(archetype);
                if (prototype != null)
                {
                    poolKey = "Enemy_" + archetype;
                    var instance = SimplePool.Rent(prototype, poolKey);
                    if (instance != null)
                    {
                        var runtime = instance.GetComponent<EnemyRuntime>();
                        if (runtime != null)
                            runtime.MarkPooled(poolKey);
                        return instance;
                    }
                }

                if (LibraryHasAnyPrefab())
                    return null;
            }

            var fallback = enemyPrefab != null ? enemyPrefab : GetOrCreateGreyboxTemplate();
            if (fallback == null)
                return null;

            var fallbackInstance = SimplePool.Rent(fallback, FallbackPoolKey);
            if (fallbackInstance == null)
                return null;

            var fallbackRuntime = fallbackInstance.GetComponent<EnemyRuntime>();
            if (fallbackRuntime != null)
                fallbackRuntime.MarkPooled(FallbackPoolKey);

            return fallbackInstance;
        }

        /// <summary>按权重随机抽一种当前可用（未达存活上限）的类型。</summary>
        private bool TryPickArchetype(out EnemyArchetype archetype)
        {
            archetype = EnemyArchetype.Normal;

            var mix = _mix != null ? _mix.ForWave(_waveNumber) : null;
            if (mix == null)
                return false;

            var types = new EnemyArchetype[MixOrder.Length];
            var weights = new int[MixOrder.Length];
            var candidateCount = 0;
            var totalWeight = 0;

            for (var i = 0; i < MixOrder.Length; i++)
            {
                var type = MixOrder[i];
                var weight = mix.WeightOf(type);
                if (weight <= 0)
                    continue;

                if (EnemyPrefabLibrary.Get(type) == null)
                    continue;

                if (AtAliveCap(type))
                    continue;

                types[candidateCount] = type;
                weights[candidateCount] = weight;
                candidateCount++;
                totalWeight += weight;
            }

            if (candidateCount == 0 || totalWeight <= 0)
                return false;

            var roll = Random.Range(0, totalWeight);
            for (var i = 0; i < candidateCount; i++)
            {
                roll -= weights[i];
                if (roll < 0)
                {
                    archetype = types[i];
                    return true;
                }
            }

            archetype = types[candidateCount - 1];
            return true;
        }

        /// <summary>该类型是否已经达到「场上同时存活」上限。</summary>
        private bool AtAliveCap(EnemyArchetype archetype)
        {
            var cap = _mix != null ? _mix.AliveCapFor(archetype) : 0;
            if (cap <= 0)
                return false;

            return CountAlive(archetype) >= cap;
        }

        private static int CountAlive(EnemyArchetype archetype)
        {
            var alive = EnemyRuntime.ActiveEnemies;
            var count = 0;
            for (var i = 0; i < alive.Count; i++)
            {
                var enemy = alive[i];
                if (enemy == null || enemy.Data == null)
                    continue;

                if (enemy.Data.Definition.archetype == archetype)
                    count++;
            }

            return count;
        }

        /// <summary>Resources/Enemies 里是否至少有一种设计 prefab。</summary>
        private static bool LibraryHasAnyPrefab()
        {
            var archetypes = EnemyArchetypeConfig.SpawnableArchetypes();
            if (archetypes == null)
                return false;

            for (var i = 0; i < archetypes.Length; i++)
            {
                if (EnemyPrefabLibrary.Get(archetypes[i]) != null)
                    return true;
            }

            return false;
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

            var availableX = Mathf.Max(0f, playAreaHalfWidth - playAreaMargin);
            var availableY = Mathf.Max(0f, playAreaHalfHeight - playAreaMargin);

            var roomX = Mathf.Max(0f, availableX - Mathf.Abs(center.x - playAreaCenter.x));
            var roomY = Mathf.Max(0f, availableY - Mathf.Abs(center.y - playAreaCenter.y));
            return Mathf.Min(roomX, roomY);
        }

        private Vector2 ClampToPlayArea(Vector2 position)
        {
            if (!clampInsidePlayArea)
                return position;

            var availableX = Mathf.Max(0f, playAreaHalfWidth - playAreaMargin);
            var availableY = Mathf.Max(0f, playAreaHalfHeight - playAreaMargin);

            return new Vector2(
                Mathf.Clamp(position.x, playAreaCenter.x - availableX, playAreaCenter.x + availableX),
                Mathf.Clamp(position.y, playAreaCenter.y - availableY, playAreaCenter.y + availableY));
        }
    }
}
