using NUnit.Framework;
using SpringUp.Organs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public sealed class AutomaticAttackChecks
    {
        [Test] public void ThreePipelinesRepeatIndependentlyWithoutCharge()
        {
            var go = new GameObject("Automatic attack check");
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>();
                pipeline.TargetSelector = () => "target";
                pipeline.StepIntervalProvider = _ => .15f;
                pipeline.CycleDurationProvider = _ => 2;
                foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
                    pipeline.GetBody(part).SetSlot(0, new OrganInstance(new OrganDefinition("test", "test", OrganType.Actuator, 5, chargeCost: 1000000)));
                int attacks = 0; pipeline.AttackProduced += _ => attacks++;
                pipeline.AdvanceAutomatic(.24f); Assert.AreEqual(0, attacks);
                pipeline.AdvanceAutomatic(.01f); Assert.AreEqual(3, attacks);
                pipeline.AdvanceAutomatic(2); Assert.AreEqual(6, attacks, "All bodies repeat independently, ignoring legacy cost data.");
                pipeline.enabled = false; pipeline.AdvanceAutomatic(100); Assert.AreEqual(6, attacks);
                foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart))) pipeline.GetBody(part).ResetTiming();
                pipeline.enabled = true; pipeline.AdvanceAutomatic(.24f); Assert.AreEqual(6, attacks);
                pipeline.AdvanceAutomatic(.01f); Assert.AreEqual(9, attacks, "Wave reset does not bank inactive time.");
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void MissingTargetDoesNotStopSubsequentAutomaticCycles()
        {
            var go = new GameObject("Automatic target check");
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>();
                pipeline.StepInterval = .1f; pipeline.CycleDurationProvider = _ => 1;
                pipeline.GetBody(BodyPart.Hand).SetSlot(0, new OrganInstance(new OrganDefinition("test", "test", OrganType.Actuator, 5)));
                int attacks = 0; pipeline.AttackProduced += _ => attacks++;
                pipeline.TargetSelector = () => null; pipeline.AdvanceAutomatic(.25f); Assert.AreEqual(0, attacks);
                pipeline.TargetSelector = () => "target"; pipeline.AdvanceAutomatic(1); Assert.AreEqual(1, attacks);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
