using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    [DisallowMultipleComponent]
    public sealed class OrganPipelineController : MonoBehaviour
    {
        [SerializeField, Min(BodyRuntime.MinimumStepInterval)]
        [Tooltip("每走到一个有器官的槽位，需要等待的秒数。目前暂用固定值。")]
        private float stepInterval = 1f;

        private BodyRuntime[] bodies;
        private readonly Queue<TriggerSignal> pendingSignals = new Queue<TriggerSignal>();
        private bool processingSignals;
        public float StepInterval
        {
            get => stepInterval;
            set
            {
                stepInterval = value;
                SanitizeInterval();
                if (bodies != null) foreach (BodyRuntime body in bodies) body.StepInterval = stepInterval;
            }
        }
        public event Action<BodyRuntime, int, OrganInstance> SlotExecuted;
        public event Action<CastEvent> AttackProduced;
        public event Action<BodyRuntime, int, OrganInstance, CastContext> TriggerActivated;
        public event Action<BodyRuntime, int, OrganInstance, CastContext, TriggerBlockReason> TriggerBlocked;
        public event Action<BodyRuntime, int, OrganInstance, CastContext> TriggeredSlotExecuted;

        // 由场景接入方提供目标编号。管道本身不认识测试敌人。
        public Func<string> TargetSelector { get; set; }

        private void Awake() => EnsureInitialized();

        public BodyRuntime GetBody(BodyPart part)
        {
            if (!Enum.IsDefined(typeof(BodyPart), part))
                throw new ArgumentOutOfRangeException(nameof(part));
            EnsureInitialized();
            return bodies[(int)part];
        }

        private void EnsureInitialized()
        {
            if (bodies != null) return;
            SanitizeInterval();
            bodies = new[]
            {
                new BodyRuntime(BodyPart.Head, stepInterval),
                new BodyRuntime(BodyPart.Hand, stepInterval),
                new BodyRuntime(BodyPart.Leg, stepInterval)
            };
            foreach (BodyRuntime body in bodies)
                body.SlotExecuted += ForwardSlotExecuted;
        }

        private void Update()
        {
            EnsureInitialized();
            SanitizeInterval();
            foreach (BodyRuntime body in bodies)
            {
                body.StepInterval = stepInterval;
                body.Tick(Time.deltaTime);
            }
        }

        private void ForwardSlotExecuted(BodyRuntime body, int index, OrganInstance organ)
        {
            SlotExecuted?.Invoke(body, index, organ);
            ExecuteAttack(organ, null);
        }

        private void ExecuteAttack(OrganInstance organ, CastContext context)
        {
            if (organ.Definition.Type != OrganType.Actuator
                || (organ.Definition.Damage <= 0f && organ.Definition.Shape != AttackShape.BlackHole))
                return;

            string targetId = organ.Definition.Shape == AttackShape.SingleTarget ? TargetSelector?.Invoke() : null;
            if (ParameterisedBehaviour.TryCreateAttack(organ, targetId, out CastEvent attack, context))
                AttackProduced?.Invoke(attack);
        }

        public void PublishSignal(TriggerSignal signal)
        {
            if (signal.Cause.Context == null || !isActiveAndEnabled) return;
            EnsureInitialized();
            pendingSignals.Enqueue(signal);
            if (processingSignals) return;

            // 追加攻击产生的新通知先排队，避免击杀回调层层递归。
            processingSignals = true;
            try
            {
                while (pendingSignals.Count > 0) ProcessSignal(pendingSignals.Dequeue());
            }
            finally
            {
                pendingSignals.Clear();
                processingSignals = false;
            }
        }

        private void ProcessSignal(TriggerSignal signal)
        {
            foreach (BodyRuntime body in bodies)
            {
                for (int index = 0; index < BodyRuntime.SlotCount; index++)
                {
                    OrganInstance trigger = body.GetSlot(index);
                    if (trigger == null || !ParameterisedBehaviour.Matches(signal, trigger.Definition)) continue;
                    if (trigger.Definition.TriggerTarget != TriggerTargetKind.SelfSlots) continue;

                    if (!signal.Cause.Context.TryEnterTrigger(trigger.InstanceId,
                        out CastContext child, out TriggerBlockReason reason))
                    {
                        TriggerBlocked?.Invoke(body, index, trigger, signal.Cause.Context, reason);
                        continue;
                    }

                    TriggerActivated?.Invoke(body, index, trigger, child);
                    body.ExecuteAllSlots((slotIndex, organ) =>
                    {
                        TriggeredSlotExecuted?.Invoke(body, slotIndex, organ, child);
                        // 扫到触发器不等于条件满足。这里只会让执行器产出攻击。
                        ExecuteAttack(organ, child);
                    });
                }
            }
        }

        private void OnValidate() => SanitizeInterval();

        private void SanitizeInterval()
        {
            if (float.IsNaN(stepInterval) || float.IsInfinity(stepInterval)) stepInterval = 1f;
            stepInterval = Mathf.Max(BodyRuntime.MinimumStepInterval, stepInterval);
        }
    }
}
