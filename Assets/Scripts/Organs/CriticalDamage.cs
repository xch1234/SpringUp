using System;
using UnityEngine;

namespace SpringUp.Organs
{
    public static class CriticalDamage
    {
        // One roll per direct target hit. Damage-over-time retains its original value.
        public static CastEvent Roll(CastEvent hit, float chance, float multiplier, float roll, out bool critical)
        {
            critical = hit.Damage > 0 && chance > 0 && roll < Mathf.Clamp01(chance);
            if (!critical) return hit;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 1)
                throw new ArgumentOutOfRangeException(nameof(multiplier));
            return new CastEvent(hit.TargetId, hit.Damage * multiplier, hit.OriginDefinitionId,
                hit.OriginInstanceId, hit.Context, hit.Status, hit.Shape, hit.Radius, hit.BlackHole, hit.EffectCenter);
        }
    }
}
