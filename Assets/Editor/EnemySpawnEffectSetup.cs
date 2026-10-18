using System.IO;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 把 <c>ArtSource/spring.png</c> 切出来的 12 帧接成敌人生成水花：
    /// 帧导入 → AnimationClip（12fps，1 秒，不循环）→ AnimatorController → Resources prefab。
    ///
    /// prefab 路径 <c>Assets/Resources/Effects/EnemySpawn.prefab</c> 与
    /// <see cref="EnemySpawnEffect.PrefabPath"/> 对应，运行时刷怪器按这个路径加载。
    /// </summary>
    public static class EnemySpawnEffectSetup
    {
        private const string SourceFolder = ".tmp_art/spring_frames";
        private const string FrameFolder = "Assets/Art/Effects/EnemySpawn";
        private const string ClipPath = FrameFolder + "/EnemySpawn.anim";
        private const string ControllerPath = FrameFolder + "/EnemySpawn.controller";
        private const string PrefabFolder = "Assets/Resources/Effects";
        private const string PrefabPath = PrefabFolder + "/EnemySpawn.prefab";

        private const int FrameCount = 12;

        /// <summary>12 帧 / 12 fps = 1.0 秒。</summary>
        private const float FrameRate = 12f;

        /// <summary>750 像素 = 2 世界单位；水柱最高约 1.6 米。</summary>
        private const float PixelsPerUnit = 375f;

        /// <summary>画在敌人（2）上面，水花才不会被敌人体挡住。</summary>
        private const int SortingOrder = 5;

        [MenuItem("Tools/SpringUp/场景美术/装配敌人生成水花")]
        public static void Apply()
        {
            if (!EnsureFramesCopied())
                return;

            ConfigureImporters();

            var sprites = LoadSprites();
            if (sprites == null)
                return;

            var clip = BuildClip(sprites);
            var controller = BuildController(clip);
            BuildPrefab(sprites[0], controller);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[EnemySpawnEffectSetup] 完成：12 帧 @ 12fps（1 秒），prefab=" + PrefabPath);
        }

        [InitializeOnLoadMethod]
        private static void AutoApply()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                if (!Directory.Exists(Path.Combine(ProjectRoot, SourceFolder)))
                    return;

                if (NeedsSetup())
                    Apply();
            };
        }

        private static bool NeedsSetup()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                return true;

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
                return true;

            // 帧率或总时长变了也要重装，只查关键帧数量发现不了"同样 12 帧但更慢"。
            if (!Mathf.Approximately(clip.frameRate, FrameRate))
                return true;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (!Mathf.Approximately(settings.stopTime, FrameCount / FrameRate))
                return true;

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };
            var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            return curve == null || curve.Length != FrameCount;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        private static bool EnsureFramesCopied()
        {
            var sourceDirectory = Path.Combine(ProjectRoot, SourceFolder);
            if (!Directory.Exists(sourceDirectory))
            {
                Debug.LogError("[EnemySpawnEffectSetup] 找不到切好的帧目录：" + sourceDirectory);
                return false;
            }

            EnsureFolder("Assets/Art", "Effects");
            EnsureFolder("Assets/Art/Effects", "EnemySpawn");

            var targetDirectory = Path.Combine(ProjectRoot, FrameFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(targetDirectory);

            for (var i = 0; i < FrameCount; i++)
            {
                var name = FrameName(i);
                var source = Path.Combine(sourceDirectory, name);
                if (!File.Exists(source))
                {
                    Debug.LogError("[EnemySpawnEffectSetup] 缺少帧文件：" + source);
                    return false;
                }

                var target = Path.Combine(targetDirectory, name);
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                    File.Copy(source, target, true);
            }

            AssetDatabase.Refresh();
            return true;
        }

        private static string FrameName(int index) => "EnemySpawn_" + index.ToString("00") + ".png";

        private static void EnsureFolder(string parent, string leaf)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void ConfigureImporters()
        {
            for (var i = 0; i < FrameCount; i++)
            {
                var path = FrameFolder + "/" + FrameName(i);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError("[EnemySpawnEffectSetup] 取不到纹理导入器：" + path);
                    continue;
                }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spritePixelsPerUnit = PixelsPerUnit;
                importer.SetTextureSettings(settings);

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 1024;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static Sprite[] LoadSprites()
        {
            var sprites = new Sprite[FrameCount];
            for (var i = 0; i < FrameCount; i++)
            {
                var path = FrameFolder + "/" + FrameName(i);
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprites[i] == null)
                {
                    Debug.LogError("[EnemySpawnEffectSetup] 帧没有导入成 Sprite：" + path);
                    return null;
                }
            }

            return sprites;
        }

        private static AnimationClip BuildClip(Sprite[] sprites)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = "EnemySpawn" };
                AssetDatabase.CreateAsset(clip, ClipPath);
            }

            clip.frameRate = FrameRate;

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };

            var keys = new ObjectReferenceKeyframe[FrameCount];
            for (var i = 0; i < FrameCount; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / FrameRate, value = sprites[i] };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = FrameCount / FrameRate;
            settings.loopTime = false;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimatorController BuildController(AnimationClip clip)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            var stateMachine = controller.layers[0].stateMachine;
            var existing = stateMachine.states;
            for (var i = 0; i < existing.Length; i++)
                stateMachine.RemoveState(existing[i].state);

            var state = stateMachine.AddState("Spawn");
            state.motion = clip;
            state.writeDefaultValues = false;
            stateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void BuildPrefab(Sprite firstFrame, RuntimeAnimatorController controller)
        {
            EnsureFolder("Assets/Resources", "Effects");

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                AssetDatabase.DeleteAsset(PrefabPath);

            var root = new GameObject("EnemySpawn");

            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = firstFrame;
            renderer.color = Color.white;
            renderer.sortingOrder = SortingOrder;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            root.AddComponent<EnemySpawnEffect>();

            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                    Debug.LogError("[EnemySpawnEffectSetup] prefab 保存失败：" + PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
