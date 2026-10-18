using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    // Run with -executeMethod SpringUp.EditorChecks.ShipWalkBoundsChecks.Run (without -quit).
    // Use an isolated batch project: this check creates and reloads a temporary scene.
    public static class ShipWalkBoundsChecks
    {
        private const string ScenePath = "Assets/ShipWalkBoundsRegressionTemp.unity";
        private static readonly List<string> Problems = new List<string>();
        private static ShipWalkBounds bounds;
        private static Transform[] wallIds;
        private static int phase;
        private static int ticks;
        private static double deadline;
        private static Vector2 oldSize;

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch editor.");
            Application.logMessageReceived += Capture;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                bounds = new GameObject("Ship bounds regression").AddComponent<ShipWalkBounds>();
                Require(bounds.transform.childCount == 0, "OnEnable/OnValidate must defer hierarchy changes in edit mode.");
                bounds.Rebuild();
                CheckWalls();
                wallIds = WallIds();
                oldSize = Wall(1).GetComponent<BoxCollider2D>().size;
                ChangeSize(new Vector2(40, 24));
                Require(oldSize == Wall(1).GetComponent<BoxCollider2D>().size, "Inspector validation must not rebuild synchronously.");

                // Unity can invoke OnValidate from its loading thread.
                Exception workerFailure = null;
                var validate = typeof(ShipWalkBounds).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
                var worker = new Thread(() =>
                {
                    try { for (var i = 0; i < 20; i++) validate.Invoke(bounds, null); }
                    catch (Exception error) { workerFailure = error; }
                });
                worker.Start();
                Require(worker.Join(5000), "Validation on a loading thread must not block.");
                Require(workerFailure == null, "Validation must not invoke main-thread Unity APIs: " + workerFailure);
                deadline = EditorApplication.timeSinceStartup + 30;
                EditorApplication.update += Advance;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Advance()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timed out waiting for editor updates.");
                if (++ticks < 2) return;
                ticks = 0;
                switch (phase++)
                {
                    case 0:
                        CheckWalls();
                        Require(WallIds().SequenceEqual(wallIds), "Validation must reuse existing walls.");
                        Require(Wall(1).GetComponent<BoxCollider2D>().size != oldSize, "Deferred rebuild must apply Inspector changes.");
                        var decoration = new GameObject("Keep decoration");
                        decoration.transform.SetParent(bounds.transform);
                        new GameObject("__ShipEdge_1").transform.SetParent(bounds.transform);
                        Object.DestroyImmediate(Wall(3).GetComponent<BoxCollider2D>());
                        bounds.Rebuild();
                        CheckWalls();
                        Require(decoration != null && bounds.transform.childCount == 6, "Repair duplicates/missing colliders without deleting decorations.");
                        Require(WallIds().SequenceEqual(wallIds), "Repair must preserve valid wall objects.");
                        Require(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath), "Failed to save test scene.");
                        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                        bounds = Object.FindAnyObjectByType<ShipWalkBounds>();
                        wallIds = WallIds();
                        break;
                    case 1:
                        CheckWalls();
                        Require(WallIds().SequenceEqual(wallIds), "Scene loading must not replace baked walls.");
                        Require(!SceneManager.GetActiveScene().isDirty, "Opening an unchanged scene must not dirty it.");
                        bounds.enabled = false;
                        oldSize = Wall(1).GetComponent<BoxCollider2D>().size;
                        ChangeSize(new Vector2(50, 28));
                        break;
                    case 2:
                        Require(Wall(1).GetComponent<BoxCollider2D>().size == oldSize, "Disabled components must not rebuild in editor callbacks.");
                        bounds.enabled = true;
                        break;
                    case 3:
                        CheckWalls();
                        Require(Wall(1).GetComponent<BoxCollider2D>().size != oldSize, "Re-enabling must apply pending changes.");
                        var pending = new GameObject("Destroyed before update").AddComponent<ShipWalkBounds>();
                        Object.DestroyImmediate(pending.gameObject);
                        break;
                    default:
                        Require(Problems.Count == 0, string.Join("\n", Problems));
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void ChangeSize(Vector2 size)
        {
            var serialized = new SerializedObject(bounds);
            serialized.FindProperty("bodySize").vector2Value = size;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform Wall(int index) => bounds.transform.Find("__ShipEdge_" + index);
        private static Transform[] WallIds() => Enumerable.Range(0, 5).Select(Wall).ToArray();

        private static void CheckWalls()
        {
            var colliders = bounds.GetComponentsInChildren<BoxCollider2D>();
            Require(colliders.Length == 5, "Exactly five ship wall colliders are required.");
            Physics2D.SyncTransforms();
            var points = bounds.Points;
            for (var i = 0; i < 5; i++)
            {
                var box = Wall(i).GetComponent<BoxCollider2D>();
                Require(box.enabled && !box.isTrigger && box.gameObject.layer == LayerMask.NameToLayer(BattleLayers.Wall), "Walls must block movement on the wall layer.");
                var midpoint = (points[i] + points[i + 1]) * .5f;
                var outward = (Vector2)box.transform.position - midpoint;
                var hit = Physics2D.Raycast(midpoint - outward.normalized, outward.normalized, 3, 1 << box.gameObject.layer);
                Require(hit.collider == box, "Each edge must stop an outward ray from inside the ship.");
            }
        }

        private static void Capture(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert
                || (type == LogType.Warning && (stack.Contains("ShipWalkBounds") || message.Contains("SendMessage"))))
                Problems.Add(message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Finish(Exception failure)
        {
            EditorApplication.update -= Advance;
            Application.logMessageReceived -= Capture;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
            if (failure == null)
                Debug.Log("[ShipWalkBounds Checks] PASS: deferred/thread-safe validation, wall reuse and repair, scene reload, disabled/destroyed callbacks, five blocking edges.");
            else
                Debug.LogException(failure);
            EditorApplication.Exit(failure == null ? 0 : 1);
        }
    }
}
