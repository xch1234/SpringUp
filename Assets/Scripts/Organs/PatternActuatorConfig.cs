using System;
using UnityEngine;

namespace SpringUp.Organs
{
    public enum ActuatorPattern { Orbit, Spiral, Laser, Homing, Fog, Vampire, Triple, Sucker, Missile, ChargedLaser, Spore, Summon }

    [CreateAssetMenu(menuName = "SpringUp/Actuators/Pattern")]
    public sealed class PatternActuatorConfig : OrganActuatorConfig
    {
        [SerializeField] private ActuatorPattern pattern;
        [SerializeField] private float speed = 8, hitRadius = .12f, lifetime = 4;
        [SerializeField] private float orbitRadius = 1, angularSpeed = 180, range = 6;
        [SerializeField] private float areaRadius = 1, interval = .5f, lifeSteal = .2f, slowMultiplier = .5f;
        [SerializeField] private float spreadAngle = 60, explosionDamage = 5, chargeTime = 1, driftDuration = 1;
        [SerializeField] private float health = 20, healthDecay = 2, followDistance = .8f;

        protected override OrganDefinition CreateDefinition() => new OrganDefinition(OrganId, DisplayName,
            OrganType.Actuator, Damage, chargeCost: ChargeCost, projectile: new PatternActuatorSettings(pattern,
                speed, hitRadius, lifetime, orbitRadius, angularSpeed, range, areaRadius, interval,
                lifeSteal, slowMultiplier, spreadAngle, explosionDamage, chargeTime, driftDuration, health, healthDecay, followDistance));
    }

    public sealed class PatternActuatorSettings : StraightProjectileSettings
    {
        public ActuatorPattern Pattern { get; }
        public float OrbitRadius { get; }
        public float AngularSpeed { get; }
        public float Range { get; }
        public float AreaRadius { get; }
        public float Interval { get; }
        public float LifeSteal { get; }
        public float SlowMultiplier { get; }
        public float SpreadAngle { get; }
        public float ExplosionDamage { get; }
        public float ChargeTime { get; }
        public float DriftDuration { get; }
        public float Health { get; }
        public float HealthDecay { get; }
        public float FollowDistance { get; }
        public bool RequiresTarget => Pattern != ActuatorPattern.Orbit && Pattern != ActuatorPattern.Fog && Pattern != ActuatorPattern.Summon;

        public PatternActuatorSettings(ActuatorPattern pattern, float speed = 8, float radius = .12f, float lifetime = 4,
            float orbitRadius = 1, float angularSpeed = 180, float range = 6, float areaRadius = 1, float interval = .5f,
            float lifeSteal = .2f, float slowMultiplier = .5f, float spreadAngle = 60, float explosionDamage = 5,
            float chargeTime = 1, float driftDuration = 1, float health = 20, float healthDecay = 2, float followDistance = .8f)
            : base(speed, radius, lifetime)
        {
            if (!Enum.IsDefined(typeof(ActuatorPattern), pattern)) throw new ArgumentOutOfRangeException(nameof(pattern));
            foreach (float value in new[] { orbitRadius, angularSpeed, range, areaRadius, interval, chargeTime, health, healthDecay })
                Validate(value, true);
            foreach (float value in new[] { lifeSteal, slowMultiplier, spreadAngle, explosionDamage, driftDuration, followDistance })
                Validate(value, false);
            if (lifeSteal > 1 || slowMultiplier > 1 || spreadAngle > 180 || driftDuration > lifetime)
                throw new ArgumentException("比例须在 0～1，散射总角度须在 0～180，漂浮时间不能超过寿命。");
            Pattern = pattern; OrbitRadius = orbitRadius; AngularSpeed = angularSpeed; Range = range;
            AreaRadius = areaRadius; Interval = interval; LifeSteal = lifeSteal; SlowMultiplier = slowMultiplier;
            SpreadAngle = spreadAngle; ExplosionDamage = explosionDamage; ChargeTime = chargeTime;
            DriftDuration = driftDuration; Health = health; HealthDecay = healthDecay; FollowDistance = followDistance;
        }
        private static void Validate(float value, bool positive)
        { if (float.IsNaN(value) || float.IsInfinity(value) || (positive ? value <= 0 : value < 0)) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
}
