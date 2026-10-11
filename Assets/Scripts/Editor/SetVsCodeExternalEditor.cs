using System;
using System.Collections.Generic;
using System.IO;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 把 Unity 的 External Script Editor 指向 VS Code，并核对当前设置。
    /// 本机 VS Code 装在 D 盘，Unity 自带的 com.unity.ide.visualstudio 只扫描
    /// %LOCALAPPDATA%\Programs 与 %ProgramFiles%（见该包 VisualStudioCodeInstallation.cs），
    /// 自动发现找不到它，因此需要手动设定编辑器路径。
    ///
    /// 注意：本文件使用的 API 已对 6000.5.11f1 的 UnityEditor.dll 做过反射核对，
    /// 编辑器自带的 UnityEditor.xml 文档与实际运行时不一致（文档里的 SetCodeEditor /
    /// GetFoundScriptEditorPaths / CodeEditor.Installations 在运行时都不存在）。
    /// 真正可用的是 SetExternalScriptEditor、CurrentEditorPath、CurrentEditor.Installations。
    /// </summary>
    public static class SetVsCodeExternalEditor
    {
        private const string MenuRoot = "Tools/编辑器/";
        private const string LogPrefix = "[SetVsCodeExternalEditor] ";

        /// <summary>已在 Unity 里运行过一次自动设置，避免每次程序集重载都重来。</summary>
        private static bool s_Initialized;

        /// <summary>
        /// Unity 包能自动发现的 VS Code 位置（与 com.unity.ide.visualstudio 的扫描口径一致）。
        /// </summary>
        private static IEnumerable<string> GetDefaultDiscoveryRoots()
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Microsoft VS Code",
                "Code.exe");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft VS Code",
                "Code.exe");
        }

        /// <summary>按优先级找 VS Code 可执行文件：已注册安装 → 包默认扫描位置 → 常见安装盘符。</summary>
        private static string FindVsCodeExecutable()
        {
            var candidates = new List<string>();

            foreach (var installation in GetAvailableInstallations())
            {
                if (!string.IsNullOrEmpty(installation.Path))
                    candidates.Add(installation.Path);
            }

            candidates.AddRange(GetDefaultDiscoveryRoots());

            foreach (var root in new[] { "C:\\", "D:\\", "E:\\" })
            {
                candidates.Add(Path.Combine(root, "Program Files", "Microsoft VS Code", "Code.exe"));
                candidates.Add(Path.Combine(root, "Program Files (x86)", "Microsoft VS Code", "Code.exe"));
                candidates.Add(Path.Combine(root, "Programs", "Microsoft VS Code", "Code.exe"));
            }

            foreach (var candidate in candidates)
            {
                if (IsVsCodeExecutable(candidate))
                    return Path.GetFullPath(candidate);
            }

            return null;
        }

        private static bool IsVsCodeExecutable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            return Path.GetFileName(path).IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 当前注册的代码编辑器能报出的全部安装项。
        /// 只能经 IExternalCodeEditor.Installations 取——CodeEditor 自己没有 Installations 成员。
        /// </summary>
        private static CodeEditor.Installation[] GetAvailableInstallations()
        {
            var currentEditor = CodeEditor.CurrentEditor;
            if (currentEditor == null)
                return Array.Empty<CodeEditor.Installation>();

            var installations = currentEditor.Installations;
            return installations ?? Array.Empty<CodeEditor.Installation>();
        }

        /// <summary>路径字符串可能不同（大小写、短路径、符号链接），先按完整路径比，退化为按文件名比。</summary>
        private static bool RefersToSameExecutable(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return false;

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>当前设置的可读描述：路径 + 由哪个编辑器实现接管。</summary>
        private static string DescribeCurrentEditor()
        {
            var path = CodeEditor.CurrentEditorPath;
            var implementation = CodeEditor.CurrentEditor == null
                ? "(无)"
                : CodeEditor.CurrentEditor.GetType().FullName;

            return "CurrentEditorPath = " + (string.IsNullOrEmpty(path) ? "(空)" : path) +
                   "\nCurrentEditor 实现 = " + implementation;
        }

        /// <summary>编辑器重新加载后自动尝试一次设置，结果写进控制台。</summary>
        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            if (s_Initialized)
                return;

            s_Initialized = true;
            WriteInitializationReport();
        }

        /// <summary>只打印状态，不改任何设置。用于确认自动设置是否成功。</summary>
        private static void WriteInitializationReport()
        {
            var report = DescribeCurrentEditor() + "\n可用安装项：\n" + DescribeInstallations();
            Debug.Log(LogPrefix + "初始化检查\n" + report);
        }

        private static string DescribeInstallations()
        {
            var installations = GetAvailableInstallations();
            if (installations.Length == 0)
                return "  （空：没有任何包注册代码编辑器安装项）";

            var lines = new List<string>();
            foreach (var installation in installations)
                lines.Add("  - " + installation.Name + "  @  " + installation.Path);
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        [MenuItem(MenuRoot + "把 External Script Editor 设为 VS Code", false, 100)]
        private static void SetVsCodeAsExternalEditor()
        {
            var vsCodePath = FindVsCodeExecutable();
            if (vsCodePath == null)
            {
                const string message =
                    "没找到 Code.exe。\n\n" +
                    "已查过的位置：Unity 已注册的代码编辑器、com.unity.ide.visualstudio 的默认扫描目录、" +
                    "C/D/E 三个盘符下的常见安装路径。\n\n" +
                    "请先跑一次「列出 Unity 发现的所有代码编辑器」看 Unity 认得哪些，再手动指定路径。";
                Debug.LogError(LogPrefix + message);
                EditorUtility.DisplayDialog("没找到 VS Code", message, "好");
                return;
            }

            var before = CodeEditor.CurrentEditorPath;
            Debug.Log(LogPrefix + "VS Code 路径：" + vsCodePath + "\n设置前 CurrentEditorPath：" + before);

            CodeEditor.SetExternalScriptEditor(vsCodePath);

            var after = CodeEditor.CurrentEditorPath;
            var applied = RefersToSameExecutable(after, vsCodePath);

            if (applied)
            {
                Debug.Log(LogPrefix + "已生效。\n" + DescribeCurrentEditor());
                EditorUtility.DisplayDialog(
                    "已设为 VS Code",
                    "External Script Editor 已指向：\n" + after +
                    "\n\n提示：从命令行打开工程时 VS Code 会进「受限模式」，" +
                    "C# Dev Kit 的 Build / Switch Solution 会被禁用；" +
                    "用命令面板的 Manage Workspace Trust 信任该文件夹即可。",
                    "好");
            }
            else
            {
                Debug.LogWarning(LogPrefix + "设置后读回的值与写入值不一致。写入：" + vsCodePath +
                                 "\n读回：" + after);
                EditorUtility.DisplayDialog(
                    "设置可能没生效",
                    "写入：" + vsCodePath + "\n读回：" + after +
                    "\n\n请到 Edit → Preferences → External Tools 手动确认。",
                    "好");
            }
        }

        [MenuItem(MenuRoot + "列出 Unity 发现的所有代码编辑器", false, 101)]
        private static void LogDiscoveredEditors()
        {
            var report = DescribeCurrentEditor() + "\n可用安装项：\n" + DescribeInstallations();
            Debug.Log(LogPrefix + "代码编辑器清单\n" + report);
            EditorUtility.DisplayDialog("Unity 发现的代码编辑器", report, "好");
        }

        /// <summary>
        /// 删除建工程时模板自带的说明内容。这部分是 URP 模板残留，与本项目无关：
        /// Assets/TutorialInfo（Readme.cs / ReadmeEditor.cs / Layout.wlt / URP 图标）与 Assets/Readme.asset。
        /// </summary>
        [MenuItem(MenuRoot + "清理 URP 模板残留（TutorialInfo / Readme）", false, 200)]
        private static void RemoveTemplateTutorialAssets()
        {
            const string tutorialInfoFolder = "Assets/TutorialInfo";
            const string readmeAsset = "Assets/Readme.asset";

            var existing = new List<string>();
            if (AssetDatabase.IsValidFolder(tutorialInfoFolder))
                existing.Add(tutorialInfoFolder);
            if (AssetDatabase.LoadMainAssetAtPath(readmeAsset) != null)
                existing.Add(readmeAsset);

            if (existing.Count == 0)
            {
                Debug.Log(LogPrefix + "模板残留已经不在了，无需清理。");
                EditorUtility.DisplayDialog("无需清理", "TutorialInfo 与 Readme.asset 都已不存在。", "好");
                return;
            }

            var confirmed = EditorUtility.DisplayDialog(
                "清理模板残留",
                "将删除：\n  " + string.Join("\n  ", existing) +
                "\n\n这些是 URP 模板自带的说明与窗口布局，不属于本项目。",
                "删除",
                "取消");
            if (!confirmed)
                return;

            foreach (var assetPath in existing)
            {
                if (AssetDatabase.DeleteAsset(assetPath))
                    Debug.Log(LogPrefix + "已删除 " + assetPath);
                else
                    Debug.LogError(LogPrefix + "删除失败 " + assetPath);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 删除工程里已经没有内容、但文件夹仍存在的空目录。
        /// Unity 里空文件夹会一直显示且没有任何作用，删掉可以减少噪音。
        /// </summary>
        [MenuItem(MenuRoot + "删除 Assets 下的空文件夹", false, 201)]
        private static void RemoveEmptyAssetFolders()
        {
            var toDelete = new List<string>();

            foreach (var folder in AssetDatabase.GetSubFolders("Assets"))
                CollectEmptyFolders(folder, toDelete);

            if (toDelete.Count == 0)
            {
                Debug.Log(LogPrefix + "Assets 下没有空文件夹。");
                EditorUtility.DisplayDialog("没有空文件夹", "Assets 下没有需要清理的空文件夹。", "好");
                return;
            }

            toDelete.Sort(StringComparer.Ordinal);
            var confirmed = EditorUtility.DisplayDialog(
                "删除空文件夹",
                "将删除 " + toDelete.Count + " 个空文件夹：\n  " + string.Join("\n  ", toDelete),
                "删除",
                "取消");
            if (!confirmed)
                return;

            foreach (var folder in toDelete)
            {
                if (AssetDatabase.DeleteAsset(folder))
                    Debug.Log(LogPrefix + "已删除空文件夹 " + folder);
                else
                    Debug.LogError(LogPrefix + "删除空文件夹失败 " + folder);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>深度优先收集空目录；子目录全部为空时父目录也随之变空，故后序判定。</summary>
        private static void CollectEmptyFolders(string folder, List<string> toDelete)
        {
            foreach (var sub in AssetDatabase.GetSubFolders(folder))
                CollectEmptyFolders(sub, toDelete);

            var hasAsset = AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length > 0;
            var hasSubFolder = AssetDatabase.GetSubFolders(folder).Length > 0;

            if (!hasAsset && !hasSubFolder)
                toDelete.Add(folder);
        }
    }
}
