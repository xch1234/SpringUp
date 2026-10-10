using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    // 检查数据只在检查期间创建，不需要在 Data 中保存第二套器官资源。
    public static class OrganTestFactory
    {
        public static void AttachStatus(OrganConfig config, HandOrganKind kind, ICollection<Object> owned,
            float chance = 100f, float duration = 2f, float bleedDamage = 2f)
        {
            if (kind != HandOrganKind.SteelPipe && kind != HandOrganKind.RustKnife && kind != HandOrganKind.SlimeGland) return;
            var status = ScriptableObject.CreateInstance<StatusEffectConfig>(); owned.Add(status);
            var data = new SerializedObject(status);
            data.FindProperty("kind").intValue = (int)(kind == HandOrganKind.SteelPipe ? StatusEffectKind.Stun
                : kind == HandOrganKind.RustKnife ? StatusEffectKind.Bleed : StatusEffectKind.Slow);
            data.FindProperty("chancePercent").floatValue = chance;
            data.FindProperty("duration").floatValue = duration;
            data.FindProperty("tickDamage").floatValue = bleedDamage;
            data.ApplyModifiedPropertiesWithoutUndo();
            var organ = new SerializedObject(config); organ.FindProperty("statusEffect").objectReferenceValue = status;
            organ.ApplyModifiedPropertiesWithoutUndo();
        }

        public static OrganConfig CreateCombo(HandOrganKind kind, ICollection<Object> owned)
        {
            var config = ScriptableObject.CreateInstance<OrganConfig>(); owned.Add(config);
            var data = new SerializedObject(config);
            data.FindProperty("kind").intValue = (int)kind;
            data.FindProperty("damage").floatValue = kind == HandOrganKind.Fist || kind == HandOrganKind.Tnt ? 5f
                : kind == HandOrganKind.SteelPipe ? 2f : 1f;
            data.ApplyModifiedPropertiesWithoutUndo();
            AttachStatus(config, kind, owned, 100f, kind == HandOrganKind.RustKnife ? 3f : 2f, 1f);
            return config;
        }
    }
}
