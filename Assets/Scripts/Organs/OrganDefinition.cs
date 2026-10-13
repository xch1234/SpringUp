using System;

namespace SpringUp.Organs
{
    // 器官的公共配置。同种器官可以共用一份配置。
    // 配置不可变；支持直接伤害及一种附带状态，目标上的状态计时另外保存。
    public sealed class OrganDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public BodyPart Part { get; }
        public OrganType Type { get; }
        public float Damage { get; }
        public TriggerOn TriggerOn { get; }
        public TriggerTargetKind TriggerTarget { get; }
        public StatusEffectData Status { get; }
        public AttackShape Shape { get; }
        public float Radius { get; }
        public BlackHoleData BlackHole { get; }
        public float ChargeCost { get; }
        public bool CanAttack => Type == OrganType.Actuator && (Damage > 0f || Shape == AttackShape.BlackHole);

        public OrganDefinition(string id, string displayName, BodyPart part, OrganType type, float damage = 0f,
            TriggerOn triggerOn = TriggerOn.Kill, TriggerTargetKind triggerTarget = TriggerTargetKind.SelfSlots,
            StatusEffectData status = null, AttackShape shape = AttackShape.SingleTarget, float radius = 0f,
            BlackHoleData blackHole = null, float chargeCost = 20f)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("器官配置编号不能为空。", nameof(id));
            if (!Enum.IsDefined(typeof(BodyPart), part))
                throw new ArgumentOutOfRangeException(nameof(part));
            if (!Enum.IsDefined(typeof(OrganType), type))
                throw new ArgumentOutOfRangeException(nameof(type));
            if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f)
                throw new ArgumentOutOfRangeException(nameof(damage), "伤害必须是有限的非负数。");
            if (!Enum.IsDefined(typeof(TriggerOn), triggerOn))
                throw new ArgumentOutOfRangeException(nameof(triggerOn));
            if (!Enum.IsDefined(typeof(TriggerTargetKind), triggerTarget))
                throw new ArgumentOutOfRangeException(nameof(triggerTarget));
            if (!Enum.IsDefined(typeof(AttackShape), shape)) throw new ArgumentOutOfRangeException(nameof(shape));
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (shape == AttackShape.BlackHole && blackHole == null) throw new ArgumentNullException(nameof(blackHole));
            if (float.IsNaN(chargeCost) || float.IsInfinity(chargeCost) || chargeCost < 0f)
                throw new ArgumentOutOfRangeException(nameof(chargeCost));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Part = part;
            Type = type;
            Damage = damage;
            TriggerOn = triggerOn;
            TriggerTarget = triggerTarget;
            Status = status;
            Shape = shape;
            Radius = radius;
            BlackHole = blackHole;
            ChargeCost = type == OrganType.Trigger ? 0f : chargeCost;
        }
    }
}
