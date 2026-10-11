using System;

namespace SpringUp.Organs
{
    // 单目标攻击需要 TargetId；自身中心爆炸可留空，由接入方提供角色位置并展开命中。
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
        public int ChainDepth => Context == null ? 0 : Context.Depth;
        public string ChainId => Context?.ChainId;

        public CastEvent(string targetId, float damage, string originDefinitionId,
            string originInstanceId, int chainDepth = 0, StatusEffectData status = null,
            AttackShape shape = AttackShape.SingleTarget, float radius = 0f, BlackHoleData blackHole = null)
            : this(targetId, damage, originDefinitionId, originInstanceId, new CastContext(chainDepth), status, shape, radius, blackHole)
        {
        }

        public CastEvent(string targetId, float damage, string originDefinitionId,
            string originInstanceId, CastContext context, StatusEffectData status = null,
            AttackShape shape = AttackShape.SingleTarget, float radius = 0f, BlackHoleData blackHole = null)
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
        }
    }
}
