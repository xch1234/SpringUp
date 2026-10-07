using System;

namespace SpringUp.Organs
{
    // 实际装入槽位的器官。配置相同，实例编号也不同。
    public sealed class OrganInstance
    {
        public string InstanceId { get; } = Guid.NewGuid().ToString("N");
        public OrganDefinition Definition { get; }

        public OrganInstance(OrganDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }
    }
}
