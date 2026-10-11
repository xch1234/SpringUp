using System;

namespace SpringUp.Organs
{
    // 黑洞参数快照，不保存运行中的倒计时。
    public sealed class BlackHoleData
    {
        public float Duration { get; }
        public float PullSpeed { get; }

        public BlackHoleData(float duration = 5f, float pullSpeed = 2f)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0.05f)
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (float.IsNaN(pullSpeed) || float.IsInfinity(pullSpeed) || pullSpeed < 0f)
                throw new ArgumentOutOfRangeException(nameof(pullSpeed));
            Duration = duration;
            PullSpeed = pullSpeed;
        }
    }
}
