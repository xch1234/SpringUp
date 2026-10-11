using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart = SpringUp.Organs.BodyPart;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class HandCombinationChecks
    {
        private static readonly List<Object> objects = new List<Object>();
        private static Scene scene;

        [MenuItem("Tools/SpringUp/Checks By Stage/Run Hand Step 5 Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("[Hand Step5 Checks] 请先停止 Play。"); return; }
            var errors = new List<string>();
            Application.LogCallback record = (message, trace, type) =>
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += record;
            try
            {
                HandBlackHoleChecks.Run();
                Isolated(CheckSixSlots);
                Isolated(CheckBleedKill);
                Isolated(CheckTwoTentacles);
                Isolated(CheckOrderedPairs);
                Isolated(CheckEmptyTargetsAndRestart);
            }
            finally { Application.logMessageReceived -= record; }
            Require(errors.Count == 0, "检查或清理出现错误：" + string.Join(" | ", errors));
            Debug.Log("[Hand Step5 Checks] PASS: all 5 combination groups (including 49 ordered pairs) and previous 42 groups passed, including cleanup.");
        }

        private static void Isolated(Action action)
        {
            scene = EditorSceneManager.NewPreviewScene();
            try { action(); }
            finally { try { Cleanup(); } finally { EditorSceneManager.ClosePreviewScene(scene); } }
        }
        private static void Cleanup()
        {
            foreach (Object item in objects)
                if (item is GameObject go) go.GetComponent<HandOrganDemo>()?.StopDemo();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }
        private static GameObject Go(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(go); SceneManager.MoveGameObjectToScene(go, scene); return go;
        }
        private static OrganConfig Config(HandOrganKind kind)
            => OrganTestFactory.CreateCombo(kind, objects);
        private static DemoTarget Target(string id, float x = 0f, float health = 100f)
        {
            var target = Go(id).AddComponent<DemoTarget>(); target.InitializeForDemo(id, health);
            target.transform.position = new Vector3(x, 0f, 0f);
            var combat = target.gameObject.AddComponent<DemoCombatTarget>();
            var data = new SerializedObject(combat); data.FindProperty("moveSpeed").floatValue = 0f;
            data.ApplyModifiedPropertiesWithoutUndo(); return target;
        }
        private static HandOrganDemo Demo(HandOrganKind[] kinds, params DemoTarget[] targets)
        {
            var demo = Go("Combination").AddComponent<HandOrganDemo>();
            var data = new SerializedObject(demo);
            for (int i = 0; i < kinds.Length; i++) data.FindProperty("slots").GetArrayElementAtIndex(i).objectReferenceValue = Config(kinds[i]);
            var list = data.FindProperty("targets"); list.arraySize = targets.Length;
            for (int i = 0; i < targets.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            data.ApplyModifiedPropertiesWithoutUndo(); Require(demo.TryStartDemo(out string error), error ?? "组合应能启动。"); return demo;
        }
        private static BodyRuntime Hand(HandOrganDemo demo) => demo.GetComponent<OrganPipelineController>().GetBody(BodyPart.Hand);
        private static DemoCombatTarget Combat(DemoTarget target) => target.GetComponent<DemoCombatTarget>();

        private static void CheckSixSlots()
        {
            var a = Target("a"); var b = Target("unlisted", 2.5f); var c = Target("outside", 4.5f);
            var demo = Demo(new[] { HandOrganKind.CollapseBody, HandOrganKind.SteelPipe, HandOrganKind.RustKnife,
                HandOrganKind.SlimeGland, HandOrganKind.Tnt, HandOrganKind.MultiTentacle }, a);
            Hand(demo).Tick(1f); demo.GetComponent<DemoBlackHoleField>().Advance(1.5f);
            Require(b.transform.position == Vector3.zero && b.CurrentHealth == 100f, "黑洞先把名单外敌人拉入 TNT 范围，不扣血。");
            Hand(demo).Tick(3f);
            Require(a.CurrentHealth == 96f && Combat(a).Statuses.StunRemaining > 0f
                && Combat(a).Statuses.BleedRemaining > 0f && Combat(a).Statuses.SlowStacks == 1,
                "三个状态应同时存在，不相互覆盖。");
            Hand(demo).Tick(1f);
            Require(a.CurrentHealth == 91f && b.CurrentHealth == 95f && c.CurrentHealth == 100f,
                "TNT 应炸到黑洞拉近的名单外敌人，不能把状态重复附加到 TNT。");
            Require(Combat(b).Statuses.SlowStacks == 0 && Combat(b).Statuses.BleedRemaining == 0f,
                "单目标状态不能误传给旁边敌人。");
            Hand(demo).Tick(1f);
            Require(demo.TriggerCount == 0 && Hand(demo).LastExecutedSlot == 5, "正常走到触手槽不等于击杀触发。");
            demo.StopDemo();
            Require(demo.GetComponent<DemoBlackHoleField>().ActiveCount == 0 && Combat(a).Statuses.SlowStacks == 0
                && Combat(a).Statuses.BleedRemaining == 0f && Combat(a).Statuses.StunRemaining == 0f,
                "组合停用时必须清除状态和黑洞。");
        }

        private static void CheckBleedKill()
        {
            var a = Target("bleed_victim", 0f, 2f); var b = Target("survivor", 1f);
            var demo = Demo(new[] { HandOrganKind.RustKnife, HandOrganKind.CollapseBody,
                HandOrganKind.Tnt, HandOrganKind.MultiTentacle }, a, b);
            CastEvent original = default;
            var appended = new List<CastEvent>();
            demo.GetComponent<OrganPipelineController>().AttackProduced += attack =>
            { if (attack.ChainDepth == 0) original = attack; else appended.Add(attack); };
            Hand(demo).Tick(1f);
            Require(a.CurrentHealth == 1f && demo.TriggerCount == 0, "直接伤害没有击杀时不能提前触发。");
            Combat(a).Advance(1f);
            Require(!a.IsAlive && demo.TriggerCount == 1 && b.CurrentHealth == 94f
                && demo.GetComponent<DemoBlackHoleField>().ActiveCount == 1 && demo.ExplosionCount == 1,
                "流血延迟击杀应追加锈刀、黑洞和 TNT。");
            Require(appended.Count == 3, "额外执行次数应准确。");
            foreach (var attack in appended)
                Require(attack.ChainId == original.ChainId && attack.ChainDepth == 1, "延迟击杀必须保留原攻击链。");
            Require(Hand(demo).LastExecutedSlot == 0, "追加执行不能移动正常游标。");
        }

        private static void CheckTwoTentacles()
        {
            var a = Target("first", 0f, 5f); var b = Target("second", 1f, 80f); var c = Target("last", 4.5f);
            var demo = Demo(new[] { HandOrganKind.Fist, HandOrganKind.RustKnife, HandOrganKind.SlimeGland,
                HandOrganKind.Tnt, HandOrganKind.MultiTentacle, HandOrganKind.MultiTentacle }, a, b, c);
            Hand(demo).Tick(1f);
            Require(demo.TriggerCount == 2 && demo.ExplosionCount == 2 && b.CurrentHealth == 56f
                && Combat(b).Statuses.SlowStacks == 2 && c.CurrentHealth == 100f,
                "两个触手是独立实例，各追加一次；同链不能无限循环。");
            // B 保留追加锈刀的流血，再让它在下一跳被该流血击杀。
            b.TryApplyAttack(new CastEvent(b.TargetId, 55f, "check", "check"));
            Combat(b).Advance(1f);
            Require(!b.IsAlive && demo.TriggerCount == 2 && c.CurrentHealth == 100f,
                "同一旧链的延迟击杀不能让已经发动过的两个触手再发动。");
            Hand(demo).Tick(1f);
            Require(c.CurrentHealth == 99f && Hand(demo).LastExecutedSlot == 1,
                "新节拍仍应从正常游标继续，并选择下一个存活目标。");
        }

        private static void CheckOrderedPairs()
        {
            // 七种器官有顺序地两两搭配，共 49 对，也包含重复安装同种器官。
            foreach (HandOrganKind first in Enum.GetValues(typeof(HandOrganKind)))
                foreach (HandOrganKind second in Enum.GetValues(typeof(HandOrganKind)))
                {
                    var target = Target("pair");
                    var demo = Demo(new[] { first, second }, target);
                    float expectedDamage = Hand(demo).GetSlot(0).Definition.Damage + Hand(demo).GetSlot(1).Definition.Damage;
                    Hand(demo).Tick(2f);
                    Require(target.CurrentHealth == 100f - expectedDamage && demo.TriggerCount == 0,
                        $"{first} + {second} 的两拍不能漏执行或重复扣血。");
                    int holes = (first == HandOrganKind.CollapseBody ? 1 : 0) + (second == HandOrganKind.CollapseBody ? 1 : 0);
                    Require((demo.GetComponent<DemoBlackHoleField>()?.ActiveCount ?? 0) == holes,
                        "重复黑洞配置必须生成独立器官实例。");
                    int slows = (first == HandOrganKind.SlimeGland ? 1 : 0) + (second == HandOrganKind.SlimeGland ? 1 : 0);
                    Require(Combat(target).Statuses.SlowStacks == slows, "同种减速应正确叠层。");
                    Cleanup(); // 下组不能受到本组留下的黑洞、状态或订阅影响。
                }
        }

        private static void CheckEmptyTargetsAndRestart()
        {
            var enemy = Target("unlisted", 1f);
            var demo = Demo(new[] { HandOrganKind.CollapseBody, HandOrganKind.SteelPipe, HandOrganKind.RustKnife,
                HandOrganKind.SlimeGland, HandOrganKind.Tnt, HandOrganKind.MultiTentacle });
            Hand(demo).Tick(6f);
            Require(enemy.CurrentHealth == 95f && demo.ExplosionCount == 1 && demo.TriggerCount == 0,
                "Targets 为空时单目标攻击应跳过，范围效果继续。");
            var field = demo.GetComponent<DemoBlackHoleField>();
            field.Advance(0f); Hand(demo).Tick(0f); Combat(enemy).Advance(0f);
            Require(enemy.transform.position.x == 1f && enemy.CurrentHealth == 95f && field.LongestRemaining == 5f,
                "零时间不能推进攻击、状态或黑洞。");
            demo.StopDemo(); Require(field.ActiveCount == 0, "清理后不能留有黑洞。");
            Require(demo.TryStartDemo(out _) && demo.TryStartDemo(out _), "重复启动应安全。");
            Hand(demo).Tick(6f);
            Require(enemy.CurrentHealth == 90f && demo.ExplosionCount == 1 && field.ActiveCount == 1,
                "重新连接不能双倍扣血或重复订阅。");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Hand Step5 Checks] " + message); }
    }
}
