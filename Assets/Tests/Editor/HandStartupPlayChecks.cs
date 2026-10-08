using System;
using SpringUp.Organs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    // 仅由独立检查工程的命令行启动。真实进入 Play，覆盖编辑模式检查漏掉的生命周期。
    [InitializeOnLoad]
    public static class HandStartupPlayChecks
    {
        private const string RunningKey = "SpringUp.HandStartup.Running";
        private const string SceneKey = "SpringUp.HandStartup.Scene";
        private static double deadline;
        private static bool entered;
        private static string failure;
        private static int editorSearchErrors;
        private static float statusStartTime, statusHealth;
        private static Vector3 statusStartPosition;
        private static float blackHoleStartTime, blackHoleDuration, blackHoleExpectedX;

        static HandStartupPlayChecks()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.update += CheckProgress;
            Application.logMessageReceived += OnLog;
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("请只在独立检查工程中使用 batchmode 运行，不要在工作场景调用。");
            HandCombinationChecks.Run();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildFixture("DemoFirst", true);
            BuildFixture("ControllerFirst", false);
            BuildExplosionFixture();
            BuildBlackHoleFixture();
            BuildCombinationFixture(false);
            BuildCombinationFixture(true);
            string scenePath = "Assets/HandStartupCheck-" + Guid.NewGuid().ToString("N") + ".unity";
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
            SessionState.SetString(SceneKey, scenePath);
            SessionState.SetBool(RunningKey, true);
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.update += CheckProgress;
            Application.logMessageReceived += OnLog;
            EditorApplication.EnterPlaymode();
        }

        private static void BuildFixture(string name, bool demoFirst)
        {
            var target = new GameObject(name + "Target").AddComponent<DemoTarget>();
            target.InitializeForDemo(name, 1000f);
            target.transform.position = new Vector3(100f, 0f, 0f);
            target.gameObject.AddComponent<DemoCombatTarget>();
            var demo = new GameObject(name).AddComponent<HandOrganDemo>();
            if (demoFirst) UnityEditorInternal.ComponentUtility.MoveComponentUp(demo);
            var settings = new SerializedObject(demo);
            settings.FindProperty("slots").GetArrayElementAtIndex(0).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/HandOrgans/Fist.asset");
            SerializedProperty targets = settings.FindProperty("targets");
            targets.arraySize = 1;
            targets.GetArrayElementAtIndex(0).objectReferenceValue = target;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildExplosionFixture()
        {
            var demo = new GameObject("ExplosionFixture").AddComponent<HandOrganDemo>();
            demo.transform.position = new Vector3(7f, 0f, 0f);
            demo.gameObject.AddComponent<DemoExplosionView>();
            var data = new SerializedObject(demo);
            data.FindProperty("slots").GetArrayElementAtIndex(0).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/HandOrgans/TNT.asset");
            SerializedProperty targets = data.FindProperty("targets"); targets.arraySize = 1;
            for (int i = 0; i < 3; i++)
            {
                var target = new GameObject("ExplosionTarget" + i).AddComponent<DemoTarget>();
                target.transform.position = new Vector3(i == 0 ? 20f : (i == 1 ? 7f : 8f), 0f, 0f);
                target.InitializeForDemo("explosion_" + i, 1000f);
                if (i == 0) targets.GetArrayElementAtIndex(0).objectReferenceValue = target;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildBlackHoleFixture()
        {
            var demo = new GameObject("BlackHoleFixture").AddComponent<HandOrganDemo>();
            demo.transform.position = new Vector3(-20f, 0f, 0f);
            var data = new SerializedObject(demo);
            data.FindProperty("slots").GetArrayElementAtIndex(0).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/HandOrgans/CollapseBody.asset");
            data.ApplyModifiedPropertiesWithoutUndo();
            var target = new GameObject("BlackHoleTarget").AddComponent<DemoTarget>();
            target.InitializeForDemo("black_hole_target", 100f);
            target.transform.position = new Vector3(-18f, 0f, 0f);
            var combat = target.gameObject.AddComponent<DemoCombatTarget>();
            var movement = new SerializedObject(combat); movement.FindProperty("moveSpeed").floatValue = 0f;
            movement.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildCombinationFixture(bool twoTentacles)
        {
            string name = twoTentacles ? "ComboChainFixture" : "ComboFixture";
            float originX = twoTentacles ? -60f : -40f;
            var demo = new GameObject(name).AddComponent<HandOrganDemo>();
            demo.transform.position = new Vector3(originX, 0f, 0f);
            string[] configs = twoTentacles
                ? new[] { "Fist", "RustKnife", "SlimeGland", "TNT", "MultiTentacle", "MultiTentacle" }
                : new[] { "CollapseBody", "SteelPipe", "RustKnife", "SlimeGland", "TNT", "MultiTentacle" };
            var data = new SerializedObject(demo);
            for (int i = 0; i < configs.Length; i++)
                data.FindProperty("slots").GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/HandCombo/" + configs[i] + ".asset");
            var targets = data.FindProperty("targets"); targets.arraySize = 2;
            for (int i = 0; i < 3; i++)
            {
                var target = new GameObject(name + "Target" + i).AddComponent<DemoTarget>();
                target.InitializeForDemo(name + "_" + i, i == 0 ? (twoTentacles ? 5f : 10f) : 80f);
                target.transform.position = new Vector3(originX + (i == 0 ? 1.5f : i == 2 ? 4.5f : twoTentacles ? 1f : 2.5f), 0f, 0f);
                var combat = target.gameObject.AddComponent<DemoCombatTarget>();
                var movement = new SerializedObject(combat); movement.FindProperty("moveSpeed").floatValue = 0f;
                movement.ApplyModifiedPropertiesWithoutUndo();
                if (i < 2) targets.GetArrayElementAtIndex(i).objectReferenceValue = target;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void OnLog(string message, string trace, LogType type)
        {
            // 最小检查工程会遇到 Unity 6000.5 搜索索引自身的启动异常。
            // 单独计数并保留原日志；它不属于器官运行代码，不能代替玩法检查的结果。
            if (type == LogType.Exception && trace.Contains("UnityEditor.Search.SearchDatabase")
                && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")
                && !trace.Contains("SpringUp."))
            {
                editorSearchErrors++;
                return;
            }
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert
                || (type == LogType.Warning && message.StartsWith("[手部测试]"))) failure = message;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) entered = true;
        }

        private static void CheckProgress()
        {
            if (failure != null) { Finish(false, failure); return; }
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "进入 Play 或攻击检查超时。"); return; }
            if (!entered || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < 2f) return;
            try
            {
                foreach (string name in new[] { "DemoFirst", "ControllerFirst" })
                {
                    HandOrganDemo demo = GameObject.Find(name).GetComponent<HandOrganDemo>();
                    OrganPipelineController controller = demo.GetComponent<OrganPipelineController>();
                    DemoTarget target = GameObject.Find(name + "Target").GetComponent<DemoTarget>();
                    Require(controller.TargetSelector != null, "自动启动没有连接目标：" + name);
                    Require(target.CurrentHealth < 1000f && target.CurrentHealth > 0f, "真实 Update 应自动造成伤害：" + name);
                    demo.enabled = false;
                    float health = target.CurrentHealth;
                    controller.GetBody(BodyPart.Hand).Tick(1f);
                    Require(target.CurrentHealth == health && controller.TargetSelector == null, "停用后应解绑。");
                    demo.enabled = true;
                }
                var explosionDemo = GameObject.Find("ExplosionFixture").GetComponent<HandOrganDemo>();
                float explosionDamage = explosionDemo.GetComponent<OrganPipelineController>()
                    .GetBody(BodyPart.Hand).GetSlot(0).Definition.Damage;
                Require(explosionDemo.ExplosionCount > 0 && explosionDemo.LastExplosionHitCount == 2,
                    "真实 Play 应自动产生范围爆炸。");
                for (int i = 0; i < 3; i++)
                    Require(GameObject.Find("ExplosionTarget" + i).GetComponent<DemoTarget>().CurrentHealth
                        == (i == 0 ? 1000f : 1000f - explosionDemo.ExplosionCount * explosionDamage),
                        "真实 Update 应以演示物体为中心：列表中的远处敌人不受伤，角色附近的名单外敌人各受一次伤害。");
                explosionDemo.enabled = false;
                var holeDemo = GameObject.Find("BlackHoleFixture").GetComponent<HandOrganDemo>();
                var holeField = holeDemo.GetComponent<DemoBlackHoleField>();
                Require(holeField != null && holeField.SpawnCount > 0, "真实 Update 应能生成黑洞。");
                holeField.enabled = false;
                Require(holeField.ActiveCount == 0, "真实 Play 停用场组件必须清除已有黑洞。");
                holeField.enabled = true;
                var holeController = holeDemo.GetComponent<OrganPipelineController>();
                holeController.enabled = false; // 只停止生成，让已生成黑洞自然到期。
                holeField.Clear();
                GameObject.Find("BlackHoleTarget").transform.position = new Vector3(-18f, 0f, 0f);
                holeController.GetBody(BodyPart.Hand).Tick(1f);
                var definition = holeController.GetBody(BodyPart.Hand).GetSlot(0).Definition;
                blackHoleStartTime = Time.time;
                blackHoleDuration = definition.BlackHole.Duration;
                blackHoleExpectedX = definition.Radius >= 2f
                    ? -18f - Mathf.Min(2f, definition.BlackHole.PullSpeed * blackHoleDuration) : -18f;
                // 下一帧检查重新启用；不能假装手动 TryStartDemo 就等于生命周期成功。
                EditorApplication.update -= CheckProgress;
                EditorApplication.update += CheckReenabled;
                deadline = EditorApplication.timeSinceStartup + Math.Max(10, blackHoleDuration + 5);
            }
            catch (Exception ex) { Finish(false, ex.Message); }
        }

        private static void CheckReenabled()
        {
            if (failure != null) { Finish(false, failure); return; }
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "重新启用后没有恢复连接。"); return; }
            foreach (string name in new[] { "DemoFirst", "ControllerFirst" })
                if (GameObject.Find(name).GetComponent<OrganPipelineController>().TargetSelector == null) return;
            try
            {
                foreach (string name in new[] { "DemoFirst", "ControllerFirst" })
                {
                    var controller = GameObject.Find(name).GetComponent<OrganPipelineController>();
                    var demo = controller.GetComponent<HandOrganDemo>();
                    var target = GameObject.Find(name + "Target").GetComponent<DemoTarget>();
                    float before = target.CurrentHealth;
                    float damage = controller.GetBody(BodyPart.Hand).GetSlot(0).Definition.Damage;
                    controller.GetBody(BodyPart.Hand).Tick(1f);
                    Require(target.CurrentHealth == before - damage, "重新启用后每拍只能扣一次血。");
                    demo.enabled = false;
                    controller.enabled = false;
                    Require(!demo.TryStartDemo(out _), "真正被禁用的控制器仍应拒绝启动。");
                }
                var statusTarget = GameObject.Find("DemoFirstTarget").GetComponent<DemoCombatTarget>();
                string targetId = statusTarget.GetComponent<DemoTarget>().TargetId;
                statusTarget.TryApplyAttack(new CastEvent(targetId, 1f, "stun", "stun", status:
                    new StatusEffectData(StatusEffectKind.Stun, 1f, 0.2f)));
                for (int i = 0; i < 3; i++)
                    statusTarget.TryApplyAttack(new CastEvent(targetId, 1f, "slow", "slow", status:
                        new StatusEffectData(StatusEffectKind.Slow, 1f, 0.4f, slowPerStack: 0.15f, maxStacks: 3)));
                statusTarget.TryApplyAttack(new CastEvent(targetId, 1f, "bleed", "bleed", status:
                    new StatusEffectData(StatusEffectKind.Bleed, 1f, 0.4f, 2f, 0.1f)));
                Require(statusTarget.CurrentSpeed == 0f && statusTarget.Statuses.SlowStacks == 3,
                    "真实 Play 命中后眩晕和三层减速应立即生效。");
                statusHealth = statusTarget.GetComponent<DemoTarget>().CurrentHealth;
                statusStartTime = Time.time;
                statusStartPosition = statusTarget.transform.position;
                EditorApplication.update -= CheckReenabled;
                EditorApplication.update += CheckStatusesInPlay;
                deadline = EditorApplication.timeSinceStartup + Math.Max(10, blackHoleDuration + 5);
            }
            catch (Exception ex) { Finish(false, ex.Message); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckStatusesInPlay()
        {
            if (failure != null) { Finish(false, failure); return; }
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "状态 Play 检查超时。"); return; }
            // 三层减速每 0.4 秒掉一层，需经过 1.2 秒才全部消失。
            if (Time.time - statusStartTime < 1.4f || Time.time - blackHoleStartTime < blackHoleDuration + 0.1f
                || Time.timeSinceLevelLoad < 7.1f) return;
            try
            {
                var combat = GameObject.Find("DemoFirstTarget").GetComponent<DemoCombatTarget>();
                Require(combat.Statuses.StunRemaining == 0f && combat.Statuses.SlowStacks == 0
                    && combat.Statuses.BleedRemaining == 0f, "三种状态必须随真实 Update 到期。");
                Require(combat.GetComponent<DemoTarget>().CurrentHealth == statusHealth - 8f,
                    "0.4 秒流血应通过真实 Update 自动扣四次血。");
                Require(combat.CurrentSpeed > 0f && combat.transform.position != statusStartPosition,
                    "状态到期后应恢复实际移动。");
                var holeTarget = GameObject.Find("BlackHoleTarget").GetComponent<DemoTarget>();
                Require(Mathf.Abs(holeTarget.transform.position.x - blackHoleExpectedX) < 0.01f
                    && holeTarget.CurrentHealth == 100f, "真实 LateUpdate 应持续吸引敌人、不扣血且不能被主动移动重置。");
                Require(GameObject.Find("BlackHoleFixture").GetComponent<DemoBlackHoleField>().ActiveCount == 0,
                    "黑洞应在真实 Play 中按配置到期消失。");
                var combo = GameObject.Find("ComboFixture").GetComponent<HandOrganDemo>();
                Require(combo.TriggerCount == 1 && combo.ExplosionCount == 2
                    && !GameObject.Find("ComboFixtureTarget0").GetComponent<DemoTarget>().IsAlive
                    && GameObject.Find("ComboFixtureTarget1").GetComponent<DemoTarget>().CurrentHealth < 80f
                    && GameObject.Find("ComboFixtureTarget1").transform.position.x < -38f
                    && GameObject.Find("ComboFixtureTarget2").GetComponent<DemoTarget>().CurrentHealth == 80f,
                    "真实 Play 六槽组合应聚怪、击杀、追加一次，范围外敌人不受伤。");
                var chainCombo = GameObject.Find("ComboChainFixture").GetComponent<HandOrganDemo>();
                Require(chainCombo.TriggerCount == 2 && chainCombo.ExplosionCount >= 3
                    && GameObject.Find("ComboChainFixtureTarget1").GetComponent<DemoTarget>().IsAlive
                    && GameObject.Find("ComboChainFixtureTarget2").GetComponent<DemoTarget>().CurrentHealth == 80f,
                    "真实 Play 两个触手应分别追加一次，之后正常节拍继续。");
                Finish(true, "startup, re-enable, status expiry, explosion, black hole pull/expiry and both six-slot combinations passed.");
            }
            catch (Exception ex) { Finish(false, ex.Message); }
        }

        private static void Finish(bool passed, string message)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= CheckProgress;
            EditorApplication.update -= CheckReenabled;
            EditorApplication.update -= CheckStatusesInPlay;
            EditorApplication.playModeStateChanged -= OnPlayState;
            Application.logMessageReceived -= OnLog;
            string scenePath = SessionState.GetString(SceneKey, "");
            if (scenePath.StartsWith("Assets/HandStartupCheck-")) AssetDatabase.DeleteAsset(scenePath);
            Debug.Log("[Hand Startup Play Checks] " + (passed ? "PASS: " : "FAIL: ") + message);
            if (editorSearchErrors > 0)
                Debug.LogWarning("[Hand Startup Play Checks] Editor environment: " + editorSearchErrors
                    + " Unity Search indexing exception(s); see original stack trace. Gameplay checks are reported separately.");
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
