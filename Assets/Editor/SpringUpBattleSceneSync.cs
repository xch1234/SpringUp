using System;
using System.Linq;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    // Update the battle portion in place: existing run/laboratory references remain valid.
    public static class SpringUpBattleSceneSync
    {
        [MenuItem("Tools/SpringUp/同步C战场到统一Demo")]
        public static void Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止 Play。");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var demo = EditorSceneManager.OpenScene(SpringUpDemoBuilder.ScenePath);
            var source = EditorSceneManager.OpenScene("Assets/Scenes/Battle_Greybox.unity", OpenSceneMode.Additive);
            try
            {
                var driver = Find<SpringUpRunDriver>(demo);
                var sourcePlayer = Find<PlayerRuntime>(source);
                var player = driver.player;
                var camera = Find<Camera>(demo);
                var sourceCamera = Find<Camera>(source);
                // Confirm the expected roots before touching the destination.
                string[] roots = { "MapBackground", "BoundaryWalls", "TrainingDummy" };
                foreach (var name in roots) Root(source, name);
                foreach (var name in roots)
                {
                    var old = demo.GetRootGameObjects().SingleOrDefault(g => g.name == name);
                    if (old != null) Object.DestroyImmediate(old);
                    var original = Root(source, name);
                    var prefab = PrefabUtility.GetCorrespondingObjectFromSource(original);
                    var copy = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, demo) : Object.Instantiate(original);
                    if (copy.scene != demo) SceneManager.MoveGameObjectToScene(copy, demo);
                    copy.name = name; copy.transform.SetPositionAndRotation(
                        name == "TrainingDummy" && original.name != name ? new Vector3(12, 0, 0) : original.transform.position, original.transform.rotation);
                    copy.transform.localScale = original.transform.localScale; copy.SetActive(original.activeSelf);
                }
                var ground = demo.GetRootGameObjects().SingleOrDefault(g => g.name == "Ground");
                if (ground != null) Object.DestroyImmediate(ground);

                player.gameObject.layer = sourcePlayer.gameObject.layer;
                player.transform.SetPositionAndRotation(sourcePlayer.transform.position, sourcePlayer.transform.rotation);
                player.transform.localScale = sourcePlayer.transform.localScale;
                Copy<SpriteRenderer>(sourcePlayer.gameObject, player.gameObject);
                Copy<BoxCollider2D>(sourcePlayer.gameObject, player.gameObject);
                Copy<Rigidbody2D>(sourcePlayer.gameObject, player.gameObject);
                var stats = new SerializedObject(player.Stats);
                float cooldown = stats.FindProperty("baseCooldown").floatValue;
                float cycle = stats.FindProperty("baseCycleDuration").floatValue;
                EditorUtility.CopySerialized(sourcePlayer.Stats, player.Stats);
                stats.Update(); stats.FindProperty("baseCooldown").floatValue = cooldown;
                stats.FindProperty("baseCycleDuration").floatValue = cycle; stats.ApplyModifiedPropertiesWithoutUndo();
                Copy<PlayerMotor>(sourcePlayer.gameObject, player.gameObject);
                var motor = new SerializedObject(player.GetComponent<PlayerMotor>());
                motor.FindProperty("stats").objectReferenceValue = player.Stats; motor.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.CopySerialized(sourceCamera, camera);
                Copy<CameraFollow>(sourceCamera.gameObject, camera.gameObject);
                camera.GetComponent<CameraFollow>().Target = player.transform;
                camera.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
                driver.battleCamera = camera;
                var spawner = Find<EnemySpawner>(demo);
                EditorUtility.CopySerialized(Find<EnemySpawner>(source), spawner);
                EditorUtility.CopySerialized(Find<WaveDirector>(source), driver.director);
                var wave = new SerializedObject(driver.director);
                wave.FindProperty("autoStart").boolValue = false;
                wave.FindProperty("spawner").objectReferenceValue = spawner;
                wave.FindProperty("intervalBetweenWaves").floatValue = 0;
                wave.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(demo);
                EditorSceneManager.SaveScene(demo);
                Debug.Log("[Battle Scene Sync] Saved demo: map, walls, dummy, player, camera and spawning; retained run, UI, loadout and attack cadence.");
            }
            finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(demo); }
        }
        private static GameObject Root(Scene scene, string name)
        {
            var roots = scene.GetRootGameObjects();
            var root = roots.SingleOrDefault(g => g.name == name);
            if (root != null) return root;
            // Older C scene overrides reference former prefab file IDs. Recover the
            // intended dummy placement using the current prefab without rewriting C.
            if (name == "TrainingDummy")
            {
                root = roots.SingleOrDefault(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == "Assets/Resources/Enemies/Dummy.prefab");
                if (root != null) return root;
            }
            throw new InvalidOperationException("源战场缺少 " + name + "；当前根物体：" + string.Join(", ", roots.Select(g => g.name)));
        }
        private static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).Single();
        private static void Copy<T>(GameObject from, GameObject to) where T : Component
            => EditorUtility.CopySerialized(from.GetComponent<T>(), to.GetComponent<T>() ?? to.AddComponent<T>());
        public static void RunBatch()
        {
            try { Sync(); BattleSceneSyncChecks.CheckScene(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
