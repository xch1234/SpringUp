using System;
using System.Linq;
using SpringUp.Laboratory;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.LaboratoryEditor
{
    public static class LaboratorySettlementChecks
    {
        private static void Require(bool value, string message) { if (!value) throw new Exception("[Laboratory Settlement] " + message); }
        public static void Run(LaboratoryPresenter presenter, LaboratoryCombatDemo demo)
        {
            var equipment = (TemporaryEquipment)presenter.source.Equipment;
            int Total() => equipment.Inventory.Count + Enumerable.Range(0, 4).Sum(p => Enumerable.Range(0, equipment.SlotCount((LaboratoryPart)p)).Count(s => equipment.GetSlot((LaboratoryPart)p, s) != null));
            int total = Total();
            void Empty() { for (int p = 0; p < 4; p++) for (int s = 0; s < equipment.SlotCount((LaboratoryPart)p); s++) equipment.TryUnequip((LaboratoryPart)p, s, out _); }
            LaboratoryItem Equip(string id, int slot = 0)
            {
                var item = equipment.Inventory.First(x => x.DefinitionId == id);
                Require(equipment.TryEquip(item.InstanceId, LaboratoryPart.Hand, slot, out _), "安装 " + id); return item;
            }
            void Step() => demo.Pipeline.GetBody(BodyPart.Hand).Tick(1);
            var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
            Require(definitions.Count == 7, "真实目录七种配置");
            foreach (var definition in definitions)
            {
                Empty(); var item = Equip(definition.Id);
                Require(ReferenceEquals(item.Definition, LaboratoryCombatDemo.Resolve(item)), "显示与结算共用快照");
                Step();
                Require(demo.targets[0].CurrentHealth == demo.targetHealth - definition.Damage, "七种器官真实伤害 " + definition.Id);
                Require(Total() == total, "七种装卸守恒");
            }
            Empty(); var first = Equip("fist"); var second = Equip("fist", 1);
            Require(first.InstanceId != second.InstanceId && first.DefinitionId == second.DefinitionId, "库存实例与配置分离");
            Require(demo.InventoryOrigins.Values.Contains(first.InstanceId) && demo.InventoryOrigins.Values.Contains(second.InstanceId), "战斗副本映射库存身份");
            var pipeline = demo.Pipeline; Step(); float hp = demo.targets[0].CurrentHealth;
            presenter.SelectSlot(1, 0); presenter.SelectSlot(1, 0);
            var candidate = equipment.Inventory.First(); presenter.SelectInventory(candidate.InstanceId); presenter.SelectInventory(candidate.InstanceId);
            Require(demo.Pipeline == pipeline && demo.targets[0].CurrentHealth == hp, "选择与取消不重置");
            demo.restartButton.onClick.Invoke(); Require(demo.Pipeline != pipeline && demo.targets[0].CurrentHealth == demo.targetHealth, "手动重启");
            demo.Initialize(); demo.Initialize(); Step();
            Require(demo.targets[0].CurrentHealth == demo.targetHealth - first.Definition.Damage, "重复初始化无重复订阅");
            Empty(); Equip("tnt"); Step();
            float damage = definitions.Single(d => d.Id == "tnt").Damage;
            Require(demo.targets.All(t => t.CurrentHealth == demo.targetHealth - damage), "默认 TNT 命中两个目标");
            demo.Restart(); demo.targets[1].transform.position = new Vector3(100, 0); Step();
            Require(demo.targets[1].CurrentHealth == demo.targetHealth, "真实坐标边界外不受伤");
            // 仅修改内存克隆，不污染真实资源；验证改 A 配置后卡片与结算一起变化。
            var config = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/Organs/Hand/Fist.asset"));
            try
            {
                var edit = new SerializedObject(config); edit.FindProperty("damage").floatValue = 7; edit.ApplyModifiedPropertiesWithoutUndo();
                Require(config.TryCreateDefinition(out var changed, out _), "修改配置生成快照");
                Empty(); var item = new LaboratoryItem("changed_fist", changed); equipment.AddSample(item); total++;
                equipment.TryEquip(item.InstanceId, LaboratoryPart.Hand, 0, out _); Step();
                Require(item.Description.Contains("7") && demo.targets[0].CurrentHealth == demo.targetHealth - 7, "配置伤害同步显示与结算");
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
            var tntConfig = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/Organs/Hand/TNT.asset"));
            try
            {
                var edit = new SerializedObject(tntConfig); edit.FindProperty("explosionRadius").floatValue = .75f; edit.ApplyModifiedPropertiesWithoutUndo();
                tntConfig.TryCreateDefinition(out var changed, out _); Empty();
                var item = new LaboratoryItem("radius_tnt", changed); equipment.AddSample(item); total++;
                equipment.TryEquip(item.InstanceId, LaboratoryPart.Hand, 0, out _); Step();
                Require(demo.targets[0].CurrentHealth == demo.targetHealth - damage && demo.targets[1].CurrentHealth == demo.targetHealth, "修改半径：边界包含，边界外排除");
            }
            finally { UnityEngine.Object.DestroyImmediate(tntConfig); }
            Empty(); Equip("rust_knife"); Step();
            var bleed = definitions.Single(d => d.Id == "rust_knife").Status;
            hp = demo.targets[0].CurrentHealth; demo.Advance(bleed.TickInterval);
            Require(demo.targets[0].CurrentHealth == hp - bleed.TickDamage, "流血真实间隔扣血");
            demo.Advance(bleed.Duration); Require(demo.StatusAt(0).BleedRemaining == 0, "流血到期");
            // 独立确定性状态检查不改变预览概率，概率边界由 A 既有检查覆盖。
            var slow = definitions.Single(d => d.Id == "slime_gland");
            var stun = definitions.Single(d => d.Id == "steel_pipe");
            var cause = new CastEvent(demo.targets[0].TargetId, slow.Damage, slow.Id, "status-test", new CastContext(), slow.Status);
            demo.StatusAt(0).TryApply(cause, 0); demo.StatusAt(0).TryApply(cause, 0);
            Require(demo.StatusAt(0).SlowStacks == 2 && Mathf.Approximately(demo.StatusAt(0).SlowMultiplier, 1 - 2 * slow.Status.SlowPerStack), "减速比例");
            demo.Advance(slow.Status.Duration); Require(demo.StatusAt(0).SlowStacks == 1, "逐层消退");
            demo.Advance(slow.Status.Duration); Require(demo.StatusAt(0).SlowStacks == 0, "减速到期");
            demo.StatusAt(0).TryApply(new CastEvent(demo.targets[0].TargetId, stun.Damage, stun.Id, "status-test", new CastContext(), stun.Status), 0);
            demo.Advance(stun.Status.Duration); Require(demo.StatusAt(0).StunRemaining == 0, "眩晕到期");
            Empty(); Equip("collapse_body"); Step(); var position = demo.targets[0].transform.position;
            Require(demo.BlackHoles.Count == 1 && demo.statusText != null, "真实黑洞生成");
            demo.Advance(.1f); Require(demo.statusText.text.Contains("范围内 A、B"), "黑洞范围信息");
            demo.Advance(definitions.Single(d => d.Id == "collapse_body").BlackHole.Duration);
            Require(demo.BlackHoles.Count == 0 && demo.targets[0].transform.position == position && !demo.statusText.text.Contains("范围内"), "黑洞到期且无位移");
            Empty(); Equip("tnt"); Equip("multi_tentacle", 1); Equip("multi_tentacle", 2);
            foreach (var target in demo.targets) target.InitializeForDemo(target.TargetId, damage);
            var ids = demo.targets.Select(t => t.TargetId).ToArray();
            CastEvent oldAttack = default;
            Action<DemoTarget, CastEvent, float> remember = (target, attack, amount) => oldAttack = attack;
            demo.targets[0].Damaged += remember;
            Step(); demo.targets[0].Damaged -= remember;
            Require(!demo.targets[0].TryApplyAttack(oldAttack), "死亡后重复攻击不能重复通知");
            Require(demo.DeathCount == 2 && demo.TriggerCount == 2 && demo.targets.All(t => !t.IsAlive), "整批范围击杀，同链每触手一次，允许空场");
            pipeline = demo.Pipeline; int triggers = demo.TriggerCount;
            demo.Advance(demo.replenishDelay - .1f); Require(demo.targets.All(t => !t.IsAlive), "补充延迟");
            demo.Advance(.11f);
            Require(demo.targets.All(t => t.CurrentHealth == demo.targetHealth) && demo.targets.Select(t => t.TargetId).Except(ids).Count() == 2, "满血新身份");
            Require(!demo.targets[0].TryApplyAttack(oldAttack), "新身份拒绝旧攻击" );
            Require(demo.Pipeline == pipeline && demo.TriggerCount == triggers && demo.DeathCount == 2, "补充不重置节拍也不触发击杀");
            Step(); Step(); Step(); Require(demo.targets[0].CurrentHealth < demo.targetHealth, "全死补充后继续");
            Empty(); Equip("rust_knife"); Equip("multi_tentacle", 1);
            var knife = definitions.Single(d => d.Id == "rust_knife");
            demo.targets[0].InitializeForDemo(demo.targets[0].TargetId, knife.Damage + bleed.TickDamage);
            Step(); demo.Advance(bleed.TickInterval);
            Require(demo.DeathCount == 1 && demo.TriggerCount == 1 && demo.StatusAt(0).BleedRemaining == 0, "流血延迟击杀保留来源并清理");
            // B 的流血来自同链追加；后来杀死 B 不能再次发动触手。
            float healthB = demo.targets[1].CurrentHealth;
            demo.targets[1].TryApplyAttack(new CastEvent(demo.targets[1].TargetId, healthB - bleed.TickDamage, "test", "test", new CastContext()));
            demo.Advance(bleed.TickInterval);
            Require(demo.DeathCount == 2 && demo.TriggerCount == 1, "同一原链后续流血击杀不重复触发");
            demo.Advance(demo.replenishDelay); Require(demo.StatusAt(0).BleedRemaining == 0 && demo.StatusAt(1).BleedRemaining == 0, "补充无旧状态");
            Empty(); var placeholder = equipment.Inventory.First(x => x.Part == LaboratoryPart.Head);
            equipment.TryEquip(placeholder.InstanceId, LaboratoryPart.Head, 0, out _);
            Require(demo.UnsupportedCount == 1 && demo.Pipeline.GetBody(BodyPart.Head).GetSlot(0) == null, "占位无假效果");
            Empty(); Equip("fist"); Equip("multi_tentacle", 3);
            Require(Total() == total, "整轮事务数量守恒");
            CheckInvalidCatalog();
            Debug.Log("[Laboratory Settlement Checks] PASS: 配置、七种装卸、实例、范围、状态、生命周期、触发链与守恒");
        }
        private static void CheckInvalidCatalog()
        {
            var catalog = UnityEngine.Object.Instantiate(OrganCatalog.LoadDefault());
            try
            {
                var edit = new SerializedObject(catalog); var array = edit.FindProperty("organs");
                var original = array.GetArrayElementAtIndex(1).objectReferenceValue;
                array.GetArrayElementAtIndex(1).objectReferenceValue = array.GetArrayElementAtIndex(0).objectReferenceValue;
                edit.ApplyModifiedPropertiesWithoutUndo();
                bool failed = false; try { catalog.CreateDefinitions(); } catch (InvalidOperationException e) { failed = e.Message.Contains("重复 ID"); }
                Require(failed, "重复 ID 明确拒绝");
                array.GetArrayElementAtIndex(1).objectReferenceValue = null; edit.ApplyModifiedPropertiesWithoutUndo();
                failed = false; try { catalog.CreateDefinitions(); } catch (InvalidOperationException e) { failed = e.Message.Contains("缺失"); }
                Require(failed, "缺失配置明确拒绝");
                var config = UnityEngine.Object.Instantiate((OrganConfig)original);
                try
                {
                    var organEdit = new SerializedObject(config); organEdit.FindProperty("statusEffect").objectReferenceValue = null; organEdit.ApplyModifiedPropertiesWithoutUndo();
                    array.GetArrayElementAtIndex(1).objectReferenceValue = config; edit.ApplyModifiedPropertiesWithoutUndo();
                    failed = false; try { catalog.CreateDefinitions(); } catch (InvalidOperationException e) { failed = e.Message.Contains("状态配置"); }
                    Require(failed, "状态关联错误明确拒绝");
                }
                finally { UnityEngine.Object.DestroyImmediate(config); }
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }
    }
}
