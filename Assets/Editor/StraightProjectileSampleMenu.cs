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
                if (definition.Torso != null && !AddSample(run, definition.Id)) break;
            refresh();
            Debug.Log("已按背包剩余空间添加躯干样板，只能装入两个躯干槽。");
        }
        [MenuItem("Tools/SpringUp/样板/休息期添加全部触发器")]
        public static void GiveTriggers()
        {
            if (!RestContext(out var run, out var definitions, out var refresh)) return;
            foreach (var definition in definitions.Values)
                if (definition.Trigger != null && !AddSample(run, definition.Id)) break;
            refresh();
            Debug.Log("已按背包剩余空间添加触发器；只发动紧邻后一个执行器，后者不自动攻击。攻击条件只监听紧邻前一个器官。");
        }
        [MenuItem("Tools/SpringUp/样板/休息期添加全部执行器")]
        public static void GiveAll()
        {
            if (!RestContext(out var run, out var definitions, out var refresh)) return;
            foreach (var definition in definitions.Values)
                if (definition.Type == SpringUp.Organs.OrganType.Actuator && !AddSample(run, definition.Id)) break;
            refresh();
            Debug.Log("已按背包剩余空间添加执行器样板。可先装备或丢弃，再添加其他样板。");
        }
        [MenuItem("Tools/SpringUp/样板/休息期添加直线子弹")]
        public static void Give()
        {
            if (!RestContext(out var run, out var definitions, out var refresh)) return;
            if (!definitions.ContainsKey("straight_projectile"))
            { Debug.LogWarning("请停止 Play 后重新进入，以加载新的器官目录。"); return; }
            if (!AddSample(run, "straight_projectile")) return;
            refresh();
            Debug.Log("直线子弹样板已加入背包，可安装到头、手或腿。样板不加入正式掉落表。");
        }
        static bool AddSample(RunController run, string id)
        {
            if (run.Inventory.IsBackpackFull) { Debug.LogWarning("背包已满，停止添加样板。"); return false; }
            run.Inventory.Spawn(id); return true;
        }
    }
}
