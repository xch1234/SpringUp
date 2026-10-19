using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    public sealed partial class OrganPipelineController
    {
        private sealed class TriggerState
        {
            public OrganInstance Organ;
            public BodyPart Part;
            public int Slot, Kills;
            public float Charge, Cooldown;
        }
        private sealed class ScheduledTrigger
        {
            public TriggerState State;
            public TriggerSignal Signal;
            public float Remaining;
        }
        private readonly Dictionary<string, TriggerState> triggerStates = new Dictionary<string, TriggerState>();
        private readonly List<ScheduledTrigger> scheduledTriggers = new List<ScheduledTrigger>();
        private Vector2? lastTriggerPosition;
        private int triggerEpoch;
        public float TriggerProbabilityMultiplier { get; set; } = 1;
        public float TriggerFrequencyMultiplier { get; set; } = 1;
        public Func<float> TriggerRandom { get; set; } = () => UnityEngine.Random.value;
        public Func<bool> MovingProvider { get; set; }
        public int PendingTriggerCount => scheduledTriggers.Count;

        private TriggerState StateFor(BodyRuntime body, int slot, OrganInstance organ)
        {
            if (!triggerStates.TryGetValue(organ.InstanceId, out var state) || state.Organ != organ
                || state.Part != body.Part || state.Slot != slot)
            {
                state = new TriggerState { Organ = organ, Part = body.Part, Slot = slot };
                triggerStates[organ.InstanceId] = state;
            }
            return state;
        }
        private bool StillEquipped(TriggerState state)
            => triggerStates.TryGetValue(state.Organ.InstanceId, out var current) && current == state
                && GetBody(state.Part).GetSlot(state.Slot) == state.Organ;
        public float TriggerProgress(BodyPart part, int slot)
        {
            var organ = GetBody(part).GetSlot(slot);
            if (organ == null || !triggerStates.TryGetValue(organ.InstanceId, out var state)) return 0;
            return organ.Definition.TriggerOn == TriggerOn.Kill ? state.Kills : state.Charge;
        }
        private void ProcessConfiguredTrigger(BodyRuntime body, int slot, OrganInstance organ, TriggerSignal signal)
        {
            // Legacy definitions without settings use the same binding rules.
            var settings = organ.Definition.Trigger ?? new TriggerSettings(organ.Definition.TriggerOn, 1, killThreshold: 1);
            if (settings.Condition != signal.Kind || signal.Kind == TriggerOn.Stationary) return;
            if (signal.Kind == TriggerOn.Hurt || signal.Kind == TriggerOn.Heal)
            {
                var predecessor = slot > 0 ? body.GetSlot(slot - 1) : null;
                bool summonBound = predecessor?.Definition.Projectile is PatternActuatorSettings p && p.Pattern == ActuatorPattern.Summon;
                if (signal.Cause.Context.ActorPosition != null)
                {
                    if (!body.IsTriggerSource(slot, signal.Cause.OriginInstanceId)) return;
                }
                else if (summonBound) return;
            }
            // Hurt/heal use the actor binding above; stationary is driven by AdvanceTriggers.
            // Every attack event (including delayed damage) must come from the adjacent predecessor.
            if (signal.Kind != TriggerOn.Hurt && signal.Kind != TriggerOn.Heal
                && !body.IsTriggerSource(slot, signal.Cause.OriginInstanceId)) return;
            var state = StateFor(body, slot, organ);
            if (!signal.Cause.Context.CanEnterTrigger(organ.InstanceId))
            {
                signal.Cause.Context.TryEnterTrigger(organ.InstanceId, out _, out var reason);
                TriggerBlocked?.Invoke(body, slot, organ, signal.Cause.Context, reason);
                return;
            }
            if (signal.Kind == TriggerOn.Kill) state.Kills++;
            if (signal.Kind == TriggerOn.Started)
            {
                scheduledTriggers.Add(new ScheduledTrigger { State = state, Signal = signal, Remaining = settings.Delay / Mathf.Max(.01f, TriggerFrequencyMultiplier) });
                return;
            }
            if (signal.Kind == TriggerOn.Hit || signal.Kind == TriggerOn.Debuff)
            {
                float probability = Mathf.Clamp01(settings.Probability * TriggerProbabilityMultiplier);
                if (probability <= 0 || (probability < 1 && TriggerRandom() >= probability)) return;
            }
            if (signal.Kind == TriggerOn.Kill)
            {
                if (state.Kills < settings.KillThreshold) return;
                state.Kills -= settings.KillThreshold;
            }
            ActivateConfigured(state, signal);
        }
        private void ActivateConfigured(TriggerState state, TriggerSignal signal)
        {
            if (!isActiveAndEnabled || !StillEquipped(state) || state.Cooldown > .000001f) return;
            var body = GetBody(state.Part);
            int nextSlot = state.Slot + 1;
            if (nextSlot >= BodyRuntime.SlotCount) return;
            var organ = body.GetTriggerSuccessor(state.Slot);
            if (organ == null) return;
            if (!signal.Cause.Context.TryEnterTrigger(state.Organ.InstanceId, out var child, out var reason))
            { TriggerBlocked?.Invoke(GetBody(state.Part), state.Slot, state.Organ, signal.Cause.Context, reason); return; }
            state.Cooldown = BodyRuntime.MinimumStepInterval;
            TriggerActivated?.Invoke(body, state.Slot, state.Organ, child);
            if (!isActiveAndEnabled || !StillEquipped(state)) return;
            TriggeredSlotExecuted?.Invoke(body, nextSlot, organ, child);
            if (!isActiveAndEnabled || !StillEquipped(state)) return;
            ExecuteAttack(organ, child, body.Part, nextSlot, signal.Cause.TargetId, signal.Position);
        }
        public void NotifyExecutionStarted(CastEvent cause) => PublishSignal(new TriggerSignal(TriggerOn.Started, cause, cause.EffectCenter));
        public void NotifyExecutionEnded(CastEvent cause, bool cancelled = false)
        { if (!cancelled) PublishSignal(new TriggerSignal(TriggerOn.Ended, cause, cause.EffectCenter)); }
        public void NotifyOwner(TriggerOn kind, CastEvent? cause = null)
        {
            var origin = OriginProvider?.Invoke();
            var payload = cause ?? new CastEvent("owner", 0, "owner", "owner", effectCenter: origin, allowZeroDamage: true);
            PublishSignal(new TriggerSignal(kind, payload, origin));
        }
        // Driven by the battle/preview clock, independent of automatic or manual attacks.
        public void AdvanceTriggers(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (!isActiveAndEnabled || deltaTime == 0) return;
            EnsureInitialized();
            int epoch = triggerEpoch;
            foreach (var key in new List<string>(triggerStates.Keys))
                if (!StillEquipped(triggerStates[key])) triggerStates.Remove(key);
            foreach (var state in triggerStates.Values) state.Cooldown = Mathf.Max(0, state.Cooldown - deltaTime);
            foreach (var pending in scheduledTriggers.ToArray())
            {
                if (!isActiveAndEnabled || epoch != triggerEpoch) return;
                if (!StillEquipped(pending.State)) { scheduledTriggers.Remove(pending); continue; }
                pending.Remaining -= deltaTime;
                if (pending.Remaining > .000001f) continue;
                scheduledTriggers.Remove(pending);
                ActivateConfigured(pending.State, pending.Signal);
            }
            var position = OriginProvider?.Invoke();
            bool moving = MovingProvider?.Invoke() ?? (position.HasValue && lastTriggerPosition.HasValue
                && (position.Value - lastTriggerPosition.Value).sqrMagnitude > .00000001f);
            lastTriggerPosition = position;
            foreach (var body in bodies)
                for (int i = 0; i < BodyRuntime.SlotCount; i++)
                {
                    if (!isActiveAndEnabled || epoch != triggerEpoch) return;
                    var organ = body.GetSlot(i); var settings = organ?.Definition.Trigger;
                    if (settings == null || settings.Condition != TriggerOn.Stationary) continue;
                    var state = StateFor(body, i, organ);
                    if (moving || !position.HasValue) { state.Charge = 0; continue; }
                    state.Charge += deltaTime * settings.ChargeRate * Mathf.Max(.01f, TriggerFrequencyMultiplier);
                    // Preserve remainder; normal fixed ticks emit at most one activation.
                    if (state.Charge + .000001f < settings.ChargeThreshold) continue;
                    state.Charge = Mathf.Max(0, state.Charge - settings.ChargeThreshold);
                    var cause = new CastEvent("owner", 0, organ.Definition.Id, organ.InstanceId,
                        effectCenter: position, allowZeroDamage: true);
                    ActivateConfigured(state, new TriggerSignal(TriggerOn.Stationary, cause, position));
                }
        }
        public void ResetTriggers()
        { triggerEpoch++; scheduledTriggers.Clear(); triggerStates.Clear(); lastTriggerPosition = OriginProvider?.Invoke(); pendingSignals.Clear(); }
        private void OnDisable() => ResetTriggers();
    }
}
