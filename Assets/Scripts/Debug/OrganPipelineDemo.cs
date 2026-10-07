using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OrganPipelineController))]
    public sealed class OrganPipelineDemo : MonoBehaviour
    {
        public enum DemoMode { SlotsOnly, PunchDamage, KillChain }

        [SerializeField, Tooltip("Slots Only：槽位测试。Punch Damage：拳头扣血。Kill Chain：击杀触发链。")]
        private DemoMode mode = DemoMode.SlotsOnly;

        [SerializeField, Tooltip("勾选后，下次启动测试时三个部位都为空。")]
        private bool startWithEmptySlots;

        [SerializeField, Tooltip("按数组顺序攻击第一个存活目标。每个目标的编号必须不同。")]
        private DemoTarget[] targets = new DemoTarget[0];

        private readonly List<DemoTarget> connectedTargets = new List<DemoTarget>();
        private OrganPipelineController controller;
        private string lastStep = "等待第一个节拍";
        private string lastAttack = "等待攻击";
        private string lastChain = "等待击杀触发";
        private int triggerCount;
        private int highlightedTriggerSlot = -1;
        private float triggerHighlightUntil;

        private void OnEnable()
        {
            controller = GetComponent<OrganPipelineController>();
            controller.SlotExecuted += OnSlotExecuted;
            triggerCount = 0;
            lastChain = "等待击杀触发";
            highlightedTriggerSlot = -1;
            controller.TriggerActivated += OnTriggerActivated;
            controller.TriggerBlocked += OnTriggerBlocked;
            controller.TriggeredSlotExecuted += OnTriggeredSlotExecuted;
            if (mode != DemoMode.SlotsOnly)
            {
                ConnectTargets();
                controller.TargetSelector = SelectTarget;
                controller.AttackProduced += ApplyAttack;
            }

            // 演示入口独占测试配置。正式游戏不挂这个组件。
            foreach (BodyPart part in new[] { BodyPart.Head, BodyPart.Hand, BodyPart.Leg })
                for (int i = 0; i < BodyRuntime.SlotCount; i++)
                    controller.GetBody(part).SetSlot(i, null);

            if (!startWithEmptySlots)
            {
                bool punchMode = mode != DemoMode.SlotsOnly;
                var definition = new OrganDefinition(punchMode ? "fist" : "demo_hand",
                    punchMode ? "拳头" : "测试器官", BodyPart.Hand, OrganType.Actuator, punchMode ? 5f : 0f);
                BodyRuntime hand = controller.GetBody(BodyPart.Hand);
                hand.SetSlot(0, new OrganInstance(definition));
                if (!punchMode) hand.SetSlot(3, new OrganInstance(definition));
                if (mode == DemoMode.KillChain)
                    hand.SetSlot(3, new OrganInstance(new OrganDefinition("multi_tentacle", "多肢触手",
                        BodyPart.Hand, OrganType.Trigger, triggerOn: TriggerOn.Kill,
                        triggerTarget: TriggerTargetKind.SelfSlots)));
            }

            lastStep = startWithEmptySlots ? "全部为空，不会执行槽位" : "等待第一个节拍";
            Debug.Log(startWithEmptySlots
                ? "[管道测试] 三个部位全空。预期：没有槽位执行日志，也没有报错。"
                : mode == DemoMode.KillChain
                ? "[触发链] 手部第 1 槽拳头，第 4 槽多肢触手。击杀后立即额外扫描手部；同一触发器每条链最多发动一次。"
                : mode == DemoMode.PunchDamage
                ? "[拳头测试] 手部第 1 槽安装拳头，每次伤害 5。按列表顺序攻击存活目标。第二步没有击杀追加攻击。"
                : "[管道测试] 手部第 1、4 槽已安装测试器官。预期顺序：1 → 4 → 1 → 4。头部和腿部为空。", this);
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.SlotExecuted -= OnSlotExecuted;
                controller.AttackProduced -= ApplyAttack;
                controller.TriggerActivated -= OnTriggerActivated;
                controller.TriggerBlocked -= OnTriggerBlocked;
                controller.TriggeredSlotExecuted -= OnTriggeredSlotExecuted;
                if (controller.TargetSelector == SelectTarget) controller.TargetSelector = null;
            }
            foreach (DemoTarget target in connectedTargets)
            {
                if (target == null) continue;
                target.Damaged -= OnTargetDamaged;
                target.Killed -= OnTargetKilled;
            }
            connectedTargets.Clear();
        }

        private void ConnectTargets()
        {
            var ids = new HashSet<string>();
            if (targets != null)
            {
                foreach (DemoTarget target in targets)
                {
                    if (target == null) continue;
                    if (string.IsNullOrWhiteSpace(target.TargetId) || !ids.Add(target.TargetId))
                    {
                        connectedTargets.Clear();
                        lastAttack = "目标编号为空或重复，请停止运行后修改";
                        Debug.LogWarning("[拳头测试] " + lastAttack, this);
                        return;
                    }
                    connectedTargets.Add(target);
                }
            }

            if (connectedTargets.Count == 0)
            {
                lastAttack = "请停止运行，把 DemoTarget 拖入 Targets 列表";
                Debug.LogWarning("[拳头测试] " + lastAttack, this);
                return;
            }

            foreach (DemoTarget target in connectedTargets)
            {
                target.Damaged += OnTargetDamaged;
                target.Killed += OnTargetKilled;
            }
            lastAttack = "等待拳头攻击，每次伤害 5";
        }

        private string SelectTarget()
        {
            foreach (DemoTarget target in connectedTargets)
                if (target != null && target.IsAlive) return target.TargetId;
            if (connectedTargets.Count > 0) lastAttack = "没有可攻击的存活目标";
            return null;
        }

        private void ApplyAttack(CastEvent attack)
        {
            foreach (DemoTarget target in connectedTargets)
            {
                if (target == null || target.TargetId != attack.TargetId) continue;
                target.TryApplyAttack(attack);
                return;
            }
        }

        private void OnTargetDamaged(DemoTarget target, CastEvent attack, float appliedDamage)
            => lastAttack = $"{target.TargetId} 扣血 {appliedDamage}，剩余 {target.CurrentHealth}/{target.MaxHealth}";

        private void OnTargetKilled(DemoTarget target, CastEvent attack)
        {
            lastAttack = $"{target.TargetId} 已死亡。下一次正常节拍再选择目标";
            if (mode == DemoMode.KillChain)
            {
                lastAttack = $"{target.TargetId} 已死亡，发送击杀通知";
                Debug.Log($"[触发链] {target.TargetId} 的击杀通知，链：{attack.ChainId}，深度：{attack.ChainDepth}", this);
                controller.PublishSignal(new TriggerSignal(TriggerOn.Kill, attack));
            }
        }

        private void OnTriggerActivated(BodyRuntime body, int index, OrganInstance organ, CastContext context)
        {
            triggerCount++;
            highlightedTriggerSlot = index;
            triggerHighlightUntil = Time.unscaledTime + 1.2f;
            lastChain = $"触发 {triggerCount} 次：第 {index + 1} 槽{organ.Definition.DisplayName}，深度 {context.Depth}";
            Debug.Log($"[触发链] {lastChain}，额外扫描 {body.Part}。链：{context.ChainId}，触发器实例：{organ.InstanceId}", this);
        }

        private void OnTriggerBlocked(BodyRuntime body, int index, OrganInstance organ,
            CastContext context, TriggerBlockReason reason)
        {
            string explanation = reason == TriggerBlockReason.DepthLimit ? "已到链深度上限 4" : "同一条链中已经发动过";
            lastChain = $"停止传导：{organ.Definition.DisplayName}{explanation}";
            Debug.Log($"[触发链] {lastChain}。链：{context.ChainId}，深度：{context.Depth}", this);
        }

        private void OnTriggeredSlotExecuted(BodyRuntime body, int index, OrganInstance organ, CastContext context)
        {
            lastStep = $"额外扫描第 {index + 1} 槽：{organ.Definition.DisplayName}";
            Debug.Log($"[触发链] {lastStep}，链：{context.ChainId}，深度：{context.Depth}", this);
        }

        private void OnSlotExecuted(BodyRuntime body, int index, OrganInstance organ)
        {
            string partName = body.Part == BodyPart.Head ? "头部" : body.Part == BodyPart.Hand ? "手部" : "腿部";
            lastStep = $"{partName}第 {index + 1} 槽，累计执行 {body.ExecutedStepCount} 次";
            Debug.Log($"[管道测试] {lastStep}。器官：{organ.Definition.DisplayName}。实例：{organ.InstanceId}", this);
        }

        private void OnGUI()
        {
            if (controller == null) return;
            GUILayout.BeginArea(new Rect(15, 15, 580, 220 + connectedTargets.Count * 24), GUI.skin.box);
            GUILayout.Label(mode == DemoMode.KillChain ? "第三步：击杀 → 多肢触手 → 手部额外执行"
                : mode == DemoMode.PunchDamage ? "第二步：拳头扣血测试（每次伤害 5）" : "第一步：六槽管道测试（暂不造成伤害）");
            GUILayout.Label(lastStep);
            DrawBody("头部", BodyPart.Head);
            DrawBody("手部", BodyPart.Hand);
            DrawBody("腿部", BodyPart.Leg);
            if (mode == DemoMode.KillChain)
            {
                Color previous = GUI.color;
                if (Time.unscaledTime < triggerHighlightUntil) GUI.color = Color.yellow;
                GUILayout.Label(lastChain);
                GUI.color = previous;
                GUILayout.Label("正常节拍：拳头、触手轮流走。触手只有收到击杀通知才发动。");
            }
            if (mode != DemoMode.SlotsOnly)
            {
                GUILayout.Label(lastAttack);
                foreach (DemoTarget target in connectedTargets)
                {
                    if (target == null) continue;
                    string state = target.CurrentHealth <= 0 ? "已死亡" : target.IsAlive ? "存活" : "已停用";
                    GUILayout.Label($"{target.TargetId}：{target.CurrentHealth}/{target.MaxHealth}（{state}）");
                }
            }
            GUILayout.EndArea();
        }

        private void DrawBody(string label, BodyPart part)
        {
            BodyRuntime body = controller.GetBody(part);
            string line = label + "：";
            for (int i = 0; i < BodyRuntime.SlotCount; i++)
            {
                OrganInstance organ = body.GetSlot(i);
                string state = organ == null ? "空" : organ.Definition.DisplayName;
                if (part == BodyPart.Hand && i == highlightedTriggerSlot && Time.unscaledTime < triggerHighlightUntil)
                    state += "!";
                string marker = body.LastExecutedSlot == i ? "*" : "";
                line += $" [{i + 1}:{state}{marker}]";
            }
            GUILayout.Label(line);
        }
    }
}
