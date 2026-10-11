using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 医疗兵：间歇性治疗周围的敌人（不含自己）。
    ///
    /// **只治别人、不治自己**——"医疗兵"的战术意义是让一支小队更难清掉，
    /// 自愈会让它变成单纯的肉盾，与"治疗周围敌人"的意图不符。
    /// 若要它自愈，把 <see cref="healSelf"/> 打开即可，这是配置不是硬编码。
    ///
    /// 治疗量按**最大生命的百分比**算：写固定值会让它在后期敌人身上毫无意义，
    /// 而百分比在任何血量规模下都保持同一战术价值。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/医疗兵（EnemyHealer）")]
    public class EnemyHealer : MonoBehaviour
    {
        [Tooltip("治疗间隔，秒。")]
        [SerializeField] private float healInterval = 3f;

        [Tooltip("治疗半径，米。")]
        [SerializeField] private float healRadius = 5f;

        [Tooltip("每次治疗回复最大生命的比例，0–1。")]
        [SerializeField] private float healPercent = 0.2f;

        [Tooltip("是否也治疗自己。")]
        [SerializeField] private bool healSelf = false;

        [Tooltip("治疗生效时在 Console 打一行，便于肉眼验收。")]
        [SerializeField] private bool logHeals = true;

        private float _cooldownRemaining;

        /// <summary>治疗间隔，秒。</summary>
        public float HealInterval { get { return healInterval; } set { healInterval = Mathf.Max(0.1f, value); } }

        /// <summary>治疗半径，米。</summary>
        public float HealRadius { get { return healRadius; } set { healRadius = Mathf.Max(0.1f, value); } }

        private void Awake()
        {
            // 首次治疗也给一点延迟，避免开局瞬间全队满血、看不出效果。
            _cooldownRemaining = healInterval;
        }

        private void Update()
        {
            _cooldownRemaining -= Time.deltaTime;
            if (_cooldownRemaining > 0f)
                return;

            _cooldownRemaining = healInterval;
            HealNearbyEnemies();
        }

        /// <summary>治疗半径内的敌人。返回被治疗的个数。</summary>
        public int HealNearbyEnemies()
        {
            var origin = (Vector2)transform.position;
            var radiusSquared = healRadius * healRadius;
            var self = GetComponent<EnemyRuntime>();
            var healed = 0;

            // 用快照：治疗过程中列表不会变，但保持一致做法便于将来扩展。
            var snapshot = EnemyRuntime.ActiveEnemies;
            for (var i = 0; i < snapshot.Count; i++)
            {
                var enemy = snapshot[i];
                if (enemy == null || !enemy.IsAlive)
                    continue;

                if (!healSelf && enemy == self)
                    continue;

                if (Vector2.SqrMagnitude(enemy.Position - origin) > radiusSquared)
                    continue;

                var amount = enemy.MaxHealth * Mathf.Clamp01(healPercent);
                var applied = enemy.Heal(amount);
                if (applied <= 0f)
                    continue;

                healed++;

                if (logHeals)
                {
                    Debug.Log("[EnemyHealer] 治疗 " + enemy.Id + " +" + applied.ToString("0.0") +
                              "（最大生命 " + enemy.MaxHealth.ToString("0") + "）");
                }
            }

            if (logHeals && healed == 0)
                Debug.Log("[EnemyHealer] 半径 " + healRadius + " 米内没有需要治疗的敌人。");

            return healed;
        }
    }
}
