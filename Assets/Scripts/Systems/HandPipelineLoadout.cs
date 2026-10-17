using System;
using System.Collections.Generic;
using SpringUp.Organs;

namespace Tideflesh.Systems
{
    // 保留旧演示/外部调用入口；统一游玩使用下面的三部位同步入口。
    public static class HandPipelineLoadout
    {
        public static int Apply(BodyRuntime hand, Inventory inventory,
            IReadOnlyDictionary<string, SpringUp.Organs.OrganDefinition> pipelineOrgans)
            => PipelineLoadout.ApplyBody(hand, inventory, pipelineOrgans);
    }

    public static class PipelineLoadout
    {
        public static int Apply(OrganPipelineController pipeline, Inventory inventory,
            IReadOnlyDictionary<string, SpringUp.Organs.OrganDefinition> pipelineOrgans)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (pipelineOrgans == null) throw new ArgumentNullException(nameof(pipelineOrgans));
            var seen = new HashSet<string>();
            var existing = new Dictionary<string, SpringUp.Organs.OrganInstance>();
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
            {
                var body = pipeline.GetBody(part);
                var key = part.ToString().ToLowerInvariant();
                for (int slot = 0; slot < BodyRuntime.SlotCount; slot++)
                {
                    var id = inventory.EquippedAt(key, slot + 1);
                    if (id != null && !seen.Add(id)) throw new InventoryException("duplicate equipped instance");
                    var previous = body.GetSlot(slot);
                    if (previous != null) existing[previous.InstanceId] = previous;
                }
            }
            // 先释放旧位置，再写入新位置，允许从后遍历的部位移动到前面的部位。
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
                for (int slot = 0; slot < BodyRuntime.SlotCount; slot++) pipeline.GetBody(part).SetSlot(slot, null);
            int skipped = 0;
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
                skipped += ApplyBody(pipeline.GetBody(part), inventory, pipelineOrgans, existing);
            return skipped;
        }

        public static int ApplyBody(
            BodyRuntime hand,
            Inventory inventory,
            IReadOnlyDictionary<string, SpringUp.Organs.OrganDefinition> pipelineOrgans,
            IReadOnlyDictionary<string, SpringUp.Organs.OrganInstance> existing = null)
        {
            if (hand == null) throw new ArgumentNullException(nameof(hand));
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (pipelineOrgans == null) throw new ArgumentNullException(nameof(pipelineOrgans));
            string part = hand.Part.ToString().ToLowerInvariant();

            var skipped = 0;
            for (var slot = 1; slot <= Inventory.SlotCounts[part]; slot++)
            {
                var instanceId = inventory.EquippedAt(part, slot);
                if (instanceId == null)
                {
                    hand.SetSlot(slot - 1, null);
                    continue;
                }

                var organId = inventory.OrganOf(instanceId).id;
                if (!pipelineOrgans.TryGetValue(organId, out var definition))
                {
                    hand.SetSlot(slot - 1, null);
                    skipped++;
                    continue;
                }

                var previous = hand.GetSlot(slot - 1);
                if (existing != null && existing.TryGetValue(instanceId, out var saved)) previous = saved;
                if (previous == null || previous.InstanceId != instanceId || previous.Definition != definition)
                    previous = new SpringUp.Organs.OrganInstance(definition, instanceId);
                hand.SetSlot(slot - 1, previous);
            }

            return skipped;
        }
    }
}
