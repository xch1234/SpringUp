using System;
using System.Collections.Generic;

namespace SpringUp.Organs
{
    // 每次普通攻击创建一条新链。追加攻击沿用同一条链的防重复记录。
    // 深度属于每个分支，不使用全局计数，以免不同攻击互相影响。
    public sealed class CastContext
    {
        public const int MaxChainDepth = 4;
        private readonly HashSet<string> triggeredInstances;

        public string ChainId { get; }
        public int Depth { get; }

        public CastContext(int depth = 0)
        {
            if (depth < 0 || depth > MaxChainDepth)
                throw new ArgumentOutOfRangeException(nameof(depth));
            ChainId = Guid.NewGuid().ToString("N");
            Depth = depth;
            triggeredInstances = new HashSet<string>();
        }

        private CastContext(CastContext parent)
        {
            ChainId = parent.ChainId;
            Depth = parent.Depth + 1;
            triggeredInstances = parent.triggeredInstances;
        }

        public bool TryEnterTrigger(string instanceId, out CastContext child, out TriggerBlockReason reason)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("触发器实例编号不能为空。", nameof(instanceId));
            child = null;
            if (Depth >= MaxChainDepth)
            {
                reason = TriggerBlockReason.DepthLimit;
                return false;
            }
            if (!triggeredInstances.Add(instanceId))
            {
                reason = TriggerBlockReason.AlreadyTriggered;
                return false;
            }

            // 必须先登记，再执行攻击。否则即时击杀可能再次发动同一触发器。
            child = new CastContext(this);
            reason = TriggerBlockReason.None;
            return true;
        }
    }
}
