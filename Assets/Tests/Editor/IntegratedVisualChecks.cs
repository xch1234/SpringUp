using System;
using System.Collections.Generic;
using System.IO;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class IntegratedVisualChecks
    {
        private const string Key = "SpringUp.Visual.Checks";
        private static double next;
        private static int stage;
        static IntegratedVisualChecks()
        { if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += StateChanged; }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅用于隔离副本。");
            EditorSceneManager.OpenScene(SpringUpDemoBuilder.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange value)
        {
            if (value != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += CaptureStages;
        }
        private static void CaptureStages()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                var driver = Object.FindFirstObjectByType<SpringUpRunDriver>();
                if (stage == 0)
                {
                    var enemy = Object.Instantiate(Resources.Load<GameObject>("Enemies/Normal"));
                    enemy.SetActive(true);
                    enemy.transform.position = driver.player.transform.position + new Vector3(2, 0, 0);
                    driver.pipeline.GetBody(SpringUp.Organs.BodyPart.Hand).SetSlot(0,
                        new SpringUp.Organs.OrganInstance(driver.Definitions["tnt"]));
                    driver.pipeline.GetBody(SpringUp.Organs.BodyPart.Hand).ResetTiming();
                    driver.pipeline.GetBody(SpringUp.Organs.BodyPart.Hand).Tick(.15f);
                    Capture("battle-1280", 1280, 720);
                    Capture("battle-1920", 1920, 1080);
                    Capture("battle-1024", 1024, 768);
                    driver.director.CompleteCurrentWave();
                    stage++; next = EditorApplication.timeSinceStartup + 1; return;
                }
                Capture("lab-1280", 1280, 720);
                Capture("lab-1920", 1920, 1080);
                Capture("lab-1024", 1024, 768);
                Debug.Log("[Integrated Visual Checks] PASS: nonblank battle and lab renders at 1280x720, 1920x1080, 1024x768.");
                Finish(0);
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }
        private static void Capture(string name, int width, int height)
        {
            var camera = Camera.main;
            var target = new RenderTexture(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousRect = camera.rect;
            var previousMask = camera.cullingMask;
            var previousActive = RenderTexture.active;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var previous = new List<(Canvas canvas, RenderMode mode, Camera camera, float distance, int order)>();
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var uiTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var uiTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var uiObject = new GameObject("Capture UI camera");
            var uiCamera = uiObject.AddComponent<Camera>();
            uiCamera.CopyFrom(camera); uiCamera.enabled = false;
            uiCamera.rect = new Rect(0, 0, 1, 1);
            uiCamera.clearFlags = CameraClearFlags.SolidColor; uiCamera.backgroundColor = Color.clear;
            uiCamera.cullingMask = 1 << 31; uiCamera.targetTexture = uiTarget;
            var layers = new Dictionary<GameObject, int>();
            try
            {
                camera.targetTexture = target;
                camera.cullingMask &= ~(1 << 31);
                var driver = Object.FindFirstObjectByType<SpringUpRunDriver>();
                float scale = Mathf.Min(width / 1440f, height / 900f);
                camera.rect = driver.Run.Phase == "combat" ? new Rect(0, 0, 1, 1 - 44f * scale / height) : new Rect(0, 0, 1, 1);
                foreach (var canvas in canvases)
                {
                    foreach (var child in canvas.GetComponentsInChildren<Transform>(true))
                        if (!layers.ContainsKey(child.gameObject))
                        { layers.Add(child.gameObject, child.gameObject.layer); child.gameObject.layer = 31; }
                    previous.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance, canvas.sortingOrder));
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = uiCamera; canvas.planeDistance = 1;
                    canvas.sortingOrder += 100;
                    var scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler != null) canvas.scaleFactor = Mathf.Min(width / scaler.referenceResolution.x, height / scaler.referenceResolution.y);
                }
                Canvas.ForceUpdateCanvases();
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                else camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                // Overlay canvases cover the full screen, not the restricted battle viewport.
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(uiCamera, new RenderPipeline.StandardRequest { destination = uiTarget });
                else uiCamera.Render();
                RenderTexture.active = uiTarget;
                uiTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0); uiTexture.Apply();
                var worldPixels = texture.GetPixels();
                var uiPixels = uiTexture.GetPixels();
                for (int i = 0; i < worldPixels.Length; i++)
                    worldPixels[i] = uiPixels[i] + worldPixels[i] * (1 - uiPixels[i].a);
                texture.SetPixels(worldPixels); texture.Apply();
                var pixels = texture.GetPixels32();
                var distinct = new HashSet<Color32>();
                for (int i = 0; i < pixels.Length; i += 17) distinct.Add(pixels[i]);
                if (distinct.Count < 16) throw new InvalidOperationException("画面疑似空白：" + name);
                Directory.CreateDirectory("Logs/VisualChecks");
                File.WriteAllBytes("Logs/VisualChecks/" + name + ".png", texture.EncodeToPNG());
            }
            finally
            {
                foreach (var item in previous)
                { item.canvas.renderMode = item.mode; item.canvas.worldCamera = item.camera; item.canvas.planeDistance = item.distance; item.canvas.sortingOrder = item.order; }
                foreach (var item in layers) if (item.Key != null) item.Key.layer = item.Value;
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                camera.rect = previousRect; camera.cullingMask = previousMask;
                Object.DestroyImmediate(uiObject); Object.DestroyImmediate(uiTexture); Object.DestroyImmediate(uiTarget);
                Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
            }
        }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= CaptureStages;
            EditorApplication.playModeStateChanged -= StateChanged; EditorApplication.Exit(code);
        }
    }
}
