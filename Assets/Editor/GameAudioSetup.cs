using System.IO;
using System.Linq;
using SpringUp.Flow;
using TideBorne.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 音频接入：
    /// 1. 把音频按用途归到 <c>Assets/Audio/Music</c> 与 <c>Assets/Audio/SFX</c>；
    /// 2. 给 BGM 设 Streaming、给音效设 DecompressOnLoad；
    /// 3. 在 Bootstrap 场景的 FlowApplication 下挂一个常驻的 <see cref="GameAudio"/> 并接好三个 clip。
    /// </summary>
    public static class GameAudioSetup
    {
        private const string BootstrapPath = "Assets/Scenes/Flow/Bootstrap.unity";
        private const string MusicFolder = "Assets/Audio/Music";
        private const string SfxFolder = "Assets/Audio/SFX";
        private const string AudioObjectName = "Audio";

        private const string RestMusicPath = MusicFolder + "/休整bgm.mp3";
        private const string BattleMusicPath = MusicFolder + "/战场bgm.mp3";
        private const string EnemyHitPath = SfxFolder + "/敌人受击.wav";

        private static readonly (string from, string to)[] Moves =
        {
            ("Assets/Audio/休整bgm.mp3", RestMusicPath),
            ("Assets/Audio/战场bgm.mp3", BattleMusicPath),
            ("Assets/Audio/敌人受击.wav", EnemyHitPath),
            ("Assets/Audio/近战音效.wav", SfxFolder + "/近战音效.wav"),
            ("Assets/Audio/远程音效1.wav", SfxFolder + "/远程音效1.wav"),
            ("Assets/Audio/炸弹（需裁剪）.wav", SfxFolder + "/炸弹（需裁剪）.wav"),
        };

        [MenuItem("Tools/SpringUp/音频/装配游戏音频")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[GameAudioSetup] 请先退出 Play 模式。");
                return;
            }

            EnsureFolder("Assets", "Audio");
            EnsureFolder("Assets/Audio", "Music");
            EnsureFolder("Assets/Audio", "SFX");

            MoveClips();
            ConfigureImporters();
            WireBootstrap();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GameAudioSetup] 完成：BGM 两条（休整/战场）、受击音效一条，已挂到 Bootstrap 的 GameAudio。");
        }

        [InitializeOnLoadMethod]
        private static void AutoApply()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                // 首次运行时音频还在 Assets/Audio 根目录，移动到 Music/SFX 是 Apply 干的活，
                // 所以两个位置任意一个存在都算「可以开始装配」。
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(RestMusicPath) == null &&
                    AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/休整bgm.mp3") == null)
                    return;

                var sceneFullPath = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    BootstrapPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sceneFullPath))
                    return;

                if (File.ReadAllText(sceneFullPath).Contains("GameAudio"))
                    return;

                Apply();
            };
        }

        private static void EnsureFolder(string parent, string leaf)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void MoveClips()
        {
            for (var i = 0; i < Moves.Length; i++)
            {
                var from = Moves[i].from;
                var to = Moves[i].to;
                if (AssetDatabase.LoadAssetAtPath<Object>(from) == null)
                    continue;
                if (AssetDatabase.LoadAssetAtPath<Object>(to) != null)
                    continue;

                var error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning("[GameAudioSetup] 移动 " + from + " 失败：" + error);
            }
        }

        private static void ConfigureImporters()
        {
            ConfigureMusic(RestMusicPath);
            ConfigureMusic(BattleMusicPath);
            ConfigureSfx(EnemyHitPath);
        }

        private static void ConfigureMusic(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                return;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static void ConfigureSfx(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                return;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 1f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static void WireBootstrap()
        {
            var scene = SceneManager.GetSceneByPath(BootstrapPath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Additive);

            try
            {
                var app = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<FlowApplication>(true))
                    .FirstOrDefault();
                if (app == null)
                {
                    Debug.LogError("[GameAudioSetup] Bootstrap 里找不到 FlowApplication。");
                    return;
                }

                var host = app.transform.Find(AudioObjectName);
                if (host == null)
                {
                    var go = new GameObject(AudioObjectName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.SetParent(app.transform, false);
                    host = go.transform;
                }

                var audio = host.GetComponent<GameAudio>();
                if (audio == null)
                    audio = host.gameObject.AddComponent<GameAudio>();

                var serialized = new SerializedObject(audio);
                serialized.FindProperty("restMusic").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(RestMusicPath);
                serialized.FindProperty("battleMusic").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(BattleMusicPath);
                serialized.FindProperty("enemyHitSfx").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(EnemyHitPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
