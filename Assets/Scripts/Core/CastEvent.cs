using System;

namespace SpringUp.Organs
{
    // 一次攻击的信息。目前只支持一个目标，不保存场景对象。
    // 伤害和目标不可变。同链攻击只共享 Context 中的防重复记录。
    public readonly struct CastEvent
    {
        public string TargetId { get; }
        public float Damage { get; }
        public string OriginDefinitionId { get; }
        public string OriginInstanceId { get; }
        public CastContext Context { get; }
        public int ChainDepth => Context == null ? 0 : Context.Depth;
        public string ChainId => Context?.ChainId;

        public CastEvent(string targetId, float damage, string originDefinitionId,
            string originInstanceId, int chainDepth = 0)
            : this(targetId, damage, originDefinitionId, originInstanceId, new CastContext(chainDepth))
        {
        }

        public CastEvent(string targetId, float damage, string originDefinitionId,
            string originInstanceId, CastContext context)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("攻击目标编号不能为空。", nameof(targetId));
            if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
                throw new ArgumentOutOfRangeException(nameof(damage));
            if (string.IsNullOrWhiteSpace(originDefinitionId))
                throw new ArgumentException("器官配置编号不能为空。", nameof(originDefinitionId));
            if (string.IsNullOrWhiteSpace(originInstanceId))
                throw new ArgumentException("器官实例编号不能为空。", nameof(originInstanceId));
            if (context == null) throw new ArgumentNullException(nameof(context));

            TargetId = targetId;
            Damage = damage;
            OriginDefinitionId = originDefinitionId;
            OriginInstanceId = originInstanceId;
            Context = context;
        }
    }
}
