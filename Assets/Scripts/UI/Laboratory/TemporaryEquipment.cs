using System;
using SpringUp.Organs;
using System.Collections.Generic;

namespace SpringUp.Laboratory
{
    // B 的界面数据投影，不是正式 JSON 配置，也不替代 A 的 OrganDefinition。
    // 与 A 接线时必须显式映射部位和类型，
    // 不得按枚举数值直接转换成 SpringUp.Organs 中的类型。
    public enum LaboratoryPart { Head, Hand, Leg, Torso }
    public enum LaboratoryOrganKind { Actuator, Trigger, TorsoSpecial }
    // 这里只描述样例规则，实际攻击和触发执行由 A 的管道负责。
    // None 表示缺少规则信息，不等同于策划中不触发其他器官的 NONE。
    public enum LaboratoryTriggerCondition { None, Kill, Hit, Started, Hurt, Heal, Ended, Debuff, Critical, Stationary }
    public enum LaboratoryTriggerTarget { None, SelfSlots, FollowingSlots, NextSlot }

    public sealed class LaboratoryItem
    {
        // 正式接入时保留库存实例编号和配置编号，刷新界面不能重建身份。
        public OrganDefinition Definition { get; }
        public LaboratoryItem(string instanceId, OrganDefinition definition)
            : this(instanceId, definition.Id, definition.DisplayName, definition.Type == OrganType.Torso ? LaboratoryPart.Torso : LaboratoryPart.Hand,
                definition.Type == OrganType.Torso ? LaboratoryOrganKind.TorsoSpecial : definition.Type == OrganType.Trigger ? LaboratoryOrganKind.Trigger : LaboratoryOrganKind.Actuator,
                Describe(definition), definition.Type == OrganType.Trigger ? (LaboratoryTriggerCondition)((int)definition.TriggerOn + 1) : LaboratoryTriggerCondition.None,
                definition.Type == OrganType.Trigger ? LaboratoryTriggerTarget.NextSlot : LaboratoryTriggerTarget.None)
        { Definition = definition; }
        private static string Describe(OrganDefinition d)
            => d.Torso != null ? d.Torso.Description : (d.Type == OrganType.Trigger ? "满足条件后追加发动。" :  $"按管道节拍自动发动；紧跟触发器时仅由触发器发动。索敌范围 {d.TargetingRange:g}。") + DescribeEffect(d);

