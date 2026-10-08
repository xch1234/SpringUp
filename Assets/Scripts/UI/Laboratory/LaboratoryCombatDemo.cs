using System.Collections.Generic;
using System.Linq;
using SpringUp.Organs;
using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Laboratory
{
    // 只做装配与演示的接线；节拍、扣血、死亡、触发和防环均由 A 实现。
    public sealed class LaboratoryCombatDemo : MonoBehaviour
    {
        public LaboratoryDemoBootstrap source;
        public DemoTarget[] targets;
        public Image[] targetImages;
        public Text[] targetLabels;
        public Text statusText, eventText;
        public Button restartButton;
        public OrganPipelineController Pipeline { get; private set; }
        public int UnsupportedCount { get; private set; }
        public int TriggerCount { get; private set; }
        private ILaboratoryEquipment equipment;
        private readonly Queue<string> events = new Queue<string>();
        private readonly Dictionary<string, string> origins = new Dictionary<string, string>();
        private float[] flashes;
        private void Start() { if (equipment == null) Initialize(); }
        private void OnEnable() { if (source != null && targets != null && targets.Length == 2) Initialize(); }
        public void Initialize()
        {
            Disconnect();
            equipment = source.Equipment;
            equipment.Changed += Restart;
            foreach (var target in targets) { target.Damaged += Damaged; target.Killed += Killed; }
            restartButton.onClick.AddListener(Restart);
            Restart();
        }
        // 仅映射 A 已实现的两种样例；未知配置明确排除，不补造伤害规则。
        public static OrganDefinition Resolve(LaboratoryItem item)
        {
            if (item == null || item.Part != LaboratoryPart.Hand) return null;
            if (item.DefinitionId == "fist" && item.OrganKind == LaboratoryOrganKind.Actuator)
                return new OrganDefinition("fist", "拳头", BodyPart.Hand, OrganType.Actuator, 5);
            if (item.DefinitionId == "multi_tentacle" && item.OrganKind == LaboratoryOrganKind.Trigger &&
                item.TriggerCondition == LaboratoryTriggerCondition.Kill && item.TriggerTarget == LaboratoryTriggerTarget.SelfSlots)
                return new OrganDefinition("multi_tentacle", "多肢触手", BodyPart.Hand, OrganType.Trigger,
                    triggerOn: TriggerOn.Kill, triggerTarget: TriggerTargetKind.SelfSlots);
            return null;
        }
        // 每次装备变更都新建管道，避免沿用上一套装备的游标和触发链状态。
        public void Restart()
        {
            if (equipment == null) return;
            DisposePipeline();
            events.Clear(); origins.Clear(); TriggerCount = 0; UnsupportedCount = 0;
            flashes = new float[targets.Length];
            targets[0].InitializeForDemo("A", 5);
            targets[1].InitializeForDemo("B", 20);
            var runner = new GameObject("Equipment demo pipeline");
            runner.transform.SetParent(transform, false);
            Pipeline = runner.AddComponent<OrganPipelineController>();
            int supported = 0;
            for (int p = 0; p < 4; p++)
                for (int s = 0; s < equipment.SlotCount((LaboratoryPart)p); s++)
                {
                    var item = equipment.GetSlot((LaboratoryPart)p, s);
                    if (item == null) continue;
                    var definition = Resolve(item);
                    if (definition == null || s >= BodyRuntime.SlotCount) { UnsupportedCount++; continue; }
                    var instance = new OrganInstance(definition);
                    Pipeline.GetBody(definition.Part).SetSlot(s, instance);
                    // 预览副本使用 A 生成的编号，仅在本次演示中映射回槽位。
                    origins.Add(instance.InstanceId, $"{LaboratoryPresenter.PartNames[p]} {s + 1} · {item.Name}");
                    supported++;
                }
            Pipeline.TargetSelector = () => targets.FirstOrDefault(t => t.IsAlive)?.TargetId;
            Pipeline.AttackProduced += Attack;
            Pipeline.SlotExecuted += Step;
            Pipeline.TriggerActivated += Trigger;
            Pipeline.TriggeredSlotExecuted += ExtraStep;
            Pipeline.TriggerBlocked += Blocked;
            statusText.text = $"参与演示：{supported} 件　未支持：{UnsupportedCount} 件\n装卸后重新开始；选择物品不影响演示。";
            Record(supported == 0 ? "没有可执行器官。" : "等待节拍。目标按 A → B 的顺序受到攻击。");
            RenderTargets();
        }
        private void Attack(CastEvent attack) => targets.FirstOrDefault(t => t.TargetId == attack.TargetId)?.TryApplyAttack(attack);
        private void Step(BodyRuntime body, int slot, OrganInstance organ) => Record($"节拍：{origins[organ.InstanceId]}");
        private void ExtraStep(BodyRuntime body, int slot, OrganInstance organ, CastContext context)
            => Record($"追加执行：{origins[organ.InstanceId]}");
        private void Trigger(BodyRuntime body, int slot, OrganInstance organ, CastContext context)
        {
            TriggerCount++;
            Record($"击杀触发：{origins[organ.InstanceId]} → 手部槽位");
        }
        private void Blocked(BodyRuntime body, int slot, OrganInstance organ, CastContext context, TriggerBlockReason reason)
            => Record($"{organ.Definition.DisplayName}停止：" + (reason == TriggerBlockReason.DepthLimit ? "达到链深上限" : "同链已发动"));
        private void Damaged(DemoTarget target, CastEvent attack, float damage)
        {
            flashes[System.Array.IndexOf(targets, target)] = Time.unscaledTime + .25f;
            Record($"{origins[attack.OriginInstanceId]} → {target.TargetId} 扣血 {damage}");
            RenderTargets();
        }
        private void Killed(DemoTarget target, CastEvent attack)
        {
            Record($"{target.TargetId} 死亡");
            // 必须传回原攻击，保留链上下文，让 A 的同链防重复继续生效。
            Pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, attack));
            if (targets.All(t => !t.IsAlive)) Record("目标全部死亡。点击重新开始可再次测试。");
        }
        private void Record(string value)
        {
            events.Enqueue(value);
            while (events.Count > 9) events.Dequeue();
            eventText.text = string.Join("\n", events);
        }
        private void Update() { if (equipment != null) RenderTargets(); }
        private void RenderTargets()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                targetLabels[i].text = $"目标 {target.TargetId}\n{target.CurrentHealth:0} / {target.MaxHealth:0}\n{(target.IsAlive ? "存活" : "已死亡")}";
                targetImages[i].color = !target.IsAlive ? new Color32(80, 85, 90, 255) :
                    Time.unscaledTime < flashes[i] ? new Color32(175, 65, 65, 255) : new Color32(45, 115, 80, 255);
            }
        }
        private void DisposePipeline()
        {
            if (Pipeline == null) return;
            Pipeline.enabled = false;
            Pipeline.TargetSelector = null;
            Pipeline.AttackProduced -= Attack; Pipeline.SlotExecuted -= Step;
            Pipeline.TriggerActivated -= Trigger; Pipeline.TriggeredSlotExecuted -= ExtraStep; Pipeline.TriggerBlocked -= Blocked;
            Pipeline.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(Pipeline.gameObject); else DestroyImmediate(Pipeline.gameObject);
            Pipeline = null;
        }
        private void Disconnect()
        {
            if (equipment != null) equipment.Changed -= Restart;
            if (targets != null) foreach (var target in targets)
                if (target != null) { target.Damaged -= Damaged; target.Killed -= Killed; }
            if (restartButton != null) restartButton.onClick.RemoveListener(Restart);
            equipment = null; DisposePipeline();
        }
        private void OnDisable() => Disconnect();
    }
}
