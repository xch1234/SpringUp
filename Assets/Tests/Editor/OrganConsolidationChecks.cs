using System;
using System.Collections.Generic;
using System.IO;
using SpringUp.Organs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart = SpringUp.Organs.BodyPart;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class OrganConsolidationChecks
    {
        private static readonly List<Object> objects = new List<Object>();
        private static readonly string[] names = { "Fist", "SteelPipe", "MultiTentacle", "RustKnife", "SlimeGland", "CollapseBody", "TNT" };
        private static Scene scene;

        [MenuItem("Tools/SpringUp/Run All Organ Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Organ Checks] 请先停止 Play。"); return; }
            var errors = new List<string>();
            Application.LogCallback record = (message, trace, type) =>
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += record;
            try
            {
                HandCombinationChecks.Run();
                Isolated(CheckAssets);
                Isolated(CheckStatusReferences);
                Isolated(CheckPresets);
                Isolated(CheckManual);
            }
            finally { Application.logMessageReceived -= record; }
            Require(errors.Count == 0, string.Join(" | ", errors));
            Debug.Log("[Organ Consolidation Checks] PASS: 4 integration groups and previous 47 groups passed; 51 total, including cleanup.");
        }

        private static void Isolated(Action check)
        {
            scene = EditorSceneManager.NewPreviewScene();
            try { check(); }
            finally
            {
                try
                {
                    foreach (Object item in objects) if (item is GameObject go) go.GetComponent<HandOrganDemo>()?.StopDemo();
                    for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                }
                finally { objects.Clear(); EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
        private static GameObject Go(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; objects.Add(go);
            SceneManager.MoveGameObjectToScene(go, scene); return go;
        }
        private static OrganConfig Asset(int index) => AssetDatabase.LoadAssetAtPath<OrganConfig>(OrganTestFactory.AssetPath(names[index]));
        private static void CheckAssets()
        {
            Require(AssetDatabase.FindAssets("t:OrganConfig", new[] { "Assets/Data/Organs/Actuators" }).Length == 19, "十九份执行器入口。六个直接效果与十三个弹体/区域效果。");
            Require(AssetDatabase.FindAssets("t:StatusEffectConfig", new[] { "Assets/Data/Statuses" }).Length == 3, "应有三份独立状态资源。");
            for (int i = 0; i < names.Length; i++)
            {
                OrganConfig config = Asset(i);
                Require(config != null && config.TryCreateDefinition(out var identity, out _) && identity.Id == OrganTestFactory.Ids[i] && config.TryCreateDefinition(out _, out _), "器官资源或状态引用失效：" + names[i]);
                if (OrganTestFactory.Status(config) != null) Require(AssetDatabase.GetAssetPath(OrganTestFactory.Status(config)).StartsWith("Assets/Data/Statuses/"), "状态必须引用独立目录。");
            }
            Require(!AssetDatabase.IsValidFolder("Assets/Data/HandCombo") && !AssetDatabase.IsValidFolder("Assets/Data/HandOrgans"), "旧重复目录应已移除。");
            Require(Directory.GetFiles(Path.Combine(Application.dataPath, "Scenes"), "OrganDemo.unity").Length == 1
                && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/OrganDemo.unity") != null, "A 的统一场景 OrganDemo 必须存在；B/C 场景不在此断言范围。");
        }
        private static void CheckStatusReferences()
        {
            var config = OrganTestFactory.CreateCombo(HandOrganKind.RustKnife, objects);
            Require(config.TryCreateDefinition(out var original, out _), "独立流血配置应可生成快照。");
            var edit = new SerializedObject(OrganTestFactory.Status(config)); edit.FindProperty("tickDamage").floatValue = 7f; edit.ApplyModifiedPropertiesWithoutUndo();
            Require(config.TryCreateDefinition(out var changed, out _) && original.Status.TickDamage == 1f && changed.Status.TickDamage == 7f,
                "修改状态资源不能改写已产生的攻击快照。");
            var organ = OrganTestFactory.Edit(config); organ.FindProperty("statusEffect").objectReferenceValue = null; organ.ApplyModifiedPropertiesWithoutUndo();
            Require(config.TryCreateDefinition(out var noStatus, out _) && noStatus.Status == null, "状态可选，清空后保留直接伤害。");
            var steel = OrganTestFactory.CreateCombo(HandOrganKind.SteelPipe, objects);
            organ.Update(); organ.FindProperty("statusEffect").objectReferenceValue = OrganTestFactory.Status(steel); organ.ApplyModifiedPropertiesWithoutUndo();
            Require(config.TryCreateDefinition(out var replaced, out _) && replaced.Status.Kind == StatusEffectKind.Stun, "直接执行器可使用选择的有效状态配置。");
        }
        private static OrganDemoSetup Setup(out DemoTarget[] enemies)
        {
            var setup = Go("Unified").AddComponent<OrganDemoSetup>(); setup.transform.position = new Vector3(7f, 0f, 0f);
            var data = new SerializedObject(setup);
            for (int i = 0; i < names.Length; i++) data.FindProperty("organs").GetArrayElementAtIndex(i).objectReferenceValue = Asset(i);
            enemies = new DemoTarget[3];
            for (int i = 0; i < 3; i++)
            {
                enemies[i] = Go("Enemy" + i).AddComponent<DemoTarget>(); enemies[i].gameObject.AddComponent<DemoCombatTarget>();
                enemies[i].InitializeForDemo("initial_" + i, 100f);
                data.FindProperty("enemies").GetArrayElementAtIndex(i).objectReferenceValue = enemies[i];
            }
            data.ApplyModifiedPropertiesWithoutUndo(); return setup;
        }
        private static void Select(OrganDemoSetup setup, OrganDemoPreset preset)
        {
            var edit = new SerializedObject(setup); edit.FindProperty("preset").intValue = (int)preset; edit.ApplyModifiedPropertiesWithoutUndo();
            Require(setup.ConfigurePreset(out string error), error ?? "预设配置失败。");
        }
        private static void CheckPresets()
        {
            var setup = Setup(out var enemies); var demo = setup.GetComponent<HandOrganDemo>();
            var controller = setup.GetComponent<OrganPipelineController>();
            int[] counts = { 6, 6, 1, 5, 1, 1 };
            for (int mode = 0; mode < counts.Length; mode++)
            {
                Select(setup, (OrganDemoPreset)mode); Require(demo.TryStartDemo(out string error), error ?? "预设应可启动。");
                int count = 0; var hand = controller.GetBody(BodyPart.Hand);
                for (int i = 0; i < 6; i++) if (hand.GetSlot(i) != null) count++;
                Require(count == counts[mode], "预设槽位数量不正确。");
                if (mode == (int)OrganDemoPreset.DoubleTentacle)
                    Require(hand.GetSlot(4).InstanceId != hand.GetSlot(5).InstanceId, "相同触手配置必须安装成独立实例。");
                if (mode == (int)OrganDemoPreset.Explosion)
                {
                    hand.Tick(1f);
                    Require(Asset(6).TryCreateDefinition(out var tnt, out _)
                        && enemies[0].CurrentHealth == Mathf.Max(0f, 100f - tnt.Damage), "统一入口 TNT 没有按唯一资源扣血。");
                }
                if (mode == (int)OrganDemoPreset.BlackHole)
                {
                    Require(controller.StepInterval == 6f, "黑洞模式应有可观察到期的节拍。");
                    hand.Tick(6f); Require(demo.GetComponent<DemoBlackHoleField>().ActiveCount == 1, "黑洞预设应生成一个黑洞。");
                }
                demo.StopDemo();
                Require((demo.GetComponent<DemoBlackHoleField>()?.ActiveCount ?? 0) == 0, "切换预设前的清理不能遗留黑洞。");
            }
        }
        private static void CheckManual()
        {
            var setup = Setup(out var enemies); var demo = setup.GetComponent<HandOrganDemo>();
            var slots = new OrganConfig[6]; slots[2] = Asset(0); demo.SetLoadout(slots, enemies);
            enemies[0].InitializeForDemo("custom", 27f); enemies[0].transform.position = new Vector3(11f, 2f, 0f);
            setup.GetComponent<OrganPipelineController>().StepInterval = 3f;
            Select(setup, OrganDemoPreset.Manual);
            Require(demo.TryStartDemo(out _), "手动模式应保留装备。");
            Require(setup.GetComponent<OrganPipelineController>().GetBody(BodyPart.Hand).GetSlot(2).Definition.Id == "fist"
                && enemies[0].CurrentHealth == 27f && enemies[0].transform.position.x == 11f
                && setup.GetComponent<OrganPipelineController>().StepInterval == 3f, "手动模式不能覆盖场景设置。");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Organ Consolidation Checks] " + message); }
    }
}
