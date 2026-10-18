using System.IO;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 把 <c>ArtSource/ship.png</c> 装成休整场景（Rest.unity）的船背景，
    /// 替代原来的海浪地砖（MapBackground 仍保留在场景里、被这张图盖住）。
    ///
    /// 尺寸口径：**图高 = 72 世界单位**，正好等于休整地图的边长，
    /// 于是船体在纵向上铺满可通行区域；横向 4:3 会自然多出约 12 单位（左右各 6）。
    /// 改大小只要改 <see cref="WorldHeight"/> 再跑一次菜单。
    /// </summary>
    public static class RestShipBackground
    {
        private const string SourceRelativePath = "ArtSource/ship.png";
        private const string AssetFolder = "Assets/Art/Backgrounds";
        private const string AssetPath = AssetFolder + "/ship.png";
        private const string ScenePath = "Assets/Scenes/Flow/Rest.unity";
        private const string RootName = "ShipBackground";
        private const string BoundsName = "ShipWalkBounds";
        private const string OldBackgroundName = "MapBackground";

        /// <summary>船体长方形中心（世界坐标），按船图量出来的。</summary>
        private static readonly Vector2 BoundsCenter = new Vector2(5.34f, 0.85f);

        /// <summary>船体长方形尺寸：x = 船身长度，y = 船宽。</summary>
        private static readonly Vector2 BoundsSize = new Vector2(30.8f, 20.7f);

        /// <summary>船头三角形高度。</summary>
        private const float BoundsBowLength = 13.3f;

        /// <summary>图在世界里的高度（单位）。休整地图边长 72；2026-10-18 按反馈缩到一半。</summary>
        private const float WorldHeight = 36f;

        private const int PixelHeight = 1086;

        /// <summary>地砖的排序是 -100，船背景排在它上面 1 层。</summary>
        private const int SortingOrder = -99;

        [MenuItem("Tools/SpringUp/场景美术/装配 Rest 船背景")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[RestShipBackground] 请先退出 Play 模式。");
                return;
            }

            if (!EnsureAssetImported())
                return;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetPath);
            if (sprite == null)
            {
                Debug.LogError("[RestShipBackground] 导入后仍取不到 Sprite：" + AssetPath);
                return;
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var root = FindRoot(scene);
                if (root == null)
                {
                    Debug.LogError("[RestShipBackground] 在 " + ScenePath + " 里找不到 World 根节点。");
                    return;
                }

                var ship = FindChild(root.transform, RootName);
                if (ship == null)
                {
                    ship = new GameObject(RootName).transform;
                    ship.SetParent(root.transform, false);
                }

                ship.localPosition = Vector3.zero;
                ship.localRotation = Quaternion.identity;
                ship.localScale = Vector3.one;

                var renderer = ship.GetComponent<SpriteRenderer>();
                if (renderer == null)
                    renderer = ship.gameObject.AddComponent<SpriteRenderer>();

                renderer.sprite = sprite;
                renderer.color = Color.white;
                renderer.sortingOrder = SortingOrder;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");

                RemoveOldBackground(root.transform);
                ConfigureWalkBounds(root.transform);
                ConfigureCameraBounds(scene);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[RestShipBackground] 已装配船背景：" + AssetPath + "，图形高 " + WorldHeight +
                          " 单位；已删除旧海浪背景并重建船形可行走区域。");
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>删掉原来的海浪地砖实例。它被船图整张盖住，留着只会白占 1600 个渲染器。</summary>
        private static void RemoveOldBackground(Transform world)
        {
            var old = FindChild(world, OldBackgroundName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
        }

        /// <summary>在 World 下重建船形可行走区域（长方形船身 + 三角形船头）。</summary>
        private static void ConfigureWalkBounds(Transform world)
        {
            var existing = FindChild(world, BoundsName);
            var go = existing != null ? existing.gameObject : new GameObject(BoundsName);

            go.transform.SetParent(world, false);
            go.transform.localPosition = new Vector3(BoundsCenter.x, BoundsCenter.y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var bounds = go.GetComponent<ShipWalkBounds>();
            if (bounds == null)
                bounds = go.AddComponent<ShipWalkBounds>();

            var serialized = new SerializedObject(bounds);
            serialized.FindProperty("bodySize").vector2Value = BoundsSize;
            serialized.FindProperty("bowLength").floatValue = BoundsBowLength;
            serialized.FindProperty("bowFacesLeft").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            bounds.Rebuild();
        }

        /// <summary>
        /// 相机夹取范围收到船图大小（48 × 36，半尺寸 24 × 18）。
        /// 不收紧的话，玩家走到甲板边缘时镜头会移出图外，露出空背景。
        /// </summary>
        private static void ConfigureCameraBounds(Scene scene)
        {
            CameraFollow follow = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                follow = root.GetComponentInChildren<CameraFollow>(true);
                if (follow != null)
                    break;
            }

            if (follow == null)
                return;

            var serialized = new SerializedObject(follow);
            serialized.FindProperty("mapCenter").vector2Value = Vector2.zero;
            serialized.FindProperty("mapHalfExtents").vector2Value = new Vector2(24f, 18f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>首次编译后自动装配一次；场景内容与导入参数都到位时跳过。</summary>
        [InitializeOnLoadMethod]
        private static void AutoApply()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                if (!File.Exists(Path.Combine(ProjectRoot, SourceRelativePath)))
                    return;

                if (IsUpToDate())
                    return;

                Apply();
            };
        }

        /// <summary>
        /// 已装配且导入的 PPU 与当前 <see cref="WorldHeight"/> 一致时跳过。
        /// 只查场景里有没有对象是不够的——改尺寸常量时也要能自动重新装配。
        /// </summary>
        private static bool IsUpToDate()
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(AssetPath) == null)
                return false;

            var importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
            if (importer == null || !Mathf.Approximately(importer.spritePixelsPerUnit, PixelHeight / WorldHeight))
                return false;

            if (!File.Exists(SceneFullPath))
                return false;

            var sceneText = File.ReadAllText(SceneFullPath);
            return sceneText.Contains(RootName)
                && sceneText.Contains(BoundsName)
                && !sceneText.Contains(OldBackgroundName);
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        private static string SceneFullPath => Path.Combine(ProjectRoot, ScenePath.Replace('/', Path.DirectorySeparatorChar));

        private static bool EnsureAssetImported()
        {
            var source = Path.Combine(ProjectRoot, SourceRelativePath);
            if (!File.Exists(source))
            {
                Debug.LogError("[RestShipBackground] 找不到源图：" + source);
                return false;
            }

            var targetDirectory = Path.Combine(ProjectRoot, AssetFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(targetDirectory);

            var target = Path.Combine(targetDirectory, "ship.png");
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                File.Copy(source, target, true);

            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
            if (importer == null)
                return false;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = PixelHeight / WorldHeight;
            importer.SetTextureSettings(settings);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = false;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return true;
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
                if (parent.GetChild(i).name == name)
                    return parent.GetChild(i);
            }

            return null;
        }
    }
}
