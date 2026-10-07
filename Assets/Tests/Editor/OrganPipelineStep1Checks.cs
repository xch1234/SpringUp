using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    // 只在编辑器使用，不会进入正式游戏。
    public static class OrganPipelineStep1Checks
    {
        [MenuItem("Tools/SpringUp/Run Step 1 Checks")]
        public static void Run()
        {
            CheckOrderAndTiming();
            CheckEmptyBody();
            CheckIndependentBodies();
            CheckEquipment();
            CheckInvalidInput();
            Debug.Log("[Step1 Checks] PASS: all 5 groups passed.");
        }

        private static OrganInstance MakeOrgan(BodyPart part = BodyPart.Hand)
            => new OrganInstance(new OrganDefinition("test", "测试器官", part, OrganType.Actuator));

        private static void CheckOrderAndTiming()
        {
            var body = new BodyRuntime(BodyPart.Hand, 1f);
            var order = new List<int>();
            body.SlotExecuted += (_, index, organ) => order.Add(index);
            body.SetSlot(0, MakeOrgan());
            body.SetSlot(3, MakeOrgan());
            body.Tick(0.5f);
            Require(order.Count == 0, "未到节拍时不应执行。");
            body.Tick(0.5f);
            body.Tick(1f);
            body.Tick(1f);
            Require(string.Join(",", order) == "0,3,0", "应跳过空槽，并从末尾回到开头。");
            body.Tick(2.5f);
            Require(string.Join(",", order) == "0,3,0,3,0", "一帧跨过两个节拍时应执行两次。");
            body.Tick(0.5f);
            Require(order.Count == 6 && order[5] == 3, "剩余半秒应保留到下一帧。");
        }

        private static void CheckEmptyBody()
        {
            var body = new BodyRuntime(BodyPart.Hand, 1f);
            body.Tick(100f);
            Require(body.ExecutedStepCount == 0, "空部位不能执行。");
            body.SetSlot(5, MakeOrgan());
            body.Tick(0.5f);
            Require(body.ExecutedStepCount == 0, "空部位不能积攒节拍。");
            body.Tick(0.5f);
            Require(body.LastExecutedSlot == 5 && body.ExecutedStepCount == 1, "第六槽应能正常执行。");
            body.SetSlot(5, null);
            body.Tick(10f);
            Require(body.ExecutedStepCount == 1, "卸下最后一个器官后应停止执行。");
        }

        private static void CheckIndependentBodies()
        {
            var head = new BodyRuntime(BodyPart.Head, 1f);
            var hand = new BodyRuntime(BodyPart.Hand, 0.5f);
            var leg = new BodyRuntime(BodyPart.Leg, 1f);
            head.SetSlot(2, MakeOrgan(BodyPart.Head));
            hand.SetSlot(0, MakeOrgan());
            head.Tick(1f);
            hand.Tick(1f);
            leg.Tick(1f);
            Require(head.ExecutedStepCount == 1 && head.LastExecutedSlot == 2, "头部应独立运行。");
            Require(hand.ExecutedStepCount == 2, "手部应使用自己的计时器。");
            Require(leg.ExecutedStepCount == 0, "空腿部不应影响其他部位。");
        }

        private static void CheckEquipment()
        {
            var body = new BodyRuntime(BodyPart.Hand, 1f);
            var first = MakeOrgan();
            var second = new OrganInstance(first.Definition);
            Require(first.InstanceId != second.InstanceId, "同种器官也必须有不同实例编号。");
            body.SetSlot(0, first);
            body.SetSlot(3, second);
            body.Tick(1f);
            body.SetSlot(0, MakeOrgan());
            body.Tick(1f);
            Require(body.LastExecutedSlot == 3, "更换器官不应重置游标。");
            Throws<ArgumentException>(() => body.SetSlot(4, second));
            Throws<ArgumentException>(() => body.SetSlot(4, MakeOrgan(BodyPart.Leg)));
            Require(body.GetSlot(4) == null, "安装失败不能改变原来的槽位。");
        }

        private static void CheckInvalidInput()
        {
            var body = new BodyRuntime(BodyPart.Hand, 1f);
            Throws<ArgumentOutOfRangeException>(() => body.SetSlot(6, MakeOrgan()));
            Throws<ArgumentOutOfRangeException>(() => body.GetSlot(-1));
            Throws<ArgumentOutOfRangeException>(() => body.StepInterval = 0);
            Throws<ArgumentOutOfRangeException>(() => body.StepInterval = float.NaN);
            Throws<ArgumentOutOfRangeException>(() => body.Tick(-1));
            Throws<ArgumentOutOfRangeException>(() => body.Tick(float.PositiveInfinity));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Step1 Checks] " + message);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("[Step1 Checks] 应拒绝这次无效操作：" + typeof(T).Name);
        }
    }
}
