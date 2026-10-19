using TideBorne.Battle;
using UnityEditor;
using UnityEngine;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 生成/补齐每波刷怪权重资产 <c>Assets/Resources/EnemyWaveMix.asset</c>。
    /// 运行时 <see cref="EnemyWaveMixSettings.Load"/> 按这个路径加载；资产被误删时这里会自动重建默认表。
    /// </summary>
    public static class EnemyWaveMixSetup
    {
        private const string AssetPath = "Assets/Resources/EnemyWaveMix.asset";

        [MenuItem("Tools/SpringUp/战斗/创建敌人波次权重资产")]
        public static void CreateIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<EnemyWaveMixSettings>(AssetPath) != null)
            {
                Debug.Log("[EnemyWaveMixSetup] 资产已存在：" + AssetPath + "（不覆盖现有配置）");
                return;
            }

            var settings = EnemyWaveMixSettings.CreateDefaultInstance();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[EnemyWaveMixSetup] 已创建 " + AssetPath + "：每 3 秒随机刷怪，7 波权重按策划表。");
        }

        [InitializeOnLoadMethod]
        private static void AutoCreate()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                if (AssetDatabase.LoadAssetAtPath<EnemyWaveMixSettings>(AssetPath) == null)
                    CreateIfMissing();
            };
        }
    }
}
