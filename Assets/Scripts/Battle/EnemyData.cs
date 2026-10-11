using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>敌人档次，对应 <c>接口草案.md</c> §6.2 的 <c>rank</c> 字段。</summary>
    public enum EnemyRank
    {
        /// <summary>普通。</summary>
        Normal = 0,

        /// <summary>精英。</summary>
        Elite = 1,

        /// <summary>Boss。</summary>
        Boss = 2
    }

    /// <summary>
    /// 敌人数据字段，与 <c>接口草案.md</c> §6.2 的十二字段表一一对应。
    ///
    /// 定位与 <see cref="PlayerStatsBase"/> 一致：这里是**基础值的存放处**，
    /// 运行时状态（当前血量等）在 <see cref="EnemyRuntime"/>。
    /// 与 <c>Event</c> 对齐的三个字段（<c>projectileCount</c> / <c>projectileSpeed</c> /
    /// <c>attackRadius</c>）在 §6.2 里就注明了「对齐 Event.xxx」，注释里保留该对应关系。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/敌人数据（EnemyData）")]
    public class EnemyData : MonoBehaviour
    {
        [Header("身份")]
        [Tooltip("敌人编号；Event.targets[] 引用它。")]
        [SerializeField] private string id = "enemy";

        [Tooltip("档次：普通 / 精英 / Boss。")]
        [SerializeField] private EnemyRank rank = EnemyRank.Normal;

        [Header("移动与生存")]
        [Tooltip("移速，米/秒。")]
        [SerializeField] private float moveSpeed = 3f;

        [Tooltip("血量上限，点。")]
        [SerializeField] private float maxHealth = 20f;

        [Header("攻击（每种敌人只有一个攻击方式）")]
        [Tooltip("攻击力；唯一的伤害来源字段。")]
        [SerializeField] private float attackPower = 5f;

        [Tooltip("攻击冷却，秒。")]
        [SerializeField] private float attackCooldown = 1.5f;

        [Tooltip("攻击距离，米。进入该距离即发起攻击。")]
        [SerializeField] private float attackRange = 1.2f;

        [Tooltip("投射物数；对齐 Event.count。")]
        [SerializeField] private int projectileCount = 1;

        [Tooltip("投射物速度；对齐 Event.speed。")]
        [SerializeField] private float projectileSpeed = 8f;

        [Tooltip("攻击判定范围；对齐 Event.radius。")]
        [SerializeField] private float attackRadius = 1f;

        [Header("掉落与受控")]
        [Tooltip("掉落表引用。归程序 D 的掉落系统使用。")]
        [SerializeField] private string dropTableId = "";

        [Tooltip("能否被黑洞吸附（坍缩体需要）。")]
        [SerializeField] private bool canBeDisplaced = true;

        /// <summary>敌人编号；`Event.targets[]` 引用它。</summary>
        public string Id { get { return id; } }

        /// <summary>档次。</summary>
        public EnemyRank Rank { get { return rank; } }

        /// <summary>移速，米/秒。</summary>
        public float MoveSpeed { get { return moveSpeed; } }

        /// <summary>血量上限，点。</summary>
        public float MaxHealth { get { return maxHealth; } }

        /// <summary>攻击力。</summary>
        public float AttackPower { get { return attackPower; } }

        /// <summary>攻击冷却，秒。</summary>
        public float AttackCooldown { get { return attackCooldown; } }

        /// <summary>攻击距离，米。</summary>
        public float AttackRange { get { return attackRange; } }

        /// <summary>投射物数；对齐 `Event.count`。</summary>
        public int ProjectileCount { get { return projectileCount; } }

        /// <summary>投射物速度；对齐 `Event.speed`。</summary>
        public float ProjectileSpeed { get { return projectileSpeed; } }

        /// <summary>攻击判定范围；对齐 `Event.radius`。</summary>
        public float AttackRadius { get { return attackRadius; } }

        /// <summary>掉落表引用。</summary>
        public string DropTableId { get { return dropTableId; } }

        /// <summary>能否被黑洞吸附。</summary>
        public bool CanBeDisplaced { get { return canBeDisplaced; } }

        /// <summary>
        /// 覆盖射程与弹速。**只给运行时（发行版）路径用**：
        /// 编辑器路径走 <c>SerializedObject</c> 写同一批字段，两者结果一致。
        /// 灰盒阶段的临时入口，正式数值由策划的配置表接管后应删除。
        /// </summary>
        public void ApplyGreyboxTuning(float range, float projectileSpeedValue, float cooldown)
        {
            attackRange = range;
            projectileSpeed = projectileSpeedValue;
            attackCooldown = cooldown;
        }
    }
}
