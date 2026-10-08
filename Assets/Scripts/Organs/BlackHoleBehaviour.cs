using System;
using UnityEngine;

namespace SpringUp.Organs
{
    // 一个黑洞的运行状态。只计算位移，不认识场景敌人，也不扣血。
    public sealed class BlackHoleBehaviour
    {
        public CastEvent Cause { get; }
        public Vector2 Center { get; }
        public float Remaining { get; private set; }
        public bool IsActive => Remaining > 0f;

        public BlackHoleBehaviour(CastEvent cause, Vector2 center)
        {
            if (cause.Context == null || cause.Shape != AttackShape.BlackHole || cause.BlackHole == null)
                throw new ArgumentException("需要有效的黑洞请求。", nameof(cause));
            if (!Finite(center.x) || !Finite(center.y)) throw new ArgumentOutOfRangeException(nameof(center));
            Cause = cause;
            Center = center;
            Remaining = cause.BlackHole.Duration;
        }

        // 先对全部敌人计算，再调用 Tick；跨越到期的长帧只吸引剩余的有效时间。
        public Vector2 PullPosition(Vector2 position, float deltaTime)
        {
            ValidateTime(deltaTime);
            if (!IsActive || !Finite(position.x) || !Finite(position.y)) return position;
            double dx = (double)position.x - Center.x, dy = (double)position.y - Center.y;
            if (dx * dx + dy * dy > (double)Cause.Radius * Cause.Radius) return position;
            return Vector2.MoveTowards(position, Center, Cause.BlackHole.PullSpeed * Mathf.Min(deltaTime, Remaining));
        }

        public void Tick(float deltaTime)
        {
            ValidateTime(deltaTime);
            Remaining = Mathf.Max(0f, Remaining - deltaTime);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void ValidateTime(float value)
        {
            if (!Finite(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
