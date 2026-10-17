namespace SpringUp.Organs
{
    // 躯干不属于六槽管道，因此这里先只列三个部位。
    public enum BodyPart { Head, Hand, Leg }

    public enum OrganType { Actuator, Trigger, Torso }
    public enum OrganMountKind { Pipeline, Torso }

    // 装配资格的统一入口。历史 head/hand/leg 分类不限制普通器官的安装位置。
    public static class OrganEquipmentRules
    {
        public static bool CanEquip(OrganMountKind kind, string part)
            => kind == OrganMountKind.Torso ? part == "torso"
                : kind == OrganMountKind.Pipeline && (part == "head" || part == "hand" || part == "leg");
    }
    public enum AttackShape { SingleTarget, Explosion, BlackHole }

    // Attack conditions listen to the adjacent predecessor; owner conditions use player events.
    public enum TriggerOn { Kill, Hit, Started, Hurt, Heal, Ended, Debuff, Critical, Stationary }
    // Keep legacy numeric values for serialized assets; runtime normalizes them to NextSlot.
    public enum TriggerTargetKind { SelfSlots, FollowingSlots, NextSlot }
    public enum TriggerBlockReason { None, AlreadyTriggered, DepthLimit }
}
