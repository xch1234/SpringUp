using System;

namespace SpringUp.Organs
{
    // 一个部位的六个槽。它不依赖场景、画面或敌人。
    public sealed class BodyRuntime
    {
        public const int SlotCount = 6;
        public const float MinimumStepInterval = 0.25f;

        private readonly OrganInstance[] slots = new OrganInstance[SlotCount];
        private readonly Func<string, bool> occupiedInOtherBody;
        private int cursor;
        private int occupiedCount;
        private double elapsed;
        private float stepInterval;
        private float cycleDuration;
        private double cycleClock, nextSlotTime;
        private float roundStep, roundPeriod;
        private int roundCursor, roundCount;
        private bool cycleInitialized;

        // Zero retains the legacy continuous-step mode used by standalone A demos.
        public float CycleDuration
        {
            get => cycleDuration;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                if ((value > 0) != (cycleDuration > 0)) ResetTiming();
                cycleDuration = value;
            }
        }

        public void ResetTiming()
        {
            elapsed = 0;
            cursor = 0;
            cycleInitialized = false;
            LastExecutedSlot = -1;
        }

        public BodyPart Part { get; }
        public int LastExecutedSlot { get; private set; } = -1;
        public int ExecutedStepCount { get; private set; }

        // 参数依次为：部位管道、槽位下标、器官实例。
        public event Action<BodyRuntime, int, OrganInstance> SlotExecuted;
        public event Action LoadoutChanged;

        public float StepInterval
        {
            get => stepInterval;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "步进间隔必须为有限正数，实际最短间隔为 0.25 秒。");
                stepInterval = Math.Max(MinimumStepInterval, value);
            }
        }

        public BodyRuntime(BodyPart part, float interval, Func<string, bool> occupiedInOtherBody = null)
        {
            if (!Enum.IsDefined(typeof(BodyPart), part))
                throw new ArgumentOutOfRangeException(nameof(part));
            Part = part;
            this.occupiedInOtherBody = occupiedInOtherBody;
            StepInterval = interval;
        }

        public OrganInstance GetSlot(int index)
        {
            ValidateIndex(index);
            return slots[index];
        }

        // Binding is positional: no skipping empty slots and no wrapping between 6 and 1.
        public bool IsTriggerBoundSlot(int index)
        {
            ValidateIndex(index);
            return index > 0 && slots[index - 1]?.Definition.Type == OrganType.Trigger;
        }

        public bool IsTriggerSource(int triggerSlot, string sourceInstanceId)
        {
            ValidateIndex(triggerSlot);
            var previous = triggerSlot > 0 ? slots[triggerSlot - 1] : null;
            return slots[triggerSlot]?.Definition.Type == OrganType.Trigger
                && previous?.Definition.CanAttack == true && previous.InstanceId == sourceInstanceId;
        }

        public OrganInstance GetTriggerSuccessor(int triggerSlot)
        {
            ValidateIndex(triggerSlot);
            var next = triggerSlot + 1 < SlotCount ? slots[triggerSlot + 1] : null;
            return slots[triggerSlot]?.Definition.Type == OrganType.Trigger
                && next?.Definition.CanAttack == true ? next : null;
        }

        public bool IsAutomaticSlot(int index)
        {
            ValidateIndex(index);
            return slots[index]?.Definition.Type == OrganType.Actuator && !IsTriggerBoundSlot(index);
        }

        private bool HasAutomaticSlots()
        {
            for (int i = 0; i < SlotCount; i++) if (IsAutomaticSlot(i)) return true;
            return false;
        }

        public void SetSlot(int index, OrganInstance organ)
        {
            ValidateIndex(index);
            if (organ != null)
            {
                if (!OrganEquipmentRules.CanEquip(organ.Definition.MountKind, Part.ToString().ToLowerInvariant()))
                    throw new ArgumentException("这个器官不能安装到该部位。", nameof(organ));
                if (occupiedInOtherBody?.Invoke(organ.InstanceId) == true)
                    throw new ArgumentException("同一个器官实例不能占用多个部位。", nameof(organ));
                for (int i = 0; i < SlotCount; i++)
                    if (i != index && slots[i]?.InstanceId == organ.InstanceId)
                        throw new ArgumentException("同一个器官实例不能占用两个槽。", nameof(organ));
            }

            bool changed = slots[index] != organ;
            if (slots[index] != null) occupiedCount--;
            slots[index] = organ;
            if (organ != null) occupiedCount++;
            if (occupiedCount == 0) { elapsed = 0; cycleInitialized = false; }
            if (changed) LoadoutChanged?.Invoke();
            // 装卸器官时保留游标位置。
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            if (!HasAutomaticSlots())
            {
                elapsed = 0;
                cycleInitialized = false;
                return;
            }

            if (cycleDuration > 0)
            {
                TickCycle(deltaTime);
                return;
            }

            elapsed += deltaTime;
            while (elapsed + .0000001 >= stepInterval && HasAutomaticSlots())
            {
                elapsed = Math.Max(0, elapsed - stepInterval);
                StepOnce();
            }
        }

        private void TickCycle(float deltaTime)
        {
            if (!cycleInitialized)
            {
                cycleInitialized = true;
                cycleClock = -stepInterval;
                BeginRound();
            }
            cycleClock += deltaTime;
            while (HasAutomaticSlots() && cycleClock + 0.0000001 >= nextSlotTime)
            {
                if (roundCursor == 0 && roundCount == 0)
                {
                    roundStep = stepInterval;
                    roundPeriod = cycleDuration;
                }
                while (roundCursor < SlotCount && !IsAutomaticSlot(roundCursor)) roundCursor++;
                if (roundCursor == SlotCount)
                {
                    // A round cannot overlap itself, even after extreme speed modifiers.
                    cycleClock -= Math.Max(roundPeriod, Math.Max(1, roundCount) * roundStep);
                    BeginRound();
                    continue;
                }
                int index = roundCursor++;
                var organ = slots[index];
                roundCount++;
                nextSlotTime += roundStep;
                LastExecutedSlot = index;
                ExecutedStepCount++;
                SlotExecuted?.Invoke(this, index, organ);
            }
        }

        private void BeginRound()
        {
            roundCursor = roundCount = 0;
            nextSlotTime = 0;
            // Attribute changes take effect at the next round boundary.
            roundStep = stepInterval;
            roundPeriod = cycleDuration;
        }

        // 触发链额外扫描一遍，既不移动正常游标，也不消耗正常计时。
        // 扫描开始时保存槽位，避免回调里的装卸操作改变这一遍的顺序。
        public void ExecuteAllSlots(Action<int, OrganInstance> execute)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            var snapshot = (OrganInstance[])slots.Clone();
            for (int i = 0; i < snapshot.Length; i++)
                if (snapshot[i] != null) execute(i, snapshot[i]);
        }

        private void StepOnce()
        {
            for (int visited = 0; visited < SlotCount; visited++)
            {
                int index = cursor;
                cursor = (cursor + 1) % SlotCount;
                OrganInstance organ = slots[index];
                if (!IsAutomaticSlot(index)) continue;

                LastExecutedSlot = index;
                ExecutedStepCount++;
                SlotExecuted?.Invoke(this, index, organ);
                return;
            }
        }

        private static void ValidateIndex(int index)
        {
            if (index < 0 || index >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(index), "槽位下标必须在 0 到 5 之间。");
        }
    }
}
