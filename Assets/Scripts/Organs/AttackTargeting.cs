using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    // 索敌距离与器官的效果半径无关。目标由战场/演示入口提供，不能跨场景扫描。
    public static class AttackTargeting
    {
        public const float DefaultRange = 3f;

        public static bool IsInRange(AttackTarget target, Vector2 origin, float range)
        {
            if (!target.IsAlive || string.IsNullOrWhiteSpace(target.Id)
                || !Finite(origin.x) || !Finite(origin.y)
                || !Finite(target.Position.x) || !Finite(target.Position.y)
                || !Finite(range) || range < 0f) return false;
            double dx = (double)target.Position.x - origin.x;
            double dy = (double)target.Position.y - origin.y;
            return dx * dx + dy * dy <= (double)range * range;
        }

        public static string SelectNearest(IReadOnlyList<AttackTarget> targets, Vector2 origin, float range)
        {
            string selected = null;
            double nearest = double.PositiveInfinity;
            if (targets == null) return null;
            foreach (var target in targets)
            {
                if (!IsInRange(target, origin, range)) continue;
                double dx = (double)target.Position.x - origin.x;
                double dy = (double)target.Position.y - origin.y;
                double distance = dx * dx + dy * dy;
                // 同距保持候选顺序，避免同一批目标之间来回切换。
                if (distance >= nearest) continue;
                nearest = distance;
                selected = target.Id;
            }
            return selected;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
