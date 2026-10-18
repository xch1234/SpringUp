using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人移动。支持四种模式（见 <see cref="EnemyMoveMode"/>），由一个组件承担，
    /// 避免为每种行为复制一份移动代码。
    ///
    /// 行为定位（`任务拆解.md` §九 留白）：「敌人 AI 的具体行为由程序 C 自己定，
    /// 只要难度曲线对得上」。所以这里只做最基本的移动策略，不预设绕障、包抄等。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [AddComponentMenu("潮涌之躯/战场/敌人移动（EnemyMotor）")]
    public class EnemyMotor : MonoBehaviour
    {
        [Tooltip("敌人数据。留空则自动取同一物体上的 EnemyData。")]
        [SerializeField] private EnemyData data;
        private EnemyMoveMode moveMode => GetComponent<EnemyData>().Definition.moveMode;
        private bool faceMoveDirection => GetComponent<EnemyData>().Definition.faceMoveDirection;

        private float stopAtAttackRangeRatio => GetComponent<EnemyData>().Definition.stopAtAttackRangeRatio;
        private float minStopDistance => GetComponent<EnemyData>().Definition.minStopDistance;

        private float preferredDistance => GetComponent<EnemyData>().Definition.preferredDistance;
        private float distanceTolerance => GetComponent<EnemyData>().Definition.distanceTolerance;

        private Rigidbody2D _body;
        private EnemyRuntime _runtime;
        private SpriteRenderer _renderer;

        [Tooltip("美术默认朝左：勾上时，向右移动会水平翻转精灵。美术若默认朝右则取消勾选。")]
        [SerializeField] private bool spriteFacesLeft = true;

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

        /// <summary>移动模式。</summary>
        public EnemyMoveMode MoveMode
        {
            get { return moveMode; }
        }

        /// <summary>
        /// 实际停靠距离：攻击距离的 <see cref="stopAtAttackRangeRatio"/> 倍，且不低于下限。
        ///
        /// **为什么要跟攻击距离挂钩**：早期版本写死 1 米，而灰盒射程是 6 米——
        /// 结果敌人一路贴到脸上才停，弹道从脸飞到脸，"远程攻击"完全看不出意义。
        /// </summary>
        public float StopDistance
        {
            get
            {
                var attackRange = Data != null ? Data.AttackRange : 0f;
                var contact = GetComponent<EnemyContactDamage>();
                if (contact != null && contact.enabled)
                    return Mathf.Min(minStopDistance, contact.ContactRadius * 0.9f);
                if (attackRange <= 0.01f)
                    return minStopDistance;

                return Mathf.Max(minStopDistance, attackRange * Mathf.Clamp01(stopAtAttackRangeRatio));
            }
        }

        /// <summary>保持距离模式的目标距离。</summary>
        public float PreferredDistance
        {
            get { return preferredDistance > 0.01f ? preferredDistance : StopDistance; }
        }

        /// <summary>本帧到玩家的距离。</summary>
        public float DistanceToPlayer { get; private set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _runtime = GetComponent<EnemyRuntime>();
            _renderer = GetComponent<SpriteRenderer>();

            if (data == null)
                data = GetComponent<EnemyData>();

            if (data == null)
            {
                Debug.LogError("[EnemyMotor] 同一物体上找不到 EnemyData。", this);
                enabled = false;
                return;
            }

            // 俯视 2D 不需要重力；全局 Physics2D 重力是 -9.81（见 Physics2DSettings.asset）。
            _body.gravityScale = 0f;
            _body.freezeRotation = true;
        }

        private void FixedUpdate()
        {
            if (_runtime != null && !_runtime.IsAlive)
            {
                _body.linearVelocity = Vector2.zero;
                return;
            }

            if (!PlayerLocator.HasPlayer)
            {
                _body.linearVelocity = Vector2.zero;
                return;
            }

            var self = (Vector2)transform.position;
            var toPlayer = PlayerLocator.PlayerPosition - self;
            var distance = toPlayer.magnitude;
            DistanceToPlayer = distance;

            // 方向：朝玩家（+1）或背向玩家（-1），或 0 表示不动。
            var sign = ResolveDirectionSign(distance);
            if (Mathf.Approximately(sign, 0f) || distance < 0.0001f)
            {
                _body.linearVelocity = Vector2.zero;
                return;
            }

            var direction = toPlayer / distance * sign;
            _body.linearVelocity = direction * Data.MoveSpeed * (_runtime != null ? _runtime.OrganSpeedMultiplier : 1f);

            if (faceMoveDirection)
                FaceDirection(direction);
        }

        /// <summary>
        /// 只做左右镜像，不旋转刚体。
        ///
        /// 旧实现把整个刚体旋到移动方向（<c>MoveRotation</c>），方块时代看不出来；
        /// 换成美术后会整体倒立。这里改为按水平分量翻 <c>flipX</c>，
        /// 竖直移动时保留上一次朝向，避免上下抖动时反复翻面。
        /// </summary>
        private void FaceDirection(Vector2 direction)
        {
            if (_renderer == null || Mathf.Abs(direction.x) < 0.0001f)
                return;

            var flip = spriteFacesLeft ? direction.x > 0f : direction.x < 0f;
            if (_renderer.flipX != flip)
                _renderer.flipX = flip;
        }

        /// <summary>
        /// 算出这一帧该朝玩家（+1）还是背向玩家（-1）还是不动（0）。
        /// 抽出来是为了让四种模式共用同一段速度与朝向代码。
        /// </summary>
        private float ResolveDirectionSign(float distance)
        {
            switch (moveMode)
            {
                case EnemyMoveMode.Stationary:
                    return 0f;

                case EnemyMoveMode.Chase:
                    // 追到停靠距离就不动，不再往玩家身上挤。
                    return distance <= StopDistance ? 0f : 1f;

                case EnemyMoveMode.KeepDistance:
                {
                    var target = PreferredDistance;
                    if (distance > target + distanceTolerance)
                        return 1f;
                    if (distance < target - distanceTolerance)
                        return -1f;
                    return 0f;
                }

                case EnemyMoveMode.Flee:
                    // 远离玩家；同时受地图边界限制（靠夹取而非碰撞，见 ClampToPlayArea）。
                    return -1f;

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 把位置夹进可通行区域。远离模式会把敌人推向地图边缘，
        /// 不夹的话它们会挤在边界墙外面出不来。
        /// </summary>
        private void LateUpdate()
        {
            if (moveMode != EnemyMoveMode.Flee || !clampToPlayArea)
                return;

            var position = (Vector2)transform.position;
            var clamped = new Vector2(
                Mathf.Clamp(position.x, -playAreaHalfWidth + 1f, playAreaHalfWidth - 1f),
                Mathf.Clamp(position.y, -playAreaHalfHeight + 1f, playAreaHalfHeight - 1f));

            if (clamped != position)
            {
                transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
                if (_body != null)
                    _body.linearVelocity = Vector2.zero;
            }
        }

        [Header("可通行区域限制")]
        [Tooltip("远离模式时把位置夹进可通行区域，避免被推出边界墙外。")]
        [SerializeField] private bool clampToPlayArea = true;

        [Tooltip("可通行区域半宽，米。由场景构建器接线。")]
        [SerializeField] private float playAreaHalfWidth = 32f;

        [Tooltip("可通行区域半高，米。由场景构建器接线。")]
        [SerializeField] private float playAreaHalfHeight = 18f;
    }
}
