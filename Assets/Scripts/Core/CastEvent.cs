using System;
using UnityEngine;

namespace SpringUp.Organs
{
    // 效果中心是请求创建时的快照；null 表示缺失，世界原点是合法位置。
    // 伤害和目标不可变。同链攻击只共享 Context 中的防重复记录。
    public readonly struct CastEvent
    {
        public string TargetId { get; }
        public float Damage { get; }
        public string OriginDefinitionId { get; }
        public string OriginInstanceId { get; }
        public CastContext Context { get; }
        public StatusEffectData Status { get; }
        public AttackShape Shape { get; }
        public float Radius { get; }
        public BlackHoleData BlackHole { get; }
        public Vector2? EffectCenter { get; }
        public int ChainDepth => Context == null ? 0 : Context.Depth;
        public string ChainId => Context?.ChainId;

        public CastEvent(string targetId, float damage, string originDefinitionId,
            string originInstanceId, int chainDepth = 0, StatusEffectData status = null,
            AttackShape shape = AttackShape.SingleTarget, float radius = 0f, BlackHoleData blackHole = null, Vector2? effectCenter = null)
            : this(targetId, damage, originDefinitionId, originInstanceId, new CastContext(chainDepth), status, shape, radius, blackHole, effectCenter)
        {
        }

        public CastEvent(string targetId, float damage, string originDefinitionId,
            string originInstanceId, CastContext context, StatusEffectData status = null,
            AttackShape shape = AttackShape.SingleTarget, float radius = 0f, BlackHoleData blackHole = null, Vector2? effectCenter = null)
        {
            if (shape == AttackShape.SingleTarget && string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("攻击目标编号不能为空。", nameof(targetId));
            if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f
                || (shape != AttackShape.BlackHole && damage == 0f))
                throw new ArgumentOutOfRangeException(nameof(damage));
            if (string.IsNullOrWhiteSpace(originDefinitionId))
                throw new ArgumentException("器官配置编号不能为空。", nameof(originDefinitionId));
            if (string.IsNullOrWhiteSpace(originInstanceId))
                throw new ArgumentException("器官实例编号不能为空。", nameof(originInstanceId));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!Enum.IsDefined(typeof(AttackShape), shape)) throw new ArgumentOutOfRangeException(nameof(shape));
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (shape == AttackShape.BlackHole && blackHole == null) throw new ArgumentNullException(nameof(blackHole));

            TargetId = targetId;
            Damage = damage;
            OriginDefinitionId = originDefinitionId;
            OriginInstanceId = originInstanceId;
            Context = context;
            Status = status;
            Shape = shape;
            Radius = radius;
            BlackHole = blackHole;
            if (effectCenter.HasValue && !IsFinite(effectCenter.Value))
                throw new ArgumentOutOfRangeException(nameof(effectCenter));
            EffectCenter = effectCenter;
        }

        public static bool IsFinite(Vector2 position) => !float.IsNaN(position.x) && !float.IsInfinity(position.x)
            && !float.IsNaN(position.y) && !float.IsInfinity(position.y);
    }
}
