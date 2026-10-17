using Tideflesh.Systems;
using System;
using System.Collections.Generic;
using SpringUp.Flow;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    public static class StraightProjectileSampleMenu
    {
        static bool RestContext(out RunController run,
            out IReadOnlyDictionary<string, SpringUp.Organs.OrganDefinition> definitions, out Action refresh)
        {
            run = null; definitions = null; refresh = null;
            if (Application.isPlaying)
            {
                var app = UnityEngine.Object.FindAnyObjectByType<FlowApplication>();
                if (app != null && !app.IsTransitioning && app.Content?.kind == ContentKind.Rest)
                {
                    run = app.Session.Run; definitions = app.Configuration.Definitions;
                    refresh = () => {
                        app.Content.Equipment.Refresh();
                        foreach (var view in app.uiRoot.GetComponentsInChildren<FlowLaboratoryWindow>()) view.RefreshInventory();
                    };
                }
                else
                {
                    var driver = UnityEngine.Object.FindAnyObjectByType<SpringUpRunDriver>();
                    if (driver != null) { run = driver.Run; definitions = driver.Definitions; refresh = driver.Equipment.Refresh; }
                }
            }
            if (run?.Stage == RunPhase.Rest) return true;
            Debug.LogWarning("请在 Bootstrap 或 SpringUpDemo 的 Play 休息期使用此菜单。"); return false;
        }
        [MenuItem("Tools/SpringUp/样板/休息期添加全部躯干")]
        public static void GiveTorsos()
        {
            if (!RestContext(out var run, out var definitions, out var refresh)) return;
            foreach (var definition in definitions.Values)
                if (definition.Torso != null) run.Inventory.Spawn(definition.Id);
            refresh();
            Debug.Log("已添加 7 种躯干样板，只能装入两个躯干槽；出战前仍须满足背包容量限制。");
        }
        [MenuItem("Tools/SpringUp/样板/休息期添加全部触发器")]
        public static void GiveTriggers()
        {
            if (!RestContext(out var run, out var definitions, out var refresh)) return;
            foreach (var definition in definitions.Values)
                if (definition.Trigger != null) run.Inventory.Spawn(definition.Id);
            refresh();
            Debug.Log("已添加 10 种触发器（含多肢触手）；FollowingSlots 发动后续执行器，SelfSlots 发动整条管道。计时/失效还需要前级执行器。出战前须满足背包容量限制。");
        }
        [MenuItem("Tools/SpringUp/样板/休息期添加全部执行器")]
        public static void GiveAll()
        {
            if (!RestContext(out var run, out var definitions, out var refresh)) return;
            foreach (var definition in definitions.Values)
                if (definition.Type == SpringUp.Organs.OrganType.Actuator) run.Inventory.Spawn(definition.Id);
            refresh();
            Debug.Log("已添加全部 19 种执行器样板；出战前请装备或丢弃多余样板，遵守背包容量限制。");
        }
        [MenuItem("Tools/SpringUp/样板/休息期添加直线子弹")]
        public static void Give()
        {
            if (!RestContext(out var run, out var definitions, out var refresh)) return;
            if (!definitions.ContainsKey("straight_projectile"))
            { Debug.LogWarning("请停止 Play 后重新进入，以加载新的器官目录。"); return; }
            run.Inventory.Spawn("straight_projectile"); refresh();
            Debug.Log("直线子弹样板已加入背包，可安装到头、手或腿。样板不加入正式掉落表。");
        }
    }
}
