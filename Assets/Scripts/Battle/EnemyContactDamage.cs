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
    /// 优先测量双方实际碰撞体的间隙（允许 0.03 单位接触容差），
    /// 不依赖敌我碰撞层回调。没有碰撞体的旧对象才使用中心距离兜底。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/接触伤害（EnemyContactDamage）")]
    public class EnemyContactDamage : MonoBehaviour
    {
        private float contactRadius => GetComponent<EnemyData>().Definition.contactRadius;

        private EnemyData _data;
        private EnemyRuntime _runtime;
        private float _cooldownRemaining;
        private Collider2D _collider;

        /// <summary>接触判定半径。</summary>
        public float ContactRadius
        {
            get { return contactRadius; }
        }

        private void Awake()
        {
            _data = GetComponent<EnemyData>();
            _runtime = GetComponent<EnemyRuntime>();
            _collider = GetComponent<Collider2D>();

            if (_data == null)
            {
                Debug.LogError("[EnemyContactDamage] 同一物体上找不到 EnemyData，无法取攻击力。", this);
                enabled = false;
                return;
            }

            // 开局给一点延迟，避免刚生成就贴脸扣血。
            _cooldownRemaining = 0.3f;
        }

        private void OnEnable() { _cooldownRemaining = 0.3f; }

        private void Update()
        {
            // A ready cooldown / overlapping projectile must not resolve while the world is paused.
            if (Time.timeScale <= 0f) return;
            if (_cooldownRemaining > 0f)
                _cooldownRemaining -= Time.deltaTime;

            if (_cooldownRemaining > 0f)
                return;

            if (_runtime != null && !_runtime.IsAlive)
                return;

            if (!PlayerLocator.HasPlayer)
                return;

            var player = PlayerLocator.Player;
            var other = player != null ? player.GetComponent<Collider2D>() : null;
            if (_collider != null && other != null)
            {
                if (!_collider.enabled || !other.enabled) return;
                var gap = _collider.Distance(other);
                if (!gap.isValid || gap.distance > 0.03f) return;
            }
            else if (Vector2.Distance(transform.position, PlayerLocator.PlayerPosition) > contactRadius) return;

            ApplyContactDamage();
        }

        /// <summary>造成一次接触伤害。冷却没好时返回 false。</summary>
        public bool ApplyContactDamage()
        {
            if (!isActiveAndEnabled || !_data.Definition.hasContactDamage || (_runtime != null && !_runtime.IsAlive) || _cooldownRemaining > 0f)
                return false;

            _cooldownRemaining = _data.ContactDamageInterval;

            EnemyEvents.RaisePlayerHurt(new EnemyEvents.PlayerHurtInfo
            {
                damage = _data.AttackPower,
                sourceEnemyId = _runtime != null ? _runtime.Id : gameObject.name
            });

            return true;
        }
    }
}
