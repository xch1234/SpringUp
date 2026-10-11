#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

namespace TideBorne.Battle
{
    /// <summary>
    /// 灰盒阶段的开发工具：按一下键就对周围敌人造成一次伤害。
    ///
    /// **存在的唯一理由**：goal 的验收句里「敌人会死」这一条，正常要靠玩家攻击来触发，
    /// 而玩家攻击来自程序 A 的器官 / `Event` 管道（尚未实现）。
    /// 没有它，这条验收就要一直等到管道接上，程序 C 无法独立闭环。
    ///
    /// 它**不是玩法内容**：不产生伤害来源器官 id（传空），也不参与掉落结算。
    /// 整个文件用 <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c> 包住，
    /// **正式发行版里不会编译进去**。
    ///
    /// 触发键用新 Input System（`Keyboard.current`）读，
    /// **不能用 <c>Input.GetKeyDown</c>**：本工程 `activeInputHandler: 1`，旧输入 API 会抛异常。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/调试·范围伤害（DevDamageField）")]
    public class DevDamageField : MonoBehaviour
    {
        [Header("按键")]
        [Tooltip("对周围敌人造成一次伤害的按键。")]
        [SerializeField] private Key triggerKey = Key.F;

        [Tooltip("每帧自动攻击，不用按键。用于长时间挂机观察。")]
        [SerializeField] private bool autoFire = false;

        [Header("参数")]
        [Tooltip("每次造成的伤害值。")]
        [SerializeField] private float damagePerHit = 10f;

        [Tooltip("作用半径，米。")]
        [SerializeField] private float radius = 3f;

        [Tooltip("自动模式的出手间隔，秒。")]
        [SerializeField] private float autoFireInterval = 0.5f;

        [Tooltip("是否打印每次命中的目标，便于肉眼核对。")]
        [SerializeField] private bool logHits = true;

        private float _autoFireRemaining;

        private void Update()
        {
            if (autoFire)
            {
                _autoFireRemaining -= Time.deltaTime;
                if (_autoFireRemaining <= 0f)
                {
                    _autoFireRemaining = autoFireInterval;
                    DealDamageInRadius();
                }
            }

            if (WasTriggerKeyPressedThisFrame())
                DealDamageInRadius();
        }

        private bool WasTriggerKeyPressedThisFrame()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return false;

            var control = keyboard[triggerKey];
            return control != null && control.wasPressedThisFrame;
        }

        /// <summary>
        /// 对半径内的全部敌人造成一次伤害。
        /// 走的是 <see cref="EnemyRuntime.TakeDamage"/>——**正是程序 A 将来要走的那条真实入口**，
        /// 所以它验证的是真实死亡路径，不是伪造的"直接销毁"。
        /// </summary>
        [ContextMenu("对周围敌人造成一次伤害")]
        public void DealDamageInRadius()
        {
            var origin = (Vector2)transform.position;
            var radiusSquared = radius * radius;
            var hits = 0;

            // 用快照：TakeDamage 可能导致敌人立刻死亡并 Destroy，不能直接遍历活列表。
            var snapshot = EnemyRuntime.ActiveEnemies;
            var enemies = new EnemyRuntime[snapshot.Count];
            for (var i = 0; i < snapshot.Count; i++)
                enemies[i] = snapshot[i];

            for (var i = 0; i < enemies.Length; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive)
                    continue;

                var offset = enemy.Position - origin;
                if (offset.sqrMagnitude > radiusSquared)
                    continue;

                // sourceOrganId 传空：这不是器官造成的伤害，不能冒充击杀触发。
                enemy.TakeDamage(damagePerHit, string.Empty, enemy.Position);
                hits++;

                if (logHits)
                    Debug.Log("[DevDamageField] 命中 " + enemy.Id + "，伤害 " + damagePerHit);
            }

            if (logHits && hits == 0)
                Debug.Log("[DevDamageField] 半径 " + radius + " 米内没有敌人。");
        }
    }
}
#endif
