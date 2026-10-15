using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PipelineOrgan = SpringUp.Organs.OrganInstance;

namespace Tideflesh.Systems
{
    // 统一游玩入口的主动攻击。实验室预览仍使用自己的自动管道。
    [DefaultExecutionOrder(-900)]
    public sealed class PlayerChargeAttack : MonoBehaviour
    {
        public const string BindingPreference = "SpringUp.AttackKey";
        private readonly List<(BodyPart part, int slot, PipelineOrgan organ)> loadout = new();
        private readonly Queue<(BodyPart part, int slot, PipelineOrgan organ)> pending = new();
        private OrganPipelineController pipeline;
        private Func<bool> combatAllowed;
        private float baseCapacity, recoveryRate, delay;
        private bool waitForRelease = true, captureReady;
        private int mouseButton;
        private bool focused = true, captureRelease;
        public string BindingNotice { get; private set; } = "";
        public bool CaptureBlocksUI => IsRebinding || captureRelease || Time.frameCount == rebindFrame;
        private int rebindFrame = -1;
        private EventSystem capturedEvents;

        public float Charge { get; private set; }
        public float Capacity { get; private set; }
        public float RequiredCharge { get; private set; }
        public bool IsAttacking => pending.Count > 0;
        public bool IsRebinding { get; private set; }
        public Key AttackKey { get; private set; } = Key.F;
        public string KeyLabel => mouseButton == 1 ? "鼠标左键" : mouseButton == 2 ? "鼠标右键" : (Keyboard.current?[AttackKey].displayName ?? AttackKey.ToString()).ToUpperInvariant();
        private ButtonControl AttackControl => mouseButton == 1 ? Mouse.current?.leftButton : mouseButton == 2 ? Mouse.current?.rightButton : Keyboard.current?[AttackKey];
        public string Feedback { get; private set; } = "";
        public bool HasAttackOrgans => loadout.Count > 0;
        public bool Ready => HasAttackOrgans && Charge >= RequiredCharge && !IsAttacking && delay <= 0 && !IsRebinding;

        public void Configure(OrganPipelineController target, Func<bool> allowed, float capacity, float recovery)
        {
            pipeline = target;
            pipeline.AutomaticExecution = false;
            combatAllowed = allowed;
            baseCapacity = float.IsNaN(capacity) || float.IsInfinity(capacity) ? 100f : Mathf.Max(1, capacity);
            recoveryRate = float.IsNaN(recovery) || float.IsInfinity(recovery) ? 20f : Mathf.Max(0, recovery);
            string saved = PlayerPrefs.GetString(BindingPreference, "F");
            mouseButton = saved == "MouseLeft" ? 1 : saved == "MouseRight" ? 2 : 0;
            bool valid = mouseButton != 0 ? Mouse.current != null : Enum.TryParse(saved, out Key parsed) && IsBindable(parsed);
            AttackKey = Key.F;
            if (valid && mouseButton == 0) AttackKey = Enum.Parse<Key>(saved);
            if (!valid) { mouseButton = 0; PlayerPrefs.SetString(BindingPreference, "F"); PlayerPrefs.Save(); }
            RefreshLoadout();
            BindingNotice = valid ? "" : "旧攻击绑定不可用，已恢复 F";
        }

        public void RefreshLoadout()
        {
            CancelRound();
            loadout.Clear();
            RequiredCharge = 0;
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
                for (int s = 0; s < BodyRuntime.SlotCount; s++)
                {
                    var organ = pipeline.GetBody(part).GetSlot(s);
                    if (organ == null || !organ.Definition.CanAttack) continue;
                    loadout.Add((part, s, organ));
                    RequiredCharge += organ.Definition.ChargeCost;
                }
            // 配装更重时允许攒够整轮费用，避免高消耗装备永远无法发动。
            Capacity = Mathf.Max(baseCapacity, RequiredCharge);
            Charge = Mathf.Min(Charge, Capacity);
            Feedback = "";
        }

