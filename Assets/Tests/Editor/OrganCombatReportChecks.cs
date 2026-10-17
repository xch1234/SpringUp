using System;
using NUnit.Framework;
using SpringUp.Organs;
using Tideflesh.Systems;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public sealed class OrganCombatReportChecks
    {
        [Test] public void BoundedHistoryAttributionAndRebinding()
        {
            var go = new GameObject("Report checks");
            try
            {
                var pipeline = go.AddComponent<OrganPipelineController>(); pipeline.AutomaticExecution = false;
                var report = go.AddComponent<OrganCombatReport>(); report.Bind(pipeline); report.Bind(pipeline);
                var definition = new SpringUp.Organs.OrganDefinition("report_fist", "测试拳头", OrganType.Actuator, 5);
                var organ = new SpringUp.Organs.OrganInstance(definition);
                pipeline.GetBody(BodyPart.Hand).SetSlot(0, organ);
                pipeline.TargetSelector = () => "target";
                for (int i = 0; i < 10000; i++) Assert.IsTrue(pipeline.TryExecuteSlot(BodyPart.Hand, 0));
                Assert.AreEqual(OrganCombatReport.HistoryLimit, report.History.Count);
                Assert.IsTrue(report.IsFlashing(BodyPart.Hand, 0));
                report.ResetWave(); pipeline.TryExecuteSlot(BodyPart.Hand, 0);
                Assert.AreEqual(1, report.History.Count, "Repeated bind must not duplicate subscriptions.");
                var hit = new CastEvent("target", 100, definition.Id, organ.InstanceId);
                report.RecordDamage(hit, 3); report.RecordDamage(hit, 2);
                report.RecordDamage(hit, 0); report.RecordDamage(hit, float.NaN);
                Assert.AreEqual(5, report.Damage[definition.Id], "Use actual loss rather than requested damage.");
                for (int i = 0; i < 1000; i++) report.RecordDamage(new CastEvent("target", 1, "source" + i, "instance" + i), 1);
                Assert.LessOrEqual(report.Damage.Count, OrganCombatReport.SourceLimit);
                float total = 0; foreach (var value in report.Damage.Values) total += value;
                Assert.AreEqual(1005, total, "Overflow aggregation must preserve damage.");
                report.ResetWave(); Assert.AreEqual(0, report.Damage.Count); Assert.AreEqual(0, report.History.Count);
                Assert.IsFalse(report.IsFlashing(BodyPart.Hand, 0));
                var trigger = new SpringUp.Organs.OrganInstance(new SpringUp.Organs.OrganDefinition("trigger", "触发器", OrganType.Trigger));
                pipeline.GetBody(BodyPart.Hand).SetSlot(1, trigger);
                pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, hit));
                Assert.AreEqual(2, report.History.Count, "Trigger and successful follow-up both have records.");
                foreach (var entry in report.History) Assert.AreEqual(hit.ChainId, entry.Chain);
                Assert.IsTrue(report.IsFlashing(BodyPart.Hand, 1));
                report.ResetWave(); report.Bind(null); pipeline.TryExecuteSlot(BodyPart.Hand, 0); Assert.AreEqual(0, report.History.Count);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void AmbiguousBehaviourIsRejected()
        {
            var config = ScriptableObject.CreateInstance<OrganConfig>();
            var trigger = ScriptableObject.CreateInstance<OrganTriggerConfig>();
            var torso = ScriptableObject.CreateInstance<TorsoOrganConfig>();
            try
            {
                var serialized = new SerializedObject(config);
                serialized.FindProperty("trigger").objectReferenceValue = trigger;
                serialized.FindProperty("torso").objectReferenceValue = torso;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(config.TryCreateDefinition(out _, out string error));
                StringAssert.Contains("只能关联一种", error);
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(trigger); Object.DestroyImmediate(torso); }
        }
        public static void RunPlay(SpringUpRunDriver driver)
        {
            Assert.Greater(driver.CombatReport.Damage.Count, 0, "Real combat damage must reach the report.");
            var hud = Object.FindFirstObjectByType<RunEquipmentHud>();
            var button = hud.transform.Find("Combat report toggle").GetComponent<Button>();
            var panel = hud.transform.Find("Combat report").gameObject;
            Assert.IsFalse(panel.activeSelf); button.onClick.Invoke(); Assert.IsTrue(panel.activeSelf);
            button.onClick.Invoke(); Assert.IsFalse(panel.activeSelf);
            driver.CombatReport.ResetWave(); Assert.AreEqual(0, driver.CombatReport.Damage.Count);
            Debug.Log("[Organ Report] PLAY PASS: actual combat attribution, expandable HUD and wave reset.");
        }
        public static void RunVisualBatch()
        {
            try
            {
                new OrganCombatReportChecks().BoundedHistoryAttributionAndRebinding();
                Debug.Log("[Organ Report] FINAL EDIT PASS: fixed-slot flash storage and same-chain trigger trace.");
                IntegratedVisualChecks.RunBatch();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void RunBatch()
        {
            try
            {
                var checks = new OrganCombatReportChecks();
                checks.BoundedHistoryAttributionAndRebinding(); checks.AmbiguousBehaviourIsRejected();
                OrganConfigurationValidator.Validate();
                Debug.Log("[Organ Report] EDIT PASS: bounded 10000 attacks, 1000 sources, actual damage, reset, unbind and ambiguous config.");
                TorsoOrganChecks.RunBatch();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
