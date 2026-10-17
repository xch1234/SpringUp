using System;
using UnityEngine;

namespace SpringUp.Organs
{
    // Direct attacks use the same config -> definition path as projectile actuators.
    [CreateAssetMenu(menuName = "SpringUp/Actuators/Direct")]
    public sealed class DirectActuatorConfig : OrganActuatorConfig
    {
        [SerializeField] private AttackShape shape = AttackShape.SingleTarget;
        [SerializeField, Min(0f)] private float explosionRadius = 2f;
        [SerializeField, Min(0f)] private float blackHoleRadius = 3f;
        [SerializeField, Min(.05f)] private float blackHoleDuration = 5f;
        [SerializeField, Min(0f)] private float blackHolePullSpeed = 2f;
        [SerializeField, Tooltip("可选：命中后施加的状态资料。状态有自己的持续时间，不需要器官再次发动；黑洞仅位移，不施加命中状态。")]
        private StatusEffectConfig statusEffect;
        public StatusEffectConfig StatusEffect => statusEffect;

        protected override OrganDefinition CreateDefinition()
        {
            StatusEffectData status = null;
            if (shape != AttackShape.BlackHole && statusEffect != null && !statusEffect.TryCreateData(out status, out var error))
                throw new ArgumentException(error);
            bool hole = shape == AttackShape.BlackHole;
            return new OrganDefinition(OrganId, DisplayName, OrganType.Actuator, hole ? 0 : Damage,
                status: status, shape: shape,
                radius: hole ? blackHoleRadius : shape == AttackShape.Explosion ? explosionRadius : 0,
                blackHole: hole ? new BlackHoleData(blackHoleDuration, blackHolePullSpeed) : null,
                chargeCost: ChargeCost);
        }
    }
}
