using System;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Laboratory;
using SpringUp.Organs;
using PipelineDefinition = SpringUp.Organs.OrganDefinition;
namespace Tideflesh.Systems
{
    // Shared validated snapshot. No scene objects or UI lifecycle.
    public sealed class RunConfiguration
    {
        public Dictionary<string, PipelineDefinition> Definitions { get; } = new();
        public Dictionary<string, OrganDefinition> Organs { get; }
        public List<WaveDefinition> Waves { get; }
        public RunConfiguration(string organsJson, string wavesJson, OrganCatalog organCatalog)
        {
            var definitions = Definitions;
            foreach (var definition in organCatalog.CreateDefinitions()) definitions.Add(definition.Id, definition);
            var catalog = Catalog.LoadOrgans(organsJson);
            foreach (var definition in definitions.Values)
            {
                if (!catalog.TryGetValue(definition.Id, out var entry) || entry.MountKind != definition.MountKind
                    || entry.type != (definition.Type == OrganType.Torso ? "torso_special" : definition.Type == OrganType.Trigger ? "trigger" : "executor"))
                    throw new InvalidOperationException("库存图鉴与战斗定义不匹配：" + definition.Id);
                // JSON 保留经济与掉落数据；运行时的战斗字段统一投影自 A 的资源快照。
                entry.damage = definition.Damage;
                entry.radius = definition.Radius;
                entry.chance = definition.Status != null ? definition.Status.Chance * 100f
                    : definition.Type == OrganType.Trigger ? 100f : 0f;
                entry.speed = definition.Projectile?.Speed ?? definition.BlackHole?.PullSpeed ?? 0f;
                entry.description = new LaboratoryItem("definition", definition).Description;
                if (definition.Status != null)
                    entry.status = new OrganStatusSpec { id = definition.Status.Kind.ToString().ToLowerInvariant(),
                        duration = definition.Status.Duration, max_stack = definition.Status.MaxStacks };
                else if (definition.BlackHole != null)
                    entry.status = new OrganStatusSpec { id = "collapse", duration = definition.BlackHole.Duration, max_stack = 1 };
            }
            var waves = Catalog.LoadWaves(wavesJson, catalog);
            // 七波闭环的临时奖励：尚未实现的蜘蛛巢换成可用 TNT，不修改原波次表。
            foreach (var wave in waves)
            {
                wave.guaranteed_drops = wave.guaranteed_drops.Select(id => id == "spider_nest" ? "tnt" : id).ToArray();
                foreach (var id in wave.guaranteed_drops.Concat(wave.optional_drops))
                    if (!definitions.ContainsKey(id)) throw new InvalidOperationException("奖励缺少战斗定义：" + id);
            }
            Organs = catalog;
            Waves = waves;
        }
        public RunController CreateRun() => new RunController(Organs, Waves, null);
    }
}
