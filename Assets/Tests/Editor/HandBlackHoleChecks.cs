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
    public static class HandBlackHoleChecks
    {
        private static readonly List<Object> objects = new List<Object>();
        private static Scene scene;
        [MenuItem("Tools/SpringUp/Checks By Stage/Run Hand Step 4 Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("[Hand Step4 Checks] 请先停止 Play。"); return; }
            var errors = new List<string>();
            Application.LogCallback record = (message, trace, type) =>
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += record;
            try
            {
                HandExplosionChecks.Run();
                Isolated(CheckConfig);
                Isolated(CheckGeometryAndTime);
                Isolated(CheckScenePull);
                Isolated(CheckRefreshAndOrigins);
                Isolated(CheckChainAndCleanup);
            }
            finally { Application.logMessageReceived -= record; }
            Require(errors.Count == 0, "检查或清理出现错误：" + string.Join(" | ", errors));
            Debug.Log("[Hand Step4 Checks] PASS: all 5 black hole groups and previous 37 groups passed, including cleanup.");
        }

        private static void Isolated(Action action)
        {
            scene = EditorSceneManager.NewPreviewScene();
            try { action(); }
            finally
            {
                try
                {
                    foreach (Object item in objects)
                        if (item is GameObject go) go.GetComponent<HandOrganDemo>()?.StopDemo();
                    for (int i = objects.Count - 1; i >= 0; i--)
                        if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                }
                finally { objects.Clear(); EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
        private static GameObject Go(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(go); SceneManager.MoveGameObjectToScene(go, scene); return go;
        }
        private static void Set(Object obj, string name, float value)
        {
            var data = new SerializedObject(obj); var field = data.FindProperty(name);
            if (field.propertyType == SerializedPropertyType.Enum) field.intValue = (int)value;
            else field.floatValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static OrganConfig Config(HandOrganKind kind = HandOrganKind.CollapseBody)
        {
            var config = ScriptableObject.CreateInstance<OrganConfig>(); objects.Add(config);
            Set(config, "kind", (int)kind); Set(config, "damage", 5f);
            return config;
        }
        private static DemoTarget Target(string name, float x, float health = 100f)
        {
            var target = Go(name).AddComponent<DemoTarget>(); target.InitializeForDemo(name, health);
            target.transform.position = new Vector3(x, 0f, 2f); return target;
        }
        private static HandOrganDemo Demo(OrganConfig config)
        {
            var demo = Go("Black hole demo").AddComponent<HandOrganDemo>();
            var data = new SerializedObject(demo);
            data.FindProperty("slots").GetArrayElementAtIndex(0).objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
            Require(demo.TryStartDemo(out string error), error ?? "空 Targets 黑洞配置应可启动。"); return demo;
        }
        private static BodyRuntime Hand(HandOrganDemo demo) => demo.GetComponent<OrganPipelineController>().GetBody(BodyPart.Hand);
        private static CastEvent Request(float radius = 3f, float duration = 5f, float speed = 2f, string id = "hole")
            => new CastEvent(null, 0f, "collapse_body", id, shape: AttackShape.BlackHole, radius: radius,
                blackHole: new BlackHoleData(duration, speed));

        private static void CheckConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/Organs/Hand/CollapseBody.asset");
            Require(asset != null && asset.TryCreateDefinition(out _, out _), "坍缩体资源应可读取。");
            var config = Config();
            Require(config.TryCreateDefinition(out OrganDefinition first, out _) && first.Damage == 0f
                && first.Shape == AttackShape.BlackHole && first.BlackHole.Duration == 5f, "黑洞默认五秒且不扣血。");
            Set(config, "blackHoleDuration", 2f); Set(config, "blackHolePullSpeed", 4f); Set(config, "blackHoleRadius", 6f);
            config.TryCreateDefinition(out OrganDefinition changed, out _);
            Require(first.BlackHole.Duration == 5f && first.Radius == 3f && changed.BlackHole.Duration == 2f
                && changed.BlackHole.PullSpeed == 4f && changed.Radius == 6f, "参数快照必须独立。");
            Require(ParameterisedBehaviour.TryCreateAttack(new OrganInstance(changed), null, out CastEvent request)
                && request.BlackHole == changed.BlackHole && request.Damage == 0f, "零伤害无目标的黑洞请求必须通过管道。");
            Require(AttackResolver.Resolve(request, new[] { new AttackTarget("a", Vector2.zero, true) }, Vector2.zero, out _).Count == 0,
                "黑洞不能被当成爆炸扣血。");
            Set(config, "blackHoleDuration", 0f); Require(!config.TryCreateDefinition(out _, out _), "零持续时间应拒绝。");
            Set(config, "blackHoleDuration", 5f); Set(config, "blackHolePullSpeed", -1f);
            Require(!config.TryCreateDefinition(out _, out _), "负吸引速度应拒绝。");
            Set(config, "blackHolePullSpeed", 0f); Set(config, "blackHoleRadius", float.NaN);
            Require(!config.TryCreateDefinition(out _, out _), "无效半径应拒绝。");
        }
        private static void CheckGeometryAndTime()
        {
            var hole = new BlackHoleBehaviour(Request(duration: 0.5f), Vector2.zero);
            Require(hole.PullPosition(new Vector2(3f, 0f), 10f) == new Vector2(2f, 0f), "边界应被拉动，长帧只使用剩余半秒。");
            Require(hole.PullPosition(new Vector2(3.01f, 0f), 1f).x == 3.01f, "范围外不吸引。");
            Require(hole.PullPosition(new Vector2(0.1f, 0f), 1f) == Vector2.zero, "不能越过中心。");
            Require(hole.PullPosition(Vector2.zero, 1f) == Vector2.zero, "中心点不能除零或抖动。");
            hole.Tick(0.5f); Require(!hole.IsActive && hole.PullPosition(Vector2.one, 1f) == Vector2.one, "到期立即停止。");
            Require(new BlackHoleBehaviour(Request(speed: 0f), Vector2.zero).PullPosition(Vector2.one, 1f) == Vector2.one,
                "零速度不移动。");
            Require(new BlackHoleBehaviour(Request(radius: 0f), Vector2.zero).PullPosition(Vector2.one, 1f) == Vector2.one,
                "零半径不能拉远处敌人。");
        }
        private static void CheckScenePull()
        {
            DemoTarget near = Target("near", 2f), outside = Target("outside", 4f), immune = Target("immune", 1f);
            DemoTarget disabled = Target("disabled", 1f), dead = Target("dead", 1f, 1f);
            disabled.enabled = false;
            dead.TryApplyAttack(new CastEvent(dead.TargetId, 1f, "test", "test"));
            var edit = new SerializedObject(immune); edit.FindProperty("canBeDisplaced").boolValue = false; edit.ApplyModifiedPropertiesWithoutUndo();
            var combat = near.gameObject.AddComponent<DemoCombatTarget>(); Set(combat, "moveSpeed", 0f);
            var demo = Demo(Config()); Hand(demo).Tick(1f);
            var field = demo.GetComponent<DemoBlackHoleField>(); field.Advance(0.5f);
            Require(near.transform.position == new Vector3(1f, 0f, 2f) && near.CurrentHealth == 100f,
                "范围内名单外敌人应被吸引、不扣血且保留 Z。");
            combat.Advance(0.5f); Require(near.transform.position.x == 1f, "主动移动不能把吸附位置重置。");
            Require(outside.transform.position.x == 4f && immune.transform.position.x == 1f
                && disabled.transform.position.x == 1f && dead.transform.position.x == 1f,
                "范围外、免疫、停用和死亡敌人不能被吸引。");
            DemoTarget later = Target("spawned_later", 2f); field.Advance(0.5f);
            Require(later.transform.position.x == 1f, "运行中新生成的敌人也能被吸引。");
            combat.Statuses.TryApply(new CastEvent(near.TargetId, 1f, "stun", "stun", status:
                new StatusEffectData(StatusEffectKind.Stun, 1f, 2f)), 0f);
            near.transform.position = new Vector3(2f, 0f, 2f); field.Advance(0.5f); combat.Advance(0.5f);
            Require(near.transform.position.x == 1f && combat.CurrentSpeed == 0f, "眩晕阻止主动移动，但不免疫外力吸引。");
            field.Advance(10f); Require(field.ActiveCount == 0, "长帧跨越期限必须移除黑洞。");
        }
        private static void CheckRefreshAndOrigins()
        {
            DemoTarget near = Target("near_origin", 7f);
            var demo = Demo(Config()); demo.transform.position = new Vector3(5f, 0f, 0f);
            Hand(demo).Tick(1f); var field = demo.GetComponent<DemoBlackHoleField>();
            demo.transform.position = new Vector3(20f, 0f, 0f); field.Advance(0.5f);
            Require(near.transform.position.x == 6f, "生成后黑洞留在原地，不跟随角色移动。");
            Hand(demo).Tick(1f); Require(field.ActiveCount == 1 && field.LongestRemaining == 5f, "同一实例重放必须替换并重置计时。");
            field.Advance(0.5f); Require(near.transform.position.x == 6f, "替换后旧位置不再吸引。");
            Transform origin = Go("Character").transform; origin.position = new Vector3(5f, 0f, 0f);
            var edit = new SerializedObject(demo); edit.FindProperty("blackHoleOrigin").objectReferenceValue = origin; edit.ApplyModifiedPropertiesWithoutUndo();
            Hand(demo).Tick(1f); field.Advance(0.5f); Require(near.transform.position.x == 5f, "应支持独立角色作为生成位置。");
            field.Spawn(Request(id: "second_instance"), Vector2.zero);
            Require(field.ActiveCount == 2, "不同器官实例可各自保留一个黑洞。");
        }
        private static void CheckChainAndCleanup()
        {
            var config = Config(); var demo = Demo(config); demo.StopDemo();
            var enemy = Target("kill", 1f, 5f);
            var data = new SerializedObject(demo);
            data.FindProperty("slots").GetArrayElementAtIndex(0).objectReferenceValue = Config(HandOrganKind.Fist);
            data.FindProperty("slots").GetArrayElementAtIndex(1).objectReferenceValue = config;
            data.FindProperty("slots").GetArrayElementAtIndex(3).objectReferenceValue = Config(HandOrganKind.MultiTentacle);
            data.FindProperty("targets").arraySize = 1;
            data.FindProperty("targets").GetArrayElementAtIndex(0).objectReferenceValue = enemy;
            data.ApplyModifiedPropertiesWithoutUndo(); Require(demo.TryStartDemo(out _), "组合应能启动。");
            CastEvent fist = default, hole = default;
            demo.GetComponent<OrganPipelineController>().AttackProduced += attack =>
            { if (attack.Shape == AttackShape.BlackHole) hole = attack; else fist = attack; };
            Hand(demo).Tick(1f);
            var field = demo.GetComponent<DemoBlackHoleField>();
            Require(demo.TriggerCount == 1 && field.ActiveCount == 1 && hole.ChainId == fist.ChainId && hole.ChainDepth == 1,
                "击杀追加执行黑洞时必须保留来源链，黑洞自身不能产生击杀。");
            demo.StopDemo(); Require(field.ActiveCount == 0, "停止演示必须清除黑洞。");
            field.Advance(10f); Require(field.ActiveCount == 0, "清理后不能重新出现。");
            Require(demo.TryStartDemo(out _), "重新连接应安全。"); Hand(demo).Tick(2f);
            Require(field.ActiveCount == 1, "主目标死亡后黑洞仍可发动。");
            // 编辑模式不能假定切换 enabled 会触发运行时 OnDisable；此处检查普通清理入口。
            // 真实停用组件的检查放在 HandStartupPlayChecks 的 Play 阶段。
            field.Clear(); Require(field.ActiveCount == 0, "清理场组件应清除黑洞。");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Hand Step4 Checks] " + message); }
    }
}
