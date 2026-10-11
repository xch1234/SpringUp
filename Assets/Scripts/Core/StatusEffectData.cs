using System;

namespace SpringUp.Organs
{
    public enum StatusEffectKind { Stun, Bleed, Slow }

    // 一次攻击携带的不可变状态资料。目标上的计时和层数另存，不会改写这份资料。
    public sealed class StatusEffectData
    {
        public StatusEffectKind Kind { get; }
        public float Chance { get; }
        public float Duration { get; }
        public float TickDamage { get; }
        public float TickInterval { get; }
        public float SlowPerStack { get; }
        public int MaxStacks { get; }

        public StatusEffectData(StatusEffectKind kind, float chance, float duration,
            float tickDamage = 0f, float tickInterval = 1f, float slowPerStack = 0f, int maxStacks = 1)
        {
            if (!Enum.IsDefined(typeof(StatusEffectKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Check(chance, 0f, 1f, nameof(chance));
            Check(duration, 0.05f, float.MaxValue, nameof(duration));
            Check(tickDamage, 0f, float.MaxValue, nameof(tickDamage));
            Check(tickInterval, 0.05f, float.MaxValue, nameof(tickInterval));
            Check(slowPerStack, 0f, 1f, nameof(slowPerStack));
            if (maxStacks < 1 || maxStacks > 5) throw new ArgumentOutOfRangeException(nameof(maxStacks));
            Kind = kind;
            Chance = chance;
            Duration = duration;
            TickDamage = tickDamage;
            TickInterval = tickInterval;
            SlowPerStack = slowPerStack;
            MaxStacks = maxStacks;
        }

        private static void Check(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, "状态数值超出有效范围。");
        }
    }
}
