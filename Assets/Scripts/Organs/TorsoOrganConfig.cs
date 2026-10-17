using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    [CreateAssetMenu(menuName = "SpringUp/Torso Config")]
    public sealed class TorsoOrganConfig : ScriptableObject
    {
        [SerializeField] private string organId = "torso_overdrive";
        [SerializeField] private string displayName = "过载躯干";
        [SerializeField] private string description;
        [SerializeField, Min(.01f)] private float damage = 1, probability = 1, frequency = 1, maxHealth = 1;
        [SerializeField, Min(.01f)] private float moveSpeed = 1, turnSpeed = 1, view = 1, incomingDamage = 1, invulnerability = 1;
        [SerializeField, Range(0, 1)] private float lifeSteal;
        [SerializeField] private bool directionalMovement;
        [SerializeField, Min(.01f)] private float frontDamage = 1;
        [SerializeField, Range(0, 180)] private float frontHalfAngle = 45;
        public string DisplayName => displayName;
        public bool TryCreateDefinition(out OrganDefinition definition, out string error)
        {
            definition = null; error = null;
            try
            {
                var settings = new TorsoSettings(damage, probability, frequency, maxHealth, moveSpeed, turnSpeed,
                    view, incomingDamage, invulnerability, lifeSteal, directionalMovement, frontDamage, frontHalfAngle, description);
                definition = new OrganDefinition(organId, displayName, OrganType.Torso, torso: settings);
                return true;
            }
            catch (ArgumentException ex) { error = displayName + ": " + ex.Message; return false; }
        }
    }
    public sealed class TorsoSettings
    {
        public readonly float Damage, Probability, Frequency, MaxHealth, MoveSpeed, TurnSpeed, View, IncomingDamage, Invulnerability, LifeSteal, FrontDamage, FrontHalfAngle;
        public readonly bool DirectionalMovement;
        public readonly string Description;
        public TorsoSettings(float damage = 1, float probability = 1, float frequency = 1, float maxHealth = 1,
            float moveSpeed = 1, float turnSpeed = 1, float view = 1, float incomingDamage = 1, float invulnerability = 1,
            float lifeSteal = 0, bool directionalMovement = false, float frontDamage = 1, float frontHalfAngle = 45, string description = "")
        {
            foreach (float value in new[] { damage, probability, frequency, maxHealth, moveSpeed, turnSpeed, view, incomingDamage, invulnerability, frontDamage })
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (float.IsNaN(lifeSteal) || lifeSteal < 0 || lifeSteal > 1) throw new ArgumentOutOfRangeException(nameof(lifeSteal));
            if (float.IsNaN(frontHalfAngle) || frontHalfAngle < 0 || frontHalfAngle > 180) throw new ArgumentOutOfRangeException(nameof(frontHalfAngle));
            Damage = damage; Probability = probability; Frequency = frequency; MaxHealth = maxHealth; MoveSpeed = moveSpeed;
            TurnSpeed = turnSpeed; View = view; IncomingDamage = incomingDamage; Invulnerability = invulnerability;
            LifeSteal = lifeSteal; DirectionalMovement = directionalMovement; FrontDamage = frontDamage; FrontHalfAngle = frontHalfAngle;
            var parts = new List<string>();
            void Factor(string label, float value) { if (value != 1) parts.Add($"{label}×{value:g}"); }
            Factor("伤害", Damage); Factor("概率", Probability); Factor("触发频率", Frequency); Factor("最大生命", MaxHealth);
            Factor("移速", MoveSpeed); Factor("转向", TurnSpeed); Factor("视野", View); Factor("受到伤害", IncomingDamage); Factor("无敌时间", Invulnerability);
            if (LifeSteal > 0) parts.Add($"实际伤害吸血 {LifeSteal:P0}");
            if (FrontDamage != 1) parts.Add($"正前方±{FrontHalfAngle:g}°伤害×{FrontDamage:g}");
            if (DirectionalMovement) parts.Add("移动沿转向后的朝向");
            Description = string.Join("；", parts) + "。躯干专用，装卸重新计算。";
        }
    }
    // Rebuilt from equipped immutable definitions; removing a torso never divides accumulated values.
    public sealed class TorsoLoadout
    {
        private readonly List<TorsoSettings> organs = new List<TorsoSettings>();
        public float Damage { get; private set; } = 1;
        public float Probability { get; private set; } = 1;
        public float Frequency { get; private set; } = 1;
        public float MaxHealth { get; private set; } = 1;
        public float MoveSpeed { get; private set; } = 1;
        public float TurnSpeed { get; private set; } = 1;
        public float View { get; private set; } = 1;
        public float IncomingDamage { get; private set; } = 1;
        public float Invulnerability { get; private set; } = 1;
        public float LifeSteal { get; private set; }
        public bool DirectionalMovement { get; private set; }
        public void Set(IEnumerable<TorsoSettings> values)
        {
            organs.Clear();
            if (values != null) foreach (var value in values) if (value != null) organs.Add(value);
            Damage = Probability = Frequency = MaxHealth = MoveSpeed = TurnSpeed = View = IncomingDamage = Invulnerability = 1;
            LifeSteal = 0; DirectionalMovement = false;
            foreach (var value in organs)
            {
                Damage *= value.Damage; Probability *= value.Probability; Frequency *= value.Frequency;
                MaxHealth *= value.MaxHealth; MoveSpeed *= value.MoveSpeed; TurnSpeed *= value.TurnSpeed; View *= value.View;
                IncomingDamage *= value.IncomingDamage; Invulnerability *= value.Invulnerability;
                LifeSteal += value.LifeSteal; DirectionalMovement |= value.DirectionalMovement;
            }
            LifeSteal = Mathf.Clamp01(LifeSteal);
        }
        public float FrontMultiplier(Vector2 owner, Vector2 forward, Vector2 target)
        {
            float result = 1;
            Vector2 delta = target - owner;
            foreach (var value in organs)
                if (delta.sqrMagnitude < .000001f || Vector2.Angle(forward, delta) <= value.FrontHalfAngle + .0001f)
                    result *= value.FrontDamage;
            return result;
        }
        public static CastEvent ScaleDamage(CastEvent hit, float scale)
            => new CastEvent(hit.TargetId, hit.Damage * scale, hit.OriginDefinitionId, hit.OriginInstanceId,
                hit.Context, hit.Status, hit.Shape, hit.Radius, hit.BlackHole, hit.EffectCenter, hit.Damage == 0);
    }
}
