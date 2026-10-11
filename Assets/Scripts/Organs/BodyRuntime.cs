using System;

namespace SpringUp.Organs
{
    // 一个部位的六个槽。它不依赖场景、画面或敌人。
    public sealed class BodyRuntime
    {
        public const int SlotCount = 6;
        public const float MinimumStepInterval = 0.05f;

        private readonly OrganInstance[] slots = new OrganInstance[SlotCount];
        private int cursor;
        private int occupiedCount;
        private double elapsed;
        private float stepInterval;

        public BodyPart Part { get; }
        public int LastExecutedSlot { get; private set; } = -1;
        public int ExecutedStepCount { get; private set; }

        // 参数依次为：部位管道、槽位下标、器官实例。
        public event Action<BodyRuntime, int, OrganInstance> SlotExecuted;

        public float StepInterval
        {
            get => stepInterval;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < MinimumStepInterval)
                    throw new ArgumentOutOfRangeException(nameof(value), "步进间隔至少为 0.05 秒。");
                stepInterval = value;
            }
        }

        public BodyRuntime(BodyPart part, float interval)
        {
            if (!Enum.IsDefined(typeof(BodyPart), part))
                throw new ArgumentOutOfRangeException(nameof(part));
            Part = part;
            StepInterval = interval;
        }

        public OrganInstance GetSlot(int index)
        {
            ValidateIndex(index);
            return slots[index];
        }

        public void SetSlot(int index, OrganInstance organ)
        {
            ValidateIndex(index);
            if (organ != null)
            {
                if (organ.Definition.Part != Part)
                    throw new ArgumentException("这个器官不能安装到该部位。", nameof(organ));
                for (int i = 0; i < SlotCount; i++)
                    if (i != index && ReferenceEquals(slots[i], organ))
                        throw new ArgumentException("同一个器官实例不能占用两个槽。", nameof(organ));
            }

            if (slots[index] != null) occupiedCount--;
            slots[index] = organ;
            if (organ != null) occupiedCount++;
            if (occupiedCount == 0) elapsed = 0;
            // 装卸器官时保留游标位置。
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            if (occupiedCount == 0)
            {
                elapsed = 0;
                return;
            }

            elapsed += deltaTime;
            while (elapsed >= stepInterval && occupiedCount > 0)
            {
                elapsed -= stepInterval;
                StepOnce();
            }
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
                if (organ == null) continue;

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
