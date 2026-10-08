using System;

namespace SpringUp.Organs
{
    // 每个敌人单独持有一份。暂定：同类状态刷新时间，只有减速叠层。
    public sealed class StatusEffectRuntime
    {
        private double time;
        private double stunUntil, slowUntil, bleedUntil, nextBleed;
        private int slowStacks;
        private StatusEffectData slow, bleed;
        private CastEvent bleedCause;

        public float StunRemaining => (float)Math.Max(0, stunUntil - time);
        // 减速显示距离下一次掉层的时间，不是全部层数消失的时间。
        public float SlowRemaining => slowStacks > 0 ? (float)Math.Max(0, slowUntil - time) : 0f;
        public float BleedRemaining => (float)Math.Max(0, bleedUntil - time);
        public int SlowStacks => slowStacks;
        public float SpeedMultiplier => time < stunUntil ? 0f : SlowMultiplier;
        private float SlowMultiplier => slow == null ? 1f : Math.Max(0f, 1f - SlowStacks * slow.SlowPerStack);

        // 概率只在成功的直接命中后判定。roll 由接入方提供，便于检查 0% 和 100%。
        public bool TryApply(CastEvent attack, float roll)
        {
            if (float.IsNaN(roll) || roll < 0f || roll > 1f) throw new ArgumentOutOfRangeException(nameof(roll));
            StatusEffectData effect = attack.Status;
            if (effect == null || attack.Context == null || effect.Chance <= 0f
                || (effect.Chance < 1f && roll >= effect.Chance)) return false;
            switch (effect.Kind)
            {
                case StatusEffectKind.Stun:
                    stunUntil = time + effect.Duration;
                    break;
                case StatusEffectKind.Slow:
                    slowStacks = Math.Min(SlowStacks + 1, effect.MaxStacks);
                    slow = effect;
                    slowUntil = time + effect.Duration;
                    break;
                case StatusEffectKind.Bleed:
                    // 刷新流血不重置下一次扣血，避免持续攻击让流血永远无法跳伤害。
                    if (bleed == null || time >= bleedUntil) nextBleed = time + effect.TickInterval;
                    bleed = effect;
                    bleedCause = attack;
                    bleedUntil = time + effect.Duration;
                    break;
            }
            return true;
        }

        // 本帧按状态计算的有效移动时间。跨越状态结束时刻也能恢复正确速度。
        public float MovementSeconds(float deltaTime)
        {
            ValidateDelta(deltaTime);
            double end = time + deltaTime;
            double cursor = time;
            double nextDecay = slowUntil;
            int stacks = slowStacks;
            double movement = 0;
            // 一帧可能跨过多次掉层，分段计算速度；这里只预览，不修改状态。
            while (cursor < end)
            {
                double segmentEnd = stacks > 0 ? Math.Min(end, nextDecay) : end;
                float multiplier = stacks > 0 ? Math.Max(0f, 1f - stacks * slow.SlowPerStack) : 1f;
                movement += Math.Max(0, segmentEnd - Math.Max(cursor, stunUntil)) * multiplier;
                cursor = segmentEnd;
                if (stacks > 0 && cursor >= nextDecay)
                {
                    stacks--;
                    nextDecay += slow.Duration;
                }
            }
            return (float)movement;
        }

        public void Tick(float deltaTime, Action<CastEvent> applyDamage, Func<bool> isAlive)
        {
            ValidateDelta(deltaTime);
            if (applyDamage == null) throw new ArgumentNullException(nameof(applyDamage));
            if (isAlive == null) throw new ArgumentNullException(nameof(isAlive));
            double end = time + deltaTime;
            if (!isAlive()) { Clear(); time = end; return; }
            // 在持续时间最后一刻到期的流血也结算；较长的一帧不会漏掉中间几次扣血。
            while (bleed != null && nextBleed <= end && nextBleed <= bleedUntil)
            {
                time = nextBleed;
                DecaySlow();
                StatusEffectData current = bleed;
                CastEvent cause = bleedCause;
                nextBleed += current.TickInterval;
                if (current.TickDamage > 0f)
                    applyDamage(new CastEvent(cause.TargetId, current.TickDamage,
                        cause.OriginDefinitionId, cause.OriginInstanceId, cause.Context));
                // 保留原链，但不携带 Status，避免流血反复施加自己。
                if (!isAlive()) { Clear(); break; }
            }
            time = end;
            if (time >= bleedUntil) { bleed = null; bleedCause = default; }
            DecaySlow();
        }

        private void DecaySlow()
        {
            // 每过一个 Duration 只掉一层；从原到期时刻继续计时，长帧也不会漏掉层数。
            while (slowStacks > 0 && time >= slowUntil)
            {
                slowStacks--;
                slowUntil += slow.Duration;
            }
            if (slowStacks == 0) slow = null;
        }

        public void Clear()
        {
            stunUntil = slowUntil = bleedUntil = time;
            slowStacks = 0;
            slow = bleed = null;
            bleedCause = default;
        }

        private static void ValidateDelta(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }
    }
}
