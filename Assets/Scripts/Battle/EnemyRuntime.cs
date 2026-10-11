using System.Collections.Generic;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人的运行时状态与生死流程：受伤、施加状态、死亡。
    ///
    /// **两条离开战场的路径必须分开**（见 <see cref="RemoveFromBattlefield"/>）：
    /// 被打死走 <see cref="Die"/>，会发 `OnKilled` 并允许掉落；
    /// 波次到点清场走 <see cref="RemoveFromBattlefield"/>，**不发 `OnKilled`、不产生掉落**。
    /// 混在一起会让「击杀触发」类器官（多肢触手 / 蜘蛛巢）在清场时白拿收益。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/敌人运行时（EnemyRuntime）")]
    public class EnemyRuntime : MonoBehaviour
    {
        /// <summary>当前战场上的全部敌人。波次清场与查询用。</summary>
        private static readonly List<EnemyRuntime> s_ActiveEnemies = new List<EnemyRuntime>();

        [Tooltip("敌人数据。留空则自动取同一物体上的 EnemyData。")]
        [SerializeField] private EnemyData data;

        [Tooltip("当前血量。")]
        [SerializeField] private float currentHealth = 20f;

        private bool _isDead;

        /// <summary>战场上的敌人数量。</summary>
        public static int ActiveCount { get { return s_ActiveEnemies.Count; } }

        /// <summary>战场上的敌人快照。返回的是内部列表，**不要改它**。</summary>
        public static IReadOnlyList<EnemyRuntime> ActiveEnemies { get { return s_ActiveEnemies; } }

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

        /// <summary>敌人编号。</summary>
        public string Id
        {
            get
            {
                var enemyData = Data;
                return enemyData != null ? enemyData.Id : string.Empty;
            }
        }

        /// <summary>当前血量。</summary>
        public float CurrentHealth { get { return currentHealth; } }

        /// <summary>血量上限。</summary>
        public float MaxHealth
        {
            get
            {
                var enemyData = Data;
                return enemyData != null ? enemyData.MaxHealth : 0f;
            }
        }

        /// <summary>是否还活着。</summary>
        public bool IsAlive { get { return !_isDead && currentHealth > 0f; } }

        /// <summary>死亡点（世界坐标），供 `OnKilled` 载荷使用。</summary>
        public Vector2 Position { get { return transform.position; } }

        /// <summary>
        /// 池键。由刷怪器写入：本敌人是从哪个池取出来的。
        /// 为空表示不是池化对象，退出战场时按销毁处理。
        /// </summary>
        public string PoolKey { get; private set; }

        /// <summary>是否为池化对象。</summary>
        public bool IsPooled { get { return !string.IsNullOrEmpty(PoolKey); } }

        /// <summary>标记本敌人为池化对象。由刷怪器在取出实例后调用。</summary>
        public void MarkPooled(string poolKey)
        {
            PoolKey = poolKey;
        }

        private void Awake()
        {
            if (data == null)
                data = GetComponent<EnemyData>();

            if (data == null)
            {
                Debug.LogError("[EnemyRuntime] 同一物体上找不到 EnemyData。", this);
                enabled = false;
                return;
            }

            // 首次启动把血量夹到上限，避免 prefab 里的初值大于上限。
            currentHealth = Mathf.Clamp(currentHealth, 0f, MaxHealth);
        }

        private void OnEnable()
        {
            // 池化复用：重新激活时恢复满血，否则第二个"敌人"会带着上次的残血出现。
            _isDead = false;

            if (data == null)
                data = GetComponent<EnemyData>();

            if (data != null)
                currentHealth = MaxHealth;

            if (!s_ActiveEnemies.Contains(this))
                s_ActiveEnemies.Add(this);
        }

        private void OnDisable()
        {
            // 归还池会置为非激活，此时必须移出活列表，否则 HUD 会把池里的算成场上敌人。
            s_ActiveEnemies.Remove(this);
        }

        private void OnDestroy()
        {
            s_ActiveEnemies.Remove(this);
        }

        /// <summary>
        /// 受伤。发 `OnDamaged`；扣到 0 或以下则转为死亡。
        /// </summary>
        /// <param name="damage">伤害值，必须为正。</param>
        /// <param name="sourceOrganId">来源器官 id，可为空（例如敌人互殴或清场）。</param>
        /// <param name="hitPoint">命中点。</param>
        /// <returns>实际扣掉的血量。</returns>
        public float TakeDamage(float damage, string sourceOrganId, Vector2 hitPoint)
        {
            if (damage <= 0f || !IsAlive)
                return 0f;

            var applied = Mathf.Min(damage, currentHealth);
            currentHealth -= applied;

            EnemyEvents.RaiseDamaged(new EnemyEvents.DamagedInfo
            {
                enemyId = Id,
                damage = applied,
                sourceOrganId = sourceOrganId,
                hitPoint = hitPoint,
                enemy = gameObject
            });

            if (currentHealth <= 0f)
                Die(sourceOrganId, hitPoint);

            return applied;
        }

        /// <summary>回血，会被上限夹住。</summary>
        public float Heal(float amount)
        {
            if (amount <= 0f || !IsAlive)
                return 0f;

            var before = currentHealth;
            currentHealth = Mathf.Min(currentHealth + amount, MaxHealth);
            return currentHealth - before;
        }

        /// <summary>施加状态。发 `OnStatusApplied`，喂给表现层。</summary>
        public void ApplyStatus(string status, int stacks, float duration)
        {
            if (!IsAlive)
                return;

            EnemyEvents.RaiseStatusApplied(new EnemyEvents.StatusAppliedInfo
            {
                enemyId = Id,
                status = status,
                stacks = stacks,
                duration = duration,
                enemy = gameObject
            });
        }

        /// <summary>被击杀：发 `OnKilled` 后退场。只有真的被打死才走这里。</summary>
        private void Die(string killerOrganId, Vector2 deathPoint)
        {
            if (_isDead)
                return;

            _isDead = true;

            EnemyEvents.RaiseKilled(new EnemyEvents.KilledInfo
            {
                enemyId = Id,
                deathPoint = deathPoint,
                killerOrganId = killerOrganId,
                enemy = gameObject
            });

            ReleaseOrDestroy();
        }

        /// <summary>
        /// 波次到点清场：把敌人从战场移除，**不发 `OnKilled`、不产生掉落物**。
        /// 与 <see cref="Die"/> 是两条完全分开的路径。
        /// </summary>
        /// <returns>是否真的移除了（已经死掉的返回 false）。</returns>
        public bool RemoveFromBattlefield()
        {
            if (_isDead)
                return false;

            // 标记为已死，防止清场途中有别的路径再触发一次死亡结算。
            _isDead = true;
            currentHealth = 0f;
            ReleaseOrDestroy();
            return true;
        }

        /// <summary>
        /// 退场方式：池化对象归还池，非池化对象销毁。
        /// 两处退场（<see cref="Die"/> 与 <see cref="RemoveFromBattlefield"/>）共用，
        /// 保证"被杀"与"清场"在回收行为上一致，不会一边漏回池。
        /// </summary>
        private void ReleaseOrDestroy()
        {
            if (IsPooled)
            {
                SimplePool.Return(gameObject, PoolKey);
                return;
            }

            Destroy(gameObject);
        }

        /// <summary>清空战场上的全部敌人，用于波次结束。不发击杀信号、不掉落。</summary>
        public static int ClearBattlefield()
        {
            // 先快照：Destroy 是延迟的，但 OnDestroy 可能在任何时刻把元素移出原列表。
            var snapshot = s_ActiveEnemies.ToArray();
            var cleared = 0;

            for (var i = 0; i < snapshot.Length; i++)
            {
                var enemy = snapshot[i];
                if (enemy != null && enemy.RemoveFromBattlefield())
                    cleared++;
            }

            return cleared;
        }
    }
}
