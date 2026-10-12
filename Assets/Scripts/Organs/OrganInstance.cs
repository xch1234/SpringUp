using System;

namespace SpringUp.Organs
{
    // 实际装入槽位的器官。配置相同，实例编号也不同。
    public sealed class OrganInstance
    {
        public string InstanceId { get; }
        public OrganDefinition Definition { get; }

        public OrganInstance(OrganDefinition definition, string instanceId = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (instanceId != null && string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException(nameof(instanceId));
            InstanceId = instanceId ?? Guid.NewGuid().ToString("N");
        }
    }
}
