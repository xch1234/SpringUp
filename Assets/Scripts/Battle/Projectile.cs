using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 灰盒弹体：按**出射时确定的方向**直飞，命中或超时后归还池。
    ///
    /// **弹道一旦出射就不再修改**——这是刻意的：
    /// 早期版本每帧把方向重算成"指向当前玩家位置"，于是弹体变成追踪弹，
    /// 玩家无法靠走位躲开，战斗就没有可读性可言。
    /// 命中判定与弹道解耦：弹体只走直线，用"是否越过目标"（而不是"离目标多远"）判断命中——
    /// 后者在弹体擦过目标后会因为距离再次变大而永远不命中。
    ///
    /// **实例复用**：不再每次 <c>new GameObject</c> + <c>Destroy</c>，改为经
    /// <see cref="SimplePool"/> 取用与归还。类幸存者一局几百上千发子弹，
    /// 逐发创建销毁会造成 GC 抖动。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/灰盒/弹体（Projectile）")]
    public class Projectile : MonoBehaviour
    {
        /// <summary>池键。弹体共用一份原型与一个池。</summary>
        private const string PoolKey = "Projectile_Greybox";

        /// <summary>灰盒弹体 prefab 的存放路径。
        /// **必须放在 Resources 下**：静态字段跨不过域重载，运行时需要一个不依赖
        /// 场景序列化的绑定途径——<c>Resources.Load</c> 就是它。
        /// 美术接手时替换该资产即可，代码不用改。</summary>
        public const string GreyboxProjectilePrefabPath = "Assets/Resources/Projectile_Greybox.prefab";

        /// <summary>prefab 在 Resources 下的名字（不含扩展名）。</summary>
        private const string PrototypeResourceName = "Projectile_Greybox";

        /// <summary>已解析的池原型。首次使用时从 Resources 加载并缓存。</summary>
        private static GameObject s_ResolvedPrototype;

        /// <summary>是否已经尝试过从 Resources 加载（避免每帧重复尝试）。</summary>
        private static bool s_PrototypeLoadAttempted;

        /// <summary>
        /// 池原型。**优先用 Resources 下的 prefab 资产**，没有才退回运行时原型。
        /// 域重载会清空静态字段，此时按需重新解析。
        /// </summary>
        private static GameObject s_Prototype;

        [Tooltip("最长存活时间，秒。到点自动归还，避免漏网弹体堆积。")]
        [SerializeField] private float lifetime = 6f;

        [Tooltip("命中判定半径，米。由 Spawn 按视觉尺寸设置。")]
        [SerializeField] private float hitRadius = 0.15f;

        private SpriteRenderer _renderer;
        private Vector2 _direction;
        private float _speed;
        private float _damage;
        private string _sourceEnemyId;
        private float _elapsed;
        private bool _inUse;

        /// <summary>出射方向（单位向量）。出射后不再改变。</summary>
        public Vector2 Direction { get { return _direction; } }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// 初始化一发弹体。**方向在这里定死**，之后只沿它直线前进。
        /// </summary>
        public void Initialize(Vector2 direction, float speed, float damage, float hitRadius,
            string sourceEnemyId, Color color)
        {
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            _speed = Mathf.Max(0.1f, speed);
            _damage = Mathf.Max(0f, damage);
            this.hitRadius = Mathf.Max(0.05f, hitRadius);
            _sourceEnemyId = sourceEnemyId;
            _elapsed = 0f;
            _inUse = true;

            if (_renderer == null)
                _renderer = GetComponent<SpriteRenderer>();

            if (_renderer != null)
                _renderer.color = color;
        }

        private void Update()
        {
            if (!_inUse)
                return;

            var deltaTime = Time.deltaTime;
            _elapsed += deltaTime;

            var previous = (Vector2)transform.position;
            var step = _direction * _speed * deltaTime;
            var next = previous + step;

            if (HitsPlayer(previous, next))
            {
                // 移到目标附近再结算，视觉上"打到了"与"扣血了"同时发生。
                transform.position = new Vector3(next.x, next.y, 0f);
                Impact();
                return;
            }

            transform.position = new Vector3(next.x, next.y, 0f);

            if (_elapsed >= lifetime)
                ReleaseToPool();
        }

        /// <summary>
        /// 这一帧的位移是否命中玩家。
        ///
        /// 两个条件任一成立即判定命中：
        /// 1. 本帧线段到玩家中心的距离 ≤ 命中半径（正常的擦身/正对）。
        /// 2. 本帧**越过了**玩家（投影落在身后）且横向偏移 ≤ 命中半径
        ///    —— 没有这一条，弹体在单帧内跨过玩家时永远不判定命中。
        /// </summary>
        private bool HitsPlayer(Vector2 previous, Vector2 next)
        {
            if (!PlayerLocator.HasPlayer)
                return false;

            var playerPosition = PlayerLocator.PlayerPosition;
            var step = next - previous;

            if (step.sqrMagnitude < 0.0000001f)
                return Vector2.Distance(previous, playerPosition) <= hitRadius;

            var length = step.magnitude;
            var direction = step / length;

            // 玩家相对本帧起点的投影长度。
            var toPlayer = playerPosition - previous;
            var projected = Vector2.Dot(toPlayer, direction);

            // 垂直于弹道方向的横向偏移。
            var closestPoint = previous + direction * Mathf.Clamp(projected, 0f, length);
            var lateral = Vector2.Distance(playerPosition, closestPoint);

            if (lateral > hitRadius)
                return false;

            // 投影落在这一段之内 = 正对命中；投影超过段长 = 本帧越过了玩家。
            return projected >= -hitRadius;
        }

        /// <summary>命中：把伤害交给玩家侧结算（走 §6.1 的 `OnPlayerHurt`）。</summary>
        private void Impact()
        {
            EnemyEvents.RaisePlayerHurt(new EnemyEvents.PlayerHurtInfo
            {
                damage = _damage,
                sourceEnemyId = _sourceEnemyId
            });

            ReleaseToPool();
        }

        /// <summary>归还池，不销毁。置为非激活即可停掉 Update。</summary>
        private void ReleaseToPool()
        {
            _inUse = false;
            SimplePool.Return(gameObject, PoolKey);
        }

        /// <summary>
        /// 发射一发灰盒弹体。经对象池取用，不新建实例。
        /// </summary>
        /// <param name="origin">出射点。</param>
        /// <param name="target">出射瞬间瞄准的目标；**只用来算方向，不保存**。</param>
        public static Projectile Spawn(Vector2 origin, Transform target, Vector2 direction,
            float speed, float damage, string sourceEnemyId, float visualSize, Color color,
            Sprite sprite)
        {
            var prototype = ResolvePrototype();
            if (prototype == null)
                return null;

            var instance = SimplePool.Rent(prototype, PoolKey);
            if (instance == null)
                return null;

            // 池往返会带上一次的位置/旋转/缩放，必须显式重置。
            instance.transform.position = new Vector3(origin.x, origin.y, 0f);
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = new Vector3(visualSize, visualSize, 1f);

            // **每次发射都显式赋精灵**：不依赖池原型里的引用能否撑过 Instantiate。
            // 实测出现过"弹体存在、位置与颜色都对，但精灵为空所以不可见"。
            var renderer = instance.GetComponent<SpriteRenderer>();
            if (renderer != null && sprite != null)
                renderer.sprite = sprite;

            var projectile = instance.GetComponent<Projectile>();

            // 方向优先用显式传入的；没传就按"出射瞬间指向目标"算一次，之后不再改。
            var resolved = direction;
            if (resolved.sqrMagnitude < 0.0001f && target != null)
                resolved = (Vector2)target.position - origin;

            // 命中半径取视觉尺寸的一半，让"打到了"和"看起来碰到了"一致。
            projectile.Initialize(resolved, speed, damage, visualSize * 0.5f, sourceEnemyId, color);

            // 一次性诊断：把"到底创建了没有、精灵是不是空、排序与颜色对不对"写进日志。
            // 反复靠肉眼判断"看不见"的原因太低效，这里把可判定的事实直接打出来。
            if (!s_LoggedFirstSpawn)
            {
                s_LoggedFirstSpawn = true;
                Debug.Log("[Projectile] 首发诊断：" +
                          "实例=" + instance.name +
                          "，激活=" + instance.activeInHierarchy +
                          "，父物体=" + (instance.transform.parent != null ? instance.transform.parent.name : "(场景根)") +
                          "，精灵=" + (renderer != null && renderer.sprite != null ? renderer.sprite.name : "(空!)") +
                          "，排序=" + (renderer != null ? renderer.sortingOrder.ToString() : "n/a") +
                          "，颜色=" + (renderer != null ? renderer.color.ToString() : "n/a") +
                          "，位置=" + instance.transform.position +
                          "，缩放=" + instance.transform.localScale +
                          "，方向=" + resolved);
            }

            return projectile;
        }

        private static bool s_LoggedFirstSpawn;

        private static GameObject ResolvePrototype()
        {
            if (s_ResolvedPrototype != null)
                return s_ResolvedPrototype;

            // 从 Resources 加载资产原型。静态字段跨不过域重载，所以每次会话第一次发射时加载一次。
            if (!s_PrototypeLoadAttempted)
            {
                s_PrototypeLoadAttempted = true;

                var prefab = Resources.Load<GameObject>(PrototypeResourceName);
                if (prefab != null)
                {
                    s_ResolvedPrototype = prefab;
                    return s_ResolvedPrototype;
                }

                Debug.LogWarning("[Projectile] 在 Resources 下找不到 " + PrototypeResourceName +
                                 "，退回运行时原型。运行时原型的引用在实例化后可能不可靠。" +
                                 "请跑一次菜单 Tools/程序C/生成战场灰盒场景。");
            }

            if (s_Prototype == null)
                s_Prototype = CreatePrototype();

            return s_Prototype;
        }

        /// <summary>
        /// 运行时兜底原型。**只在没有 prefab 时使用**——它的精灵引用在实例化后不保证可靠，
        /// 所以正式路径是构建器生成的 prefab 资产。
        ///
        /// 用 <c>hideFlags = DontSave | HideInHierarchy</c> 保证它不进场景文件、
        /// 也不显示在 Hierarchy 里：早期版本放了一个显式模板物体作为池原型，
        /// 而域重载会清空静态字段，于是每次进播放模式都再建一个并跨会话堆积
        /// （表现为 Hierarchy 里一大堆不会自动消失的灰色残留）。
        /// </summary>
        private static GameObject CreatePrototype()
        {
            var prototype = new GameObject("Projectile_Greybox_Prototype");
            prototype.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;

            var renderer = prototype.AddComponent<SpriteRenderer>();
            renderer.sprite = GreyboxAssets.SquareSprite;
            renderer.sortingOrder = 3;

            prototype.AddComponent<Projectile>();
            prototype.SetActive(false);
            return prototype;
        }

        /// <summary>
        /// 把弹体落成 prefab 资产并返回它（已存在则直接读）。
        ///
        /// **不覆盖已存在的资产**：prefab 上的精灵由场景构建器接线，
        /// 若每次都重建，接线会被冲掉——那样接线等于白做。
        /// </summary>
        public static GameObject EnsureProjectilePrefab(Sprite sprite, float visualSize, Color color)
        {
#if UNITY_EDITOR
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxProjectilePrefabPath);
            if (existing != null)
                return existing;

            var prototype = CreatePrototype();
            if (sprite != null)
            {
                var renderer = prototype.GetComponent<SpriteRenderer>();
                if (renderer != null)
                    renderer.sprite = sprite;
            }

            prototype.transform.localScale = new Vector3(visualSize, visualSize, 1f);
            var prototypeRenderer = prototype.GetComponent<SpriteRenderer>();
            if (prototypeRenderer != null)
                prototypeRenderer.color = color;

            // 存成资产时不能带 DontSave / HideInHierarchy，否则存不进去。
            prototype.hideFlags = HideFlags.None;

            var folder = "Assets/Prefabs";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "Prefabs");

            var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(prototype, GreyboxProjectilePrefabPath);
            Object.DestroyImmediate(prototype);

            if (prefab == null)
            {
                Debug.LogError("[Projectile] prefab 保存失败：" + GreyboxProjectilePrefabPath);
                return null;
            }

            Debug.Log("[Projectile] 灰盒弹体 prefab 已生成：" + GreyboxProjectilePrefabPath);
            return prefab;
#else
            return null;
#endif
        }
    }
}
