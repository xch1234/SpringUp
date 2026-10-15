using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEngine;
using UnityEngine.InputSystem;
using PipelineOrgan = SpringUp.Organs.OrganInstance;

namespace Tideflesh.Systems
{
    // 统一游玩入口的主动攻击。实验室预览仍使用自己的自动管道。
    public sealed class PlayerChargeAttack : MonoBehaviour
    {
        public const string BindingPreference = "SpringUp.AttackKey";
        private readonly List<(BodyPart part, int slot, PipelineOrgan organ)> loadout = new();
        private readonly Queue<(BodyPart part, int slot, PipelineOrgan organ)> pending = new();
        private OrganPipelineController pipeline;
        private Func<bool> combatAllowed;
        private float baseCapacity, recoveryRate, delay;
        private bool waitForRelease;
        private int rebindFrame;

        public float Charge { get; private set; }
        public float Capacity { get; private set; }
        public float RequiredCharge { get; private set; }
        public bool IsAttacking => pending.Count > 0;
        public bool IsRebinding { get; private set; }
        public Key AttackKey { get; private set; } = Key.F;
        public string KeyLabel => (Keyboard.current?[AttackKey].displayName ?? AttackKey.ToString()).ToUpperInvariant();
        public string Feedback { get; private set; } = "";
        public bool HasAttackOrgans => loadout.Count > 0;
        public bool Ready => HasAttackOrgans && Charge >= RequiredCharge && !IsAttacking && !IsRebinding;

        public void Configure(OrganPipelineController target, Func<bool> allowed, float capacity, float recovery)
        {
            pipeline = target;
            pipeline.AutomaticExecution = false;
            combatAllowed = allowed;
            baseCapacity = float.IsNaN(capacity) || float.IsInfinity(capacity) ? 100f : Mathf.Max(1, capacity);
            recoveryRate = float.IsNaN(recovery) || float.IsInfinity(recovery) ? 20f : Mathf.Max(0, recovery);
            if (Enum.TryParse(PlayerPrefs.GetString(BindingPreference, "F"), out Key saved) && IsBindable(saved))
                AttackKey = saved;
            RefreshLoadout();
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
            Charge = Capacity;
            waitForRelease = true;
        }

        public void RestoreCharge(float value) => Charge = Mathf.Clamp(value, 0, Capacity);

        public void CancelRound()
        {
            while (pending.Count > 0)
                Charge = Mathf.Min(Capacity, Charge + pending.Dequeue().organ.Definition.ChargeCost);
            delay = 0;
        }

        public bool TryAttack()
        {
            if (!isActiveAndEnabled || pipeline == null || !pipeline.isActiveAndEnabled
                || combatAllowed?.Invoke() != true || Time.timeScale <= 0 || IsRebinding) return false;
            if (IsAttacking) { Feedback = "本轮攻击进行中"; return false; }
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
            bool capturing = IsRebinding;
            if (capturing) ReadRebind();
            if (combatAllowed?.Invoke() != true || !pipeline.isActiveAndEnabled || Time.timeScale <= 0)
            { CancelRound(); return; }

            Charge = Mathf.Min(Capacity, Charge + recoveryRate * Time.deltaTime);
            if (Charge >= RequiredCharge && Feedback == "充能不足，等待恢复") Feedback = "";
            if (IsAttacking)
            {
                delay -= Time.deltaTime;
                if (delay <= 0) ExecuteNext();
            }
            if (capturing || IsRebinding || Keyboard.current == null) return;
            var key = Keyboard.current[AttackKey];
            if (waitForRelease)
            {
                if (!key.isPressed) waitForRelease = false;
                return;
            }
            if (key.wasPressedThisFrame) TryAttack();
        }

        public void ToggleRebind()
        {
            IsRebinding = !IsRebinding;
            rebindFrame = Time.frameCount;
            waitForRelease = true;
            Feedback = "";
        }

        private void ReadRebind()
        {
            if (Time.frameCount == rebindFrame || Keyboard.current == null) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            { IsRebinding = false; return; }
            foreach (var key in Keyboard.current.allKeys)
            {
                if (!key.wasPressedThisFrame) continue;
                if (!SetAttackKey(key.keyCode)) Feedback = "请选择其他键（移动、疾跑和界面操作键已占用）";
                return;
            }
        }

        public bool SetAttackKey(Key key)
        {
            if (!IsBindable(key)) return false;
            AttackKey = key;
            PlayerPrefs.SetString(BindingPreference, key.ToString());
            PlayerPrefs.Save();
            IsRebinding = false;
            waitForRelease = true;
            Feedback = "";
            return true;
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
                case Key.Escape: case Key.Enter: case Key.NumpadEnter: case Key.Space: case Key.Tab:
                    return false;
                default: return true;
            }
        }

        private void OnDisable()
        {
            IsRebinding = false;
            CancelRound();
        }
    }
}
