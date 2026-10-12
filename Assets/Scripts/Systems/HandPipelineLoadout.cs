using System;
using System.Collections.Generic;
using SpringUp.Organs;

namespace Tideflesh.Systems
{
    /// <summary>
    /// 把手部已装备槽写进程序 A 的六槽管道。
    /// 槽号沿用库存的 1–6。管道里没有对应 id 的器官留空，并计入返回值。
    /// </summary>
    public static class HandPipelineLoadout
    {
        public static int Apply(
            BodyRuntime hand,
            Inventory inventory,
            IReadOnlyDictionary<string, SpringUp.Organs.OrganDefinition> pipelineOrgans)
        {
            if (hand == null) throw new ArgumentNullException(nameof(hand));
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (pipelineOrgans == null) throw new ArgumentNullException(nameof(pipelineOrgans));
            if (hand.Part != BodyPart.Hand)
                throw new ArgumentException("only the hand pipeline accepts this loadout", nameof(hand));

            var skipped = 0;
            for (var slot = 1; slot <= Inventory.SlotCounts["hand"]; slot++)
            {
                var instanceId = inventory.EquippedAt("hand", slot);
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
                if (previous == null || previous.InstanceId != instanceId || previous.Definition != definition)
                    hand.SetSlot(slot - 1, new SpringUp.Organs.OrganInstance(definition, instanceId));
            }

            return skipped;
        }
    }
}
