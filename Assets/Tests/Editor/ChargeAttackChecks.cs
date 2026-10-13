using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SpringUp.Organs;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using OrganDefinition = SpringUp.Organs.OrganDefinition;
using OrganInstance = SpringUp.Organs.OrganInstance;
using BodyPart = SpringUp.Organs.BodyPart;

namespace SpringUp.EditorChecks
{
    public sealed class ChargeAttackChecks
    {
        [Test]
        public void OrganChargeCostsAreConfigurableAndValidated()
        {
            var config = Object.Instantiate(AssetDatabase.LoadAssetAtPath<OrganConfig>("Assets/Data/Organs/Hand/Fist.asset"));
            try
            {
                var serialized = new SerializedObject(config);
                serialized.FindProperty("chargeCost").floatValue = 37;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(config.TryCreateDefinition(out var definition, out _));
                Assert.AreEqual(37, definition.ChargeCost);
                serialized.FindProperty("chargeCost").floatValue = -1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(config.TryCreateDefinition(out _, out _));
                Assert.Throws<ArgumentOutOfRangeException>(() => new OrganDefinition("bad", "bad", BodyPart.Hand,
                    OrganType.Actuator, 5, chargeCost: float.NaN));
                var trigger = new OrganDefinition("trigger", "trigger", BodyPart.Hand, OrganType.Trigger);
                Assert.AreEqual(0, trigger.ChargeCost);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [UnityTest]
        public IEnumerator ManualInputChargeRoundsAndRebinding()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            bool hadBinding = PlayerPrefs.HasKey(PlayerChargeAttack.BindingPreference);
            string oldBinding = PlayerPrefs.GetString(PlayerChargeAttack.BindingPreference);
            var previousInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            var root = new GameObject("Charge checks");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                Time.timeScale = 1;
                var pipeline = root.AddComponent<OrganPipelineController>();
                pipeline.StepInterval = .15f;
                var hand = pipeline.GetBody(BodyPart.Hand);
                hand.SetSlot(0, new OrganInstance(new OrganDefinition("first", "first", BodyPart.Hand, OrganType.Actuator, 5, chargeCost: 60)));
                hand.SetSlot(3, new OrganInstance(new OrganDefinition("second", "second", BodyPart.Hand, OrganType.Actuator, 5, chargeCost: 70)));
                hand.SetSlot(5, new OrganInstance(new OrganDefinition("trigger", "trigger", BodyPart.Hand, OrganType.Trigger)));
                bool combat = true;
                string target = "target";
                pipeline.TargetSelector = () => target;
                pipeline.TargetValidator = id => id == target;
                var shots = new List<string>();
                pipeline.AttackProduced += shot => shots.Add(shot.OriginDefinitionId);
                var attack = root.AddComponent<PlayerChargeAttack>();
                attack.Configure(pipeline, () => combat, 100, 20);
                attack.SetAttackKey(Key.F);
                attack.BeginWave();
                Assert.AreEqual(130, attack.RequiredCharge);
                Assert.AreEqual(130, attack.Capacity, "高费用配装仍能蓄满一轮。");
                float until = Time.time + .3f;
                while (Time.time < until) yield return null;
                Assert.IsEmpty(shots, "不按键不会自动攻击。");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
                until = Time.time + .5f;
                while (Time.time < until) yield return null;
                CollectionAssert.AreEqual(new[] { "first", "second" }, shots, "F 键按槽位顺序发动一轮，跳过空槽和触发器。");
                Assert.Less(attack.Charge, attack.RequiredCharge);
                Assert.Greater(attack.Charge, 0, "消耗后随时间恢复。");
                Assert.IsFalse(attack.TryAttack(), "充能不足禁止攻击。");
                attack.BeginWave();
                until = Time.time + .2f;
                while (Time.time < until) yield return null;
                Assert.AreEqual(2, shots.Count, "长按不会重复攻击或跨波触发。");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                target = null;
                float before = attack.Charge;
                Assert.IsFalse(attack.TryAttack());
                Assert.AreEqual(before, attack.Charge, "没有目标不扣充能。");
                target = "target";
                Assert.IsTrue(attack.TryAttack());
                Assert.IsFalse(attack.TryAttack(), "不能重叠开启攻击轮。");
                target = null;
                until = Time.time + .3f;
                while (Time.time < until) yield return null;
                Assert.GreaterOrEqual(attack.Charge, 70, "后续槽位失去目标会退还该槽位费用。");
                Assert.AreEqual(3, shots.Count);

                combat = false;
                before = attack.Charge;
                Assert.IsFalse(attack.TryAttack(), "休息期禁止实战攻击。");
                until = Time.time + .2f;
                while (Time.time < until) yield return null;
                Assert.AreEqual(before, attack.Charge, "充能只在战斗中恢复。");
                attack.ToggleRebind();
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.G));
                yield return null;
                yield return null;
                Assert.AreEqual(Key.G, attack.AttackKey);
                Assert.AreEqual("G", PlayerPrefs.GetString(PlayerChargeAttack.BindingPreference));
                Assert.IsFalse(attack.IsRebinding);
                Assert.IsFalse(attack.SetAttackKey(Key.W), "移动键冲突不能被绑定。");
                combat = true; target = "target"; attack.BeginWave();
                yield return null;
                Assert.AreEqual(3, shots.Count, "改键确认本身不会攻击。");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
                yield return null; yield return null;
                Assert.AreEqual(3, shots.Count, "改键后旧键失效。");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.G));
                until = Time.time + .4f;
                while (Time.time < until) yield return null;
                Assert.AreEqual(5, shots.Count, "新键发动一整轮。");

                attack.BeginWave();
                Assert.IsTrue(attack.TryAttack());
                combat = false; attack.CancelRound();
                until = Time.time + .2f;
                while (Time.time < until) yield return null;
                Assert.AreEqual(6, shots.Count, "休息/死亡后剩余槽位不继续攻击。");
                attack.Configure(pipeline, () => combat, 100, 20);
                Assert.AreEqual(Key.G, attack.AttackKey, "重新初始化读取保存的按键。");
            }
            finally
            {
                Object.Destroy(root);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = previousInput;
                InputSystem.settings.backgroundBehavior = previousBackground;
                if (hadBinding) PlayerPrefs.SetString(PlayerChargeAttack.BindingPreference, oldBinding);
                else PlayerPrefs.DeleteKey(PlayerChargeAttack.BindingPreference);
                PlayerPrefs.Save();
                Time.timeScale = 1;
            }
            yield return new ExitPlayMode();
        }
    }
}
