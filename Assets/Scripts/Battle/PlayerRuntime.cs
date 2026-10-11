using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 「运行时状态」表：战斗中被改的状态，与「属性」表（<see cref="PlayerStatsBase"/>）分开。
    /// 字段与 <c>接口草案.md</c> §5.3 的 <c>PlayerRuntime</c> 表一一对应。
    ///
    /// 分工：器官（程序 A）只管改属性，血量与派生速率由这里维护。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/玩家运行时状态（PlayerRuntime）")]
    public class PlayerRuntime : MonoBehaviour
    {
        [Tooltip("属性表。留空则自动取同一物体上的 PlayerStatsBase。")]
        [SerializeField] private PlayerStatsBase stats;

        [Header("运行时状态")]
        [Tooltip("当前血量。只由战斗结算与回血改动；属性变化导致上限变小时会被夹到新上限。")]
        [SerializeField] private float currentHealth = 100f;

        [Tooltip("是否启用 healthRegen 的持续回血。")]
        [SerializeField] private bool applyHealthRegen = true;

        [Header("派生值 · 部位执行速率（接口草案 §5.3）")]
        [Tooltip("头 / 手 / 腿 的实际执行速率，派生值，不可直接改。由程序 A 写入计时触发器的百分比之积。")]
        [SerializeField] private float[] partRate = new float[3];

        /// <summary>部位数量：头 / 手 / 腿。</summary>
        public const int PartCount = 3;

        /// <summary>属性表发生变化（含血量被夹取）时触发。</summary>
        public event System.Action<PlayerStatsSnapshot> StatsChanged;

        /// <summary>
        /// 玩家**实际受到伤害**后触发（已算过受伤倍率与固定减伤）。
        /// 这是受伤触发（`接口草案.md` 的 `trigger_on = 受伤`）应该接的事件。
        /// 注意与 <c>EnemyEvents.PlayerHurt</c> 的区别：那条是敌人的**请求**，载荷是原始攻击力；
        /// 这条是结算**结果**，载荷是真正扣掉的血量。
        /// </summary>
        public event System.Action<float> PlayerDamaged;

        /// <summary>当前血量。</summary>
        public float CurrentHealth { get { return currentHealth; } }

        /// <summary>当前血量上限，取自属性表最终值。</summary>
        public float MaxHealth { get { return Stats.BaseMaxHealth; } }

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

        /// <summary>上次广播出去的属性快照，用于抑制无变化的重复事件。</summary>
        private PlayerStatsSnapshot _lastSnapshot;
        private bool _hasLastSnapshot;

        /// <summary>
        /// 某个部位的实际执行速率。
        /// <c>partRate[i] = 该部位内所有计时触发器的百分比之积</c>（接口草案 §5.3，暂定相乘）。
        /// </summary>
        public float GetPartRate(int partIndex)
        {
            if (partRate == null || partIndex < 0 || partIndex >= partRate.Length)
            {
                Debug.LogError("[PlayerRuntime] 部位下标越界：" + partIndex, this);
                return 0f;
            }

            return partRate[partIndex];
        }

        /// <summary>实际步进间隔，秒：<c>PlayerStats.baseCooldown × partRate[i]</c>。</summary>
        public float GetStepInterval(int partIndex)
        {
            return Stats.BaseCooldown * GetPartRate(partIndex);
        }

        /// <summary>写入某个部位的执行速率。由程序 A 计算后调用。</summary>
        public void SetPartRate(int partIndex, float value)
        {
            if (partRate == null || partIndex < 0 || partIndex >= partRate.Length)
            {
                Debug.LogError("[PlayerRuntime] 部位下标越界：" + partIndex, this);
                return;
            }

            partRate[partIndex] = value;
        }

        /// <summary>扣血。返回实际扣掉的量（受当前血量限制，不会扣成负数）。</summary>
        public float TakeDamage(float amount)
        {
            if (amount <= 0f)
                return 0f;

            var applied = Mathf.Min(amount, currentHealth);
            currentHealth -= applied;
            return applied;
        }

        /// <summary>回血，会被当前血量上限夹住。</summary>
        public float Heal(float amount)
        {
            if (amount <= 0f)
                return 0f;

            var before = currentHealth;
            currentHealth = Mathf.Min(currentHealth + amount, MaxHealth);
            return currentHealth - before;
        }

        /// <summary>把当前血量设为上限。开局与调试用。</summary>
        [ContextMenu("当前血量 = 上限")]
        public void ResetHealthToMax()
        {
            currentHealth = MaxHealth;
        }

        private void Awake()
        {
            if (stats == null)
                stats = GetComponent<PlayerStatsBase>();

            if (stats == null)
            {
                Debug.LogError("[PlayerRuntime] 同一物体上找不到 PlayerStatsBase，属性无法读取。", this);
                enabled = false;
                return;
            }

            if (partRate == null || partRate.Length != PartCount)
                partRate = new float[PartCount];

            // 首次启动把血量夹到上限，避免 inspector 里的初值超过上限。
            currentHealth = Mathf.Clamp(currentHealth, 0f, MaxHealth);
            PublishSnapshotIfChanged(true);
        }

        private void OnEnable()
        {
            // 受伤触发（接口草案 §6.1 的 OnPlayerHurt）：订阅敌人信号，在这里统一结算减免。
            // 放在玩家侧而不是敌人侧：damageTakenMultiplier 与 armor 是玩家属性，
            // 让每个敌人各自算一遍会把减免逻辑复制到 N 个敌人上。
            EnemyEvents.PlayerHurt += OnPlayerHurt;
        }

        private void OnDisable()
        {
            EnemyEvents.PlayerHurt -= OnPlayerHurt;
        }

        /// <summary>
        /// 敌人打中玩家。伤害 = 攻击力 × 受伤倍率 − 固定减伤，最低 0。
        /// 结算后抛 <see cref="PlayerDamaged"/>。
        ///
        /// **刻意不复用 <c>EnemyEvents.PlayerHurt</c>**：那条事件的载荷定义是「伤害值、来源敌人 id」，
        /// 即敌人的原始攻击力。若把减免后的实际伤害再抛回同一条事件，
        /// 这个字段就同时代表两种量，任何按"原始攻击力"理解的订阅方都会算错。
        /// 而且"受伤再次抛受伤"会让受伤触发的连锁没有明确终点。
        /// </summary>
        private void OnPlayerHurt(EnemyEvents.PlayerHurtInfo info)
        {
            var applied = CalculateIncomingDamage(info.damage);
            if (applied <= 0f)
                return;

            var actual = TakeDamage(applied);
            if (actual <= 0f)
                return;

            var handler = PlayerDamaged;
            if (handler != null)
                handler(actual);
        }

        /// <summary>把敌人的攻击力换算成实际扣血：先乘受伤倍率，再减固定减伤。</summary>
        public float CalculateIncomingDamage(float attackPower)
        {
            if (attackPower <= 0f)
                return 0f;

            return Mathf.Max(0f, attackPower * Stats.DamageTakenMultiplier - Stats.Armor);
        }

        private void Update()
        {
            if (applyHealthRegen)
            {
                var regen = Stats.HealthRegen;
                if (regen > 0f && currentHealth < MaxHealth)
                    Heal(regen * Time.deltaTime);
            }

            // 属性可能被器官改动，每帧检查一次并广播（§5.4 丙：发变更事件，不轮询数值本身）。
            PublishSnapshotIfChanged(false);
        }

        /// <summary>
        /// 属性变化时广播。同时承担 §5.5 的边界规则：
        /// 上限变小时把当前血量夹到新上限；上限变大时不补满新增部分。
        /// </summary>
        private void PublishSnapshotIfChanged(bool force)
        {
            var snapshot = Stats.CreateSnapshot();

            // 上限变小 → 当前血量夹到新上限；变大不动（§5.5）。
            if (currentHealth > snapshot.baseMaxHealth)
                currentHealth = snapshot.baseMaxHealth;

            if (!force && _hasLastSnapshot && PlayerStatsBase.AreSnapshotsEqual(_lastSnapshot, snapshot))
                return;

            _lastSnapshot = snapshot;
            _hasLastSnapshot = true;

            Stats.RaiseStatsChanged(snapshot);

            var handler = StatsChanged;
            if (handler != null)
                handler(snapshot);
        }
    }
}
