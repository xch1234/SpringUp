using UnityEngine;
namespace TideBorne.Battle
{
    public enum EnemyRank
    {
        /// <summary>普通。</summary>
        Normal = 0,

        /// <summary>精英。</summary>
        Elite = 1,

        /// <summary>Boss。</summary>
        Boss = 2
    }

    [DisallowMultipleComponent]
    public class EnemyData : MonoBehaviour
    {
        [SerializeField] private EnemyDefinition definition;
        public EnemyDefinition Definition => definition != null ? definition : EnemyCatalog.Load().Get(EnemyArchetype.Normal);
        public void Configure(EnemyDefinition value) { definition = value; }
        void Awake()
        {
            if (definition == null) return;
            SetBehaviour<EnemyContactDamage>(Definition.hasContactDamage);
            SetBehaviour<EnemyAttack>(Definition.hasRangedAttack);
            SetBehaviour<EnemyProducer>(Definition.hasProducer);
            SetBehaviour<EnemyHealer>(Definition.hasHealer);
        }
        void SetBehaviour<T>(bool allowed) where T : Behaviour
        {
            var component = GetComponent<T>();
            if (component == null && allowed) component = gameObject.AddComponent<T>();
            if (component != null) component.enabled = allowed;
        }
        public string Id => Definition.id;
        public EnemyRank Rank => Definition.rank;
        public float MoveSpeed => Definition.moveSpeed;
        public float MaxHealth => Definition.maxHealth;
        public float AttackPower => Definition.attackPower;
        public float AttackCooldown => Definition.attackCooldown;
        public float AttackRange => Definition.attackRange;
        public int ProjectileCount => Definition.projectileCount;
        public float ProjectileSpeed => Definition.projectileSpeed;
        public float AttackRadius => Definition.attackRadius;
        public string DropTableId => Definition.dropTableId;
        public bool CanBeDisplaced => Definition.canBeDisplaced;
    }
}
