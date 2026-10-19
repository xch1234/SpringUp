using NUnit.Framework;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Part = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public static class RuntimeBugfixChecks
    {
        public static void RunLogic()
        {
            new AutomaticAttackChecks().ThreePipelinesRepeatIndependentlyWithoutCharge();
            new AutomaticAttackChecks().MissingTargetDoesNotStopSubsequentAutomaticCycles();
            foreach (float period in new[] { 0f, 2f })
            {
                var body = new BodyRuntime(Part.Hand, .001f) { CycleDuration = period };
                for (int i = 0; i < 6; i++) body.SetSlot(i, new OrganInstance(new OrganDefinition("test", "test", OrganType.Actuator, 1)));
                Assert.AreEqual(.25f, body.StepInterval);
                body.Tick(.24f); Assert.AreEqual(0, body.ExecutedStepCount);
                body.Tick(.01f); Assert.AreEqual(1, body.ExecutedStepCount);
                body.Tick(.24f); Assert.AreEqual(1, body.ExecutedStepCount);
                body.Tick(.01f); Assert.AreEqual(2, body.ExecutedStepCount);
            }
            foreach (var kind in new[] { TriggerOn.Hit, TriggerOn.Hurt })
            {
                var go = new GameObject("Trigger interval check");
                try
                {
                    var pipe = go.AddComponent<OrganPipelineController>(); pipe.AutomaticExecution = false;
                    pipe.TargetSelector = () => "target"; pipe.TriggerRandom = () => 0;
                    var body = pipe.GetBody(Part.Head);
                    var source = new OrganInstance(new OrganDefinition("source", "source", OrganType.Actuator, 1));
                    body.SetSlot(0, source);
                    body.SetSlot(1, new OrganInstance(new OrganDefinition("trigger", "trigger", OrganType.Trigger,
                        triggerOn:kind, trigger:new TriggerSettings(kind, 1))));
                    body.SetSlot(2, new OrganInstance(source.Definition));
                    int attacks = 0; pipe.AttackProduced += _ => attacks++;
                    void Signal() => pipe.PublishSignal(new TriggerSignal(kind, new CastEvent("target", 1, source.Definition.Id, source.InstanceId)));
                    Signal(); Signal(); Assert.AreEqual(1, attacks);
                    pipe.AdvanceTriggers(.24f); Signal(); Assert.AreEqual(1, attacks);
                    pipe.AdvanceTriggers(.01f); Signal(); Assert.AreEqual(2, attacks);
                    pipe.AdvanceTriggers(0); Signal(); Assert.AreEqual(2, attacks, "Pause does not advance cooldown");
                }
                finally { Object.DestroyImmediate(go); }
            }
            var view = ScriptableObject.CreateInstance<SceneView>();
            try
            {
                view.size = float.Epsilon;
                Assert.IsTrue(SceneViewScaleGuard.Repair(view));
                Assert.AreEqual(10, view.size, .001f);
                view.size = 4;
                Assert.IsFalse(SceneViewScaleGuard.Repair(view)); Assert.AreEqual(4, view.size);
            }
            finally { Object.DestroyImmediate(view); }
            Debug.Log("RUNTIME_BUGFIX_LOGIC_PASS: minimum slot/trigger interval 0.25s, pause, Scene view scale recovery.");
        }
    }
}