        public void BeginWave()
        {
            RefreshLoadout();
            delay = 0;
            Charge = Capacity;
            waitForRelease = true;
        }

        public void RestoreCharge(float value) => Charge = Mathf.Clamp(value, 0, Capacity);

        public void CancelRound()
        {
            while (pending.Count > 0)
                Charge = Mathf.Min(Capacity, Charge + pending.Dequeue().organ.Definition.ChargeCost);
            waitForRelease = true;
        }

        public bool TryAttack()
        {
            if (!isActiveAndEnabled || pipeline == null || !pipeline.isActiveAndEnabled
                || combatAllowed?.Invoke() != true || Time.timeScale <= 0 || IsRebinding) return false;
            if (IsAttacking || delay > 0) { Feedback = "本轮攻击进行中"; return false; }
            if (loadout.Count == 0) { Feedback = "请在休息期装备攻击器官"; return false; }
            if (Charge < RequiredCharge) { Feedback = "充能不足，等待恢复"; return false; }
            string target = pipeline.TargetSelector?.Invoke();
            if (string.IsNullOrEmpty(target) || (pipeline.TargetValidator != null && !pipeline.TargetValidator(target)))
            { Feedback = "范围内没有可攻击的敌人"; return false; }

            Charge -= RequiredCharge;
            foreach (var entry in loadout) pending.Enqueue(entry);
            Feedback = "";
            ExecuteNext();
            return true;
        }

        private void ExecuteNext()
        {
            var entry = pending.Dequeue();
            if (!ReferenceEquals(pipeline.GetBody(entry.part).GetSlot(entry.slot), entry.organ)
                || !pipeline.TryExecuteSlot(entry.part, entry.slot))
                Charge = Mathf.Min(Capacity, Charge + entry.organ.Definition.ChargeCost);
            delay = Mathf.Max(BodyRuntime.MinimumStepInterval,
                pipeline.StepIntervalProvider?.Invoke(entry.part) ?? pipeline.GetBody(entry.part).StepInterval);
            if (!IsAttacking && Feedback == "本轮攻击进行中") Feedback = "";
        }

        private void Update()
        {
            if (pipeline == null) return;
            if (captureRelease && !AnyPressed() && Time.frameCount != rebindFrame) captureRelease = false;
            bool capturing = CaptureBlocksUI;
            if (capturing && capturedEvents == null && EventSystem.current != null && EventSystem.current.enabled)
            { capturedEvents = EventSystem.current; capturedEvents.enabled = false; }
            else if (!capturing) RestoreEvents();
            if (IsRebinding) ReadRebind();
            if (!focused || combatAllowed?.Invoke() != true || !pipeline.isActiveAndEnabled || Time.timeScale <= 0)
            { CancelRound(); return; }
            Charge = Mathf.Min(Capacity, Charge + recoveryRate * Time.deltaTime);
            delay = Mathf.Max(0, delay - Time.deltaTime);
            if ((!IsAttacking && delay <= 0 && Feedback == "本轮攻击进行中")
                || (Charge >= RequiredCharge && Feedback == "充能不足，等待恢复")) Feedback = "";
            if (IsAttacking && delay <= 0) ExecuteNext();
            if (capturing || CaptureBlocksUI) { SuppressInput(); return; }
            var control = AttackControl;
            if (control == null) { SuppressInput(); return; }
            if ((Mouse.current?.leftButton.isPressed == true || Mouse.current?.rightButton.isPressed == true) && PointerOnControl())
            { waitForRelease = true; return; }
            if (waitForRelease)
            {
                if (!control.isPressed) waitForRelease = false;
                return;
            }
            if (control.isPressed && !IsAttacking && delay <= 0) TryAttack();
        }

