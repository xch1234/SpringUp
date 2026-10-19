using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 把 ArtSource 里按编号切好的敌人帧导入 Assets，并生成循环动画：
    /// 帧导入 → AnimationClip（12fps，循环）→ AnimatorController（状态 Move）。
    ///
    /// 目录契约与 <see cref="EnemyPrefabBuilder"/> 的「有美术就用美术」一致：
    /// <c>Assets/Art/Enemies/&lt;原型&gt;/&lt;原型&gt;_00.png …</c> + <c>&lt;原型&gt;.controller</c>。
    /// RangedChaser 之前已经单独装配过，不在这里重复处理。
    /// </summary>
    public static class EnemyArtSetup
    {
        private const string SourceRoot = ".tmp_art/enemy_frames";
        private const string TargetRoot = "Assets/Art/Enemies";
        private const float FrameRate = 12f;

        /// <summary>512 像素的画布 = 1 世界单位，配合 prefab 的 1.35 缩放。
        /// 敌人 2/3/5/6 按要求放大一倍，用 256；敌人 1（普通）保持 512。</summary>
        private const float PixelsPerUnit = 512f;

        /// <summary>布局版本。切图口径变了就 +1，保证下次编译会强制重导。</summary>
        private const int LayoutVersion = 5;

        private static readonly string[] Archetypes = { "Normal", "Fast", "RangedKeeper", "Medic", "Arsenal", "Dummy" };

        private const string BulletSource = ".tmp_art/bullet/enemy_bullet.png";
        private const string BulletAsset = "Assets/Art/Enemies/Bullet/enemy_bullet.png";

        /// <summary>子弹贴图同样 256px = 1 世界单位；按要求放大一倍后取 128。</summary>
        private const float BulletPixelsPerUnit = 128f;

        [MenuItem("Tools/SpringUp/敌人美术/导入敌人动画")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[EnemyArtSetup] 请先退出 Play 模式。");
                return;
            }

            var imported = 0;
            for (var i = 0; i < Archetypes.Length; i++)
            {
                if (ImportArchetype(Archetypes[i]))
                    imported++;
            }

            ImportBullet();
            UpdateSceneDummies();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 美术就位后立刻重建敌人 prefab，否则生成器可能先按灰盒跑完了。
            EnemyPrefabBuilder.GenerateAll();
            WriteMarker();
            Debug.Log("[EnemyArtSetup] 完成：导入 " + imported + " 个敌人动画 + 子弹贴图，并已重建敌人 prefab。");
        }

        /// <summary>
        /// 每种敌人的导入 PPU：敌人 1（普通）保持 1 倍，其余敌人按规定放大一倍。
        /// </summary>
        private static float PixelsPerUnitFor(string archetype)
        {
            if (archetype == "Normal")
                return PixelsPerUnit;          // 敌人 1：保持原大小
            if (archetype == "Dummy")
                return PixelsPerUnit / 2.5f;   // 木桩：放大到 2.5 倍
            if (archetype == "Arsenal")
                return PixelsPerUnit / 3f;     // 敌人 6：在放大的基础上再 ×1.5
            return PixelsPerUnit * 0.5f;       // 其余：放大的敌人
        }

        private static string MarkerPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "EnemyArtSetup.version");

        private static bool MarkerMatches()
        {
            return File.Exists(MarkerPath) &&
                   File.ReadAllText(MarkerPath).Trim() == "v" + LayoutVersion;
        }

        private static void WriteMarker()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath));
                File.WriteAllText(MarkerPath, "v" + LayoutVersion);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[EnemyArtSetup] 写布局标记失败：" + exception.Message);
            }
        }

        [InitializeOnLoadMethod]
        private static void AutoApply()
        {
            EditorApplication.delayCall += TryAutoApply;
        }

        private static void TryAutoApply()
        {
            // Play 模式下 Unity 不编译、不导入，等退出后再做。
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += TryAutoApply;
                return;
            }

            CleanupEmptyDuplicateFolders();

            if (NeedsImport())
                Apply();
        }

        /// <summary>布局版本、帧数、PPU 或控制器与源不一致时需要重导。</summary>
        private static bool NeedsImport()
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(BulletAsset) == null)
                return true;

            if (!MarkerMatches())
                return true;

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;

            for (var i = 0; i < Archetypes.Length; i++)
            {
                var sourceDirectory = Path.Combine(projectRoot, SourceRoot, Archetypes[i]);
                if (!Directory.Exists(sourceDirectory))
                    continue;

                var sourceCount = Directory.GetFiles(sourceDirectory, "*.png").Length;
                var targetDirectory = Path.Combine(projectRoot, TargetRoot, Archetypes[i]);
                var targetCount = Directory.Exists(targetDirectory)
                    ? Directory.GetFiles(targetDirectory, "*.png").Length
                    : 0;

                if (sourceCount != targetCount)
                    return true;

                if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        TargetRoot + "/" + Archetypes[i] + "/" + Archetypes[i] + ".controller") == null)
                    return true;

                var importer = AssetImporter.GetAtPath(
                    TargetRoot + "/" + Archetypes[i] + "/" + Archetypes[i] + "_00.png") as TextureImporter;
                if (importer != null && !Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnitFor(Archetypes[i])))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 清掉首次导入时可能产生的空重名目录（`Normal 1` 这类）。
        /// 只删里面没有任何资产的目录，避免误删。
        /// </summary>
        private static void CleanupEmptyDuplicateFolders()
        {
            for (var i = 0; i < Archetypes.Length; i++)
            {
                var duplicate = TargetRoot + "/" + Archetypes[i] + " 1";
                if (!AssetDatabase.IsValidFolder(duplicate))
                    continue;

                var contents = AssetDatabase.FindAssets(string.Empty, new[] { duplicate });
                if (contents != null && contents.Length > 0)
                    continue;

                AssetDatabase.DeleteAsset(duplicate);
                Debug.Log("[EnemyArtSetup] 已删除空的重名目录：" + duplicate);
            }
        }

        /// <summary>导入一个敌人的帧、clip、controller。返回是否真的做了导入。</summary>
        private static bool ImportArchetype(string archetype)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var sourceDirectory = Path.Combine(projectRoot, SourceRoot, archetype);
            var targetDirectory = Path.Combine(projectRoot, TargetRoot, archetype);

            if (!Directory.Exists(sourceDirectory))
            {
                Debug.LogWarning("[EnemyArtSetup] 缺少切好的帧目录：" + sourceDirectory);
                return false;
            }

            Directory.CreateDirectory(targetDirectory);

            var frameNames = new List<string>();
            foreach (var file in Directory.GetFiles(sourceDirectory, "*.png"))
                frameNames.Add(Path.GetFileName(file));
            frameNames.Sort(System.StringComparer.Ordinal);

            if (frameNames.Count == 0)
                return false;

            for (var i = 0; i < frameNames.Count; i++)
            {
                var source = Path.Combine(sourceDirectory, frameNames[i]);
                var target = Path.Combine(targetDirectory, frameNames[i]);
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                    File.Copy(source, target, true);
            }

            // 源帧数变少时（例如 1normal 只取上排），把多余的旧帧从 Assets 删掉，
            // 否则它们会继续被动画/生成器扫到。
            var expected = new HashSet<string>(frameNames);
            foreach (var existing in Directory.GetFiles(targetDirectory, "*.png"))
            {
                var name = Path.GetFileName(existing);
                if (expected.Contains(name))
                    continue;

                AssetDatabase.DeleteAsset(TargetRoot + "/" + archetype + "/" + name);
            }

            AssetDatabase.Refresh();
            ConfigureImporters(archetype, frameNames);

            var sprites = new Sprite[frameNames.Count];
            for (var i = 0; i < frameNames.Count; i++)
            {
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(TargetRoot + "/" + archetype + "/" + frameNames[i]);
                if (sprites[i] == null)
                {
                    Debug.LogError("[EnemyArtSetup] 帧没有导入成 Sprite：" + frameNames[i]);
                    return false;
                }
            }

            var clip = BuildClip(archetype, sprites);
            BuildController(archetype, clip);
            return true;
        }

        /// <summary>导入敌人子弹贴图（单帧，不带动画）。</summary>
        private static void ImportBullet()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var source = Path.Combine(projectRoot, BulletSource.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                Debug.LogWarning("[EnemyArtSetup] 缺少子弹贴图：" + source);
                return;
            }

            var targetDirectory = Path.Combine(projectRoot, "Assets/Art/Enemies/Bullet");
            Directory.CreateDirectory(targetDirectory);
            var target = Path.Combine(targetDirectory, "enemy_bullet.png");
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                File.Copy(source, target, true);

            AssetDatabase.ImportAsset(BulletAsset, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(BulletAsset) as TextureImporter;
            if (importer == null)
                return;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = BulletPixelsPerUnit;
            importer.SetTextureSettings(settings);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 256;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 休整场景里的三根 BowDummy 是早期从 Training dummy 复制出来的普通物体，
        /// 不是 Dummy prefab 的实例，所以 prefab 换图不会自动更新它们；这里直接改渲染器与 Animator。
        /// </summary>
        private static void UpdateSceneDummies()
        {
            const string scenePath = "Assets/Scenes/Flow/Rest.unity";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TargetRoot + "/Dummy/Dummy_00.png");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(TargetRoot + "/Dummy/Dummy.controller");
            if (sprite == null || controller == null)
            {
                Debug.LogWarning("[EnemyArtSetup] 木桩美术未就绪，跳过场景木桩更新。");
                return;
            }

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            try
            {
                var updated = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (renderer == null || !renderer.gameObject.name.StartsWith("BowDummy_"))
                            continue;

                        renderer.sprite = sprite;
                        renderer.color = Color.white;

                        var animator = renderer.GetComponent<Animator>();
                        if (animator == null)
                            animator = renderer.gameObject.AddComponent<Animator>();
                        animator.runtimeAnimatorController = controller;
                        animator.applyRootMotion = false;
                        updated++;
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[EnemyArtSetup] 已更新休整场景木桩贴图：" + updated + " 根。");
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ConfigureImporters(string archetype, List<string> frameNames)
        {
            for (var i = 0; i < frameNames.Count; i++)
            {
                var path = TargetRoot + "/" + archetype + "/" + frameNames[i];
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spritePixelsPerUnit = PixelsPerUnitFor(archetype);
                importer.SetTextureSettings(settings);

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 512;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static AnimationClip BuildClip(string archetype, Sprite[] sprites)
        {
            var clipPath = TargetRoot + "/" + archetype + "/" + archetype + "_Move.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = archetype + "_Move" };
                AssetDatabase.CreateAsset(clip, clipPath);
            }

            clip.frameRate = FrameRate;

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };

            var keys = new ObjectReferenceKeyframe[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / FrameRate, value = sprites[i] };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = sprites.Length / FrameRate;
            settings.loopTime = true;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void BuildController(string archetype, AnimationClip clip)
        {
            var controllerPath = TargetRoot + "/" + archetype + "/" + archetype + ".controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            var stateMachine = controller.layers[0].stateMachine;
            var existing = stateMachine.states;
            for (var i = 0; i < existing.Length; i++)
                stateMachine.RemoveState(existing[i].state);

            var state = stateMachine.AddState("Move");
            state.motion = clip;
            state.writeDefaultValues = false;
            stateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
        }
    }
}
