using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using BodyPart = SpringUp.Organs.BodyPart;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class HandOrganChecks
    {
        private static readonly List<Object> objects = new List<Object>();

        [MenuItem("Tools/SpringUp/Run Hand Step 1 Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Hand Step1 Checks] 请先停止 Play，再运行编辑模式检查。");
                return;
            }
            var loggedErrors = new List<string>();
            Application.LogCallback captureErrors = (message, trace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    loggedErrors.Add(message);
            };
            Application.logMessageReceived += captureErrors;
            try
            {
                OrganPipelineStep3Checks.Run();
                CheckAssets();
                CheckEditableSnapshot();
                CheckConfiguredChain();
                CheckRestartAndCleanup();
                CheckInvalidSetup();
            }
            finally
            {
                try
                {
                    // 直接调用清理逻辑，不能在编辑模式下用消息模拟 OnDisable。
                    foreach (Object item in objects)
                        if (item is GameObject go && go.GetComponent<HandOrganDemo>() != null)
                            go.GetComponent<HandOrganDemo>().StopDemo();
                    for (int i = objects.Count - 1; i >= 0; i--)
                        if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                }
                finally
                {
                    objects.Clear();
                    Application.logMessageReceived -= captureErrors;
                }
            }
            // Unity 的断言日志不一定抛出 C# 异常，必须检查日志，且必须包含清理阶段。
            Require(loggedErrors.Count == 0, "检查或清理期间出现错误：" + string.Join(" | ", loggedErrors));
            Debug.Log("[Hand Step1 Checks] PASS: all 5 groups passed; previous 17 groups also passed; cleanup completed without errors or assertions.");
        }

        private static GameObject MakeObject(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(go);
            return go;
        }

        private static OrganConfig Config(HandOrganKind kind, float damage)
        {
            var config = ScriptableObject.CreateInstance<OrganConfig>();
            objects.Add(config);
            SetNumber(config, "kind", (int)kind);
            SetNumber(config, "damage", damage);
            // 第一步只检查直接伤害；状态效果由第二步的检查覆盖。
            SetNumber(config, "statusChancePercent", 0f);
            return config;
        }

        private static void SetNumber(Object target, string name, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty field = serialized.FindProperty(name);
            if (field.propertyType == SerializedPropertyType.Enum) field.intValue = (int)value;
            else field.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static DemoTarget Target(float health, string id)
        {
            var target = MakeObject(id).AddComponent<DemoTarget>();
            target.InitializeForDemo(id, health);
            return target;
        }

        private static HandOrganDemo Demo(OrganConfig[] slots, params DemoTarget[] targets)
        {
            var demo = MakeObject("Hand organ check").AddComponent<HandOrganDemo>();
            var serialized = new SerializedObject(demo);
            SerializedProperty array = serialized.FindProperty("slots");
            array.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            array = serialized.FindProperty("targets");
            array.arraySize = targets.Length;
            for (int i = 0; i < targets.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return demo;
        }

        private static BodyRuntime Hand(HandOrganDemo demo)
            => demo.GetComponent<OrganPipelineController>().GetBody(BodyPart.Hand);

        private static void CheckAssets()
        {
            string[] names = { "Fist", "SteelPipe", "MultiTentacle", "RustKnife", "SlimeGland", "CollapseBody", "TNT" };
            for (int i = 0; i < names.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/HandOrgans/" + names[i] + ".asset");
                Require(asset != null && (int)asset.Kind == i, "七份样例配置应能加载，且种类正确。");
                bool supported = asset.TryCreateDefinition(out _, out string error);
                Require(supported, "七种手部器官的独立效果都应能读取配置。");
                if (!supported) Require(!string.IsNullOrEmpty(error), "拒绝时应解释原因。");
            }
        }

        private static void CheckEditableSnapshot()
        {
            OrganConfig pipe = Config(HandOrganKind.SteelPipe, 8f);
            Require(pipe.TryCreateDefinition(out OrganDefinition original, out _), "钢管应能生成配置。");
            SetNumber(pipe, "damage", 12f);
            Require(pipe.TryCreateDefinition(out OrganDefinition changed, out _), "修改伤害后应能生成配置。");
            Require(original.Damage == 8f && changed.Damage == 12f, "修改资源不能改掉已经生成的快照。");
            DemoTarget target = Target(40f, "editable");
            HandOrganDemo demo = Demo(new[] { pipe, pipe, null, null, null, null }, target);
            Require(demo.TryStartDemo(out _), "可安装同一资源的两个独立实例。");
            BodyRuntime hand = Hand(demo);
            Require(hand.GetSlot(0).InstanceId != hand.GetSlot(1).InstanceId, "同一资源不能共用器官实例编号。");
            SetNumber(pipe, "damage", 1f);
            hand.Tick(2f);
            Require(target.CurrentHealth == 16f, "已启动的两次攻击应各扣 12，而不是随资源变成 1。");
            Require(demo.TryStartDemo(out _), "可重新读取配置。");
            hand.Tick(1f);
            Require(target.CurrentHealth == 15f, "重新读取后，新伤害 1 应真正用于扣血。");
        }

        private static void CheckConfiguredChain()
        {
            DemoTarget a = Target(5f, "chain_a");
            DemoTarget b = Target(20f, "chain_b");
            HandOrganDemo demo = Demo(new[] { Config(HandOrganKind.Fist, 5f), null, null,
                Config(HandOrganKind.MultiTentacle, 0f), null, null }, a, b);
            Require(demo.TryStartDemo(out _), "拳头和触手应能运行。");
            BodyRuntime hand = Hand(demo);
            hand.Tick(1f);
            Require(a.CurrentHealth == 0f && b.CurrentHealth == 15f && demo.TriggerCount == 1,
                "击杀 A 应立刻追加攻击 B，且触手发动一次。");
            hand.Tick(1f);
            Require(b.CurrentHealth == 15f && hand.LastExecutedSlot == 3, "额外扫描不能移动正常游标。");
            hand.Tick(1f);
            Require(b.CurrentHealth == 10f, "下一个拳头节拍继续正常攻击。");
        }

        private static void CheckRestartAndCleanup()
        {
            DemoTarget target = Target(40f, "restart");
            HandOrganDemo demo = Demo(new[] { Config(HandOrganKind.SteelPipe, 8f), null, null, null, null, null }, target);
            Require(demo.TryStartDemo(out _) && demo.TryStartDemo(out _), "重复初始化应安全。");
            Hand(demo).Tick(1f);
            Require(target.CurrentHealth == 32f, "不能重复订阅造成双倍扣血。");
            demo.StopDemo();
            demo.StopDemo(); // 清理允许重复调用，不能留下回调或再次扣血。
            Hand(demo).Tick(10f);
            Require(target.CurrentHealth == 32f && Hand(demo).GetSlot(0) == null, "停用应拆除自身安装并停止伤害。");
            Require(demo.GetComponent<OrganPipelineController>().TargetSelector == null, "停用应解绑目标选择。");
        }

        private static void CheckInvalidSetup()
        {
            OrganConfig invalid = Config(HandOrganKind.Fist, -1f);
            Require(!invalid.TryCreateDefinition(out _, out _), "应拒绝负伤害。");
            SetNumber(invalid, "damage", float.NaN);
            Require(!invalid.TryCreateDefinition(out _, out _), "应拒绝非数值伤害。");
            DemoTarget target = Target(20f, "duplicate");
            HandOrganDemo pending = Demo(new[] { Config((HandOrganKind)999, 20f), null, null, null, null, null }, target);
            Require(!pending.TryStartDemo(out _), "无效器官种类应拒绝运行。");
            Require(Hand(pending).GetSlot(0) == null, "失败不能留下半套装备。");
            var empty = new OrganConfig[6];
            HandOrganDemo duplicate = Demo(empty, target, Target(20f, "duplicate"));
            Require(!duplicate.TryStartDemo(out _), "重复敌人编号应拒绝。");
            HandOrganDemo wrongSize = Demo(new OrganConfig[7], target);
            Require(!wrongSize.TryStartDemo(out _), "应拒绝非六槽数组。");
            HandOrganDemo noTarget = Demo(empty);
            Require(!noTarget.TryStartDemo(out _), "未接目标时应给出设置提示。");
            HandOrganDemo allEmpty = Demo(empty, target);
            Require(allEmpty.TryStartDemo(out _), "六槽全空应允许测试。");
            Hand(allEmpty).Tick(100f);
            Require(target.CurrentHealth == 20f, "空槽不能产生攻击。");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Hand Step1 Checks] " + message);
        }
    }
}
