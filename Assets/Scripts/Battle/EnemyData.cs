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
        private float healthMultiplier = 1, frequencyMultiplier = 1, damageMultiplier = 1, speedMultiplier = 1;
        public void ApplyWaveModifiers(EnemyWaveMix wave)
        {
            if (Definition.archetype == EnemyArchetype.Dummy) wave = null;
            healthMultiplier = EnemyWaveMix.ValidMultiplier(wave?.healthMultiplier ?? 1);
            frequencyMultiplier = EnemyWaveMix.ValidMultiplier(wave?.attackFrequencyMultiplier ?? 1);
            damageMultiplier = EnemyWaveMix.ValidMultiplier(wave?.damageMultiplier ?? 1);
            speedMultiplier = EnemyWaveMix.ValidMultiplier(wave?.moveSpeedMultiplier ?? 1);
        }
        public float ContactDamageInterval => ScaleAttackInterval(Definition.damageIntervalOverride > .01f
            ? Definition.damageIntervalOverride : Mathf.Max(.1f, Definition.attackCooldown));
        private float ScaleAttackInterval(float interval) => Mathf.Max(.01f, interval / frequencyMultiplier);
        public string Id => Definition.id;
        public EnemyRank Rank => Definition.rank;
        public float MoveSpeed => Definition.moveSpeed * speedMultiplier;
        public float MaxHealth => Definition.maxHealth * healthMultiplier;
        public float AttackPower => Definition.attackPower * damageMultiplier;
        public float AttackCooldown => ScaleAttackInterval(Definition.attackCooldown);
        public float AttackRange => Definition.attackRange;
        public int ProjectileCount => Definition.projectileCount;
        public float ProjectileSpeed => Definition.projectileSpeed;
        public float AttackRadius => Definition.attackRadius;
        public string DropTableId => Definition.dropTableId;
        public bool CanBeDisplaced => Definition.canBeDisplaced;
    }
}
