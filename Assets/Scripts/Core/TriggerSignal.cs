using System;

namespace SpringUp.Organs
{
    // 敌人已经死亡后发出的通知。保留原攻击，不重建它的链上下文。
    public readonly struct TriggerSignal
    {
        public TriggerOn Kind { get; }
        public CastEvent Cause { get; }

        public TriggerSignal(TriggerOn kind, CastEvent cause)
        {
            if (!Enum.IsDefined(typeof(TriggerOn), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (cause.Context == null || string.IsNullOrWhiteSpace(cause.TargetId))
                throw new ArgumentException("触发通知必须带上原攻击。", nameof(cause));
            Kind = kind;
            Cause = cause;
        }
    }
}
