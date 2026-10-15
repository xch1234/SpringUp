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
using UnityEngine.EventSystems;
using UnityEngine.UI;
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

        [Test]
        public void BindingCompatibilityAndReservedKeys()
        {
            var saved = PlayerPrefs.GetString(PlayerChargeAttack.BindingPreference, "F");
            var root = new GameObject("Binding checks");
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var pipeline = root.AddComponent<OrganPipelineController>();
                var attack = root.AddComponent<PlayerChargeAttack>();
                foreach (var invalid in new[] { "B", "E", "Tab", "99999", "None", "bad" })
                {
                    PlayerPrefs.SetString(PlayerChargeAttack.BindingPreference, invalid);
                    attack.Configure(pipeline, () => true, 100, 20);
                    Assert.AreEqual(Key.F, attack.AttackKey);
                    Assert.IsNotEmpty(attack.BindingNotice);
                }
                Assert.IsFalse(attack.SetAttackKey(Key.B)); StringAssert.Contains("背包", attack.Feedback);
                Assert.IsFalse(attack.SetAttackKey(Key.E)); StringAssert.Contains("商人", attack.Feedback);
                Assert.IsTrue(attack.SetMouseBinding(false)); Assert.AreEqual("鼠标左键", attack.KeyLabel);
                attack.Configure(pipeline, () => true, 100, 20); Assert.AreEqual("鼠标左键", attack.KeyLabel);
                Assert.IsTrue(attack.SetMouseBinding(true));
                Assert.AreEqual("MouseRight", PlayerPrefs.GetString(PlayerChargeAttack.BindingPreference));
                Assert.IsTrue(attack.SetAttackKey(Key.F)); Assert.AreEqual("F", attack.KeyLabel);
            }
            finally { Object.DestroyImmediate(root); InputSystem.RemoveDevice(mouse); PlayerPrefs.SetString(PlayerChargeAttack.BindingPreference, saved); }
        }

        [UnityTest]
        public IEnumerator HeldZeroCostAttackRespectsFinalIntervalAndReleaseGate()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var saved = PlayerPrefs.GetString(PlayerChargeAttack.BindingPreference, "F");
            var previousInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            var root = new GameObject("Held input checks"); var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            GameObject canvas = null, events = null;
            try
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                Time.timeScale = 1;
                var pipeline = root.AddComponent<OrganPipelineController>(); pipeline.StepInterval = .15f;
                pipeline.GetBody(BodyPart.Hand).SetSlot(0, new OrganInstance(new OrganDefinition("free", "free", BodyPart.Hand, OrganType.Actuator, 5, chargeCost: 0)));
                string target = "target"; bool allowed = true; pipeline.TargetSelector = () => target; pipeline.TargetValidator = id => id == target;
                var times = new List<float>(); pipeline.AttackProduced += _ => times.Add(Time.time);
                var attack = root.AddComponent<PlayerChargeAttack>(); attack.Configure(pipeline, () => allowed, 100, 20);
                attack.SetAttackKey(Key.F); attack.BeginWave(); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
                { float until = Time.time + .65f; while (Time.time < until) yield return null; }
                Assert.That(times.Count, Is.InRange(3, 6), $"pressed={keyboard.fKey.isPressed} feedback={attack.Feedback} time={Time.time} scale={Time.timeScale}");
                for (int i = 1; i < times.Count; i++) Assert.GreaterOrEqual(times[i] - times[i - 1], .149f);
                Assert.IsFalse(attack.TryAttack(), "点按不能绕过最后出手间隔");
                allowed = false; yield return null; allowed = true; int count = times.Count;
                { float until = Time.time + .3f; while (Time.time < until) yield return null; } Assert.AreEqual(count, times.Count, "恢复后持续按住不攻击");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                target = null; InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
                { float until = Time.time + .2f; while (Time.time < until) yield return null; } Assert.AreEqual(count, times.Count);
                target = "target"; { float until = Time.time + .2f; while (Time.time < until) yield return null; } Assert.Greater(times.Count, count);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                count = times.Count; { float until = Time.time + .3f; while (Time.time < until) yield return null; } Assert.AreEqual(count, times.Count);
                pipeline.GetBody(BodyPart.Hand).SetSlot(1, new OrganInstance(new OrganDefinition("second", "second", BodyPart.Hand, OrganType.Actuator, 5, chargeCost: 0)));
                attack.RefreshLoadout(); Assert.IsTrue(attack.TryAttack());
                { float until = Time.time + .2f; while (Time.time < until) yield return null; } Assert.AreEqual(count + 2, times.Count, "松键不取消已经开始的多器官轮次");
                pipeline.GetBody(BodyPart.Hand).SetSlot(1, null); attack.RefreshLoadout();
                { float until = Time.time + .2f; while (Time.time < until) yield return null; }
                events = new GameObject("UI input", typeof(EventSystem));
                canvas = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var button = new GameObject("Interactive", typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(canvas.transform, false);
                var rect = (RectTransform)button.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                attack.SetMouseBinding(false); yield return null; yield return null;
                var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center }.WithButton(MouseButton.Left));
                count = times.Count; { float until = Time.time + .2f; while (Time.time < until) yield return null; }
                Assert.AreEqual(count, times.Count, "点击 UI 不攻击");
                Object.Destroy(button.GetComponent<Button>()); { float until = Time.time + .2f; while (Time.time < until) yield return null; }
                Assert.AreEqual(count, times.Count, "点击 UI 后按住离开也不攻击");
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center }); yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center }.WithButton(MouseButton.Left));
                { float until = Time.time + .2f; while (Time.time < until) yield return null; } Assert.Greater(times.Count, count, "释放重按鼠标可攻击");
                attack.SendMessage("OnApplicationFocus", false); attack.SendMessage("OnApplicationFocus", true);
                count = times.Count; { float until = Time.time + .2f; while (Time.time < until) yield return null; } Assert.AreEqual(count, times.Count, "恢复焦点须释放");
                attack.ToggleRebind(); yield return null; yield return null;
                Assert.IsTrue(attack.IsRebinding, "等待启动输入释放");
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center }); yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center }.WithButton(MouseButton.Right));
                yield return null; yield return null;
                Assert.IsFalse(attack.IsRebinding); Assert.AreEqual("鼠标右键", attack.KeyLabel);
                Assert.AreEqual(count, times.Count, "换绑点击不得攻击");
                InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null; yield return null;
                attack.SetAttackKey(Key.F);
                pipeline.GetBody(BodyPart.Hand).SetSlot(0, new OrganInstance(new OrganDefinition("waiting", "waiting", BodyPart.Hand, OrganType.Actuator, 5, chargeCost: 5)));
                attack.RefreshLoadout(); attack.RestoreCharge(0); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
                { float until = Time.time + .1f; while (Time.time < until) yield return null; }
                Assert.AreEqual(count, times.Count, "充能不足时按住等待");
                { float until = Time.time + .3f; while (Time.time < until) yield return null; }
                Assert.Greater(times.Count, count, "持续按住在充能恢复后发动");


            }
            finally
            {
                Object.Destroy(root); Object.Destroy(canvas); Object.Destroy(events); InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                InputSystem.settings.editorInputBehaviorInPlayMode = previousInput;
                InputSystem.settings.backgroundBehavior = previousBackground;
                PlayerPrefs.SetString(PlayerChargeAttack.BindingPreference, saved); Time.timeScale = 1;
            }
            yield return new ExitPlayMode();
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
                Assert.AreEqual(2, shots.Count, "跨波必须释放按键后重新输入。");

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