        // Raycast now, before the EventSystem update; decorative HUD graphics do not block attacks.
        public static bool PointerOnControl()
        {
            if (EventSystem.current == null || Mouse.current == null) return false;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() }, hits);
            foreach (var hit in hits)
                if (hit.gameObject.GetComponentInParent<Selectable>() != null) return true;
            return false;
        }
        public void SuppressInput() { waitForRelease = true; }
        public void CancelRebind() { captureRelease = IsRebinding; IsRebinding = false; rebindFrame = Time.frameCount; SuppressInput(); }
        public void ToggleRebind()
        {
            if (IsRebinding) { CancelRebind(); return; }
            IsRebinding = true; captureReady = false; rebindFrame = Time.frameCount;
            CancelRound(); Feedback = "";
        }
        private static bool AnyPressed()
        {
            if (Keyboard.current != null)
                foreach (var key in Keyboard.current.allKeys) if (key.isPressed) return true;
            return Mouse.current?.leftButton.isPressed == true || Mouse.current?.rightButton.isPressed == true;
        }
        private void ReadRebind()
        {
            if (Time.frameCount == rebindFrame) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { CancelRebind(); return; }
            if (!captureReady) { captureReady = !AnyPressed(); return; }
            if (Mouse.current?.leftButton.wasPressedThisFrame == true) { SetMouseBinding(false); return; }
            if (Mouse.current?.rightButton.wasPressedThisFrame == true) { SetMouseBinding(true); return; }
            if (Keyboard.current == null) return;
            foreach (var key in Keyboard.current.allKeys)
            {
                if (!key.wasPressedThisFrame) continue;
                SetAttackKey(key.keyCode); return;
            }
        }
        public bool SetMouseBinding(bool right)
        {
            if (Mouse.current == null) { Feedback = "鼠标设备不可用"; return false; }
            mouseButton = right ? 2 : 1; SaveBinding(right ? "MouseRight" : "MouseLeft"); return true;
        }
        public bool SetAttackKey(Key key)
        {
            if (!IsBindable(key)) { Feedback = Conflict(key); return false; }
            mouseButton = 0; AttackKey = key; SaveBinding(key.ToString()); return true;
        }
        private void SaveBinding(string value)
        {
            PlayerPrefs.SetString(BindingPreference, value); PlayerPrefs.Save();
            CancelRebind(); Feedback = ""; BindingNotice = "";
        }
        public static string Conflict(Key key)
        {
            switch (key)
            {
                case Key.B: case Key.Tab: return "该键用于背包，请继续选择";
                case Key.E: return "E 用于商人交互，请继续选择";
                case Key.W: case Key.A: case Key.S: case Key.D:
                case Key.UpArrow: case Key.DownArrow: case Key.LeftArrow: case Key.RightArrow: return "该键用于移动，请继续选择";
                case Key.LeftShift: case Key.RightShift: return "该键用于疾跑，请继续选择";
                case Key.Escape: case Key.Enter: case Key.NumpadEnter: case Key.Space: return "该键用于界面控制，请继续选择";
                default: return "该键为系统修饰键或非法键，请继续选择";
            }
        }

        private static bool IsBindable(Key key)
        {
            if (!Enum.IsDefined(typeof(Key), key) || key == Key.None) return false;
            switch (key)
            {
                case Key.W: case Key.A: case Key.S: case Key.D:
                case Key.UpArrow: case Key.DownArrow: case Key.LeftArrow: case Key.RightArrow:
                case Key.LeftShift: case Key.RightShift: case Key.LeftCtrl: case Key.RightCtrl:
                case Key.LeftAlt: case Key.RightAlt: case Key.LeftMeta: case Key.RightMeta:
                case Key.B: case Key.E: case Key.Escape: case Key.Enter: case Key.NumpadEnter: case Key.Space: case Key.Tab:
                    return false;
                default: return true;
            }
        }

        private void OnApplicationFocus(bool value) { focused = value; CancelRound(); CancelRebind(); }
        private void OnApplicationPause(bool value) { CancelRound(); CancelRebind(); }

        private void RestoreEvents() { if (capturedEvents != null) capturedEvents.enabled = true; capturedEvents = null; }

        private void OnDisable()
        {
            IsRebinding = false;
            RestoreEvents();
            CancelRound();
        }
    }
}
