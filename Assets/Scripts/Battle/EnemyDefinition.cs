using UnityEngine;
namespace TideBorne.Battle
{
    [CreateAssetMenu(menuName = "SpringUp/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [Header("身份与击杀收益")]
        public EnemyArchetype archetype;
        public bool allowRegularSpawn;
        [Min(0)] public int baseMarrow;
        public bool hasContactDamage, hasRangedAttack, hasProducer, hasHealer;
        public Color color;
        public string id;
        public EnemyRank rank;
        [Header("生存与攻击基础值")]
        [Min(0)] public float moveSpeed;
        [Min(0.01f)] public float maxHealth;
        public float attackPower;
        public float attackCooldown;
        public float attackRange;
        public int projectileCount;
        public float projectileSpeed;
        public float attackRadius;
        public string dropTableId;
        public bool canBeDisplaced;
        [Header("移动行为")]
        public EnemyMoveMode moveMode;
        public bool faceMoveDirection;
        public float facingLerpSpeed;
        public float stopAtAttackRangeRatio;
        public float minStopDistance;
        public float preferredDistance;
        public float distanceTolerance;
        [Header("远程与接触攻击")]
        public bool attackAutomatically;
        public float initialDelay;
        public bool useProjectiles;
        public float spreadAngleDegrees;
        public float contactRadius;
        public float damageIntervalOverride;
        [Header("产兵（保留全场数量上限）")]
        public EnemyArchetype productArchetype;
        public float produceInterval;
        public float spawnJitter;
        [Tooltip("全场 ActiveCount >= 此值 + 1 时暂停，含所有类型与所有工厂；不是每座工厂的产出计数。")]
        [Min(0)] public int maxAliveProducts;
        [Header("治疗")]
        [Min(0.01f)] public float healInterval;
        public float healRadius;
        public float healPercent;
        public bool healSelf;
        // Compatibility names for the prefab generator; values live only in this asset.
        public string displayName => archetype.ToString();
        public float health => maxHealth;
    }
}
