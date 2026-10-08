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
    public static class HandExplosionChecks
    {
        private static readonly List<Object> objects = new List<Object>();
        private static Scene testScene;
        [MenuItem("Tools/SpringUp/Run Hand Step 3 Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Hand Step3 Checks] 请先停止 Play。");
                return;
            }
            var errors = new List<string>();
            Application.LogCallback record = (message, trace, type) =>
            {
                if (type == LogType.Assert || type == LogType.Error || type == LogType.Exception) errors.Add(message);
            };
            Application.logMessageReceived += record;
            try
            {
                HandStatusChecks.Run();
                RunIsolated(CheckConfig);
                RunIsolated(CheckGeometry);
                RunIsolated(CheckActualDamage);
                RunIsolated(CheckSnapshot);
                RunIsolated(CheckKillChain);
                RunIsolated(CheckEmptyAndCleanup);
                RunIsolated(CheckUnlistedTargets);
                RunIsolated(CheckCharacterCenter);
            }
            finally
            {
                try
                {
                    foreach (Object item in objects)
                        if (item is GameObject go) go.GetComponent<HandOrganDemo>()?.StopDemo();
                    for (int i = objects.Count - 1; i >= 0; i--)
                        if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                }
                finally { objects.Clear(); Application.logMessageReceived -= record; }
            }
            Require(errors.Count == 0, "检查或清理出现错误：" + string.Join(" | ", errors));
            Debug.Log("[Hand Step3 Checks] PASS: all 8 explosion groups and previous 29 groups passed, including cleanup.");
        }

        private static void RunIsolated(Action check)
        {
            // 扫描场景的检查必须使用隔离场景，不能伤到用户工作场景中的敌人。
            testScene = EditorSceneManager.NewPreviewScene();
            try { check(); }
            finally
            {
                foreach (Object item in objects)
                    if (item is GameObject go) go.GetComponent<HandOrganDemo>()?.StopDemo();
                for (int i = objects.Count - 1; i >= 0; i--)
                    if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                objects.Clear();
                EditorSceneManager.ClosePreviewScene(testScene);
            }
        }

        private static GameObject Go(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(go);
            SceneManager.MoveGameObjectToScene(go, testScene);
            return go;
        }
        private static OrganConfig Config(bool trigger = false, float damage = 10f, float radius = 2f)
        {
            var config = ScriptableObject.CreateInstance<OrganConfig>();
            objects.Add(config);
            var data = new SerializedObject(config);
            data.FindProperty("kind").intValue = (int)(trigger ? HandOrganKind.MultiTentacle : HandOrganKind.Tnt);
            data.FindProperty("damage").floatValue = damage;
            data.FindProperty("explosionRadius").floatValue = radius;
            data.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }
        private static DemoTarget Target(string id, float x, float health = 100f)
        {
            var target = Go(id).AddComponent<DemoTarget>();
            target.InitializeForDemo(id, health);
            target.transform.position = new Vector3(x, 0f, 0f);
            return target;
        }
        private static HandOrganDemo Demo(OrganConfig config, bool trigger, params DemoTarget[] targets)
        {
            var demo = Go("TNT check").AddComponent<HandOrganDemo>();
            var data = new SerializedObject(demo);
            data.FindProperty("slots").GetArrayElementAtIndex(0).objectReferenceValue = config;
            if (trigger) data.FindProperty("slots").GetArrayElementAtIndex(3).objectReferenceValue = Config(true);
            SerializedProperty array = data.FindProperty("targets"); array.arraySize = targets.Length;
            for (int i = 0; i < targets.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            Require(demo.TryStartDemo(out string error), error ?? "TNT 应可启动。");
            return demo;
        }
        private static BodyRuntime Hand(HandOrganDemo demo) => demo.GetComponent<OrganPipelineController>().GetBody(BodyPart.Hand);
        private static CastEvent Explosion(float radius = 2f, string targetId = "a")
            => new CastEvent(targetId, 10f, "tnt", "tnt_instance", shape: AttackShape.Explosion, radius: radius);

        private static void CheckConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/HandOrgans/TNT.asset");
            Require(asset != null && asset.TryCreateDefinition(out _, out _), "TNT 资源应可加载。");
            OrganConfig config = Config();
            Require(config.TryCreateDefinition(out OrganDefinition original, out _), "TNT 配置应可读取。");
            var edit = new SerializedObject(config);
            edit.FindProperty("explosionRadius").floatValue = 4f;
            edit.ApplyModifiedPropertiesWithoutUndo();
            config.TryCreateDefinition(out OrganDefinition changed, out _);
            Require(original.Radius == 2f && changed.Radius == 4f, "范围应读取快照，不能改动上次的攻击配置。");
            ParameterisedBehaviour.TryCreateAttack(new OrganInstance(changed), "a", out CastEvent attack);
            Require(attack.Shape == AttackShape.Explosion && attack.Radius == 4f && attack.Damage == 10f,
                "爆炸形状、伤害和半径必须从配置传到攻击。");
            edit.Update(); edit.FindProperty("explosionRadius").floatValue = -1f; edit.ApplyModifiedPropertiesWithoutUndo();
            Require(!config.TryCreateDefinition(out _, out _), "负半径应拒绝。");
            Throws(() => Explosion(float.NaN));
            Throws(() => Explosion(float.PositiveInfinity));
        }

        private static void CheckGeometry()
        {
            var targets = new[] {
                new AttackTarget("a", Vector2.zero, true),
                new AttackTarget("edge", new Vector2(2f,0f), true),
                new AttackTarget("edge", new Vector2(2f,0f), true),
                new AttackTarget("outside", new Vector2(2.01f,0f), true),
                new AttackTarget("diagonal", new Vector2(1.5f,1.5f), true),
                new AttackTarget("dead", Vector2.zero, false),
                new AttackTarget("bad", new Vector2(float.NaN,0f), true) };
            CastEvent request = Explosion();
            List<CastEvent> hits = AttackResolver.Resolve(request, targets, Vector2.zero, out Vector2 center);
            Require(center == Vector2.zero && hits.Count == 2 && hits[1].TargetId == "edge",
                "圆形范围应包含边界，排除范围外、死亡和无效目标，并按编号去重。");
            foreach (CastEvent hit in hits)
                Require(hit.Shape == AttackShape.SingleTarget && hit.Context == request.Context
                    && hit.OriginInstanceId == request.OriginInstanceId, "展开后的命中保持来源和链，不能再次变成爆炸。");
            Require(AttackResolver.Resolve(Explosion(0f), targets, Vector2.zero, out _).Count == 1, "零半径只命中中心点上的存活目标。");
            var single = new CastEvent("a", 5f, "fist", "fist");
            Require(AttackResolver.Resolve(single, targets, Vector2.zero, out _).Count == 1, "普通攻击仍只命中主目标。");
            Require(AttackResolver.Resolve(Explosion(targetId: "dead"), targets, Vector2.zero, out _).Count == 2,
                "敌人死亡不能影响以角色为中心的爆炸。");
        }

        private static void CheckActualDamage()
        {
            DemoTarget a = Target("a", 0f), b = Target("b", 1f), c = Target("c", 3.5f);
            var demo = Demo(Config(), false, a, b, c);
            int hitA = 0, hitB = 0; a.Damaged += (_, __, ___) => hitA++; b.Damaged += (_, __, ___) => hitB++;
            Hand(demo).Tick(1f);
            Require(a.CurrentHealth == 90f && b.CurrentHealth == 90f && c.CurrentHealth == 100f,
                "范围内应各扣 10，范围外不受伤。");
            Require(hitA == 1 && hitB == 1 && demo.ExplosionCount == 1 && demo.LastExplosionHitCount == 2,
                "主目标不能同时受到直接伤害和爆炸伤害。");
            Hand(demo).Tick(1f);
            Require(a.CurrentHealth == 80f && b.CurrentHealth == 80f && demo.ExplosionCount == 2,
                "下一次正常攻击仍可再次爆炸。");
        }

        private static void CheckSnapshot()
        {
            DemoTarget a = Target("a", 0f), b = Target("b", 1f);
            var demo = Demo(Config(), false, a, b);
            a.Damaged += (_, __, ___) => b.transform.position = new Vector3(100f, 0f, 0f);
            Hand(demo).Tick(1f);
            Require(b.CurrentHealth == 90f, "本次命中名单应在任何受伤回调前固定。");
            Hand(demo).Tick(1f);
            Require(b.CurrentHealth == 90f, "下一次爆炸重新读取位置，不应继续命中移出范围的敌人。");
        }

        private static void CheckKillChain()
        {
            DemoTarget a = Target("a", 0f, 5f), b = Target("b", 1f, 5f), c = Target("c", 1.5f, 20f);
            var demo = Demo(Config(), true, a, b, c);
            var hitsC = new List<CastEvent>(); c.Damaged += (_, attack, __) => hitsC.Add(attack);
            demo.GetComponent<OrganPipelineController>().TriggerActivated += (_, __, ___, ____) =>
                Require(a.CurrentHealth == 0f && b.CurrentHealth == 0f && c.CurrentHealth == 10f,
                    "第一次爆炸全部扣完血后才能发动击杀触发。");
            Hand(demo).Tick(1f);
            Require(demo.TriggerCount == 1 && demo.ExplosionCount == 2 && c.CurrentHealth == 0f,
                "一次爆炸击杀多人只能让同一触手发动一次；追加爆炸可以继续击杀。");
            Require(hitsC.Count == 2 && hitsC[0].ChainId == hitsC[1].ChainId
                && hitsC[0].ChainDepth == 0 && hitsC[1].ChainDepth == 1, "追加爆炸必须继承触发链。");
            Hand(demo).Tick(2f);
            Require(demo.ExplosionCount == 3 && demo.LastExplosionHitCount == 0,
                "全灭后仍能在角色处爆炸，但不应再命中死亡敌人。");
        }

        private static void CheckEmptyAndCleanup()
        {
            Require(AttackResolver.Resolve(default, new AttackTarget[0], Vector2.zero, out _).Count == 0, "空请求不产生命中。");
            DemoTarget a = Target("a", 0f), b = Target("b", 1f);
            b.enabled = false;
            var demo = Demo(Config(), false, a, b);
            Hand(demo).Tick(1f);
            Require(b.CurrentHealth == 100f, "停用目标不能受爆炸伤害。");
            demo.StopDemo(); Hand(demo).Tick(10f);
            Require(a.CurrentHealth == 90f && demo.ExplosionCount == 1, "停用演示后不能继续爆炸。");
            var zero = Demo(Config(damage: 0f), false, a);
            Hand(zero).Tick(1f);
            Require(zero.ExplosionCount == 0, "零伤害配置不应产生攻击。");
        }

        private static void CheckUnlistedTargets()
        {
            DemoTarget aim = Target("aim", 0f);
            DemoTarget splash = Target("unlisted", 1f, 5f);
            DemoTarget outside = Target("outside", 5f);
            DemoTarget disabled = Target("disabled", 1f); disabled.enabled = false;
            var demo = Demo(Config(), true, aim); // 只登记主目标，其他敌人都不在 Targets。
            Hand(demo).Tick(1f);
            Require(splash.CurrentHealth == 0f && aim.CurrentHealth == 80f && demo.TriggerCount == 1,
                "名单外敌人应受爆炸伤害，其死亡也应触发一次追加爆炸。");
            Require(outside.CurrentHealth == 100f && disabled.CurrentHealth == 100f,
                "场景扫描仍应排除范围外或被停用的敌人。");
            DemoTarget spawned = Target("spawned_later", 1f);
            Hand(demo).Tick(2f); // 下一拍经过触手，再下一拍执行 TNT。
            Require(spawned.CurrentHealth == 90f, "运行后新加入场景的敌人也应受伤。");
            demo.StopDemo();
            Require(demo.TryStartDemo(out _), "重新连接应安全。");
            Hand(demo).Tick(2f);
            Require(spawned.CurrentHealth == 80f, "重新启动不能产生重复伤害。");
            demo.StopDemo();
            // 单目标执行器仍不能伤到旁边未选中的敌人。
            OrganConfig single = Config();
            var data = new SerializedObject(single); data.FindProperty("kind").intValue = (int)HandOrganKind.Fist;
            data.ApplyModifiedPropertiesWithoutUndo();
            var fist = Demo(single, false, aim);
            Hand(fist).Tick(1f);
            Require(spawned.CurrentHealth == 80f, "拳头仍是单目标攻击。");
        }

        private static void CheckCharacterCenter()
        {
            DemoTarget listed = Target("far_first", 20f);
            DemoTarget near = Target("near_character", 7f);
            DemoTarget edge = Target("edge_character", 9f);
            var demo = Demo(Config(), false, listed, near);
            demo.transform.position = new Vector3(7f, 0f, 0f);
            Hand(demo).Tick(1f);
            Require(listed.CurrentHealth == 100f && near.CurrentHealth == 90f && edge.CurrentHealth == 90f,
                "中心必须是演示物体位置，不能是列表首位敌人；角色半径边界也应命中。");
            // 改列表顺序也不能改变中心。
            demo.StopDemo();
            var data = new SerializedObject(demo);
            data.FindProperty("targets").GetArrayElementAtIndex(0).objectReferenceValue = near;
            data.FindProperty("targets").GetArrayElementAtIndex(1).objectReferenceValue = listed;
            data.ApplyModifiedPropertiesWithoutUndo();
            Require(demo.TryStartDemo(out _), "调整顺序后应能启动。");
            Hand(demo).Tick(1f);
            Require(listed.CurrentHealth == 100f && near.CurrentHealth == 80f && edge.CurrentHealth == 80f,
                "Targets 顺序不能改变爆炸中心。");
            // 单独的角色 Transform 覆盖演示物体位置，移动后下一次爆炸读取新位置。
            Transform character = Go("Character").transform;
            character.position = new Vector3(20f, 0f, 0f);
            data.Update(); data.FindProperty("explosionOrigin").objectReferenceValue = character;
            data.ApplyModifiedPropertiesWithoutUndo();
            Hand(demo).Tick(1f);
            Require(listed.CurrentHealth == 90f && near.CurrentHealth == 80f,
                "指定角色后应使用角色位置。");
            character.position = new Vector3(7f, 0f, 0f);
            Hand(demo).Tick(1f);
            Require(listed.CurrentHealth == 90f && near.CurrentHealth == 70f && edge.CurrentHealth == 70f,
                "角色移动后爆炸中心应跟随移动。");
            demo.StopDemo();
            var empty = Demo(Config(), false); // TNT 不需要任何主目标。
            empty.transform.position = new Vector3(7f, 0f, 0f);
            Hand(empty).Tick(1f);
            Require(near.CurrentHealth == 60f && edge.CurrentHealth == 60f,
                "空 Targets 也必须能在自身位置爆炸。");
            empty.transform.position = new Vector3(50f, 0f, 0f);
            Hand(empty).Tick(1f);
            Require(empty.ExplosionCount == 2 && empty.LastExplosionHitCount == 0,
                "范围内无敌人时仍执行爆炸，但不扣任何敌人的血。");
        }

        private static void Throws(Action action)
        {
            try { action(); } catch (ArgumentOutOfRangeException) { return; }
            throw new InvalidOperationException("非法半径应拒绝。");
        }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("[Hand Step3 Checks] " + message);
        }
    }
}
