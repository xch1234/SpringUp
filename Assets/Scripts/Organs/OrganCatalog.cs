using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    // 显式引用唯一资源；调用方持有开局快照，运行时不依赖编辑器数据库。
    [CreateAssetMenu(menuName = "SpringUp/Organ Catalog")]
    public sealed class OrganCatalog : ScriptableObject
    {
        [SerializeField] private OrganConfig[] organs;
        public IReadOnlyList<OrganDefinition> CreateDefinitions()
        {
            if (organs == null || organs.Length != 7) throw new InvalidOperationException("器官目录必须引用七份手部配置。");
            var result = new List<OrganDefinition>();
            var ids = new HashSet<string>();
            foreach (var config in organs)
            {
                if (config == null) throw new InvalidOperationException("器官目录存在缺失配置。");
                if (!config.TryCreateDefinition(out var definition, out var error))
                    throw new InvalidOperationException(config.name + "：" + error);
                if (!ids.Add(definition.Id)) throw new InvalidOperationException("器官目录重复 ID：" + definition.Id);
                result.Add(definition);
            }
            return result.AsReadOnly();
        }
        public static OrganCatalog LoadDefault()
        {
            var catalog = Resources.Load<OrganCatalog>("OrganCatalog");
            if (catalog == null) throw new InvalidOperationException("缺少 Resources/OrganCatalog，无法启动真实配置预览。");
            return catalog;
        }
    }
}
