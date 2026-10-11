using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 接触伤害：碰到玩家就按冷却扣血。
    ///
    /// 与 <see cref="EnemyAttack"/>（远程弹道）是**两个独立组件**，可以同时挂——
    /// 「移动射击敌人」就是两者都挂：既会射子弹，贴上你也会造成伤害。
    ///
    /// 伤害仍走 <see cref="EnemyEvents.PlayerHurt"/>，减免与结算只在玩家侧生效
    /// （见 <see cref="PlayerRuntime"/>），避免每个敌人各算一遍。
    ///
    /// 命中判定用**距离检测**而不是 <c>OnCollisionEnter2D</c>：
    /// 1. 敌我双方在高速移动，靠碰撞回调容易漏帧；
    /// 2. 不依赖碰撞层与触发器配置，灰盒阶段少一个出错来源。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/接触伤害（EnemyContactDamage）")]
    public class EnemyContactDamage : MonoBehaviour
    {
        [Tooltip("接触判定半径，米。")]
        [SerializeField] private float contactRadius = 0.7f;

        [Tooltip("造成伤害的间隔，秒。留 0 则沿用 EnemyData.attackCooldown。")]
        [SerializeField] private float damageIntervalOverride = 0f;

        private EnemyData _data;
        private EnemyRuntime _runtime;
        private float _cooldownRemaining;

        /// <summary>接触判定半径。</summary>
        public float ContactRadius
        {
            get { return contactRadius; }
            set { contactRadius = Mathf.Max(0.05f, value); }
        }

        private void Awake()
        {
            _data = GetComponent<EnemyData>();
            _runtime = GetComponent<EnemyRuntime>();

            if (_data == null)
            {
                Debug.LogError("[EnemyContactDamage] 同一物体上找不到 EnemyData，无法取攻击力。", this);
                enabled = false;
                return;
            }

            // 开局给一点延迟，避免刚生成就贴脸扣血。
            _cooldownRemaining = 0.3f;
        }

        private void Update()
        {
            if (_cooldownRemaining > 0f)
                _cooldownRemaining -= Time.deltaTime;

            if (_cooldownRemaining > 0f)
                return;

            if (_runtime != null && !_runtime.IsAlive)
                return;

            if (!PlayerLocator.HasPlayer)
                return;

            var distance = Vector2.Distance(transform.position, PlayerLocator.PlayerPosition);
            if (distance > contactRadius)
                return;

            ApplyContactDamage();
        }

        /// <summary>造成一次接触伤害。冷却没好时返回 false。</summary>
        public bool ApplyContactDamage()
        {
            if (_cooldownRemaining > 0f)
                return false;

            _cooldownRemaining = damageIntervalOverride > 0.01f
                ? damageIntervalOverride
                : Mathf.Max(0.1f, _data.AttackCooldown);

            EnemyEvents.RaisePlayerHurt(new EnemyEvents.PlayerHurtInfo
            {
                damage = _data.AttackPower,
                sourceEnemyId = _runtime != null ? _runtime.Id : gameObject.name
            });

            return true;
        }
    }
}
