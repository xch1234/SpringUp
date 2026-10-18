using System;

namespace SpringUp.Organs
{
    // 器官的公共配置。同种器官可以共用一份配置。
    // 配置不可变；支持直接伤害及一种附带状态，目标上的状态计时另外保存。
    public sealed class OrganDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        // 仅保留旧调用/演示的分类信息，不再用于装配或选择运行管道。
        public BodyPart Part { get; }
        public OrganMountKind MountKind => Type == OrganType.Torso ? OrganMountKind.Torso : OrganMountKind.Pipeline;
        public OrganType Type { get; }
        public float Damage { get; }
        public float TargetingRange { get; }
        public TriggerOn TriggerOn { get; }
        public TriggerTargetKind TriggerTarget { get; }
        public StatusEffectData Status { get; }
        public AttackShape Shape { get; }
        public float Radius { get; }
        public BlackHoleData BlackHole { get; }
        public StraightProjectileSettings Projectile { get; }
        public float ChargeCost { get; }
        public TriggerSettings Trigger { get; }
        public TorsoSettings Torso { get; }
        public bool CanAttack => Type == OrganType.Actuator && (Damage > 0f || Shape == AttackShape.BlackHole || Projectile != null);

        public OrganDefinition(string id, string displayName, OrganType type, float damage = 0f,
            TriggerOn triggerOn = TriggerOn.Kill, TriggerTargetKind triggerTarget = TriggerTargetKind.NextSlot,
            StatusEffectData status = null, AttackShape shape = AttackShape.SingleTarget, float radius = 0f,
            BlackHoleData blackHole = null, float chargeCost = 20f, StraightProjectileSettings projectile = null, TriggerSettings trigger = null, TorsoSettings torso = null, float targetingRange = AttackTargeting.DefaultRange)
            : this(id, displayName, BodyPart.Hand, type, damage, triggerOn, triggerTarget, status, shape, radius, blackHole, chargeCost, projectile, trigger, torso, targetingRange) { }

        public OrganDefinition(string id, string displayName, BodyPart part, OrganType type, float damage = 0f,
            TriggerOn triggerOn = TriggerOn.Kill, TriggerTargetKind triggerTarget = TriggerTargetKind.NextSlot,
            StatusEffectData status = null, AttackShape shape = AttackShape.SingleTarget, float radius = 0f,
            BlackHoleData blackHole = null, float chargeCost = 20f, StraightProjectileSettings projectile = null, TriggerSettings trigger = null, TorsoSettings torso = null, float targetingRange = AttackTargeting.DefaultRange)
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
            if (float.IsNaN(targetingRange) || float.IsInfinity(targetingRange) || targetingRange <= 0f)
                throw new ArgumentOutOfRangeException(nameof(targetingRange), "索敌范围必须是有限的正数。");
            TargetingRange = targetingRange;
            Damage = damage;
            TriggerOn = triggerOn;
            TriggerTarget = type == OrganType.Trigger ? TriggerTargetKind.NextSlot : triggerTarget;
            Status = status;
            Shape = shape;
            Radius = radius;
            BlackHole = blackHole;
            bool summon = projectile is PatternActuatorSettings settings && settings.Pattern == ActuatorPattern.Summon;
            if (projectile != null && (type != OrganType.Actuator || shape != AttackShape.SingleTarget || (damage <= 0 && !summon)))
                throw new ArgumentException("弹体需要正伤害的执行器定义。", nameof(projectile));
            Projectile = projectile;
            Trigger = trigger; Torso = torso;
            if ((type == OrganType.Torso) != (torso != null)) throw new ArgumentException("躯干定义必须带有躯干配置。");
            ChargeCost = type != OrganType.Actuator ? 0f : chargeCost;
        }
    }
}
