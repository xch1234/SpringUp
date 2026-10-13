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
        public bool UseUnscaledTime { get; set; }
        public bool AutomaticExecution { get; set; } = true;
        public Func<BodyPart, float> StepIntervalProvider { get; set; }
        public Func<BodyPart, float> CycleDurationProvider { get; set; }
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

        // 所有形状的普通攻击都需要目标。接入方负责范围内索敌和出手前的实时校验。
        public Func<string> TargetSelector { get; set; }
        public Func<string, bool> TargetValidator { get; set; }
        public Func<string, AttackTarget?> TargetSnapshotProvider { get; set; }

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
            if (!AutomaticExecution) return;
            EnsureInitialized();
            SanitizeInterval();
            foreach (BodyRuntime body in bodies)
            {
                body.StepInterval = Mathf.Max(BodyRuntime.MinimumStepInterval, StepIntervalProvider?.Invoke(body.Part) ?? stepInterval);
                body.CycleDuration = CycleDurationProvider?.Invoke(body.Part) ?? 0f;
                body.Tick(UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
            }
        }

        private void ForwardSlotExecuted(BodyRuntime body, int index, OrganInstance organ)
        {
            SlotExecuted?.Invoke(body, index, organ);
            ExecuteAttack(organ, null);
        }

        public bool TryExecuteSlot(BodyPart part, int index)
        {
            if (!isActiveAndEnabled) return false;
            var body = GetBody(part);
            var organ = body.GetSlot(index);
            if (organ == null) return false;
            bool produced = ExecuteAttack(organ, null);
            if (produced) SlotExecuted?.Invoke(body, index, organ);
            return produced;
        }

        private bool ExecuteAttack(OrganInstance organ, CastContext context, string causeTargetId = null, Vector2? causePosition = null)
        {
            if (!organ.Definition.CanAttack) return false;

            // 已成立的击杀链允许范围效果沿用触发敌人的身份（它可能已死亡）。
            // 普通攻击，以及追加的单体攻击，必须重新索敌并校验，不能打已回池的目标。
            bool inheritedArea = context != null && organ.Definition.Shape != AttackShape.SingleTarget;
            string targetId = inheritedArea ? causeTargetId : TargetSelector?.Invoke();
            if (string.IsNullOrWhiteSpace(targetId)) return false;
            if (!inheritedArea && TargetValidator != null && !TargetValidator(targetId)) return false;
            Vector2? center = causePosition;
            if (!inheritedArea)
            {
                var target = TargetSnapshotProvider?.Invoke(targetId);
                if (TargetSnapshotProvider != null && (!target.HasValue || !target.Value.IsAlive
                    || target.Value.Id != targetId || !CastEvent.IsFinite(target.Value.Position))) return false;
                center = target?.Position;
            }
            if (ParameterisedBehaviour.TryCreateAttack(organ, targetId, out CastEvent attack, context, center))
            {
                AttackProduced?.Invoke(attack);
                return true;
            }
            return false;
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
                        ExecuteAttack(organ, child, signal.Cause.TargetId, signal.Position);
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
