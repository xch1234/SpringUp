using System;
using System.Linq;
using System.Reflection;
using SpringUp.Flow;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class BattleMapChecks
    {
        const string Key = "SpringUp.BattleMapChecks";
        static int stage;
        static double next, deadline;
        static BattleMapSettings runtimeSettings;
        static int firstSeed;

        static BattleMapChecks()
        { if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += StateChanged; }

        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("[Battle Map Checks] " + message); }

        public static void RunBatch()
        {
            BattleMapAuthoring.InstallBatch();
            CheckTiles();
            System.IO.Directory.CreateDirectory("Logs/Presentation");
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Key, true);
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.EnterPlaymode();
        }

        static void CheckTiles()
        {
            var settings = Object.Instantiate(BattleMapAuthoring.EnsureSettings());
            var root = new GameObject("Map regression"); root.SetActive(false);
            var colors = new[] { Color.red, Color.green, Color.blue };
            var sprites = colors.Select(color => {
                var texture = new Texture2D(8, 8); texture.SetPixels(Enumerable.Repeat(color, 64).ToArray()); texture.Apply();
                return Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.25f, .75f), 8);
            }).ToArray();
            var globalRandom = UnityEngine.Random.state;
            try
            {
                settings.columns = 21; settings.rows = 13; settings.cellSize = 2;
                settings.randomizeOnEntry = false; settings.seed = 912;
                settings.centerVariants = new[] { sprites[0], null, sprites[1], sprites[2] };
                var layout = root.AddComponent<BattleMapLayout>(); layout.settings = settings;
                layout.terrain = new GameObject("Tiles").transform; layout.terrain.SetParent(root.transform);
                layout.terrain.position = new Vector3(5, -3, 0);
                layout.walls = new GameObject("Walls").transform; layout.walls.SetParent(root.transform);
                foreach (string name in new[] { "Wall_Top", "Wall_Bottom", "Wall_Left", "Wall_Right" })
                {
                    var wall = new GameObject(name, typeof(BoxCollider2D)); wall.transform.SetParent(layout.walls);
                }
                var cameraObject = new GameObject("Camera", typeof(Camera)); cameraObject.transform.SetParent(root.transform);
                layout.battleCamera = cameraObject.GetComponent<Camera>(); layout.battleCamera.orthographic = true;
                cameraObject.AddComponent<CameraFollow>().Target = root.transform;
                root.SetActive(true);
                UnityEngine.Random.InitState(177);
                float expectedNext = UnityEngine.Random.value;
                UnityEngine.Random.InitState(177);
                layout.Rebuild();
                Require(UnityEngine.Random.value == expectedNext, "Visual randomness does not consume combat/loot RNG");
                var tiles = layout.terrain.GetComponentsInChildren<SpriteRenderer>(true);
                Require(tiles.Length == 21 * 13, "Rectangular map tile count");
                var choices = new int[3];
                foreach (var tile in tiles)
                {
                    var xy = tile.name.Split('_'); int x = int.Parse(xy[1]), y = int.Parse(xy[2]);
                    bool interior = x > 0 && x < 20 && y > 0 && y < 12;
                    if (interior)
                    {
                        int index = Array.IndexOf(sprites, tile.sprite);
                        Require(index >= 0, "Every interior tile chooses from the non-null pool"); choices[index]++;
                        Require(tile.sharedMaterial.mainTexture == tile.sprite.texture, "Each variant has its own correct texture binding");
                    }
                    else
                    {
                        int row = y == 12 ? 0 : y == 0 ? 2 : 1;
                        int column = x == 0 ? 0 : x == 20 ? 2 : 1;
                        Require(tile.sprite == settings.borderSprites[row * 3 + column], "Corners and edges retain their nine-slice role");
                    }
                    var expected = layout.Center + new Vector2((x + .5f) * 2 - 21, (y + .5f) * 2 - 13);
                    Require(Vector2.Distance(tile.bounds.center, expected) < .001f && Vector2.Distance(tile.bounds.size, new Vector2(2, 2)) < .001f,
                        "No gaps/stretching with custom sprite pivots");
                }
                Require(choices.All(n => n > 30), "All three independent alternatives appear across the grid");
                var before = tiles.Select(t => t.sprite).ToArray(); layout.Rebuild();
                Require(before.SequenceEqual(layout.terrain.GetComponentsInChildren<SpriteRenderer>(true).Select(t => t.sprite)), "Same seed reproduces the layout");
                Physics2D.SyncTransforms();
                Require(Mathf.Abs(layout.walls.Find("Wall_Right").GetComponent<BoxCollider2D>().bounds.min.x - 26) < .001f,
                    "Right wall follows shifted rectangular map boundary");
                Require(layout.ClampInside(new Vector2(100, -100)) == new Vector2(25, -15), "Enemy clamp uses actual center and half extents");
                settings.centerVariants = new Sprite[] { null }; settings.columns = 9; settings.rows = 7;
                layout.Rebuild();
                Require(layout.terrain.childCount == 63, "Shrinking removes old tiles");
                Require(layout.terrain.Find("Tile_01_01").GetComponent<SpriteRenderer>().sprite == settings.borderSprites[4], "Empty pool falls back to 5.png");
                settings.columns = 23; settings.rows = 15; layout.Rebuild();
                Require(layout.terrain.childCount == 345, "Growing fills the new area");
                settings.columns = 0; settings.rows = -1; settings.cellSize = 0;
                Require(settings.Columns == 3 && settings.Rows == 3 && settings.CellSize == .5f, "Invalid dimensions safely clamped");
                Debug.Log("[Battle Map Checks] CORE PASS: three variants, deterministic seed, null fallback, edges, custom pivots, rectangular resize, independent RNG, walls and flee bounds.");
            }
            finally
            {
                UnityEngine.Random.state = globalRandom;
                Object.DestroyImmediate(root); Object.DestroyImmediate(settings);
                foreach (var sprite in sprites) { var texture = sprite.texture; Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
            }
        }

        static void StateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 1; deadline = next + 120;
            Application.runInBackground = true;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout at " + stage);
                var app = Object.FindAnyObjectByType<FlowApplication>();
                if (app == null || app.IsTransitioning || app.Content == null) return;
                var content = app.Content;
                var layout = content.world != null ? content.world.GetComponent<BattleMapLayout>() : null;
                switch (stage)
                {
                    case 0: app.StartNewRun(); break;
                    case 1:
                        Require(layout != null && layout.terrain.childCount == 1600 && layout.HalfExtents == new Vector2(36, 36), "Formal scene uses default 72x72 map");
                        firstSeed = layout.AppliedSeed;
                        runtimeSettings = Object.Instantiate(layout.settings);
                        runtimeSettings.columns = 20; runtimeSettings.rows = 14; runtimeSettings.cellSize = 1.8f;
                        runtimeSettings.cameraHalfHeight = 5;
                        layout.settings = runtimeSettings; layout.Rebuild();
                        content.player.transform.position = Vector3.zero;
                        break;
                    case 2:
                        Require(layout.terrain.childCount == 280, "Formal scene rebuilds rectangular grid");
                        Require(Mathf.Approximately(layout.battleCamera.orthographicSize, 5), "FlowContent respects configured base view");
                        CheckSpawnBounds(layout, content);
                        CheckWallsAndFlee(layout);
                        Capture(layout.battleCamera, "battle_map_36x25_view5.png");
                        layout.battleCamera.orthographicSize = 14;
                        Capture(layout.battleCamera, "battle_map_36x25_overview.png");
                        content.Torso.Apply(new[] { app.Configuration.Definitions["torso_wideview"].Torso }, content.pipeline);
                        runtimeSettings.cameraHalfHeight = 6;
                        break;
                    case 3:
                        Require(Mathf.Approximately(layout.battleCamera.orthographicSize, 7.2f), "Live base view edit preserves 1.2x torso modifier");
                        content.player.transform.position = new Vector3(17, 11, 0);
                        break;
                    case 4:
                        float halfWidth = layout.battleCamera.orthographicSize * layout.battleCamera.aspect;
                        Require(layout.battleCamera.transform.position.x <= Mathf.Max(0, layout.HalfExtents.x - halfWidth) + .01f,
                            "Camera follow uses resized map and final view multiplier");
                        runtimeSettings.cameraHalfHeight = 50; break;
                    case 5:
                        Require(Vector2.Distance(layout.battleCamera.transform.position, layout.Center) < .01f,
                            "View larger than map centers safely");
                        content.director.CompleteCurrentWave(); break;
                    case 6:
                        Require(content.kind == ContentKind.Rest && content.world.GetComponent<BattleMapLayout>() == null, "Ship rest map remains independent");
                        Require(content.viewSettings == BattleMapAuthoring.EnsureSettings(), "Rest scene references the shared map settings asset");
                        Require(Mathf.Approximately(content.world.GetComponentInChildren<Camera>().orthographicSize,
                            content.viewSettings.CameraHalfHeight * content.Torso.Loadout.View), "Rest uses the shared saved view setting and torso multiplier");
                        app.NextWave(); break;
                    case 7:
                        Require(layout.terrain.childCount == layout.settings.Columns * layout.settings.Rows && layout.AppliedSeed != firstSeed, "New battle loads saved settings and rerolls once");
                        Debug.Log("[Battle Map Checks] PLAY PASS: Bootstrap wiring, rectangular map, spawn bounds, live view, torso multiplier, camera clamp, rest isolation and next-wave reload.");
                        Finish(0); return;
                }
                stage++; next = EditorApplication.timeSinceStartup + .35;
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void CheckSpawnBounds(BattleMapLayout layout, FlowContent content)
        {
            var pick = typeof(EnemySpawner).GetMethod("PickSpawnPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var position in new[] { Vector2.zero, new Vector2(17, 11), new Vector2(-17, -11) })
            {
                content.player.transform.position = position;
                foreach (var spawner in layout.spawners)
                    for (int i = 0; i < 100; i++)
                    {
                        var point = (Vector2)pick.Invoke(spawner, null);
                        Require(Vector2.Distance(point, layout.ClampInside(point)) < .001f, "Enemies spawn inside resized walls even near corners");
                    }
            }
            content.player.transform.position = Vector3.zero;
        }

        static void CheckWallsAndFlee(BattleMapLayout layout)
        {
            Physics2D.SyncTransforms();
            foreach (var direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                var hit = Physics2D.Raycast(layout.Center, direction, 200, 1 << BattleLayers.Resolve(BattleLayers.Wall));
                float expected = direction.x != 0 ? layout.HalfExtents.x : layout.HalfExtents.y;
                Require(hit.collider != null && Mathf.Abs(hit.distance - expected) < .01f, "Physical walls match resized terrain on all four sides");
            }
            var definition = ScriptableObject.CreateInstance<EnemyDefinition>(); definition.moveMode = EnemyMoveMode.Flee;
            var enemy = new GameObject("Flee boundary regression"); enemy.SetActive(false);
            enemy.AddComponent<EnemyData>().Configure(definition);
            var motor = enemy.AddComponent<EnemyMotor>();
            try
            {
                enemy.SetActive(true); enemy.transform.position = new Vector3(90, -90, 0);
                typeof(EnemyMotor).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(motor, null);
                Require(Vector2.Distance(enemy.transform.position, layout.ClampInside(new Vector2(90, -90))) < .001f,
                    "Actual fleeing enemy uses the player's current map, not legacy fixed bounds");
                enemy.SetActive(false); enemy.SetActive(true); enemy.transform.position = new Vector3(-90, 90, 0);
                typeof(EnemyMotor).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(motor, null);
                Require(Vector2.Distance(enemy.transform.position, layout.ClampInside(new Vector2(-90, 90))) < .001f,
                    "Reused enemies refresh their map reference");
            }
            finally { Object.DestroyImmediate(enemy); Object.DestroyImmediate(definition); }
            foreach (var spawner in layout.spawners)
            {
                spawner.ConfigurePlayArea(new Vector2(5, -3), new Vector2(.75f, .75f));
                var clamp = typeof(EnemySpawner).GetMethod("ClampToPlayArea", BindingFlags.Instance | BindingFlags.NonPublic);
                Require((Vector2)clamp.Invoke(spawner, new object[] { new Vector2(100, 100) }) == new Vector2(5, -3),
                    "Very small maps cannot produce inverted spawn bounds");
                spawner.ConfigurePlayArea(layout.Center, layout.HalfExtents);
            }
        }

        static void Capture(Camera camera, string name)
        {
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24); var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes("Logs/Presentation/" + name, pixels.EncodeToPNG());
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); }
        }

        static void Finish(int code)
        {
            if (runtimeSettings != null) Object.DestroyImmediate(runtimeSettings);
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= StateChanged; EditorApplication.Exit(code);
        }
    }
}
