using System;
using System.Linq;
using System.Reflection;
using SpringUp.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class BattleHudChecks
    {
        const string Key = "SpringUp.BattleHudChecks";
        static int stage;
        static double next, deadline;
        static BattleHudSettings settings;
        static IDisposable pause;
        static string pausedTime;
        static float remaining;
        static TideBorne.Battle.BattleMapSettings restView;
        static readonly Vector3[] Corners = new Vector3[4];

        static BattleHudChecks()
        { if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += StateChanged; }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            BattleHudSettingsEditor.EnsureAsset();
            System.IO.Directory.CreateDirectory("Logs/Presentation");
            EditorSceneManager.OpenScene(FlowSceneBuilder.BootstrapPath);
            SessionState.SetBool(Key, true); EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.EnterPlaymode();
        }

        static void StateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = 0; next = EditorApplication.timeSinceStartup + 1; deadline = next + 120;
            Application.runInBackground = true; EditorApplication.update += Tick;
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Battle HUD Checks] " + message); }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout at stage " + stage);
                var app = Object.FindAnyObjectByType<FlowApplication>();
                if (app == null || app.IsTransitioning || app.Content == null) return;
                var hud = app.BattleHud; var content = app.Content;
                switch (stage)
                {
                    case 0:
                        Require(!hud.WaveText.gameObject.activeInHierarchy, "No battle HUD on main menu");
                        app.StartNewRun(); break;
                    case 1:
                        Require(!app.hud.enabled && app.hud.text == "", "Old top status line suppressed in battle");
                        settings = Object.Instantiate(hud.Settings);
                        typeof(BattleHudPresenter).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hud, settings);
                        Require(hud.WaveText.text == "波次 1/7", "Wave data displayed");
                        var art = Resources.Load<SpringUp.Laboratory.OrganArtCatalog>("OrganArtCatalog");
                        foreach (var entry in art.entries)
                        {
                            if (!app.Configuration.Definitions.TryGetValue(entry.definitionId, out var definition)) continue;
                            var view = OrganUiBuilder.Icon(hud.transform, "Icon verification", art, 0, 0, 50, false);
                            view.Bind(new SpringUp.Laboratory.LaboratoryItem("art-check", definition));
                            Require(view.icon.enabled && view.icon.sprite == entry.sprite && view.fallback.text == "", "Playable organ displays mapped art: " + entry.definitionId);
                            Object.DestroyImmediate(view.gameObject);
                        }
                        Require(Mathf.Approximately(hud.HealthFill.rectTransform.anchorMax.x, 1), "Full health fills the entire bar");
                        remaining = content.director.RemainingTime;
                        break;
                    case 2:
                        Require(content.director.RemainingTime < remaining, "Real wave timer runs");
                        content.player.TakeDamage(17); app.Session.Run.CreditKill("hud-test", 80);
                        hud.Refresh();
                        Require(hud.HealthText.text == $"生命 {content.player.CurrentHealth:0}/{content.player.MaxHealth:0}", "Health responds to damage");
                        Require(Mathf.Approximately(hud.HealthFill.rectTransform.anchorMax.x, content.player.CurrentHealth / content.player.MaxHealth), "Red bar shrinks with actual damage");
                        Require(hud.HealthFill.color.r > .8f && hud.HealthFill.color.g < .1f && !hud.HealthFill.raycastTarget, "Red fill does not intercept clicks");
                        Require(hud.MarrowText.text == "骨髓 80", "Marrow reflects actual run balance");
                        pause = app.Pause.Acquire(); pausedTime = hud.TimeText.text;
                        Capture(app, 1280, 720, "battle_hud_1280.png");
                        Capture(app, 1024, 768, "battle_hud_1024.png");
                        break;
                    case 3:
                        Require(hud.TimeText.text == pausedTime, "Countdown freezes while paused");
                        var equipment = content.EquipmentHudRect;
                        settings.equipment.offset = new Vector2(76, -90);
                        settings.equipment.size = new Vector2(424, 212);
                        settings.equipment.scale = .85f; settings.equipment.opacity = .45f;
                        content.player.ResetHealthToMax(); hud.Refresh();
                        Require(Mathf.Approximately(hud.HealthFill.rectTransform.anchorMax.x, 1), "Healing refills the bar");
                        content.player.TakeDamage(content.player.MaxHealth * .5f); hud.Refresh();
                        Canvas.ForceUpdateCanvases();
                        Require(Mathf.Approximately(hud.HealthFill.rectTransform.rect.width / ((RectTransform)hud.HealthFill.transform.parent).rect.width, .5f), "Half health occupies half of the track");
                        Require(equipment.anchoredPosition == settings.equipment.offset && Mathf.Approximately(equipment.rect.width * equipment.localScale.x, 424 * .85f)
                            && Mathf.Approximately(equipment.rect.height * equipment.localScale.y, 212 * .85f), "Equipment position and dimensions update live");
                        Require(Mathf.Approximately(equipment.GetComponent<CanvasGroup>().alpha, .45f), "Equipment opacity updates live");
                        Capture(app, 1280, 720, "battle_hud_custom_equipment.png");
                        settings.healthBar.showValue = false; hud.Refresh(); Require(!hud.HealthText.enabled, "Blood bar can hide numeric label");
                        settings.healthBar.showValue = true;
                        settings.healthBar.fillColor = Color.magenta; hud.Refresh(); Require(hud.HealthFill.color == Color.magenta, "Health color is configurable");
                        settings.equipment.anchor = HudAnchor.BottomRight; settings.equipment.offset = new Vector2(-24, 24); hud.Refresh();
                        Require(equipment.anchorMin == Vector2.right && equipment.pivot == Vector2.right, "Equipment supports alternate screen anchors");
                        settings.equipment.anchor = HudAnchor.TopLeft; settings.equipment.offset = new Vector2(76, -90);
                        settings.equipment.visible = false; hud.Refresh(); Require(!equipment.gameObject.activeSelf, "Equipment visibility toggle");
                        settings.equipment.visible = true; settings.equipment.opacity = 0; hud.Refresh();
                        Require(!equipment.GetComponent<CanvasGroup>().blocksRaycasts, "Transparent equipment does not block clicks");
                        settings.equipment.opacity = .45f;
                        settings.marrow.opacity = .35f; settings.marrow.fontSize = 36;
                        settings.marrow.size = new Vector2(400, 64); settings.marrow.scale = 1.1f;
                        settings.marrow.offset = new Vector2(15, -18);
                        settings.marrow.text = "资源：{marrow}";
                        settings.wave.anchor = HudAnchor.TopLeft; settings.wave.offset = new Vector2(600, -28);
                        settings.wave.text = "第 {wave} 波 / 共 {maxWave} 波";
                        settings.health.text = "HP {health} / {maxHealth}\n战斗中";
                        settings.health.size = new Vector2(350, 90);
                        settings.time.text = "还剩 {seconds} 秒";
                        hud.Refresh(); Canvas.ForceUpdateCanvases();
                        Require(hud.MarrowText.text == "资源：80" && hud.MarrowText.fontSize == 36, "Live template and font size update");
                        Require(Mathf.Approximately(hud.MarrowRect.GetComponent<CanvasGroup>().alpha, .35f), "Live opacity update");
                        Require(hud.MarrowRect.sizeDelta == new Vector2(400, 64) && Mathf.Approximately(hud.MarrowRect.localScale.x, 1.1f), "Live dimensions and scale update");
                        Require(hud.WaveText.text == "第 1 波 / 共 7 波" && hud.HealthText.text.Contains("\n战斗中"), "Multiple tokens and multiline labels");
                        equipment.GetWorldCorners(Corners); var bottom = Corners[0];
                        hud.MarrowRect.GetWorldCorners(Corners);
                        var expected = bottom + hud.transform.TransformVector(settings.marrow.offset);
                        Require(Vector3.Distance(Corners[1], expected) < .01f, "Marrow follows moved/scaled equipment lower edge");
                        Require(Vector3.Distance(app.RewardFlights.Receiver.position, hud.MarrowRect.TransformPoint(hud.MarrowRect.rect.center)) < .01f,
                            "Reward destination follows the customized marrow label");
                        settings.marrow.visible = false; hud.Refresh(); Require(!hud.MarrowRect.gameObject.activeSelf, "Individual visibility toggle");
                        settings.marrow.visible = true;
                        settings.time.text = "{typo} {seconds}"; hud.Refresh();
                        Require(hud.TimeText.text.StartsWith("{typo} "), "Unknown template token does not throw");
                        var defaults = ScriptableObject.CreateInstance<BattleHudSettings>();
                        EditorUtility.CopySerialized(defaults, settings); Object.DestroyImmediate(defaults);
                        hud.Refresh();
                        pause.Dispose(); pause = null;
                        app.ToggleInventory(); hud.Refresh(); Require(!hud.WaveText.gameObject.activeInHierarchy, "Inventory hides HUD by default");
                        settings.hideWithWindows = false; hud.Refresh(); Require(hud.WaveText.gameObject.activeInHierarchy, "Modal visibility is configurable");
                        app.CloseTop(); settings.hideWithWindows = true;
                        content.director.CompleteCurrentWave(); break;
                    case 4:
                        hud.Refresh();
                        Require(content.kind == ContentKind.Rest && !app.hud.enabled, "Shared HUD replaces the duplicate legacy status line in rest");
                        Require(hud.WaveText.gameObject.activeInHierarchy && hud.TimeText.gameObject.activeInHierarchy
                            && hud.HealthText.gameObject.activeInHierarchy && hud.MarrowText.gameObject.activeInHierarchy,
                            "Wave, rest timer, health and marrow use the shared HUD in rest");
                        Require(hud.TimeText.text == "剩余时间 休整中" && hud.MarrowText.text == "骨髓 " + app.Session.Run.Marrow,
                            "Rest has a clear timer status and current balance");
                        var originalPosition = content.player.transform.position;
                        content.player.transform.position = content.Laboratory.position; hud.Refresh();
                        Require(hud.HelpText.text.Contains("E：实验室装配"), "Shared controls retain laboratory interaction hint");
                        content.player.transform.position = content.Merchant.position; hud.Refresh();
                        Require(hud.HelpText.text.Contains("E：商店"), "Shared controls retain shop interaction hint");
                        content.player.transform.position = originalPosition;
                        Require(content.EquipmentHudRect.gameObject.activeSelf, "Equipment survives rest transition");
                        settings.equipment.offset = new Vector2(45, -75); hud.Refresh();
                        Require(content.EquipmentHudRect.anchoredPosition == settings.equipment.offset, "Equipment configuration applies in rest");
                        settings.wave.offset = new Vector2(-90, -32); settings.wave.opacity = .7f; hud.Refresh();
                        Require(((RectTransform)hud.WaveText.transform.parent).anchoredPosition == settings.wave.offset
                            && Mathf.Approximately(hud.WaveText.GetComponentInParent<CanvasGroup>().alpha, .7f),
                            "Wave position and opacity settings apply in rest");
                        settings.wave.offset = new Vector2(-24, -24); settings.wave.opacity = 1; hud.Refresh();
                        Capture(app, 1280, 720, "rest_shared_hud.png");
                        app.ToggleInventory(); hud.Refresh();
                        Require(!hud.HealthText.gameObject.activeInHierarchy, "Rest modal also hides the shared HUD");
                        app.CloseTop(); hud.Refresh();
                        Require(Mathf.Approximately(content.world.GetComponentInChildren<Camera>().orthographicSize,
                            content.viewSettings.CameraHalfHeight * content.Torso.Loadout.View), "Rest camera uses shared map view");
                        restView = Object.Instantiate(content.viewSettings); content.viewSettings = restView;
                        restView.cameraHalfHeight = 7.25f; break;
                    case 5:
                        Require(Mathf.Approximately(content.world.GetComponentInChildren<Camera>().orthographicSize,
                            7.25f * content.Torso.Loadout.View), "Rest camera responds live to view changes");
                        app.NextWave(); break;
                    case 6:
                        hud.Refresh();
                        Require(hud.WaveText.text == "波次 2/7" && hud.TimeText.text.Contains(":"), "HUD rebinds next wave and its timer");
                        Require(Object.FindObjectsByType<BattleHudPresenter>().Length == 1, "No duplicate HUD across scenes");
                        Require(hud.MarrowText.text == "骨髓 " + app.Session.Run.Marrow, "Wave reward reflected in next battle");
                        Require(Mathf.Approximately(hud.HealthFill.rectTransform.anchorMax.x, 1), "Next wave resets the health bar");
                        Require(content.EquipmentHudRect.anchoredPosition == settings.equipment.offset, "Equipment layout persists into next wave");
                        Debug.Log("[Battle HUD Checks] PASS: mapped organ icons, shared rest view and live changes, health/equipment HUD, two resolutions, modal/rest/next-wave lifecycle.");
                        Finish(0); return;
                }
                stage++; next = EditorApplication.timeSinceStartup + .45;
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void Capture(FlowApplication app, int width, int height, string name)
        {
            var camera = app.Content.world.GetComponentInChildren<Camera>();
            var canvas = app.hud.GetComponentInParent<Canvas>();
            var mode = canvas.renderMode; var originalCamera = canvas.worldCamera; float distance = canvas.planeDistance;
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(width, height, 24); var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); app.BattleHud.Refresh(); Canvas.ForceUpdateCanvases();
                foreach (var label in new[] { app.BattleHud.WaveText, app.BattleHud.TimeText, app.BattleHud.HealthText, app.BattleHud.MarrowText })
                {
                    label.rectTransform.GetWorldCorners(Corners);
                    foreach (var corner in Corners)
                    {
                        var point = camera.WorldToViewportPoint(corner);
                        Require(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1, "HUD inside " + width + "x" + height);
                    }
                    Require(label.cachedTextGenerator.characterCountVisible >= label.text.Length, "Default label fits its text box");
                    Require(!label.raycastTarget, "HUD never intercepts pointer input");
                }
                foreach (var label in new[] { app.BattleHud.WaveText, app.BattleHud.TimeText, app.BattleHud.HealthText })
                {
                    var point = camera.WorldToViewportPoint(label.rectTransform.position);
                    Require(point.x > .7f && point.y > .7f, "Primary stats appear in top-right");
                }
                app.Content.EquipmentHudRect.GetWorldCorners(Corners); float equipmentBottom = camera.WorldToViewportPoint(Corners[0]).y;
                app.BattleHud.MarrowRect.GetWorldCorners(Corners);
                Require(camera.WorldToViewportPoint(Corners[1]).y < equipmentBottom, "Marrow is below equipment");
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes("Logs/Presentation/" + name, pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = originalCamera; canvas.planeDistance = distance;
                camera.targetTexture = previous; RenderTexture.active = active;
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                Canvas.ForceUpdateCanvases();
            }
        }

        static void Finish(int code)
        {
            if (restView != null) Object.DestroyImmediate(restView);
            pause?.Dispose(); pause = null;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= StateChanged;
            EditorApplication.Exit(code);
        }
    }
}
