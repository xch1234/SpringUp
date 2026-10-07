using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using BodyPart = SpringUp.Organs.BodyPart;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class OrganPipelineStep3Checks
    {
        private static readonly List<GameObject> objects = new List<GameObject>();

        [MenuItem("Tools/SpringUp/Run Step 3 Checks")]
        public static void Run()
        {
            try
            {
                OrganPipelineStep2Checks.Run();
                CheckImmediateAttackAndTiming();
                CheckLoopGuardAndNewChains();
                CheckInstanceIdentity();
                CheckDepthLimit();
                CheckNoKillAndNoTarget();
                CheckSlotSnapshot();
                CheckQueueRecovery();
                Debug.Log("[Step3 Checks] PASS: all 7 groups passed; Steps 1 and 2 also passed.");
            }
            finally
            {
                foreach (GameObject item in objects)
                    if (item != null) Object.DestroyImmediate(item);
                objects.Clear();
            }
        }

        private static GameObject MakeObject(string name)
        {
            var item = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(item);
            return item;
        }

        private static OrganInstance Fist()
            => new OrganInstance(new OrganDefinition("fist", "拳头", BodyPart.Hand, OrganType.Actuator, 5f));

        private static OrganInstance Tentacle()
            => new OrganInstance(new OrganDefinition("multi_tentacle", "多肢触手", BodyPart.Hand, OrganType.Trigger));

        private static TriggerSignal Kill(CastContext context = null)
            => new TriggerSignal(TriggerOn.Kill, new CastEvent("simulated_dead_enemy", 5f, "fist", "source_fist",
                context ?? new CastContext()));

        // 接线与正式演示相同：实际扣血后，由死亡通知转发原攻击。
        private sealed class Fixture
        {
            public readonly OrganPipelineController Controller;
            public readonly BodyRuntime Hand;
            public readonly List<DemoTarget> Targets = new List<DemoTarget>();
            public readonly List<CastEvent> Attacks = new List<CastEvent>();
            public readonly List<TriggerBlockReason> Blocks = new List<TriggerBlockReason>();
            public int Activations;

            public Fixture(params float[] health)
            {
                Controller = MakeObject("Step 3 pipeline").AddComponent<OrganPipelineController>();
                Hand = Controller.GetBody(BodyPart.Hand);
                Hand.SetSlot(0, Fist());
                Hand.SetSlot(3, Tentacle());
                for (int i = 0; i < health.Length; i++)
                {
                    DemoTarget target = MakeObject("Chain target " + i).AddComponent<DemoTarget>();
                    target.InitializeForDemo("chain_target_" + i, health[i]);
                    target.Killed += (deadTarget, attack) => Controller.PublishSignal(new TriggerSignal(TriggerOn.Kill, attack));
                    Targets.Add(target);
                }
                Controller.TargetSelector = () =>
                {
                    foreach (DemoTarget target in Targets)
                        if (target.IsAlive) return target.TargetId;
                    return null;
                };
                Controller.AttackProduced += attack =>
                {
                    Attacks.Add(attack);
                    foreach (DemoTarget target in Targets)
                        if (target.TargetId == attack.TargetId)
                        {
                            Require(target.TryApplyAttack(attack), "链中的攻击必须实际扣血。");
                            return;
                        }
                    throw new InvalidOperationException("攻击目标不存在。");
                };
                Controller.TriggerActivated += (body, index, organ, context) => Activations++;
                Controller.TriggerBlocked += (body, index, organ, context, reason) => Blocks.Add(reason);
            }
        }

        private static void CheckImmediateAttackAndTiming()
        {
            var f = new Fixture(5f, 20f);
            f.Hand.Tick(1f);
            Require(f.Targets[0].CurrentHealth == 0f && f.Targets[1].CurrentHealth == 15f,
                "同一正常节拍内，击杀 A 后必须立刻追加攻击 B。");
            Require(f.Attacks.Count == 2 && f.Activations == 1, "应有一次普通攻击、一次追加攻击、一次触发。");
            Require(f.Attacks[0].ChainDepth == 0 && f.Attacks[1].ChainDepth == 1,
                "普通攻击深度为 0，第一次追加攻击为 1。");
            Require(f.Attacks[0].ChainId == f.Attacks[1].ChainId, "击杀通知不能创建一条新链。");
            Require(f.Attacks[0].OriginInstanceId == f.Attacks[1].OriginInstanceId,
                "追加攻击允许再次执行同一个拳头。");
            f.Hand.Tick(0.5f);
            Require(f.Hand.ExecutedStepCount == 1, "追加执行不能消耗正常计时。");
            f.Hand.Tick(0.5f);
            Require(f.Hand.LastExecutedSlot == 3 && f.Attacks.Count == 2,
                "下一正常节拍仍走触手槽，不能直接攻击或重置游标。");
            f.Hand.Tick(1f);
            Require(f.Targets[1].CurrentHealth == 10f && f.Attacks[2].ChainId != f.Attacks[0].ChainId,
                "下一次普通攻击应另起新链。");
        }

        private static void CheckLoopGuardAndNewChains()
        {
            var f = new Fixture(5f, 5f, 5f, 20f);
            f.Hand.Tick(1f);
            Require(f.Targets[0].CurrentHealth == 0f && f.Targets[1].CurrentHealth == 0f
                && f.Targets[2].CurrentHealth == 5f, "追加击杀不能让同一触手再次发动。");
            Require(f.Activations == 1 && f.Blocks.Contains(TriggerBlockReason.AlreadyTriggered),
                "应明确报告同链重复触发被拦截。");
            CastEvent oldAttack = f.Attacks[0];
            f.Hand.Tick(2f);
            Require(f.Targets[2].CurrentHealth == 0f && f.Targets[3].CurrentHealth == 15f && f.Activations == 2,
                "下一条链中，同一个触手应能再次发动。");
            int count = f.Attacks.Count;
            f.Controller.PublishSignal(new TriggerSignal(TriggerOn.Kill, oldAttack));
            Require(f.Attacks.Count == count, "迟到的旧通知不能因新链开始而绕过防重复。");
        }

        private static void CheckInstanceIdentity()
        {
            var f = new Fixture(100f);
            f.Hand.SetSlot(4, new OrganInstance(f.Hand.GetSlot(3).Definition));
            TriggerSignal signal = Kill();
            f.Controller.PublishSignal(signal);
            Require(f.Activations == 2 && f.Targets[0].CurrentHealth == 90f,
                "配置相同但实例不同的两个触手，应各能触发一次。");
            Require(f.Attacks[0].ChainDepth == 1 && f.Attacks[1].ChainDepth == 1,
                "同一通知的两个分支都应为深度 1，不能累加成深度 2。");
            f.Controller.PublishSignal(signal);
            Require(f.Activations == 2 && f.Attacks.Count == 2, "重复通知不能再次发动任何一个实例。");
        }

        private static void CheckDepthLimit()
        {
            var root = new CastContext();
            CastContext current = root;
            for (int i = 1; i <= CastContext.MaxChainDepth; i++)
            {
                Require(current.TryEnterTrigger("depth_trigger_" + i, out CastContext next, out _),
                    "深度 1 到 4 应允许进入。");
                Require(next.Depth == i && next.ChainId == root.ChainId, "深度应沿当前分支递增。");
                current = next;
            }
            Require(!current.TryEnterTrigger("fifth_trigger", out _, out TriggerBlockReason reason)
                && reason == TriggerBlockReason.DepthLimit, "即使是全新的触发器，深度 5 也必须被拦截。");

            var f = new Fixture(100f);
            f.Controller.PublishSignal(Kill(new CastContext(3)));
            Require(f.Attacks.Count == 1 && f.Attacks[0].ChainDepth == 4, "管道应允许产生深度 4 的攻击。");
            f.Controller.PublishSignal(new TriggerSignal(TriggerOn.Kill, f.Attacks[0]));
            Require(f.Attacks.Count == 1 && f.Blocks.Contains(TriggerBlockReason.DepthLimit),
                "深度 4 的结果不能继续生成深度 5 攻击。");
        }

        private static void CheckNoKillAndNoTarget()
        {
            var living = new Fixture(20f);
            living.Hand.Tick(2f);
            Require(living.Attacks.Count == 1 && living.Activations == 0,
                "普通受伤或走过触手槽不能冒充击杀通知。");
            var empty = new Fixture();
            empty.Hand.Tick(2f);
            empty.Controller.PublishSignal(default);
            Require(empty.Attacks.Count == 0 && empty.Activations == 0, "没有目标或有效通知时应安全跳过。");
            empty.Controller.PublishSignal(Kill());
            Require(empty.Attacks.Count == 0 && empty.Activations == 1, "全灭后的最后一次击杀允许触发，但不能攻击尸体。");
            empty.Controller.GetBody(BodyPart.Head).Tick(10f);
            empty.Controller.GetBody(BodyPart.Leg).Tick(10f);
        }

        private static void CheckSlotSnapshot()
        {
            var body = new BodyRuntime(BodyPart.Hand, 1f);
            body.SetSlot(0, Fist());
            body.SetSlot(3, Tentacle());
            body.SetSlot(5, Fist());
            body.Tick(1.5f);
            var order = new List<int>();
            body.ExecuteAllSlots((index, organ) =>
            {
                order.Add(index);
                if (index == 0) body.SetSlot(5, null);
            });
            Require(string.Join(",", order) == "0,3,5", "额外扫描应按开始时的槽位顺序各执行一次。");
            Require(body.ExecutedStepCount == 1 && body.LastExecutedSlot == 0,
                "额外扫描不能改普通节拍的计数或游标。");
            body.Tick(0.5f);
            Require(body.LastExecutedSlot == 3 && body.ExecutedStepCount == 2,
                "扫描后应保留原先剩余的半秒。");
        }

        private static void CheckQueueRecovery()
        {
            var f = new Fixture(100f);
            Action<BodyRuntime, int, OrganInstance, CastContext> fail = (body, index, organ, context)
                => throw new InvalidOperationException("expected test exception");
            f.Controller.TriggerActivated += fail;
            bool threw = false;
            try { f.Controller.PublishSignal(Kill()); }
            catch (InvalidOperationException exception) when (exception.Message == "expected test exception") { threw = true; }
            finally { f.Controller.TriggerActivated -= fail; }
            Require(threw, "测试应执行到预期的异常回调。");
            f.Controller.PublishSignal(Kill());
            Require(f.Attacks.Count == 1, "一次回调失败后，通知队列不能永久卡住。");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Step3 Checks] " + message);
        }
    }
}
