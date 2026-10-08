using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using BodyPart = SpringUp.Organs.BodyPart;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class HandStatusChecks
    {
        private static readonly List<Object> objects = new List<Object>();

        [MenuItem("Tools/SpringUp/Run Hand Step 2 Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Hand Step2 Checks] 请先停止 Play。");
                return;
            }
            var errors = new List<string>();
            Application.LogCallback record = (message, trace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
            };
            Application.logMessageReceived += record;
            try
            {
                HandOrganChecks.Run();
                CheckConfig();
                CheckChanceAndIsolation();
                CheckStunAndMovement();
                CheckSlow();
                CheckBleedTiming();
                CheckBleedKillChain();
                CheckCleanupAndInvalid();
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
            Require(errors.Count == 0, "出现错误或断言：" + string.Join(" | ", errors));
            Debug.Log("[Hand Step2 Checks] PASS: all 7 status groups and previous 22 groups passed, including cleanup.");
        }

        private static GameObject Go(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(go);
            return go;
        }

        private static OrganConfig Config(HandOrganKind kind, float chance = 100f)
        {
            var config = ScriptableObject.CreateInstance<OrganConfig>();
            objects.Add(config);
            var data = new SerializedObject(config);
            data.FindProperty("kind").intValue = (int)kind;
            data.FindProperty("damage").floatValue = 1f;
            data.FindProperty("statusChancePercent").floatValue = chance;
            data.FindProperty("statusDuration").floatValue = 3f;
            data.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static CastEvent Attack(StatusEffectData effect, string id = "a", CastContext context = null)
            => new CastEvent(id, 1f, "test_status", "instance", context ?? new CastContext(), effect);
        private static StatusEffectData Stun(float chance = 1f, float duration = 2f)
            => new StatusEffectData(StatusEffectKind.Stun, chance, duration);
        private static StatusEffectData Slow(float duration = 3f, float amount = 0.15f)
            => new StatusEffectData(StatusEffectKind.Slow, 1f, duration, slowPerStack: amount, maxStacks: 3);
        private static StatusEffectData Bleed(float duration = 3f)
            => new StatusEffectData(StatusEffectKind.Bleed, 1f, duration, 2f, 1f);
        private static void Wait(StatusEffectRuntime status, float seconds)
            => status.Tick(seconds, _ => { }, () => true);

        private static void CheckConfig()
        {
            OrganConfig config = Config(HandOrganKind.RustKnife, 30f);
            Require(config.TryCreateDefinition(out OrganDefinition old, out _), "锈刀配置应可读取。");
            var edit = new SerializedObject(config);
            edit.FindProperty("bleedDamage").floatValue = 7f;
            edit.ApplyModifiedPropertiesWithoutUndo();
            Require(config.TryCreateDefinition(out OrganDefinition changed, out _), "修改后可读取。");
            Require(old.Status.TickDamage == 2f && changed.Status.TickDamage == 7f, "状态参数必须形成独立快照。");
            ParameterisedBehaviour.TryCreateAttack(new OrganInstance(changed), "a", out CastEvent attack);
            Require(attack.Status.TickDamage == 7f && attack.Status.Chance == 0.3f, "配置必须传到攻击。");
            edit.Update(); edit.FindProperty("statusDuration").floatValue = 0f; edit.ApplyModifiedPropertiesWithoutUndo();
            Require(!config.TryCreateDefinition(out _, out _), "零持续时间应提示无效，可用概率 0 关闭状态。");
        }

        private static void CheckChanceAndIsolation()
        {
            var a = new StatusEffectRuntime();
            var b = new StatusEffectRuntime();
            Require(!a.TryApply(Attack(Stun(0f)), 0f), "0% 永远不施加。");
            Require(!a.TryApply(Attack(Stun(0.2f)), 0.2f), "20% 的边界应排除。");
            Require(a.TryApply(Attack(Stun(0.2f)), 0.19f), "低于 20% 的抽样应成功。");
            Require(b.SpeedMultiplier == 1f, "不同敌人的状态不能串用。");
            a.Clear();
            Require(a.TryApply(Attack(Stun()), 1f), "100% 必须施加，包括随机上界。");
        }

        private static void CheckStunAndMovement()
        {
            var status = new StatusEffectRuntime();
            status.TryApply(Attack(Stun()), 0f);
            Require(status.SpeedMultiplier == 0f && status.MovementSeconds(1f) == 0f, "眩晕应停止移动。");
            Near(status.MovementSeconds(3f), 1f, "跨过眩晕结束时，应只移动剩余的一秒。");
            Wait(status, 1f);
            status.TryApply(Attack(Stun()), 0f);
            Wait(status, 1.5f);
            Require(status.SpeedMultiplier == 0f, "再次命中应刷新眩晕时间。");
            Wait(status, 0.5f);
            Require(status.SpeedMultiplier == 1f, "眩晕结束恢复移动。");
        }

        private static void CheckSlow()
        {
            var status = new StatusEffectRuntime();
            for (int i = 1; i <= 4; i++)
            {
                status.TryApply(Attack(Slow()), 0f);
                Near(status.SpeedMultiplier, 1f - Math.Min(i, 3) * 0.15f, "减速按基础速度逐层相加。");
            }
            Require(status.SlowStacks == 3, "不能超过三层。");
            Near(status.MovementSeconds(12f), 9.3f, "长帧应按三层、两层、一层、无减速分段计算移动。");
            Require(status.SlowStacks == 3, "移动预览不能修改层数。");
            status.TryApply(Attack(Stun(duration: 1f)), 0f);
            Require(status.SpeedMultiplier == 0f, "眩晕优先于减速。");
            Wait(status, 1f);
            Near(status.SpeedMultiplier, 0.55f, "眩晕到期后剩余减速继续生效。");
            status.TryApply(Attack(Slow()), 0f);
            Wait(status, 2.5f);
            Require(status.SlowStacks == 3, "满层再次命中也刷新时间。");
            Wait(status, 0.5f);
            Require(status.SlowStacks == 2, "第一次到期只能减少一层。");
            Near(status.SpeedMultiplier, 0.70f, "两层速度为 70%。");
            Near(status.SlowRemaining, 3f, "掉层后重新计算下一次掉层时间。");
            Wait(status, 3f);
            Require(status.SlowStacks == 1, "第二次到期还剩一层。");
            Near(status.SpeedMultiplier, 0.85f, "一层速度为 85%。");
            Wait(status, 3f);
            Require(status.SpeedMultiplier == 1f && status.SlowStacks == 0 && status.SlowRemaining == 0f,
                "最后一层到期才恢复基础速度。");
            for (int i = 0; i < 3; i++) status.TryApply(Attack(Slow()), 0f);
            Wait(status, 4f);
            Require(status.SlowStacks == 2, "长帧后仍应保留未到期层数。");
            status.TryApply(Attack(Slow()), 0f);
            Wait(status, 2.5f);
            Require(status.SlowStacks == 3, "掉层后再次命中应加一层，并从命中时重新计时。");
            Wait(status, 7f);
            Require(status.SlowStacks == 0, "长帧跨过多次到期应全部补算。");
            for (int i = 0; i < 3; i++) status.TryApply(Attack(Slow()), 0f);
            status.TryApply(Attack(Stun(duration: 4f)), 0f);
            Near(status.MovementSeconds(12f), 6.95f, "眩晕结束时应使用当时剩余层数的速度。");
            status.Clear();
            for (int i = 0; i < 3; i++) status.TryApply(Attack(Slow(amount: 1f)), 0f);
            Require(status.SpeedMultiplier == 0f, "总减速超过 100% 也不能出现负速度。");
        }

        private static void CheckBleedTiming()
        {
            var status = new StatusEffectRuntime();
            var ticks = new List<CastEvent>();
            CastEvent cause = Attack(Bleed());
            status.TryApply(cause, 0f);
            status.Tick(0.5f, ticks.Add, () => true);
            Require(ticks.Count == 0, "流血首次扣血需要等待间隔。");
            status.TryApply(cause, 0f);
            status.Tick(0.5f, ticks.Add, () => true);
            Require(ticks.Count == 1, "刷新不能推迟下一次流血。");
            status.Tick(2.5f, ticks.Add, () => true);
            Require(ticks.Count == 3 && status.BleedRemaining == 0f, "长帧不能漏扣，也不能超过持续时间。");
            foreach (CastEvent tick in ticks)
                Require(tick.Status == null && tick.Damage == 2f && tick.Context == cause.Context
                    && tick.OriginInstanceId == cause.OriginInstanceId, "流血保留来源和链，不携带可再次施加的状态。");
            status.Clear(); ticks.Clear();
            status.TryApply(Attack(Bleed()), 0f);
            status.Tick(3f, ticks.Add, () => true);
            Require(ticks.Count == 3, "到期时刻的一次流血也应结算。");
        }

        private static void CheckBleedKillChain()
        {
            DemoTarget a = Go("a").AddComponent<DemoTarget>(); a.InitializeForDemo("a", 3f);
            DemoTarget b = Go("b").AddComponent<DemoTarget>(); b.InitializeForDemo("b", 20f);
            var ac = a.gameObject.AddComponent<DemoCombatTarget>();
            var bc = b.gameObject.AddComponent<DemoCombatTarget>();
            var demo = Go("status demo").AddComponent<HandOrganDemo>();
            var settings = new SerializedObject(demo);
            settings.FindProperty("slots").GetArrayElementAtIndex(0).objectReferenceValue = Config(HandOrganKind.RustKnife);
            settings.FindProperty("slots").GetArrayElementAtIndex(3).objectReferenceValue = Config(HandOrganKind.MultiTentacle);
            var targets = settings.FindProperty("targets"); targets.arraySize = 2;
            targets.GetArrayElementAtIndex(0).objectReferenceValue = a;
            targets.GetArrayElementAtIndex(1).objectReferenceValue = b;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Require(demo.TryStartDemo(out _), "状态演示应能启动。");
            var controller = demo.GetComponent<OrganPipelineController>();
            var attacks = new List<CastEvent>(); controller.AttackProduced += attacks.Add;
            controller.GetBody(BodyPart.Hand).Tick(1f);
            Require(a.CurrentHealth == 2f && demo.TriggerCount == 0, "未击杀前不能触发。");
            ac.Advance(1f);
            Require(a.CurrentHealth == 0f && b.CurrentHealth == 19f && demo.TriggerCount == 1,
                "流血击杀必须立即触发触手并攻击下一个敌人。");
            Require(attacks.Count == 2 && attacks[1].ChainId == attacks[0].ChainId && attacks[1].ChainDepth == 1,
                "延迟流血击杀仍属于原链。");
            bc.Advance(1f);
            Require(b.CurrentHealth == 17f, "追加攻击附带的流血也应正常结算。");
            demo.StopDemo();
            bc.Advance(5f);
            Require(b.CurrentHealth == 17f && bc.Statuses.BleedRemaining == 0f, "停止演示应清除残留流血。");
        }

        private static void CheckCleanupAndInvalid()
        {
            var status = new StatusEffectRuntime();
            status.TryApply(Attack(Bleed()), 0f);
            int ticks = 0;
            status.Tick(100f, _ => ticks++, () => false);
            Require(ticks == 0 && status.BleedRemaining == 0f, "死亡目标不能继续流血。");
            var target = Go("wrong target").AddComponent<DemoTarget>(); target.InitializeForDemo("correct", 10f);
            var combat = target.gameObject.AddComponent<DemoCombatTarget>();
            Require(!combat.TryApplyAttack(Attack(Stun(), "wrong")) && combat.Statuses.StunRemaining == 0f,
                "未命中的攻击不能施加状态。");
            Throws(() => new StatusEffectData(StatusEffectKind.Stun, float.NaN, 1f));
            Throws(() => new StatusEffectData(StatusEffectKind.Bleed, 1f, 3f, tickInterval: 0f));
            Throws(() => status.Tick(-1f, _ => { }, () => true));
        }

        private static void Throws(Action action)
        {
            try { action(); } catch (ArgumentOutOfRangeException) { return; }
            throw new InvalidOperationException("无效状态参数必须拒绝。");
        }
        private static void Near(float actual, float expected, string message) => Require(Math.Abs(actual - expected) < 0.0001f, message);
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("[Hand Step2 Checks] " + message);
        }
    }
}
