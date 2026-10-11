using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    // 战场提供一次位置快照。此类不依赖 DemoTarget，正式敌人也可提供相同资料。
    public readonly struct AttackTarget
    {
        public string Id { get; }
        public Vector2 Position { get; }
        public bool IsAlive { get; }
        public AttackTarget(string id, Vector2 position, bool isAlive)
        {
            Id = id;
            Position = position;
            IsAlive = isAlive;
        }
    }

    public static class AttackResolver
    {
        // 只决定本次命中名单，不扣血。名单在任何击杀回调之前固定下来。
        public static List<CastEvent> Resolve(CastEvent attack, IReadOnlyList<AttackTarget> targets,
            Vector2 explosionCenter, out Vector2 center)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            center = default;
            var hits = new List<CastEvent>();
            if (attack.Context == null) return hits;
            if (attack.Shape == AttackShape.BlackHole) return hits; // 持续位移由 BlackHoleBehaviour 处理。
            if (attack.Shape == AttackShape.SingleTarget)
            {
                for (int i = 0; i < targets.Count; i++)
                    if (Valid(targets[i]) && targets[i].Id == attack.TargetId)
                    {
                        center = targets[i].Position;
                        hits.Add(attack);
                        break;
                    }
                return hits;
            }

            // TNT 的中心由角色提供，与目标编号、列表顺序和敌人是否死亡无关。
            center = explosionCenter;
            if (float.IsNaN(center.x) || float.IsInfinity(center.x)
                || float.IsNaN(center.y) || float.IsInfinity(center.y)) return hits;
            var ids = new HashSet<string>();
            double radiusSquared = (double)attack.Radius * attack.Radius;
            for (int i = 0; i < targets.Count; i++)
            {
                AttackTarget target = targets[i];
                if (!Valid(target)) continue;
                double dx = (double)target.Position.x - center.x;
                double dy = (double)target.Position.y - center.y;
                if (dx * dx + dy * dy > radiusSquared || !ids.Add(target.Id)) continue;
                // 展开成单目标命中，防止接入方把每次扣血又当成一次爆炸。
                hits.Add(new CastEvent(target.Id, attack.Damage, attack.OriginDefinitionId,
                    attack.OriginInstanceId, attack.Context, attack.Status));
            }
            return hits;
        }

        private static bool Valid(AttackTarget target)
            => target.IsAlive && !string.IsNullOrWhiteSpace(target.Id)
                && !float.IsNaN(target.Position.x) && !float.IsInfinity(target.Position.x)
                && !float.IsNaN(target.Position.y) && !float.IsInfinity(target.Position.y);
    }
}
