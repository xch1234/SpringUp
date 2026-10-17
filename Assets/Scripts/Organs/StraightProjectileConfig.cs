using System;
using UnityEngine;

namespace SpringUp.Organs
{
    [CreateAssetMenu(menuName = "SpringUp/Actuators/Straight Projectile")]
    public sealed class StraightProjectileConfig : OrganActuatorConfig
    {
        [SerializeField, Min(.01f), Tooltip("飞行速度，世界单位/秒；样板初值，可调整。")]
        private float speed = 8f;
        [SerializeField, Min(.01f), Tooltip("子弹碰撞半径，表现尺寸随之变化。")]
        private float hitRadius = .12f;
        [SerializeField, Min(.01f), Tooltip("未命中时最多存在多少秒。")]
        private float lifetime = 2f;

        protected override OrganDefinition CreateDefinition()
        {
            if (Damage <= 0f) throw new ArgumentOutOfRangeException(nameof(Damage));
            return new OrganDefinition(OrganId, DisplayName, OrganType.Actuator, Damage,
                chargeCost: ChargeCost, projectile: new StraightProjectileSettings(speed, hitRadius, lifetime), targetingRange: TargetingRange);
        }
    }

    public class StraightProjectileSettings
    {
        public float Speed { get; }
        public float HitRadius { get; }
        public float Lifetime { get; }
        public StraightProjectileSettings(float speed, float hitRadius, float lifetime)
        {
            Validate(speed, nameof(speed)); Validate(hitRadius, nameof(hitRadius)); Validate(lifetime, nameof(lifetime));
            Speed = speed; HitRadius = hitRadius; Lifetime = lifetime;
        }
        private static void Validate(float value, string name)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(name); }
    }
}
