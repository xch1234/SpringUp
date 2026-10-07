using System;

namespace SpringUp.Organs
{
    // 器官的公共配置。同种器官可以共用一份配置。
    // 目前只加入直接伤害。投射物和状态字段以后再补。
    public sealed class OrganDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public BodyPart Part { get; }
        public OrganType Type { get; }
        public float Damage { get; }
        public TriggerOn TriggerOn { get; }
        public TriggerTargetKind TriggerTarget { get; }

        public OrganDefinition(string id, string displayName, BodyPart part, OrganType type, float damage = 0f,
            TriggerOn triggerOn = TriggerOn.Kill, TriggerTargetKind triggerTarget = TriggerTargetKind.SelfSlots)
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

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Part = part;
            Type = type;
            Damage = damage;
            TriggerOn = triggerOn;
            TriggerTarget = triggerTarget;
        }
    }
}
