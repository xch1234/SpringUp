using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    // Names retained only for historical test fixtures, not production configuration.
    public enum HandOrganKind { Fist, SteelPipe, MultiTentacle, RustKnife, SlimeGland, CollapseBody, Tnt }
    public static class OrganTestFactory
    {
        public static readonly string[] Ids = { "fist", "steel_pipe", "multi_tentacle", "rust_knife", "slime_gland", "collapse_body", "tnt" };
        public static string AssetPath(string name) => "Assets/Data/Organs/" + (name == "MultiTentacle" ? "Triggers/" : "Actuators/") + name + ".asset";
        public static StatusEffectConfig Status(OrganConfig config) => (config.Actuator as DirectActuatorConfig)?.StatusEffect;
        public static SerializedObject Edit(OrganConfig config) => new SerializedObject(config.Actuator != null ? (Object)config.Actuator : config.Trigger);

        public static OrganConfig Create(HandOrganKind kind, ICollection<Object> owned, float damage = 5)
        {
            var config = ScriptableObject.CreateInstance<OrganConfig>(); owned.Add(config);
            bool trigger = kind == HandOrganKind.MultiTentacle;
            ScriptableObject behaviour = trigger ? (ScriptableObject)ScriptableObject.CreateInstance<OrganTriggerConfig>() : ScriptableObject.CreateInstance<DirectActuatorConfig>();
            owned.Add(behaviour);
            var data = new SerializedObject(behaviour);
            data.FindProperty("organId").stringValue = Enum.IsDefined(typeof(HandOrganKind), kind) ? Ids[(int)kind] : "invalid";
            data.FindProperty("displayName").stringValue = kind.ToString();
            if (trigger)
            {
                data.FindProperty("condition").intValue = (int)TriggerOn.Kill;
                data.FindProperty("triggerTarget").intValue = (int)TriggerTargetKind.SelfSlots;
                data.FindProperty("killThreshold").intValue = 1;
            }
            else
            {
                data.FindProperty("damage").floatValue = damage;
                data.FindProperty("shape").intValue = !Enum.IsDefined(typeof(HandOrganKind), kind) ? 999
                    : (int)(kind == HandOrganKind.Tnt ? AttackShape.Explosion : kind == HandOrganKind.CollapseBody ? AttackShape.BlackHole : AttackShape.SingleTarget);
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            var wrapper = new SerializedObject(config);
            wrapper.FindProperty(trigger ? "trigger" : "actuator").objectReferenceValue = behaviour;
            wrapper.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }
        public static void AttachStatus(OrganConfig config, HandOrganKind kind, ICollection<Object> owned,
            float chance = 100f, float duration = 2f, float bleedDamage = 2f)
        {
            if (kind != HandOrganKind.SteelPipe && kind != HandOrganKind.RustKnife && kind != HandOrganKind.SlimeGland) return;
            var status = ScriptableObject.CreateInstance<StatusEffectConfig>(); owned.Add(status);
            var data = new SerializedObject(status);
            data.FindProperty("kind").intValue = (int)(kind == HandOrganKind.SteelPipe ? StatusEffectKind.Stun : kind == HandOrganKind.RustKnife ? StatusEffectKind.Bleed : StatusEffectKind.Slow);
            data.FindProperty("chancePercent").floatValue = chance;
            data.FindProperty("duration").floatValue = duration;
            data.FindProperty("tickDamage").floatValue = bleedDamage;
            data.ApplyModifiedPropertiesWithoutUndo();
            var organ = Edit(config); organ.FindProperty("statusEffect").objectReferenceValue = status;
            organ.ApplyModifiedPropertiesWithoutUndo();
        }
        public static OrganConfig CreateCombo(HandOrganKind kind, ICollection<Object> owned)
        {
            var config = Create(kind, owned, kind == HandOrganKind.Fist || kind == HandOrganKind.Tnt ? 5f : kind == HandOrganKind.SteelPipe ? 2f : 1f);
            AttachStatus(config, kind, owned, 100f, kind == HandOrganKind.RustKnife ? 3f : 2f, 1f);
            return config;
        }
        // Deep-clone the behaviour too: changing a wrapper clone must not mutate the source asset.
        public static OrganConfig Clone(OrganConfig original)
        {
            var config = Object.Instantiate(original);
            var edit = new SerializedObject(config);
            var field = config.Actuator != null ? "actuator" : config.Trigger != null ? "trigger" : "torso";
            edit.FindProperty(field).objectReferenceValue = Object.Instantiate(edit.FindProperty(field).objectReferenceValue);
            edit.ApplyModifiedPropertiesWithoutUndo(); return config;
        }
        public static void DestroyClone(OrganConfig config)
        {
            if (config == null) return;
            Object.DestroyImmediate(config.Actuator != null ? (Object)config.Actuator : config.Trigger != null ? (Object)config.Trigger : config.Torso);
            Object.DestroyImmediate(config);
        }
    }
}