        private static string DescribeEffect(OrganDefinition d)
        {
            if (d.Trigger != null) return d.Trigger.Description
                + "只发动紧邻后一个执行器，该执行器不自动攻击；"
                + "同链每实例一次，深度上限 4。";
            if (d.Projectile is PatternActuatorSettings p)
            {
                string common = $"{d.DisplayName}；单次/单跳伤害 {d.Damage:g}。";
                switch (p.Pattern)
                {
                    case ActuatorPattern.Orbit: return common + $"绕自身，半径 {p.OrbitRadius:g}，角速度 {p.AngularSpeed:g}°/s，持续 {p.Lifetime:g}s。";
                    case ActuatorPattern.Spiral: return common + $"前进速度 {p.Speed:g}，旋转半径 {p.OrbitRadius:g}，角速度 {p.AngularSpeed:g}°/s。";
                    case ActuatorPattern.Laser: return common + $"单次穿透，长 {p.Range:g}，宽 {2*p.HitRadius:g}，墙体阻挡。";
                    case ActuatorPattern.ChargedLaser: return common + $"锁定方向蓄力 {p.ChargeTime:g}s，长 {p.Range:g}，宽 {2*p.HitRadius:g}。";
                    case ActuatorPattern.Homing: return common + $"转向 {p.AngularSpeed:g}°/s；失去原目标后直飞。";
                    case ActuatorPattern.Fog: return common + $"在自身位置留雾，半径 {p.AreaRadius:g}，每 {p.Interval:g}s 一跳，持续 {p.Lifetime:g}s。";
                    case ActuatorPattern.Vampire: return common + $"直线针，按实际直接伤害的 {p.LifeSteal*100:g}% 回血。";
                    case ActuatorPattern.Triple: return common + $"三发，总夹角 {p.SpreadAngle:g}°，每颗独立命中。";
                    case ActuatorPattern.Sucker: return common + $"碰敌附着，每 {p.Interval:g}s 伤害，速度倍率 {p.SlowMultiplier:g}，总寿命 {p.Lifetime:g}s。";
                    case ActuatorPattern.Missile: return common + $"命中再爆炸 {p.ExplosionDamage:g}，半径 {p.AreaRadius:g}；直接伤害与爆炸叠加。";
                    case ActuatorPattern.Spore: return common + $"漂浮 {p.DriftDuration:g}s 后停留，总寿命 {p.Lifetime:g}s，碰敌消失。";
                    case ActuatorPattern.Summon: return $"跟随召唤物，生命 {p.Health:g}，每秒自然损失 {p.HealthDecay:g}，跟随速度 {p.Speed:g}；暂不攻击。";
                }
            }
            if (d.Projectile != null) return $"直线子弹，命中伤害 {d.Damage:g}，速度 {d.Projectile.Speed:g}，寿命 {d.Projectile.Lifetime:g}s；碰敌消失，实战撞墙消失。";
            if (d.Type == OrganType.Trigger) return "前一个器官击杀后只发动紧邻后一个执行器；后者不自动攻击，同链每实例最多一次，深度上限 4。";
            if (d.BlackHole != null) return $"黑洞半径 {d.Radius:g}，持续 {d.BlackHole.Duration:g}秒，吸引速度 {d.BlackHole.PullSpeed:g}；预览不展示位移。";
            string text = d.Shape == AttackShape.Explosion ? $"范围伤害 {d.Damage:g}，半径 {d.Radius:g}。" : $"单目标伤害 {d.Damage:g}。";
            var s = d.Status;
            if (s == null) return text;
            text += $"{s.Chance * 100:g}% ";
            if (s.Kind == StatusEffectKind.Stun) return text + $"眩晕 {s.Duration:g}秒。";
            if (s.Kind == StatusEffectKind.Bleed) return text + $"流血 {s.Duration:g}秒，每 {s.TickInterval:g}秒 扣 {s.TickDamage:g}。";
            return text + $"减速每层 {s.SlowPerStack * 100:g}%，上限 {s.MaxStacks} 层，每 {s.Duration:g}秒 减层。";
        }
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public string Name { get; }
        public LaboratoryPart Part { get; }
        public OrganMountKind MountKind => OrganKind == LaboratoryOrganKind.TorsoSpecial
            ? OrganMountKind.Torso : OrganMountKind.Pipeline;
        public string MountLabel => MountKind == OrganMountKind.Torso ? "躯干专用" : "通用";
        public bool CanEquip(LaboratoryPart targetPart) => Enum.IsDefined(typeof(LaboratoryPart), targetPart)
            && OrganEquipmentRules.CanEquip(MountKind, targetPart.ToString().ToLowerInvariant());
        public LaboratoryOrganKind OrganKind { get; }
        public LaboratoryTriggerCondition TriggerCondition { get; }
        public LaboratoryTriggerTarget TriggerTarget { get; }
        public string Kind => OrganKind == LaboratoryOrganKind.Trigger ? "触发器" :
            OrganKind == LaboratoryOrganKind.TorsoSpecial ? "特殊器官" : "执行器";
        public string Description { get; }
        public LaboratoryItem(string instanceId, string definitionId, string name, LaboratoryPart part,
            LaboratoryOrganKind kind, string description,
            LaboratoryTriggerCondition triggerCondition = LaboratoryTriggerCondition.None,
            LaboratoryTriggerTarget triggerTarget = LaboratoryTriggerTarget.None)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("实例编号不能为空。", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("配置编号不能为空。", nameof(definitionId));
            if (!Enum.IsDefined(typeof(LaboratoryPart), part)) throw new ArgumentOutOfRangeException(nameof(part));
            if (!Enum.IsDefined(typeof(LaboratoryOrganKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(LaboratoryTriggerCondition), triggerCondition)) throw new ArgumentOutOfRangeException(nameof(triggerCondition));
            if (!Enum.IsDefined(typeof(LaboratoryTriggerTarget), triggerTarget)) throw new ArgumentOutOfRangeException(nameof(triggerTarget));
            if ((part == LaboratoryPart.Torso) != (kind == LaboratoryOrganKind.TorsoSpecial))
                throw new ArgumentException("躯干物品必须使用特殊器官类型。", nameof(kind));
            if (kind == LaboratoryOrganKind.Trigger
                ? triggerCondition == LaboratoryTriggerCondition.None || triggerTarget == LaboratoryTriggerTarget.None
                : triggerCondition != LaboratoryTriggerCondition.None || triggerTarget != LaboratoryTriggerTarget.None)
                throw new ArgumentException("触发规则必须与器官类型匹配。", nameof(triggerCondition));
            InstanceId = instanceId; DefinitionId = definitionId;
            Name = string.IsNullOrWhiteSpace(name) ? definitionId : name;
            Part = part; OrganKind = kind; Description = description ?? string.Empty;
            TriggerCondition = triggerCondition; TriggerTarget = triggerTarget;
        }
    }
    public interface ILaboratoryEquipment
    {
        // B 本地装备事务接口，尚不是团队冻结的库存协议。
        event Action Changed;
        IReadOnlyList<LaboratoryItem> Inventory { get; }
        int SlotCount(LaboratoryPart part);
        LaboratoryItem GetSlot(LaboratoryPart part, int index);
        bool TryEquip(string instanceId, LaboratoryPart part, int index, out string error);
        bool TryUnequip(LaboratoryPart part, int index, out string error);
    }
    public sealed class TemporaryEquipment : ILaboratoryEquipment
    {
        private readonly List<LaboratoryItem> inventory = new List<LaboratoryItem>();
        private readonly List<LaboratoryItem>[] slots = new List<LaboratoryItem>[4];
        public IReadOnlyList<LaboratoryItem> Inventory { get; }
        public event Action Changed;
        public TemporaryEquipment(int torsoSlots = 2)
        {
            if (torsoSlots < 1) throw new ArgumentOutOfRangeException(nameof(torsoSlots));
            Inventory = inventory.AsReadOnly();
            for (int p = 0; p < 4; p++)
            {
                slots[p] = new List<LaboratoryItem>();
                for (int i = 0; i < (p == 3 ? torsoSlots : 6); i++) slots[p].Add(null);
            }
        }
        public int SlotCount(LaboratoryPart part) => ValidPart(part) ? slots[(int)part].Count : 0;
        public LaboratoryItem GetSlot(LaboratoryPart part, int index) => ValidSlot(part, index) ? slots[(int)part][index] : null;
        private static bool ValidPart(LaboratoryPart part) => (int)part >= 0 && (int)part < 4;
        private bool ValidSlot(LaboratoryPart part, int index) => ValidPart(part) && index >= 0 && index < SlotCount(part);
        public void AddSample(LaboratoryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (inventory.Exists(x => x.InstanceId == item.InstanceId)) throw new ArgumentException("背包中已存在该实例。");
            foreach (var body in slots)
                if (body.Exists(x => x != null && x.InstanceId == item.InstanceId)) throw new ArgumentException("该实例已经装备。");
            inventory.Add(item); Changed?.Invoke();
        }
        public bool TryEquip(string instanceId, LaboratoryPart part, int index, out string error)
        {
            error = null;
            if (!ValidSlot(part, index)) { error = "请选择有效的目标槽位。"; return false; }
            int from = inventory.FindIndex(x => x.InstanceId == instanceId);
            if (from < 0) { error = "该器官已不在背包中，请重新选择。"; return false; }
            LaboratoryItem item = inventory[from];
            if (!item.CanEquip(part)) { error = "普通器官只能装入头、手、腿；特殊器官仅限躯干。"; return false; }
            LaboratoryItem old = slots[(int)part][index];
            slots[(int)part][index] = item;
            if (old == null) inventory.RemoveAt(from); else inventory[from] = old;
            Changed?.Invoke(); return true;
        }
        public bool TryUnequip(LaboratoryPart part, int index, out string error)
        {
            error = null;
            if (!ValidSlot(part, index)) { error = "请选择有效的目标槽位。"; return false; }
            LaboratoryItem item = slots[(int)part][index];
            if (item == null) { error = "此槽位为空，没有可拆下的器官。"; return false; }
            inventory.Add(item); slots[(int)part][index] = null;
            Changed?.Invoke(); return true;
        }
        public void ResizeTorso(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var body = slots[3];
            if (body.Count == count) return;
            for (int i = count; i < body.Count; i++) if (body[i] != null) inventory.Add(body[i]);
            if (body.Count > count) body.RemoveRange(count, body.Count - count);
            while (body.Count < count) body.Add(null);
            Changed?.Invoke();
        }
    }
}
