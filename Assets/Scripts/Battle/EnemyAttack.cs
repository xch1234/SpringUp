using System;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人攻击：进入 <see cref="EnemyData.AttackRange"/> 后按 <see cref="EnemyData.AttackCooldown"/>
    /// 周期性出手。
    ///
    /// **与 `Event` 管道的对接点**（`接口草案.md` §6.3，**【暂定】**）：
    /// §6.3 定的是「敌人的攻击产出一个 `Event`，与玩家攻击走同一套结构」，理由是
    /// 「程序 A 的管道只认 `Event`；敌人若另开一套字段，触发链收到敌人事件会因字段缺失而断链」。
    ///
    /// **但 `Event` 结构体属程序 A**（`任务拆解.md` 接口 1：谁定义 = 程序 A），
    /// 且 §1 里 `radius` 的「(类型, 距离)」格式与 `pierce` 的数值口径都还挂着待定。
    /// 所以本模块**不代为定义 `Event`**，只抛 <see cref="Attacked"/>：
    /// 载荷是程序 C 已确定的全部字段，程序 A 拿到后自己构造 `Event` 即可，不需要改本文件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/敌人攻击（EnemyAttack）")]
    public class EnemyAttack : MonoBehaviour
    {
        /// <summary>敌人出手时的载荷。字段名对齐 <c>接口草案.md</c> §6.2。</summary>
        public struct AttackInfo
        {
            /// <summary>攻击者编号。</summary>
            public string enemyId;

            /// <summary>攻击力；唯一的伤害来源字段。</summary>
            public float attackPower;

            /// <summary>投射物数。对齐 `Event.count`。</summary>
            public int projectileCount;

            /// <summary>投射物速度。对齐 `Event.speed`。</summary>
            public float projectileSpeed;

            /// <summary>攻击判定范围。对齐 `Event.radius`。</summary>
            public float attackRadius;

            /// <summary>攻击者位置。</summary>
            public Vector2 origin;

            /// <summary>攻击者物体。</summary>
            public GameObject enemy;
        }

        [Tooltip("敌人数据。留空则自动取同一物体上的 EnemyData。")]
        [SerializeField] private EnemyData data;

        [Tooltip("是否在进入攻击距离时自动出手。关掉则只能由外部调用 TryAttack。")]
        [SerializeField] private bool attackAutomatically = true;

        [Tooltip("开局到第一次出手之间的延迟，秒。避免所有敌人同时打第一下。")]
        [SerializeField] private float initialDelay = 0.5f;

        [Header("灰盒弹道")]
        [Tooltip("true = 发射可见弹体（推荐，玩家能看清攻击从哪来）；false = 出手即扣血，无飞行物。")]
        [SerializeField] private bool useProjectiles = true;

        [Tooltip("弹体视觉边长，世界单位。")]
        [SerializeField] private float projectileVisualSize = 0.25f;

        [Tooltip("弹体颜色。")]
        [SerializeField] private Color projectileColor = new Color(1f, 0.75f, 0.3f, 1f);

        [Tooltip("弹体精灵。**发射时会显式赋给实例**——不依赖池原型里的引用能否撑过实例化。" +
                 "留空则弹体不可见。")]
        [SerializeField] private Sprite projectileSprite;

        [Tooltip("多发弹体之间的夹角，度。projectileCount > 1 时按此散开。")]
        [SerializeField] private float spreadAngleDegrees = 12f;

        private EnemyRuntime _runtime;
        private float _cooldownRemaining;
        private bool _loggedFirstShot;

        /// <summary>敌人出手时触发。程序 A 的 `Event` 管道与表现层接这里。</summary>
        public event Action<AttackInfo> Attacked;

        /// <summary>攻击冷却是否已经走完。</summary>
        public bool IsReady { get { return _cooldownRemaining <= 0f; } }

        /// <summary>敌人数据。</summary>
        public EnemyData Data
        {
            get
            {
                if (data == null)
                    data = GetComponent<EnemyData>();

                return data;
            }
        }

        private void Awake()
        {
            _runtime = GetComponent<EnemyRuntime>();

            if (data == null)
                data = GetComponent<EnemyData>();

            if (data == null)
            {
                Debug.LogError("[EnemyAttack] 同一物体上找不到 EnemyData。", this);
                enabled = false;
                return;
            }

            _cooldownRemaining = initialDelay;
        }

        private void Update()
        {
            if (_cooldownRemaining > 0f)
                _cooldownRemaining -= Time.deltaTime;

            if (!attackAutomatically || !IsReady)
                return;

            if (_runtime != null && !_runtime.IsAlive)
                return;

            if (!PlayerLocator.HasPlayer)
                return;

            var toPlayer = PlayerLocator.PlayerPosition - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > Data.AttackRange * Data.AttackRange)
                return;

            TryAttack();
        }

        /// <summary>
        /// 出手一次。冷却没好时返回 false。
        ///
        /// 伤害的减免与结算在玩家侧（<see cref="PlayerRuntime"/> 订阅 `EnemyEvents.PlayerHurt`），
        /// 这样 `damageTakenMultiplier` 只在一个地方生效。
        /// </summary>
        public bool TryAttack()
        {
            if (!IsReady)
                return false;

            var enemyData = Data;
            _cooldownRemaining = enemyData.AttackCooldown;

            var info = new AttackInfo
            {
                enemyId = _runtime != null ? _runtime.Id : gameObject.name,
                attackPower = enemyData.AttackPower,
                projectileCount = enemyData.ProjectileCount,
                projectileSpeed = enemyData.ProjectileSpeed,
                attackRadius = enemyData.AttackRadius,
                origin = transform.position,
                enemy = gameObject
            };

            var handler = Attacked;
            if (handler != null)
                handler(info);

            if (useProjectiles)
                FireProjectiles(info);
            else
                ApplyInstantHit(info);

            return true;
        }

        /// <summary>
        /// 发射可见弹体。多发时按 <see cref="spreadAngleDegrees"/> 绕基准方向散开。
        /// `projectileCount` 是数据层早已定义的字段，这里把它用起来。
        /// </summary>
        private void FireProjectiles(AttackInfo info)
        {
            if (!PlayerLocator.HasPlayer)
                return;

            var target = PlayerLocator.Player;
            var origin = (Vector2)transform.position;
            var baseDirection = ((Vector2)target.position - origin).normalized;
            var count = Mathf.Max(1, info.projectileCount);
            var speed = info.projectileSpeed > 0.01f ? info.projectileSpeed : 6f;

            if (!_loggedFirstShot)
            {
                _loggedFirstShot = true;
                Debug.Log("[EnemyAttack] 首发弹体：数量 " + count + "，速度 " + speed +
                          " 米/秒，射程 " + Data.AttackRange + " 米，视觉边长 " + projectileVisualSize +
                          "，预计飞行约 " +
                          (speed > 0.01f ? (Data.AttackRange / speed).ToString("0.00") : "?") + " 秒。" +
                          "若看不到弹体，请把这个数字回报给程序 C。");
            }

            for (var i = 0; i < count; i++)
            {
                // 以中心对称的方式散开：count=1 时偏移为 0，count=3 时为 -1/0/+1 个夹角。
                var offsetIndex = i - (count - 1) * 0.5f;
                var angle = spreadAngleDegrees * offsetIndex;
                var direction = Rotate(baseDirection, angle);

                Projectile.Spawn(origin, target, direction, speed, info.attackPower,
                    info.enemyId, projectileVisualSize, projectileColor, projectileSprite);
            }
        }

        /// <summary>出手即扣血，无飞行物。保留给"瞬发"类敌人，也便于对比调试。</summary>
        private void ApplyInstantHit(AttackInfo info)
        {
            EnemyEvents.RaisePlayerHurt(new EnemyEvents.PlayerHurtInfo
            {
                damage = info.attackPower,
                sourceEnemyId = info.enemyId
            });
        }

        /// <summary>把方向绕 Z 轴旋转指定角度（度）。</summary>
        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.0001f)
                return direction;

            var radians = degrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);

            return new Vector2(
                direction.x * cos - direction.y * sin,
                direction.x * sin + direction.y * cos);
        }
    }
}
