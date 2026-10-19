using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    [DisallowMultipleComponent]
    public sealed partial class OrganPipelineController : MonoBehaviour
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
        public event Action<ActuatorContext> ActuatorProduced;
        public Func<Vector2?> OriginProvider { get; set; }
        public event Action<BodyRuntime, int, OrganInstance, CastContext> TriggerActivated;
        public event Action<BodyRuntime, int, OrganInstance, CastContext, TriggerBlockReason> TriggerBlocked;
        public event Action<BodyRuntime, int, OrganInstance, CastContext> TriggeredSlotExecuted;

        // 所有形状的普通攻击都需要目标。接入方负责范围内索敌和出手前的实时校验。
        // Range-aware adapters take precedence; legacy delegates remain for isolated test clients.
        public Func<float, string> TargetSelectorInRange { get; set; }
        public Func<string, float, AttackTarget?> TargetSnapshotInRange { get; set; }
        public Func<Vector2, float, string> TargetSelectorFrom { get; set; }
        public Func<Vector2, string, float, AttackTarget?> TargetSnapshotFrom { get; set; }
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
                new BodyRuntime(BodyPart.Head, stepInterval, id => OccupiedElsewhere(BodyPart.Head, id)),
                new BodyRuntime(BodyPart.Hand, stepInterval, id => OccupiedElsewhere(BodyPart.Hand, id)),
                new BodyRuntime(BodyPart.Leg, stepInterval, id => OccupiedElsewhere(BodyPart.Leg, id))
            };
            foreach (BodyRuntime body in bodies)
                { body.SlotExecuted += ForwardSlotExecuted; body.LoadoutChanged += ResetTriggers; }
        }

        private void Update() => AdvanceAutomatic(UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        public void AdvanceAutomatic(float deltaTime)
        {
            if (!isActiveAndEnabled || !AutomaticExecution) return;
            EnsureInitialized();
            SanitizeInterval();
            foreach (BodyRuntime body in bodies)
            {
                body.StepInterval = Mathf.Max(BodyRuntime.MinimumStepInterval, StepIntervalProvider?.Invoke(body.Part) ?? stepInterval);
                body.CycleDuration = CycleDurationProvider?.Invoke(body.Part) ?? 0f;
                body.Tick(deltaTime);
            }
        }

        private bool OccupiedElsewhere(BodyPart part, string instanceId)
        {
            if (bodies == null) return false;
            foreach (var body in bodies)
                if (body.Part != part)
                    for (int i = 0; i < BodyRuntime.SlotCount; i++)
                        if (body.GetSlot(i)?.InstanceId == instanceId) return true;
            return false;
        }

        private void ForwardSlotExecuted(BodyRuntime body, int index, OrganInstance organ)
        {
            SlotExecuted?.Invoke(body, index, organ);
            ExecuteAttack(organ, null, body.Part, index);
        }

        public bool TryExecuteSlot(BodyPart part, int index)
        {
            if (!isActiveAndEnabled) return false;
            var body = GetBody(part);
            var organ = body.GetSlot(index);
            if (organ == null || !body.IsAutomaticSlot(index)) return false;
            bool produced = ExecuteAttack(organ, null, part, index);
            if (produced) SlotExecuted?.Invoke(body, index, organ);
            return produced;
        }

        private string SelectTargetFor(OrganDefinition definition, CastContext context)
            => context?.ActorPosition != null
                ? (context.ActorPosition() is Vector2 position ? TargetSelectorFrom?.Invoke(position, definition.TargetingRange) : null)
                : TargetSelectorInRange != null ? TargetSelectorInRange(definition.TargetingRange) : TargetSelector?.Invoke();

        private AttackTarget? SnapshotFor(string id, OrganDefinition definition, CastContext context)
            => context?.ActorPosition != null
                ? (context.ActorPosition() is Vector2 position ? TargetSnapshotFrom?.Invoke(position, id, definition.TargetingRange) : null)
                : TargetSnapshotInRange != null ? TargetSnapshotInRange(id, definition.TargetingRange) : TargetSnapshotProvider?.Invoke(id);

        private bool ExecuteAttack(OrganInstance organ, CastContext context, BodyPart part, int slot,
            string causeTargetId = null, Vector2? causePosition = null)
        {
            if (!organ.Definition.CanAttack) return false;
            if (context?.ActorPosition != null && !context.ActorPosition().HasValue) return false;

            if (organ.Definition.Projectile is PatternActuatorSettings self && !self.RequiresTarget)
            {
                var origin = context?.ActorPosition != null ? context.ActorPosition() : OriginProvider?.Invoke();
                if (!origin.HasValue || !CastEvent.IsFinite(origin.Value) || ActuatorProduced == null) return false;
                string selected = SelectTargetFor(organ.Definition, context);
                var target = selected == null ? null : SnapshotFor(selected, organ.Definition, context);
                Vector2 direction = target.HasValue ? target.Value.Position - origin.Value : Vector2.up;
                if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
                var cause = new CastEvent(selected ?? "owner", organ.Definition.Damage, organ.Definition.Id,
                    organ.InstanceId, context ?? new CastContext(), effectCenter: origin, allowZeroDamage: true);
                ActuatorProduced.Invoke(new ActuatorContext(cause, part, slot, origin.Value, direction, self));
                return true;
            }

            // 已成立的击杀链允许范围效果沿用触发敌人的身份（它可能已死亡）。
            // 普通攻击，以及追加的单体攻击，必须重新索敌并校验，不能打已回池的目标。
            bool inheritedArea = context != null && causeTargetId != null && causeTargetId != "owner" && organ.Definition.Shape != AttackShape.SingleTarget;
            string targetId = inheritedArea ? causeTargetId : SelectTargetFor(organ.Definition, context);
            if (string.IsNullOrWhiteSpace(targetId)) return false;
            if (!inheritedArea && context?.ActorPosition == null && TargetSnapshotInRange == null && TargetValidator != null && !TargetValidator(targetId)) return false;
            Vector2? center = causePosition;
            if (!inheritedArea)
            {
                var target = SnapshotFor(targetId, organ.Definition, context);
                if ((context?.ActorPosition != null || TargetSnapshotInRange != null || TargetSnapshotProvider != null) && (!target.HasValue || !target.Value.IsAlive
                    || target.Value.Id != targetId || !CastEvent.IsFinite(target.Value.Position))) return false;
                center = target?.Position;
            }
            if (organ.Definition.Projectile != null)
            {
                var origin = context?.ActorPosition != null ? context.ActorPosition() : OriginProvider?.Invoke();
                if (!origin.HasValue || !center.HasValue || !CastEvent.IsFinite(origin.Value) || ActuatorProduced == null) return false;
                Vector2 direction = center.Value - origin.Value;
                if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
                var cause = new CastEvent(targetId, organ.Definition.Damage, organ.Definition.Id,
                    organ.InstanceId, context ?? new CastContext(), organ.Definition.Status, effectCenter: center);
                ActuatorProduced.Invoke(new ActuatorContext(cause, part, slot, origin.Value, direction, organ.Definition.Projectile));
                return true;
            }
            if (ParameterisedBehaviour.TryCreateAttack(organ, targetId, out CastEvent attack, context, center))
            {
                int epoch = triggerEpoch;
                NotifyExecutionStarted(attack);
                AttackProduced?.Invoke(attack);
                if (epoch == triggerEpoch && attack.Shape != AttackShape.BlackHole) NotifyExecutionEnded(attack);
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
            if (!isActiveAndEnabled) return;
            int epoch = triggerEpoch;
            foreach (BodyRuntime body in bodies)
            {
                for (int index = 0; index < BodyRuntime.SlotCount; index++)
                {
                    if (!isActiveAndEnabled || epoch != triggerEpoch) return;
                    OrganInstance trigger = body.GetSlot(index);
                    if (trigger == null || !ParameterisedBehaviour.Matches(signal, trigger.Definition)) continue;
                    ProcessConfiguredTrigger(body, index, trigger, signal);
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
