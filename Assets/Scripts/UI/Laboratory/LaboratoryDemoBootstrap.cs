using UnityEngine;
namespace SpringUp.Laboratory
{
    // 只提供本轮样例装备，正式库存由后续适配器接入。
    public sealed class LaboratoryDemoBootstrap : MonoBehaviour
    {
        [Min(1), Tooltip("临时躯干槽数；缩小时器官退回背包。")]
        public int torsoSlotCount = 2;
        private TemporaryEquipment equipment;
        public ILaboratoryEquipment Equipment { get { EnsureInitialized(); return equipment; } }
        public void EnsureInitialized()
        {
            if (equipment != null) return;
            equipment = new TemporaryEquipment(Mathf.Max(1, torsoSlotCount));
            var fist = Sample(LaboratoryPart.Hand, false);
            var trigger = Sample(LaboratoryPart.Hand, true);
            equipment.AddSample(fist); equipment.TryEquip(fist.InstanceId, LaboratoryPart.Hand, 0, out _);
            equipment.AddSample(trigger); equipment.TryEquip(trigger.InstanceId, LaboratoryPart.Hand, 3, out _);
            for (int p = 0; p < 4; p++)
                for (int i = 0; i < 3; i++) equipment.AddSample(Sample((LaboratoryPart)p, i == 1));
        }
        public void SyncTorsoCapacity()
        {
            EnsureInitialized(); torsoSlotCount = Mathf.Max(1, torsoSlotCount);
            equipment.ResizeTorso(torsoSlotCount);
        }
        private void Update()
        {
            if (equipment != null && equipment.SlotCount(LaboratoryPart.Torso) != Mathf.Max(1, torsoSlotCount)) SyncTorsoCapacity();
        }
        private static LaboratoryItem Sample(LaboratoryPart part, bool trigger)
        {
            if (part == LaboratoryPart.Torso)
                return new LaboratoryItem(System.Guid.NewGuid().ToString("N"), trigger ? "sample_shell" : "sample_compound_eye",
                    trigger ? "甲壳样例" : "复眼样例", part, LaboratoryOrganKind.TorsoSpecial,
                    "躯干装配演示样例；本轮不修改生命、视野等属性。");
            string name = part == LaboratoryPart.Hand ? (trigger ? "多肢触手" : "拳头") : (trigger ? "触发样例" : "攻击样例");
            string definitionId = part == LaboratoryPart.Hand ? (trigger ? "multi_tentacle" : "fist") :
                "sample_" + part.ToString().ToLowerInvariant() + (trigger ? "_trigger" : "_actuator");
            return new LaboratoryItem(System.Guid.NewGuid().ToString("N"), definitionId, name, part,
                trigger ? LaboratoryOrganKind.Trigger : LaboratoryOrganKind.Actuator,
                part == LaboratoryPart.Hand
                    ? (trigger ? "击杀时追加执行手部槽位；同一触发器每条链最多发动一次。" : "单目标攻击，每次伤害 5。")
                    : "仅供装配的占位样例，暂不支持动态测试。",
                trigger ? LaboratoryTriggerCondition.Kill : LaboratoryTriggerCondition.None,
                trigger ? LaboratoryTriggerTarget.SelfSlots : LaboratoryTriggerTarget.None);
        }
    }
}
