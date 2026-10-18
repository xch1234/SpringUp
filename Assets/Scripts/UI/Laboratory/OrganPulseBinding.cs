using System;
using SpringUp.Organs;

namespace SpringUp.Laboratory
{
    // A lightweight event adapter, also usable by an isolated laboratory preview pipeline.
    public sealed class OrganPulseBinding : IDisposable
    {
        OrganPipelineController pipeline;
        readonly Action<BodyPart, int> pulse;
        public OrganPulseBinding(OrganPipelineController source, Action<BodyPart, int> pulse)
        {
            pipeline = source; this.pulse = pulse;
            if (pipeline == null) return;
            pipeline.AttackProduced += Attack;
            pipeline.ActuatorProduced += Actuator;
            pipeline.TriggerActivated += Trigger;
        }
        void Attack(CastEvent hit)
        {
            if (string.IsNullOrEmpty(hit.OriginInstanceId)) return;
            for (int p = 0; p < 3; p++) for (int s = 0; s < BodyRuntime.SlotCount; s++)
                if (pipeline.GetBody((BodyPart)p).GetSlot(s)?.InstanceId == hit.OriginInstanceId)
                { pulse((BodyPart)p, s); return; }
        }
        void Actuator(ActuatorContext context) => Attack(context.Cause);
        void Trigger(BodyRuntime body, int index, OrganInstance organ, CastContext context) => pulse(body.Part, index);
        public void Dispose()
        {
            if (pipeline != null)
            {
                pipeline.AttackProduced -= Attack;
                pipeline.ActuatorProduced -= Actuator;
                pipeline.TriggerActivated -= Trigger;
            }
            pipeline = null;
        }
    }
}
