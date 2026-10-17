using System;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Laboratory;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using OrganInstance = SpringUp.Organs.OrganInstance;
using OrganDefinition = SpringUp.Organs.OrganDefinition;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class AttackTargetingChecks
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Attack Targeting Checks] " + message);
        }

        private const string BatchKey = "SpringUp.Targeting.Batch";
        static AttackTargetingChecks()
        {
            if (SessionState.GetBool(BatchKey, false))
                EditorApplication.playModeStateChanged += OnBatchPlayState;
        }

        // 在独立副本运行：编辑模式回归后，真实进入 Play 验证 C 的生命周期。
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅用于独立 batchmode 检查工程。");
            Run();
            OrganConsolidationChecks.Run();
            CheckLaboratory();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/TargetingCheck.unity");
            SessionState.SetBool(BatchKey, true);
            EditorApplication.playModeStateChanged += OnBatchPlayState;
            EditorApplication.EnterPlaymode();
        }

        private static void OnBatchPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            SessionState.SetBool(BatchKey, false);
            EditorApplication.playModeStateChanged -= OnBatchPlayState;
            try
            {
                CheckBattle(SceneManager.GetActiveScene());
                CheckDelayedPosition(SceneManager.GetActiveScene());
                BattleCombatChecks.RunPlay();
                Debug.Log("[Attack Targeting Checks] PLAY PASS: real enemy activation, pooling, scene isolation and binding lifecycle.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        [MenuItem("Tools/SpringUp/Run Attack Targeting Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先停止 Play。");
            CheckGeometry();
            CheckEffectCenters();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                CheckPipeline(scene);
                CheckDelayedPosition(scene);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Debug.Log("[Attack Targeting Checks] EDIT PASS: geometry, all shapes, revalidation and chain.");
        }

        private static void CheckGeometry()
        {
            var targets = new[] { new AttackTarget("outside", new Vector2(3.01f, 0), true),
                new AttackTarget("dead", Vector2.zero, false),
                new AttackTarget("edge", new Vector2(3, 0), true),
                new AttackTarget("nearest", Vector2.one, true),
                new AttackTarget("tie", Vector2.one, true),
                new AttackTarget("invalid", new Vector2(float.NaN, 0), true) };
            Require(AttackTargeting.SelectNearest(targets, Vector2.zero, 3) == "nearest", "选择最近目标且同距稳定。");
            Require(AttackTargeting.IsInRange(targets[2], Vector2.zero, 3), "包含距离边界。");
            Require(!AttackTargeting.IsInRange(targets[0], Vector2.zero, 3), "拒绝边界外。");
            Require(AttackTargeting.SelectNearest(targets, Vector2.zero, 0) == null, "无合格目标返回空。");
            Require(AttackTargeting.SelectNearest(targets, Vector2.zero, float.NaN) == null, "非法范围不能攻击。");
            Require(AttackTargeting.SelectNearest(targets, new Vector2(float.PositiveInfinity, 0), 3) == null, "非法原点不能攻击。");
        }

        private static void CheckEffectCenters()
        {
            var attack = new CastEvent("target", 10, "tnt", "tnt", shape: AttackShape.Explosion,
                radius: 1, effectCenter: new Vector2(2, 0));
            var targets = new[] { new AttackTarget("target", new Vector2(2, 0), true),
                new AttackTarget("splash", new Vector2(2.5f, 0), true),
                new AttackTarget("player_side", new Vector2(-.5f, 0), true),
                new AttackTarget("edge", new Vector2(3, 0), true) };
            var hits = AttackResolver.Resolve(attack, targets, out var center);
            Require(center == new Vector2(2, 0) && hits.Count == 3 && hits.All(x => x.TargetId != "player_side"),
                "爆炸以目标为中心，包含边界且排除玩家身边目标。");
            targets[0] = new AttackTarget("new_generation", new Vector2(100, 0), true);
            hits = AttackResolver.Resolve(attack, targets, out center);
            Require(center == new Vector2(2, 0) && hits.Count == 2, "目标移动或换代不改变快照。");
            var missing = new CastEvent("target", 10, "tnt", "tnt", shape: AttackShape.Explosion, radius: 10);
            Require(AttackResolver.Resolve(missing, targets, out _).Count == 0, "缺位置不能回退玩家或原点。");
            var atOrigin = new CastEvent("origin", 10, "tnt", "tnt", shape: AttackShape.Explosion,
                radius: 0, effectCenter: Vector2.zero);
            Require(AttackResolver.Resolve(atOrigin, new[] { new AttackTarget("origin", Vector2.zero, true) }, out _).Count == 1,
                "世界原点是合法中心。");
        }

        private static void CheckLaboratory()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/LaboratoryDemo.unity", OpenSceneMode.Additive);
            try
            {
                var presenter = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LaboratoryPresenter>()).Single();
                var demo = presenter.GetComponentInChildren<LaboratoryCombatDemo>();
                presenter.InitializeView(); demo.Initialize();
                SpringUp.LaboratoryEditor.TemporaryEquipmentChecks.Run();
                SpringUp.LaboratoryEditor.LaboratorySettlementChecks.Run(presenter, demo);
                var equipment = (TemporaryEquipment)presenter.source.Equipment;
                foreach (var id in new[] { "fist", "tnt", "collapse_body" })
                {
                    for (int i = 0; i < 6; i++) equipment.TryUnequip(LaboratoryPart.Hand, i, out _);
                    var item = equipment.Inventory.First(x => x.DefinitionId == id);
                    Require(equipment.TryEquip(item.InstanceId, LaboratoryPart.Hand, 0, out _), "预览安装 " + id);
                    demo.attackRange = 0.5f;
                    int emitted = 0;
                    demo.Pipeline.AttackProduced += _ => emitted++;
                    demo.Pipeline.GetBody(BodyPart.Hand).Tick(1);
                    Require(emitted == 0, "预览范围外不发动 " + id);
                    demo.attackRange = 3;
                    demo.Pipeline.GetBody(BodyPart.Hand).Tick(1);
                    Require(emitted == 1, "预览进入范围发动一次 " + id);
                }
                Debug.Log("[Attack Targeting Checks] LAB PASS: existing settlement checks and three-shape range gating.");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static GameObject Go(Scene scene, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static void CheckPipeline(Scene scene)
        {
            var pipeline = Go(scene, "pipeline checks").AddComponent<OrganPipelineController>();
            var hand = pipeline.GetBody(BodyPart.Hand);
            string targetId = null;
            bool valid = true;
            int count = 0;
            CastEvent last = default;
            pipeline.TargetSelector = () => targetId;
            pipeline.TargetValidator = _ => valid;
            pipeline.TargetSnapshotProvider = id => valid ? new AttackTarget(id, Vector2.one, true) : (AttackTarget?)null;
            pipeline.AttackProduced += attack => { count++; last = attack; };
            foreach (var shape in new[] { AttackShape.SingleTarget, AttackShape.Explosion, AttackShape.BlackHole })
            {
                var config = ScriptableObject.CreateInstance<DirectActuatorConfig>();
                try
                {
                    var edit = new SerializedObject(config);
                    edit.FindProperty("shape").intValue = (int)shape;
                    edit.ApplyModifiedPropertiesWithoutUndo();
                    Require(config.TryCreateDefinition(out var definition, out var error), error);
                    var organ = new OrganInstance(definition);
                    hand.SetSlot(0, organ);
                    int before = count;
                    targetId = null; valid = true; hand.Tick(1);
                    Require(count == before, shape + " 无目标不能发动。");
                    Require(!ParameterisedBehaviour.TryCreateAttack(organ, null, out _), shape + " 工厂也拒绝无目标。");
                    targetId = "selected"; valid = false; hand.Tick(1);
                    Require(count == before, shape + " 出手前目标失效必须跳过。");
                    valid = true; hand.Tick(1);
                    Require(count == before + 1 && last.TargetId == targetId && last.Shape == shape,
                        shape + " 有效目标时只发动一次并携带身份。");
                }
                finally { Object.DestroyImmediate(config); }
            }
            // 正常节拍无目标时继续走槽，但不积攒攻击；击杀追加的范围效果保留因果身份。
            hand.SetSlot(1, new OrganInstance(new OrganDefinition("tentacle", "触手", BodyPart.Hand,
                OrganType.Trigger, triggerOn: TriggerOn.Kill, triggerTarget: TriggerTargetKind.SelfSlots)));
            targetId = null;
            int beforeChain = count;
            var cause = new CastEvent("killed_enemy", 5, "fist", "fist_instance");
            pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, cause, Vector2.zero));
            Require(count == beforeChain + 1 && last.TargetId == "killed_enemy" && last.ChainId == cause.ChainId,
                "已经成立的击杀链可沿用死亡目标身份发动范围效果。");
            pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, cause, Vector2.zero));
            Require(count == beforeChain + 1, "同链触发器不能重复发动。");
        }

        private static EnemyRuntime Enemy(Scene scene, string name, float x)
        {
            var go = Go(scene, name);
            go.SetActive(false);
            go.transform.position = new Vector3(x, 0, 0);
            go.AddComponent<EnemyData>();
            var runtime = go.AddComponent<EnemyRuntime>();
            go.SetActive(true);
            return runtime;
        }

        private static void CheckDelayedPosition(Scene scene)
        {
            var pipeline = Go(scene, "delayed position").AddComponent<OrganPipelineController>();
            var hand = pipeline.GetBody(BodyPart.Hand);
            hand.SetSlot(0, new OrganInstance(new OrganDefinition("tnt", "TNT", BodyPart.Hand,
                OrganType.Actuator, 10, shape: AttackShape.Explosion, radius: 1)));
            hand.SetSlot(1, new OrganInstance(new OrganDefinition("tentacle", "触手", BodyPart.Hand,
                OrganType.Trigger, triggerOn: TriggerOn.Kill)));
            var target = Go(scene, "bleeding target").AddComponent<DemoTarget>();
            target.InitializeForDemo("bleeding", 2);
            var statuses = new StatusEffectRuntime();
            var cause = new CastEvent(target.TargetId, 1, "knife", "knife_instance", status:
                new StatusEffectData(StatusEffectKind.Bleed, 1, 3, tickDamage: 1), effectCenter: Vector2.one);
            target.TryApplyAttack(cause);
            statuses.TryApply(cause, 0);
            target.transform.position = new Vector3(8, 4, 0);
            CastEvent produced = default;
            pipeline.AttackProduced += hit => produced = hit;
            target.Killed += (dead, hit) => pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, hit, dead.DeathPosition));
            // 模拟受伤观察者移动对象；死亡快照必须先于外部回调保存。
            target.Damaged += (dead, hit, amount) => dead.transform.position = Vector3.zero;
            statuses.Tick(1, hit => target.TryApplyAttack(hit), () => target.IsAlive);
            Require(produced.EffectCenter == new Vector2(8, 4) && produced.ChainId == cause.ChainId,
                "流血致死使用实际死亡位置，并保留原链。");
        }

        private static void CheckBattle(Scene scene)
        {
            var origin = Go(scene, "origin").transform;
            var targeting = origin.gameObject.AddComponent<BattleOrganTargeting>();
            targeting.AttackOrigin = origin;
            targeting.AttackRange = 3;
            var far = Enemy(scene, "same species far", 3.01f);
            var edge = Enemy(scene, "same species edge", 3);
            var near = Enemy(scene, "same species near", 1);
            Require(near.Id == edge.Id && near.TargetId != edge.TargetId, "同类敌人拥有不同出生身份。");
            Require(targeting.SelectTarget() == near.TargetId, "C 活列表按最近距离选敌。");
            string old = near.TargetId;
            near.transform.position = new Vector3(4, 0, 0);
            Require(!targeting.ValidateTarget(old) && targeting.SelectTarget() == edge.TargetId, "移动出范围后立即失效。");
            near.gameObject.SetActive(false);
            Require(!targeting.ValidateTarget(old), "回池后不能攻击。");
            near.transform.position = Vector3.zero;
            near.gameObject.SetActive(true);
            Require(near.TargetId != old && !targeting.ValidateTarget(old), "再次出生不能接受旧身份。");
            near.enabled = false;
            Require(targeting.SelectTarget() == edge.TargetId, "停用组件的敌人不可选。");
            var health = new SerializedObject(edge);
            health.FindProperty("currentHealth").floatValue = 0;
            health.ApplyModifiedPropertiesWithoutUndo();
            Require(targeting.SelectTarget() == null, "只剩死亡/范围外目标时无目标。");
            far.transform.position = Vector3.zero;
            targeting.AttackRange = -1;
            Require(targeting.SelectTarget() == null, "负范围不能攻击。");
            targeting.AttackRange = 3;
            var other = SceneManager.CreateScene("Targeting other scene");
            try
            {
                SceneManager.MoveGameObjectToScene(far.gameObject, other);
                Require(targeting.SelectTarget() == null, "不能选中其他场景目标。");
            }
            finally { SceneManager.UnloadSceneAsync(other); }
            var pipeline = targeting.GetComponent<OrganPipelineController>();
            Require(pipeline.TargetSelector != null && pipeline.TargetValidator != null, "自动绑定目标入口。");
            targeting.enabled = false;
            Require(pipeline.TargetSelector == null && pipeline.TargetValidator == null, "停用必须解绑。");
            targeting.enabled = true;
            Require(pipeline.TargetSelector != null && pipeline.TargetValidator != null, "重新启用正常绑定。");
        }
    }
}
