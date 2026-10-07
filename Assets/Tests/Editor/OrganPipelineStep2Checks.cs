using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using BodyPart = SpringUp.Organs.BodyPart;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class OrganPipelineStep2Checks
    {
        private static readonly List<GameObject> objects = new List<GameObject>();

        [MenuItem("Tools/SpringUp/Run Step 2 Checks")]
        public static void Run()
        {
            try
            {
                OrganPipelineStep1Checks.Run();
                CheckAttackData();
                CheckPipelineDamage();
                CheckTargetRules();
                CheckNoAttackCases();
                CheckInvalidInput();
                Debug.Log("[Step2 Checks] PASS: all 5 groups passed; Step 1 also passed.");
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

        private static DemoTarget MakeTarget(string id, float health)
        {
            DemoTarget target = MakeObject(id).AddComponent<DemoTarget>();
            target.InitializeForDemo(id, health);
            return target;
        }

        private static OrganInstance Fist(float damage = 5f)
            => new OrganInstance(new OrganDefinition("fist", "拳头", BodyPart.Hand, OrganType.Actuator, damage));

        private static CastEvent Attack(string id, float damage = 5f)
            => new CastEvent(id, damage, "fist", "test_fist_instance");

        private static void CheckAttackData()
        {
            OrganInstance fist = Fist();
            Require(ParameterisedBehaviour.TryCreateAttack(fist, "a", out CastEvent first), "拳头应生成攻击。");
            Require(ParameterisedBehaviour.TryCreateAttack(fist, "b", out CastEvent second), "拳头应能再次攻击。");
            Require(first.TargetId == "a" && second.TargetId == "b", "新攻击不能改变旧攻击目标。");
            Require(first.Damage == 5f && second.Damage == 5f, "连续攻击不能叠加上次的伤害。");
            Require(first.OriginInstanceId == fist.InstanceId && first.OriginDefinitionId == "fist", "应保留来源。");
            Require(first.ChainDepth == 0, "普通攻击链深度应为零。");
            ParameterisedBehaviour.TryCreateAttack(Fist(8f), "a", out CastEvent other);
            Require(other.Damage == 8f, "伤害必须读取配置，不能在执行器中写死。");
        }

        private static void CheckPipelineDamage()
        {
            var controller = MakeObject("Pipeline check").AddComponent<OrganPipelineController>();
            DemoTarget a = MakeTarget("a", 5f);
            DemoTarget b = MakeTarget("b", 20f);
            controller.TargetSelector = () => a.IsAlive ? a.TargetId : b.IsAlive ? b.TargetId : null;
            int attacks = 0;
            int deaths = 0;
            int hits = 0;
            controller.AttackProduced += attack =>
            {
                attacks++;
                DemoTarget target = attack.TargetId == a.TargetId ? a : b;
                Require(target.TryApplyAttack(attack), "管道生成的攻击应实际扣血。");
            };
            a.Damaged += (_, attack, amount) => { hits++; Require(amount == 5f, "每次应扣 5 血。"); };
            b.Damaged += (_, attack, amount) => { hits++; Require(amount == 5f, "每次应扣 5 血。"); };
            a.Killed += (_, attack) => deaths++;
            b.Killed += (_, attack) => deaths++;
            BodyRuntime body = controller.GetBody(BodyPart.Hand);
            body.SetSlot(0, Fist());
            body.Tick(0.5f);
            Require(a.CurrentHealth == 5f && attacks == 0, "不足一个节拍不能扣血。");
            body.Tick(0.5f);
            Require(a.CurrentHealth == 0f && b.CurrentHealth == 20f, "击杀 A 不能在第二步追加攻击 B。");
            for (int i = 1; i <= 4; i++)
            {
                body.Tick(1f);
                Require(b.CurrentHealth == 20f - i * 5f, "B 应按 20、15、10、5、0 扣血。");
            }
            body.Tick(10f);
            Require(attacks == 5 && hits == 5 && deaths == 2, "全灭后不能继续攻击或重复死亡。");
        }

        private static void CheckTargetRules()
        {
            DemoTarget target = MakeTarget("rules", 3f);
            int deaths = 0;
            float actualDamage = 0f;
            target.Killed += (_, attack) => deaths++;
            target.Damaged += (_, attack, amount) => actualDamage += amount;
            Require(!target.TryApplyAttack(default), "空攻击应被拒绝。");
            Require(!target.TryApplyAttack(Attack("other")), "目标编号不匹配时不能扣血。");
            target.enabled = false;
            Require(!target.TryApplyAttack(Attack("rules")), "停用的目标不能受击。");
            target.enabled = true;
            Require(target.TryApplyAttack(Attack("rules")), "有效攻击应命中。");
            Require(target.CurrentHealth == 0f && actualDamage == 3f && deaths == 1, "过量伤害应夹到零，实际扣血为 3。");
            Require(!target.TryApplyAttack(Attack("rules")) && deaths == 1, "死亡后不能重复受击或死亡。");
            target.gameObject.SetActive(false);
            target.gameObject.SetActive(true);
            Require(!target.IsAlive, "重新启用不能复活死亡目标。");
        }

        private static void CheckNoAttackCases()
        {
            var controller = MakeObject("Empty target check").AddComponent<OrganPipelineController>();
            int attacks = 0;
            controller.AttackProduced += _ => attacks++;
            BodyRuntime body = controller.GetBody(BodyPart.Hand);
            body.SetSlot(0, Fist());
            body.Tick(1f);
            Require(attacks == 0, "没有目标选择器时不能生成攻击。");
            controller.TargetSelector = () => "a";
            body.SetSlot(0, new OrganInstance(new OrganDefinition("placeholder", "占位", BodyPart.Hand, OrganType.Actuator)));
            body.Tick(1f);
            Require(attacks == 0, "第一步的零伤害占位器官不能攻击。");
            var trigger = new OrganInstance(new OrganDefinition("trigger", "触发器", BodyPart.Hand, OrganType.Trigger, 5f));
            body.SetSlot(0, trigger);
            body.Tick(1f);
            Require(attacks == 0, "触发器不能当成拳头执行器。");
            Require(!ParameterisedBehaviour.TryCreateAttack(Fist(), null, out _), "无目标应安全跳过。");
        }

        private static void CheckInvalidInput()
        {
            Throws<ArgumentOutOfRangeException>(() => Fist(-1f));
            Throws<ArgumentOutOfRangeException>(() => Fist(float.NaN));
            Throws<ArgumentOutOfRangeException>(() => Attack("a", float.PositiveInfinity));
            Throws<ArgumentException>(() => Attack(""));
            Throws<ArgumentOutOfRangeException>(() => new CastEvent("a", 5f, "fist", "instance", -1));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Step2 Checks] " + message);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("[Step2 Checks] 应拒绝无效输入：" + typeof(T).Name);
        }
    }
}
