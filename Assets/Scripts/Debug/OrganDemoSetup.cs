using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    public enum OrganDemoPreset
    {
        [InspectorName("综合组合")] Combination,
        [InspectorName("拳头与两个触手")] DoubleTentacle,
        [InspectorName("拳头")] Fist,
        [InspectorName("眩晕、流血、逐层减速")] Statuses,
        [InspectorName("TNT 范围爆炸")] Explosion,
        [InspectorName("黑洞吸引")] BlackHole,
        [InspectorName("手动设置 HandOrganDemo")] Manual
    }

    // 只安排演示装备与敌人位置；所有模式都引用同一套器官资源。
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HandOrganDemo))]
    public sealed class OrganDemoSetup : MonoBehaviour
    {
        [SerializeField, Tooltip("停止 Play 后选择。手动模式使用 HandOrganDemo 的 Slots 和场景已有设置。")]
        private OrganDemoPreset preset;
        [SerializeField, Tooltip("七种器官各一份，来自 Data/Organs/Hand。")]
        private OrganConfig[] organs = new OrganConfig[7];
        [SerializeField, Tooltip("三个演示敌人，依次为 A、B、C。")]
        private DemoTarget[] enemies = new DemoTarget[3];

        private void Awake()
        {
            if (!ConfigurePreset(out string error))
            {
                GetComponent<HandOrganDemo>().enabled = false;
                Debug.LogWarning("[器官演示] " + error, this);
            }
        }

        // 正常运行与检查共用；不向状态/器官资源写入测试数值。
        public bool ConfigurePreset(out string error)
        {
            error = null;
            if (!Enum.IsDefined(typeof(OrganDemoPreset), preset)) { error = "演示模式无效。"; return false; }
            if (preset == OrganDemoPreset.Manual) return true;
            var library = new Dictionary<HandOrganKind, OrganConfig>();
            if (organs != null)
                foreach (OrganConfig organ in organs)
                {
                    if (organ == null || library.ContainsKey(organ.Kind)) { error = "器官目录存在空项或重复种类。"; return false; }
                    library.Add(organ.Kind, organ);
                }
            foreach (HandOrganKind kind in Enum.GetValues(typeof(HandOrganKind)))
                if (!library.ContainsKey(kind)) { error = "缺少器官配置：" + kind; return false; }
            if (enemies == null || enemies.Length != 3 || enemies[0] == null || enemies[1] == null || enemies[2] == null
                || enemies[0] == enemies[1] || enemies[0] == enemies[2] || enemies[1] == enemies[2])
            { error = "请指定三个不同的演示敌人。"; return false; }

            HandOrganKind[] kinds;
            switch (preset)
            {
                case OrganDemoPreset.DoubleTentacle:
                    kinds = new[] { HandOrganKind.Fist, HandOrganKind.RustKnife, HandOrganKind.SlimeGland,
                        HandOrganKind.Tnt, HandOrganKind.MultiTentacle, HandOrganKind.MultiTentacle }; break;
                case OrganDemoPreset.Fist: kinds = new[] { HandOrganKind.Fist }; break;
                case OrganDemoPreset.Statuses:
                    kinds = new[] { HandOrganKind.SteelPipe, HandOrganKind.RustKnife, HandOrganKind.SlimeGland,
                        HandOrganKind.SlimeGland, HandOrganKind.SlimeGland }; break;
                case OrganDemoPreset.Explosion: kinds = new[] { HandOrganKind.Tnt }; break;
                case OrganDemoPreset.BlackHole: kinds = new[] { HandOrganKind.CollapseBody }; break;
                default:
                    kinds = new[] { HandOrganKind.CollapseBody, HandOrganKind.SteelPipe, HandOrganKind.RustKnife,
                        HandOrganKind.SlimeGland, HandOrganKind.Tnt, HandOrganKind.MultiTentacle }; break;
            }
            var slots = new OrganConfig[BodyRuntime.SlotCount];
            for (int i = 0; i < kinds.Length; i++) slots[i] = library[kinds[i]];
            GetComponent<HandOrganDemo>().SetLoadout(slots, enemies);
            GetComponent<OrganPipelineController>().StepInterval = preset == OrganDemoPreset.BlackHole ? 6f : 1f;
            float[] offsets = preset == OrganDemoPreset.Explosion ? new[] { 0f, 1f, 3.5f }
                : preset == OrganDemoPreset.BlackHole ? new[] { 2f, -2f, 4f }
                : preset == OrganDemoPreset.DoubleTentacle ? new[] { 1.5f, 1f, 4.5f } : new[] { 1.5f, 2.5f, 4.5f };
            for (int i = 0; i < enemies.Length; i++)
            {
                float health = preset == OrganDemoPreset.Explosion || preset == OrganDemoPreset.BlackHole ? 100f
                    : preset == OrganDemoPreset.Statuses ? 500f : i == 0 ? 60f : 200f;
                if (preset == OrganDemoPreset.DoubleTentacle && i == 0) health = Mathf.Max(1f, library[HandOrganKind.Fist].Damage);
                if (preset == OrganDemoPreset.Fist) health = i == 0 ? 20f : 40f;
                enemies[i].transform.position = transform.position + Vector3.right * offsets[i];
                enemies[i].InitializeForDemo("target_" + (char)('a' + i), health);
            }
            return true;
        }
    }
}
