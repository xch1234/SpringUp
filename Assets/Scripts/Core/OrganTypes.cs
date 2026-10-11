namespace SpringUp.Organs
{
    // 躯干不属于六槽管道，因此这里先只列三个部位。
    public enum BodyPart { Head, Hand, Leg }

    public enum OrganType { Actuator, Trigger }
    public enum AttackShape { SingleTarget, Explosion, BlackHole }

    // 第三步只实现击杀与同部位重触发，其余条件以后再加入。
    public enum TriggerOn { Kill }
    public enum TriggerTargetKind { SelfSlots }
    public enum TriggerBlockReason { None, AlreadyTriggered, DepthLimit }
}
