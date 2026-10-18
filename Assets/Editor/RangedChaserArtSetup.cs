using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 把 run&amp;shoot 切出来的 15 帧接成 RangedChaser 的循环动画：
    /// 帧导入设置、AnimationClip、AnimatorController、prefab 装配一条龙。
    ///
    /// **为什么要脚本而不是手点 Sprite Editor**：切图与导入设置是可复现的参数，
    /// 换成代码后换图重跑就行；Sprite Editor 保存的本来也是这份 importer 元数据。
    ///
    /// 首次编译后自动跑一次；装好后 <see cref="NeedsSetup"/> 为 false，不会重复执行。
    /// 需要重跑时用菜单 Tools/SpringUp/敌人美术/装配 RangedChaser 动画。
    /// </summary>
    public static class RangedChaserArtSetup
    {
        private const string SourceFolder = ".tmp_art/unique";
        private const string FrameFolder = "Assets/Art/Enemies/RangedChaser";
        private const string ClipPath = FrameFolder + "/RangedChaser_Run.anim";
        private const string ControllerPath = FrameFolder + "/RangedChaser.controller";
        private const string PrefabPath = "Assets/Resources/Enemies/RangedChaser.prefab";

        private const int FrameCount = 15;
        private const float FrameRate = 12f;

        /// <summary>帧图 452 像素 = 1 世界单位，与原有敌人 prefab 的 1.35 缩放配套。</summary>
        private const float PixelsPerUnit = 452f;

        [MenuItem("Tools/SpringUp/敌人美术/装配 RangedChaser 动画")]
        public static void Apply()
        {
            if (!EnsureFramesCopied())
            {
                Debug.LogError("[RangedChaserArtSetup] 找不到帧源文件 " + SourceFolder + "，装配中止。");
                return;
            }

            ConfigureImporters();

            var sprites = LoadSprites();
            if (sprites == null)
                return;

            var clip = BuildClip(sprites);
            var controller = BuildController(clip);
            ApplyToPrefab(controller, sprites[0]);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[RangedChaserArtSetup] 完成：" + FrameCount + " 帧 @ " + FrameRate + "fps，" +
                      "clip=" + ClipPath + "，controller=" + ControllerPath + "，prefab=" + PrefabPath);
        }

        /// <summary>首次编译后自动装配一次；已装配则跳过。</summary>
        [InitializeOnLoadMethod]
        private static void AutoApply()
        {
            EditorApplication.delayCall += () =>
            {
                if (!NeedsSetup())
                    return;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
                Debug.Log("[RangedChaserArtSetup] 需要装配：prefab=" + (prefab != null) +
                          " animator=" + (animator != null) +
                          " controller=" + (animator != null && animator.runtimeAnimatorController != null));

                Apply();
            };
        }

        private static bool NeedsSetup()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                return false;

            var animator = prefab.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                return true;

            // 判据用 stopTime 与关键帧数量，不用 clip.length：后者是曲线推导出来的，
            // 和 stopTime 不是同一个数，拿它做判断会每次域重载都重装一遍。
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
                return true;

            var expectedStop = FrameCount / FrameRate;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (!Mathf.Approximately(settings.stopTime, expectedStop))
                return true;

            var curve = AnimationUtility.GetObjectReferenceCurve(clip, SpriteBinding());
            return curve == null || curve.Length != FrameCount
                || !Mathf.Approximately(curve[curve.Length - 1].time, (FrameCount - 1) / FrameRate);
        }

        /// <summary>写 sprite 关键帧用的绑定：根节点上的 SpriteRenderer.m_Sprite。</summary>
        private static EditorCurveBinding SpriteBinding()
        {
            return new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };
        }

        /// <summary>把 .tmp_art 里切好的帧复制进 Assets。Unity 不导入以 . 开头的目录，所以必须先搬进来。</summary>
        private static bool EnsureFramesCopied()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var sourceDir = Path.Combine(projectRoot, SourceFolder);
            if (!Directory.Exists(sourceDir))
                return false;

            var targetDir = Path.Combine(projectRoot, FrameFolder);
            Directory.CreateDirectory(targetDir);

            for (var i = 0; i < FrameCount; i++)
            {
                var name = FrameName(i);
                var source = Path.Combine(sourceDir, name);
                if (!File.Exists(source))
                {
                    Debug.LogError("[RangedChaserArtSetup] 缺少帧文件：" + source);
                    return false;
                }

                var target = Path.Combine(targetDir, name);
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                    File.Copy(source, target, true);
            }

            AssetDatabase.Refresh();
            return true;
        }

        private static string FrameName(int index) => "RangedChaser_" + index.ToString("00") + ".png";

        private static void ConfigureImporters()
        {
            for (var i = 0; i < FrameCount; i++)
            {
                var path = FrameFolder + "/" + FrameName(i);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError("[RangedChaserArtSetup] 取不到纹理导入器：" + path);
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
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 512;
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
                    Debug.LogError("[RangedChaserArtSetup] 帧没有导入成 Sprite：" + path);
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
                clip = new AnimationClip { name = "RangedChaser_Run" };
                AssetDatabase.CreateAsset(clip, ClipPath);
            }

            clip.frameRate = FrameRate;

            var binding = SpriteBinding();

            // 键只放到第 14 帧（14/12 秒），最后一帧由 stopTime（15/12 秒）兜住；
            // 早先额外补一个 15/12 的同值键，会让最后一帧多停一拍。
            var keys = new ObjectReferenceKeyframe[FrameCount];
            for (var i = 0; i < FrameCount; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / FrameRate, value = sprites[i] };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.startTime = 0f;
            clipSettings.stopTime = FrameCount / FrameRate;
            clipSettings.loopTime = true;
            clipSettings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);

            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimatorController BuildController(AnimationClip clip)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            var stateMachine = controller.layers[0].stateMachine;

            // 重跑时先清空旧状态，避免叠加出多个 Run。
            var existing = stateMachine.states;
            for (var i = 0; i < existing.Length; i++)
                stateMachine.RemoveState(existing[i].state);

            var state = stateMachine.AddState("Run");
            state.motion = clip;
            state.writeDefaultValues = false;
            stateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void ApplyToPrefab(AnimatorController controller, Sprite firstFrame)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var renderer = root.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.sprite = firstFrame;
                    // 真美术自带颜色，不能再乘染色，否则会整体变暗。
                    renderer.color = Color.white;
                }
                else
                {
                    Debug.LogError("[RangedChaserArtSetup] prefab 根节点上没有 SpriteRenderer。");
                }

                var animator = root.GetComponent<Animator>();
                if (animator == null)
                    animator = root.AddComponent<Animator>();

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
