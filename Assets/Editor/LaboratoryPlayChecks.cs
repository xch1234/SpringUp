using System;
using System.IO;
using System.Linq;
using SpringUp.Laboratory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpringUp.LaboratoryEditor
{
    // 隔离工程的批处理入口（不加 -quit），使用真实 Update 节拍和按钮回调。
    [InitializeOnLoad]
    public static class LaboratoryPlayChecks
    {
        private const string Key = "SpringUp.LaboratoryPlayChecks";
        private static double start, phaseStart;
        private static int phase;
        static LaboratoryPlayChecks()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    phase = 0; start = phaseStart = EditorApplication.timeSinceStartup;
                    EditorApplication.update += Check;
                }
            };
        }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在隔离工程中以批处理运行此检查，不要添加 -quit。");
            EditorSceneManager.OpenScene(LaboratorySceneMenu.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        private static void Check()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - start > 25) throw new Exception("Play 检查超时。");
                var p = UnityEngine.Object.FindFirstObjectByType<LaboratoryPresenter>();
                var demo = UnityEngine.Object.FindFirstObjectByType<LaboratoryCombatDemo>();
                if (p == null || demo == null || demo.Pipeline == null) return;
                if (EditorApplication.timeSinceStartup - phaseStart < 1.3) return;
                var equipment = p.source.Equipment;
                if (phase == 0)
                {
                    Require(demo.targets[0].CurrentHealth == 0 && demo.targets[1].CurrentHealth == 15 && demo.TriggerCount == 1, "真实节拍产生击杀连锁");
                    var pipeline = demo.Pipeline;
                    p.SelectSlot(1, 3);
                    Require(demo.Pipeline == pipeline && pipeline.enabled, "选择不影响演示运行");
                    p.removeButton.onClick.Invoke();
                    Require(demo.targets[0].CurrentHealth == 5 && demo.targets[1].CurrentHealth == 20, "拆下按钮重置目标血量");
                }
                else if (phase == 1)
                {
                    Require(demo.targets[1].CurrentHealth == 20 && demo.TriggerCount == 0, "仅拳头按正常节拍攻击");
                    var trigger = equipment.Inventory.First(x => x.DefinitionId == "multi_tentacle");
                    p.SelectInventory(trigger.InstanceId); p.SelectSlot(1, 3); p.installButton.onClick.Invoke();
                }
                else if (phase == 2)
                {
                    Require(demo.targets[1].CurrentHealth == 15 && demo.TriggerCount == 1, "安装按钮恢复触发链");
                    demo.restartButton.onClick.Invoke();
                    Require(demo.targets[0].CurrentHealth == 5 && demo.targets[1].CurrentHealth == 20, "重新开始按钮重置血量");
                    p.gameObject.SetActive(false); p.gameObject.SetActive(true);
                    Require(demo.Pipeline != null && demo.targets[0].CurrentHealth == 5, "重新打开恢复依赖");
                }
                else
                {
                    Require(demo.targets[1].CurrentHealth == 15 && demo.TriggerCount == 1, "重新打开不会重复扣血");
                    Directory.CreateDirectory("Logs/LaboratoryRebuild");
                    File.WriteAllText("Logs/LaboratoryRebuild/play-result.txt", "通过：真实节拍、击杀链、选择独立、按钮装卸、重新开始和关闭重开。自动检查不模拟物理鼠标。");
                    Finish(0); return;
                }
                phase++; phaseStart = EditorApplication.timeSinceStartup;
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Check;
            Debug.Log("[Laboratory Play Checks] " + (code == 0 ? "PASS" : "FAIL"));
            EditorApplication.Exit(code);
        }
    }
}
