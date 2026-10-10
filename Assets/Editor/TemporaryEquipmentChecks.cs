using System;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Laboratory;

namespace SpringUp.LaboratoryEditor
{
    // 可独立运行纯逻辑检查，也由实验室界面检查菜单调用。
    public static class TemporaryEquipmentChecks
    {
        public static void Run()
        {
            CheckIdentityAndMetadata();
            var service = new TemporaryEquipment(2);
            var a = Item(LaboratoryPart.Hand); var b = Item(LaboratoryPart.Hand);
            service.AddSample(a); service.AddSample(b);
            int changes = 0; service.Changed += () => changes++;
            Check(!service.TryUnequip(LaboratoryPart.Hand, 0, out _), "空槽不可拆下");
            Check(!service.TryEquip(a.InstanceId, LaboratoryPart.Head, 0, out _), "拒绝错误部位");
            Check(!service.TryEquip(a.InstanceId, LaboratoryPart.Hand, 6, out _), "拒绝无效槽位");
            Check(changes == 0 && service.Inventory.Count == 2, "失败操作不改变装备或库存");
            Check(service.TryEquip(a.InstanceId, LaboratoryPart.Hand, 0, out _), "安装成功");
            Check(!service.TryEquip(a.InstanceId, LaboratoryPart.Hand, 1, out _), "拒绝重复安装");
            Check(service.TryEquip(b.InstanceId, LaboratoryPart.Hand, 0, out _), "同名实例可替换");
            Check(ReferenceEquals(service.Inventory.Single(), a) && ReferenceEquals(service.GetSlot(LaboratoryPart.Hand, 0), b), "替换保留实例身份");
            Check(service.TryUnequip(LaboratoryPart.Hand, 0, out _), "拆下成功");
            Check(!service.TryUnequip(LaboratoryPart.Hand, 0, out _), "拒绝重复拆下");
            Check(changes == 3, "每次成功操作只通知一次");
            var torso = Item(LaboratoryPart.Torso); service.AddSample(torso);
            Check(service.TryEquip(torso.InstanceId, LaboratoryPart.Torso, 1, out _), "躯干安装成功");
            service.ResizeTorso(1);
            Check(service.Inventory.Contains(torso), "缩容退回已装备器官");
            service.ResizeTorso(8); Check(service.SlotCount(LaboratoryPart.Torso) == 8, "躯干扩容成功");
            var all = new List<LaboratoryItem> { a,b,torso };
            for (int p = 0; p < 4; p++) for (int i = 0; i < 9; i++)
            { var item = Item((LaboratoryPart)p); all.Add(item); service.AddSample(item); }
            Check(service.Inventory.Count > 30, "休整背包不限制容量");
            var random = new Random(42);
            for (int i = 0; i < 500; i++)
            {
                var part = (LaboratoryPart)random.Next(4); int slot = random.Next(service.SlotCount(part));
                if (random.Next(2) == 0 && service.Inventory.Count > 0)
                    service.TryEquip(service.Inventory[random.Next(service.Inventory.Count)].InstanceId, part, slot, out _);
                else service.TryUnequip(part, slot, out _);
                var current = new List<LaboratoryItem>(service.Inventory);
                for (int p = 0; p < 4; p++) for (int j = 0; j < service.SlotCount((LaboratoryPart)p); j++)
                { var item = service.GetSlot((LaboratoryPart)p,j); if (item != null) current.Add(item); }
                Check(current.Count == all.Count && current.Select(x => x.InstanceId).Distinct().Count() == all.Count && all.All(current.Contains), "混合操作后物品守恒");
            }
        }
        private static void CheckIdentityAndMetadata()
        {
            var original = new LaboratoryItem("external-42", "fist", "拳头", LaboratoryPart.Hand, LaboratoryOrganKind.Actuator, "");
            var refreshed = new LaboratoryItem("external-42", "fist", "拳头", LaboratoryPart.Hand, LaboratoryOrganKind.Actuator, "updated");
            Check(original.InstanceId == refreshed.InstanceId && original.DefinitionId == refreshed.DefinitionId, "展示刷新保留外部实例身份");
            var service = new TemporaryEquipment();
            service.AddSample(original);
            Reject(() => service.AddSample(refreshed), "拒绝重复库存实例");
            service.TryEquip(original.InstanceId, LaboratoryPart.Hand, 0, out _);
            Reject(() => service.AddSample(refreshed), "拒绝重复已装备实例");
            service.TryUnequip(LaboratoryPart.Hand, 0, out _);
            Check(service.Inventory.Single().InstanceId == "external-42", "装备事务保留外部实例身份");
            var trigger = new LaboratoryItem("external-43", "multi_tentacle", "任意显示名称", LaboratoryPart.Hand,
                LaboratoryOrganKind.Trigger, "不含规则关键词", LaboratoryTriggerCondition.Kill, LaboratoryTriggerTarget.SelfSlots);
            Check(trigger.TriggerCondition == LaboratoryTriggerCondition.Kill && trigger.TriggerTarget == LaboratoryTriggerTarget.SelfSlots, "触发规则不依赖显示文字");
            Reject(() => new LaboratoryItem(" ", "fist", "", LaboratoryPart.Hand, LaboratoryOrganKind.Actuator, ""), "拒绝空实例编号");
            Reject(() => new LaboratoryItem("id", " ", "", LaboratoryPart.Hand, LaboratoryOrganKind.Actuator, ""), "拒绝空配置编号");
            Reject(() => new LaboratoryItem("id", "def", "", LaboratoryPart.Hand, LaboratoryOrganKind.Trigger, ""), "拒绝缺失的触发规则");
            Reject(() => new LaboratoryItem("id", "def", "", LaboratoryPart.Torso, LaboratoryOrganKind.Actuator, ""), "拒绝非法躯干器官类型");
        }
        private static void Reject(Action action, string message)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new Exception("装备检查失败：" + message);
        }
        private static LaboratoryItem Item(LaboratoryPart part) => new LaboratoryItem(Guid.NewGuid().ToString("N"),
            "sample_" + part, "同名样例", part,
            part == LaboratoryPart.Torso ? LaboratoryOrganKind.TorsoSpecial : LaboratoryOrganKind.Actuator, "");
        private static void Check(bool valid, string message) { if (!valid) throw new Exception("装备检查失败：" + message); }
    }
}
