using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    public static class SpringUpBuild
    {
        public static void WindowsBatch()
        {
            var output = Environment.GetEnvironmentVariable("SPRINGUP_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Builds/SpringUpDemo/SpringUpDemo.exe";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SpringUpDemoBuilder.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Windows 构建失败。");
            Debug.Log("[SpringUp Build] PASS: " + output);
        }
    }
}
