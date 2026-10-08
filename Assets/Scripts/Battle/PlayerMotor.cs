using UnityEngine;
using UnityEngine.InputSystem;

namespace TideBorne.Battle
{
    /// <summary>
    /// 玩家移动：读输入方向、按 <see cref="PlayerStatsBase.MoveSpeed"/> 驱动刚体。
    ///
    /// 本项目只启用新 Input System（<c>ProjectSettings.asset</c> 的 <c>activeInputHandler: 1</c>），
    /// 因此不使用旧的 <c>Input.GetAxis</c>——它在当前设置下会在运行时抛异常。
    /// 输入引用来自 <c>Assets/InputSystem_Actions.inputactions</c> 的 Player 动作表：
    /// <c>Move</c>（Value / Vector2）与 <c>Sprint</c>（Button）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [AddComponentMenu("潮涌之躯/战场/玩家移动（PlayerMotor）")]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("输入（拖入 InputSystem_Actions 的 Player/Move 与 Player/Sprint）")]
        [Tooltip("移动输入，期望 Vector2。")]
        [SerializeField] private InputActionReference moveAction;

        [Tooltip("疾跑输入。留空则不启用疾跑。")]
        [SerializeField] private InputActionReference sprintAction;

        [Header("引用")]
        [Tooltip("属性表。留空则自动取同一物体上的 PlayerStatsBase。")]
        [SerializeField] private PlayerStatsBase stats;

        [Header("手感参数")]
        [Tooltip("是否让角色朝向移动方向。2D 俯视下等于绕 Z 轴旋转。")]
        [SerializeField] private bool faceMoveDirection = true;

        [Tooltip("朝向插值速度上限，度/秒。实际速度取它与 PlayerStats.TurnSpeed 的较小值，" +
                 "让「履带」这类改转向速度的器官能生效（策划案差异点）。0 表示立即转向。")]
        [SerializeField] private float facingLerpSpeed = 720f;

        [Tooltip("疾跑倍率。体力系统尚未定（策划案待定问题 7），故暂不消耗资源。")]
        [SerializeField] private float sprintMultiplier = 1.5f;

        private Rigidbody2D _body;
        private Vector2 _moveInput;
        private bool _sprintHeld;

        /// <summary>本帧是否按住疾跑。</summary>
        public bool IsSprinting { get { return _sprintHeld; } }

        /// <summary>本帧的移动输入，未归一化。</summary>
        public Vector2 MoveInput { get { return _moveInput; } }

        /// <summary>属性表。</summary>
        public PlayerStatsBase Stats
        {
            get
            {
                if (stats == null)
                    stats = GetComponent<PlayerStatsBase>();

                return stats;
            }
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            if (stats == null)
                stats = GetComponent<PlayerStatsBase>();

            if (stats == null)
            {
                Debug.LogError("[PlayerMotor] 同一物体上找不到 PlayerStatsBase，无法读取 moveSpeed。", this);
                enabled = false;
                return;
            }

            // 俯视 2D 不需要重力；全局 Physics2D 重力是 -9.81（见 Physics2DSettings.asset）。
            _body.gravityScale = 0f;
            _body.freezeRotation = true;
        }

        private void OnEnable()
        {
            EnableAction(moveAction);
            EnableAction(sprintAction);
        }

        private void OnDisable()
        {
            _sprintHeld = false;
            _moveInput = Vector2.zero;
        }

        private void OnDestroy()
        {
            DisableAction(moveAction);
            DisableAction(sprintAction);
        }

        /// <summary>读输入、不做物理写入（物理写入放 FixedUpdate）。</summary>
        private void Update()
        {
            _moveInput = ReadMoveInput();
            // 每帧读值而不是只听 performed 回调：回调收不到"松开"，会卡在按下状态。
            _sprintHeld = ReadSprintHeld();
        }

        private void FixedUpdate()
        {
            var direction = _moveInput;
            if (direction.sqrMagnitude > 1f)
                direction = direction.normalized;

            var speed = Stats.MoveSpeed;
            if (_sprintHeld)
                speed *= sprintMultiplier;

            // Rigidbody2D.linearVelocity 是 Unity 6 的写法；旧版本叫 velocity。
            _body.linearVelocity = direction * speed;

            if (faceMoveDirection && direction.sqrMagnitude > 0.0001f)
            {
                var targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                var currentAngle = _body.rotation;

                // 转向速度取「本组件上限」与「属性表 turnSpeed」的较小值。
                // 这样器官（履带：直线冲锋更快但转向变慢）能真正影响手感，
                // 而 turnSpeed 本身也是接口草案 §5.2 列的字段，不能只是躺着。
                var statTurnSpeed = Stats.TurnSpeed;
                var maxStep = Mathf.Min(facingLerpSpeed, statTurnSpeed);

                var nextAngle = maxStep <= 0f
                    ? targetAngle
                    : Mathf.MoveTowardsAngle(currentAngle, targetAngle, maxStep * Time.fixedDeltaTime);

                _body.MoveRotation(nextAngle);
            }
        }

        private Vector2 ReadMoveInput()
        {
            if (moveAction == null || moveAction.action == null)
                return Vector2.zero;

            return moveAction.action.ReadValue<Vector2>();
        }

        /// <summary>疾跑键是否按住。按钮型动作用 <c>IsPressed</c>，不依赖回调时序。</summary>
        private bool ReadSprintHeld()
        {
            if (sprintAction == null || sprintAction.action == null)
                return false;

            return sprintAction.action.IsPressed();
        }

        private static void EnableAction(InputActionReference reference)
        {
            if (reference != null && reference.action != null)
                reference.action.Enable();
        }

        private static void DisableAction(InputActionReference reference)
        {
            if (reference != null && reference.action != null)
                reference.action.Disable();
        }
    }
}
