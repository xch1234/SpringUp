using System.IO;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 在休整场景船头三角形区域放三根木桩，三根成正三角形。
    ///
    /// 当前口径（2026-10-18 调整）：
    /// - 边长 = 4 个角色宽度（5.4 单位），是初版 2 个角色宽度的两倍；
    /// - 三根的质心落在三角形的**竖直边**（船头三角形与长方形船身的交界线）上；
    /// - 场景里原来那根独立的 Training dummy 会被删除，只保留这三根。
    ///
    /// 木桩直接复制场景里的 Training dummy（或已有的 BowDummy），所以外观、碰撞体、数据一致；
    /// 运行时会由 <c>FlowContent.BindTrainingTargets</c> 统一设成训练靶（打不死、可复位）。
    /// </summary>
    public static class RestBowDummies
    {
        private const string ScenePath = "Assets/Scenes/Flow/Rest.unity";
        private const string SourceDummyName = "Training dummy";
        private const string NamePrefix = "BowDummy_";
        private const string NewPrefix = "BowDummyNew_";
        private const int DummyCount = 3;

        /// <summary>木桩间距 = 这个倍数 × 角色宽度（初始 2，按要求翻倍到 4）。</summary>
        private const float SideInPlayerWidths = 4f;

        /// <summary>布局版本。改了上面的口径就 +1，保证下次编译会自动重排。</summary>
        private const int LayoutVersion = 2;

        [MenuItem("Tools/SpringUp/场景美术/在船头放三根木桩")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[RestBowDummies] 请先退出 Play 模式。");
                return;
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var world = FindRoot(scene);
                if (world == null)
                {
                    Debug.LogError("[RestBowDummies] 在 " + ScenePath + " 里找不到 World 根节点。");
                    return;
                }

                var source = FindChild(world.transform, SourceDummyName) ?? FindFirstByPrefix(world.transform, NamePrefix);
                if (source == null)
                {
                    Debug.LogError("[RestBowDummies] 找不到可复制的木桩（既没有「" + SourceDummyName + "」也没有 " + NamePrefix + "）。");
                    return;
                }

                var bounds = world.GetComponentInChildren<ShipWalkBounds>(true);
                if (bounds == null)
                {
                    Debug.LogError("[RestBowDummies] 找不到 ShipWalkBounds，无法计算船头三角形。");
                    return;
                }

                var anchor = BaseEdgeMidpoint(bounds);
                var side = SideInPlayerWidths * GreyboxAssets.PlayerWorldSize;
                var radius = side / Mathf.Sqrt(3f);

                // 先克隆成临时名字，再清理旧对象。这样即使源本身是旧的 BowDummy，也不会在重建途中被删掉。
                var created = new GameObject[DummyCount];
                for (var i = 0; i < DummyCount; i++)
                {
                    var angle = Mathf.Deg2Rad * (90f + 120f * i);
                    var position = anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                    var copy = Object.Instantiate(source.gameObject);
                    copy.name = NewPrefix + (i + 1);
                    // Instantiate 在编辑器里会落进当前激活场景，先挪回 Rest 再挂到 World 下。
                    SceneManager.MoveGameObjectToScene(copy, scene);
                    copy.transform.SetParent(source.parent, false);
                    copy.transform.position = new Vector3(position.x, position.y, 0f);
                    copy.transform.rotation = Quaternion.identity;
                    created[i] = copy;
                }

                for (var i = world.transform.childCount - 1; i >= 0; i--)
                {
                    var child = world.transform.GetChild(i);
                    if (child != null && child.name.StartsWith(NamePrefix))
                        Object.DestroyImmediate(child.gameObject);
                }

                var original = FindChild(world.transform, SourceDummyName);
                if (original != null)
                    Object.DestroyImmediate(original.gameObject);

                for (var i = 0; i < created.Length; i++)
                    created[i].name = NamePrefix + (i + 1);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                WriteMarker();
                Debug.Log("[RestBowDummies] 已按质心 (" + anchor.x.ToString("0.##") + ", " +
                          anchor.y.ToString("0.##") + ") 重排 " + DummyCount + " 根木桩，边长 " +
                          side.ToString("0.##") + " 单位；原 Training dummy 已删除。");
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>首次编译后自动执行一次；布局版本或场景内容与当前口径不符时重排。</summary>
        [InitializeOnLoadMethod]
        private static void AutoApply()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                var sceneFullPath = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    ScenePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(sceneFullPath))
                    return;

                var text = File.ReadAllText(sceneFullPath);
                var marker = MarkerPath;
                var applied = File.Exists(marker) && File.ReadAllText(marker).Trim() == Signature();

                if (applied && text.Contains(NamePrefix) && !text.Contains(SourceDummyName))
                    return;

                Apply();
            };
        }

        private static string Signature()
        {
            return "v" + LayoutVersion + "|side=" + SideInPlayerWidths.ToString("0.###") + "|anchor=base-edge";
        }

        private static string MarkerPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "RestBowDummies.layout");

        private static void WriteMarker()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath));
                File.WriteAllText(MarkerPath, Signature());
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[RestBowDummies] 写布局标记失败：" + exception.Message);
            }
        }

        /// <summary>
        /// 三角形竖直边的中点：长方形与船头三角形的交界线（局部 x = ±半长，y = 0）。
        /// </summary>
        private static Vector2 BaseEdgeMidpoint(ShipWalkBounds bounds)
        {
            var serialized = new SerializedObject(bounds);
            var size = serialized.FindProperty("bodySize").vector2Value;
            var facesLeft = serialized.FindProperty("bowFacesLeft").boolValue;

            var dir = facesLeft ? -1f : 1f;
            var local = new Vector2(dir * size.x * 0.5f, 0f);
            return bounds.transform.TransformPoint(local);
        }

        private static GameObject FindRoot(Scene scene)
        {
            foreach (var candidate in scene.GetRootGameObjects())
            {
                if (candidate.name.StartsWith("World"))
                    return candidate;
            }

            return null;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                    return child;
            }

            return null;
        }

        private static Transform FindFirstByPrefix(Transform parent, string prefix)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith(prefix))
                    return child;
            }

            return null;
        }
    }
}
