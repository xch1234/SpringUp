using System.IO;
using UnityEditor;
using UnityEngine;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 一次性维护：
    /// 1. 把扩展名写成 .PNG、实际内容是 JPEG 的源图改名为 .jpg（Unity 按扩展名挑导入器）；
    /// 2. 再把这张 8000×4000 的母图移出 Assets，避免它作为纹理常驻显存。
    ///
    /// 只执行一次：目标不存在就静默跳过。
    /// </summary>
    public static class EnemyArtMaintenance
    {
        private const string LegacyPngPath = "Assets/Art/Enemies/run&shoot.PNG";
        private const string SourceAssetPath = "Assets/Art/Enemies/run&shoot.jpg";

        /// <summary>源图移出后的存放目录（工程根目录下，不在 Assets 里，不会被导入）。</summary>
        private const string SourceFolderName = "ArtSource";

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            RenameLegacyExtension();
            MoveSourceOutOfAssets();
        }

        private static void RenameLegacyExtension()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LegacyPngPath) == null)
                return;

            var error = AssetDatabase.MoveAsset(LegacyPngPath, SourceAssetPath);
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning("[EnemyArtMaintenance] 源图改名失败：" + error);
            else
                Debug.Log("[EnemyArtMaintenance] 源图已从 " + LegacyPngPath + " 改名为 " + SourceAssetPath);
        }

        private static void MoveSourceOutOfAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SourceAssetPath) == null)
                return;

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var targetDirectory = Path.Combine(projectRoot, SourceFolderName);
            Directory.CreateDirectory(targetDirectory);

            var sourceFullPath = Path.Combine(projectRoot, SourceAssetPath.Replace('/', Path.DirectorySeparatorChar));
            var targetFullPath = Path.Combine(targetDirectory, Path.GetFileName(sourceFullPath));
            if (File.Exists(targetFullPath))
                File.Delete(targetFullPath);

            File.Move(sourceFullPath, targetFullPath);

            var metaPath = sourceFullPath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            AssetDatabase.Refresh();
            Debug.Log("[EnemyArtMaintenance] 源图已移出 Assets：" + targetFullPath + "（不再作为纹理导入）");
        }
    }
}
