using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tideflesh.Systems
{
    public sealed class CatalogException : Exception
    {
        public string OrganId { get; }
        public string Reason { get; }

        public CatalogException(string organId, string reason)
            : base($"organ {organId}: {reason}")
        {
            OrganId = organId;
            Reason = reason;
        }
    }

    [Serializable]
    public class OrganStatusSpec
    {
        public string id;
        public float duration;
        public int max_stack;
    }

    [Serializable]
    public class OrganModifiers
    {
        public int unequipped_capacity_delta;
        public float marrow_gain_mult;
    }

    [Serializable]
    public class OrganDefinition
    {
        public string id;
        public string name;
        public string description;
        public string part;
        public string type;
        public string rarity;
        public int shop_price;
        public string icon;
        public float damage;
        public int count = 1;
        public float radius;
        public float pierce;
        public float speed;
        public float chance;
        public string trigger_on;
        public string trigger_target;
        public float rate_percent;
        public OrganStatusSpec status;
        public OrganModifiers modifiers;
        public string[] costs;

        public bool HasStatus => status != null && !string.IsNullOrEmpty(status.id);

        public float MarrowGainMult
        {
            get
            {
                if (modifiers == null || modifiers.marrow_gain_mult <= 0f)
                    return 1f;
                return modifiers.marrow_gain_mult;
            }
        }

        public int UnequippedCapacityDelta =>
            modifiers == null ? 0 : modifiers.unequipped_capacity_delta;
    }

    [Serializable]
    public class WaveDefinition
    {
        public int wave;
        public int duration_sec;
        public string[] guaranteed_drops;
        public string[] optional_drops;
        public int marrow_reward;
    }

    [Serializable]
    class OrganFile
    {
        public int version;
        public OrganDefinition[] organs;
    }

    [Serializable]
    class WaveFile
    {
        public WaveDefinition[] waves;
    }

    public static class Catalog
    {
        static readonly HashSet<string> Parts = new() { "head", "hand", "leg", "torso" };
        static readonly HashSet<string> Types = new() { "trigger", "executor", "torso_special" };
        static readonly HashSet<string> Rarities = new() { "common", "rare", "mechanical", "story" };
        static readonly HashSet<string> TriggerOn = new() { "hit", "kill", "hurt", "interval", "expire" };
        static readonly HashSet<string> TriggerTargets = new()
        {
            "SELF_SLOTS", "PART:head", "PART:hand", "PART:leg", "ALL_EMITTERS", "NONE"
        };

        public static Dictionary<string, OrganDefinition> LoadOrgans(string json)
        {
            var file = JsonUtility.FromJson<OrganFile>(json);
            if (file?.organs == null)
                throw new CatalogException("<unknown>", "missing organs list");

            var indexed = new Dictionary<string, OrganDefinition>();
            foreach (var organ in file.organs)
            {
                ValidateOrgan(organ);
                if (indexed.ContainsKey(organ.id))
                    throw new CatalogException(organ.id, "duplicate id");
                indexed[organ.id] = organ;
            }
            return indexed;
        }

        public static List<WaveDefinition> LoadWaves(
            string json,
            Dictionary<string, OrganDefinition> catalog)
        {
            var file = JsonUtility.FromJson<WaveFile>(json);
            if (file?.waves == null)
                throw new CatalogException("<unknown>", "missing waves list");

            foreach (var wave in file.waves)
            {
                if (wave.guaranteed_drops == null)
                    throw new CatalogException("<unknown>", "guaranteed_drops must be a list of strings");
                if (wave.optional_drops == null)
                    throw new CatalogException("<unknown>", "optional_drops must be a list of strings");
                foreach (var dropId in wave.guaranteed_drops)
                    RequireDrop(dropId, catalog);
                foreach (var dropId in wave.optional_drops)
                    RequireDrop(dropId, catalog);
            }
            return new List<WaveDefinition>(file.waves);
        }

        static void RequireDrop(string dropId, Dictionary<string, OrganDefinition> catalog)
        {
            if (string.IsNullOrEmpty(dropId) || !catalog.ContainsKey(dropId))
                throw new CatalogException(string.IsNullOrEmpty(dropId) ? "<unknown>" : dropId, "drop id not in catalog");
        }

        static void ValidateOrgan(OrganDefinition organ)
        {
            var organId = string.IsNullOrEmpty(organ.id) ? "<unknown>" : organ.id;
            if (string.IsNullOrEmpty(organ.id))
                throw new CatalogException(organId, "missing id");
            Require(organ.name, organId, "name");
            Require(organ.description, organId, "description");
            Require(organ.part, organId, "part");
            Require(organ.type, organId, "type");
            Require(organ.rarity, organId, "rarity");
            Require(organ.icon, organId, "icon");
            if (!Parts.Contains(organ.part))
                throw new CatalogException(organId, "invalid part");
            if (!Types.Contains(organ.type))
                throw new CatalogException(organId, "invalid type");
            if (!Rarities.Contains(organ.rarity))
                throw new CatalogException(organId, "invalid rarity");
            if (organ.type == "torso_special" && organ.part != "torso")
                throw new CatalogException(organId, "torso_special part must be torso");
            if (organ.type == "trigger")
            {
                Require(organ.trigger_on, organId, "trigger_on");
                Require(organ.trigger_target, organId, "trigger_target");
                if (!TriggerOn.Contains(organ.trigger_on))
                    throw new CatalogException(organId, "invalid trigger_on");
                if (!TriggerTargets.Contains(organ.trigger_target))
                    throw new CatalogException(organId, "invalid trigger_target");
                if (organ.trigger_on == "interval" && organ.rate_percent <= 0f)
                    throw new CatalogException(organId, "missing rate_percent");
                if (organ.rate_percent != 0f && (organ.rate_percent < 0.1f || organ.rate_percent > 1f))
                    throw new CatalogException(organId, "rate_percent out of range");
            }
            if (organ.type == "executor")
            {
                // damage/count/radius may be 0; presence is implied by type. M1 fists use radius 0.
            }
            if (organ.type == "torso_special")
            {
                if (organ.modifiers == null)
                    throw new CatalogException(organId, "missing modifiers");
                if (organ.costs == null)
                    throw new CatalogException(organId, "missing costs");
            }
        }

        static void Require(string value, string organId, string key)
        {
            if (string.IsNullOrEmpty(value))
                throw new CatalogException(organId, $"missing {key}");
        }
    }
}
