using System;
using UnityEngine;

namespace SpringUp.Organs
{
    // 读取配置并生成攻击信息。这里不扣血，也不播放特效。
    public static class ParameterisedBehaviour
    {
        public static bool TryCreateAttack(OrganInstance organ, string targetId, out CastEvent attack,
            CastContext context = null, Vector2? effectCenter = null)
        {
            if (organ == null) throw new ArgumentNullException(nameof(organ));
            attack = default;
            OrganDefinition definition = organ.Definition;
            if (definition.Type != OrganType.Actuator || (definition.Damage <= 0f && definition.Shape != AttackShape.BlackHole)
                || string.IsNullOrWhiteSpace(targetId))
                return false;

            if ((definition.Shape != AttackShape.SingleTarget && !effectCenter.HasValue)
                || (effectCenter.HasValue && !CastEvent.IsFinite(effectCenter.Value))) return false;

            attack = new CastEvent(targetId, definition.Damage, definition.Id, organ.InstanceId,
                context ?? new CastContext(), definition.Status, definition.Shape, definition.Radius, definition.BlackHole, effectCenter);
            return true;
        }

        public static bool Matches(TriggerSignal signal, OrganDefinition definition)
            => signal.Cause.Context != null && definition.Type == OrganType.Trigger
                && definition.TriggerOn == signal.Kind;
    }
}
