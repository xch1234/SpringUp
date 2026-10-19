using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Flow
{
    // A single persistent view reads current scene data; settings can be edited while paused.
    [DefaultExecutionOrder(200)]
    public sealed class BattleHudPresenter : MonoBehaviour
    {
        sealed class Item
        {
            public RectTransform rect;
            public Text label;
            public Image background;
            public CanvasGroup group;
            public Outline outline;
        }
        FlowApplication app;
        BattleHudSettings settings;
        bool ownsSettings;
        RectTransform root;
        readonly Vector3[] corners = new Vector3[4];
        Item wave, time, health, marrow, help;
        Image healthTrack, healthFill;
        readonly List<Image> images = new List<Image>();
        public IReadOnlyList<Image> Images => images;
        public Image HealthFill => healthFill;
        public BattleHudSettings Settings => settings;
        public Text WaveText => wave.label;
        public Text TimeText => time.label;
        public Text HealthText => health.label;
        public Text MarrowText => marrow.label;
        public Text HelpText => help.label;
        public RectTransform MarrowRect => marrow.rect;

        public static BattleHudPresenter Create(FlowApplication owner)
        {
            var go = new GameObject("Battle HUD", typeof(RectTransform), typeof(BattleHudPresenter));
            go.layer = owner.uiRoot.gameObject.layer;
            go.transform.SetParent(owner.uiRoot, false); go.transform.SetAsFirstSibling();
            var view = go.GetComponent<BattleHudPresenter>(); view.app = owner;
            view.root = (RectTransform)go.transform;
            view.root.anchorMin = Vector2.zero; view.root.anchorMax = Vector2.one;
            view.root.offsetMin = view.root.offsetMax = Vector2.zero;
            view.settings = Resources.Load<BattleHudSettings>("BattleHudSettings");
            if (view.settings == null)
            {
                view.settings = ScriptableObject.CreateInstance<BattleHudSettings>();
                view.ownsSettings = true;
            }
            view.wave = view.CreateItem("Wave"); view.time = view.CreateItem("Time");
            view.health = view.CreateItem("Health"); view.marrow = view.CreateItem("Marrow");
            view.help = view.CreateItem("Controls");
            view.CreateHealthBar();
            view.Refresh(); return view;
        }

        void CreateHealthBar()
        {
            var track = new GameObject("Health track", typeof(RectTransform), typeof(Image));
            track.layer = gameObject.layer; track.transform.SetParent(health.rect, false);
            track.transform.SetAsFirstSibling();
            healthTrack = track.GetComponent<Image>(); healthTrack.raycastTarget = false;
            var fill = new GameObject("Red health fill", typeof(RectTransform), typeof(Image));
            fill.layer = gameObject.layer; fill.transform.SetParent(track.transform, false);
            healthFill = fill.GetComponent<Image>(); healthFill.raycastTarget = false;
        }

        void RefreshHealthBar()
        {
            var style = settings.healthBar;
            float inset = Mathf.Clamp(style.padding, 0, Mathf.Min(health.rect.rect.width, health.rect.rect.height) * .45f);
            var track = healthTrack.rectTransform;
            track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
            track.offsetMin = Vector2.one * inset; track.offsetMax = -Vector2.one * inset;
            healthTrack.color = style.emptyColor; healthFill.color = style.fillColor;
            var player = app.Content != null ? app.Content.player : null;
            float fraction = player != null && player.MaxHealth > 0 ? Mathf.Clamp01(player.CurrentHealth / player.MaxHealth) : 0;
            var fill = healthFill.rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(fraction, 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            health.label.enabled = style.showValue;
        }

        Item CreateItem(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.layer = gameObject.layer; go.transform.SetParent(root, false);
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            textGo.layer = gameObject.layer; textGo.transform.SetParent(go.transform, false);
            var label = textGo.GetComponent<Text>(); label.font = app.hud.font;
            label.raycastTarget = false; label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            var rect = label.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10, 3); rect.offsetMax = new Vector2(-10, -3);
            var background = go.GetComponent<Image>(); background.raycastTarget = false;
            var group = go.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            var outline = textGo.GetComponent<Outline>(); outline.effectColor = new Color(0, 0, 0, .8f);
            outline.effectDistance = new Vector2(1, -1);
            return new Item { rect = (RectTransform)go.transform, label = label, background = background, group = group, outline = outline };
        }

        void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (app == null || settings == null) return;
            app.Content?.ApplyEquipmentHudLayout(settings.equipment);
            bool runScene = app.Content != null && (app.Content.kind == ContentKind.Battle || app.Content.kind == ContentKind.Rest)
                && app.Content.player != null && app.Session != null;
            bool visible = runScene && !app.IsTransitioning && (!settings.hideWithWindows || !app.HasModal);
            // Keep layout current even while hidden, so reward destinations survive paused windows.
            Apply(wave, settings.wave, visible); Apply(time, settings.time, visible);
            Apply(health, settings.health, visible); Apply(marrow, settings.marrow, visible); Apply(help, settings.help, visible);
            RefreshHealthBar();
            RefreshImages(visible);
            app.RewardFlights?.SetReceiverTarget(runScene && settings.marrow.visible ? marrow.rect : null);
            if (runScene)
            {
                wave.label.text = Format(settings.wave.text); time.label.text = Format(settings.time.text);
                health.label.text = Format(settings.health.text); marrow.label.text = Format(settings.marrow.text);
                help.label.text = Format(settings.help.text);
            }
        }

        void Apply(Item item, HudTextStyle style, bool visible)
        {
            item.rect.gameObject.SetActive(visible && style.visible);
            ApplyLayout(item.rect, style.anchor, style.offset, Vector2.Max(new Vector2(20, 20), style.size), style.scale);
            item.group.alpha = Mathf.Clamp01(style.opacity);
            item.label.fontSize = Mathf.Clamp(style.fontSize, 8, 96);
            item.label.fontStyle = style.bold ? FontStyle.Bold : FontStyle.Normal;
            item.label.color = style.textColor; item.label.alignment = style.alignment;
            item.background.color = style.backgroundColor; item.outline.enabled = style.outline;
        }

        void RefreshImages(bool visible)
        {
            int count = settings.images != null ? settings.images.Length : 0;
            while (images.Count > count)
            {
                var image = images[images.Count - 1];
                image.gameObject.SetActive(false); Destroy(image.gameObject); images.RemoveAt(images.Count - 1);
            }
            while (images.Count < count)
            {
                var go = new GameObject("HUD Image", typeof(RectTransform), typeof(Image));
                go.layer = gameObject.layer; go.transform.SetParent(root, false);
                // Decorative images sit behind labels and never intercept clicks.
                go.transform.SetAsFirstSibling();
                var image = go.GetComponent<Image>(); image.raycastTarget = false; images.Add(image);
            }
            for (int i = 0; i < count; i++)
            {
                var style = settings.images[i]; var image = images[i];
                image.gameObject.SetActive(visible && style != null && style.visible && style.sprite != null);
                if (style == null) continue;
                image.name = string.IsNullOrEmpty(style.name) ? "HUD Image" : style.name;
                image.sprite = style.sprite; image.preserveAspect = style.preserveAspect;
                var color = style.color; color.a *= Mathf.Clamp01(style.opacity); image.color = color;
                ApplyLayout(image.rectTransform, style.anchor, style.offset, Vector2.Max(Vector2.one, style.size), style.scale);
            }
        }

        void ApplyLayout(RectTransform rect, HudAnchor placement, Vector2 offset, Vector2 size, float scale)
        {
            Vector2 anchor = placement switch
            {
                HudAnchor.TopRight => Vector2.one,
                HudAnchor.BottomLeft => Vector2.zero,
                HudAnchor.BottomRight => Vector2.right,
                HudAnchor.Center => Vector2.one * .5f,
                _ => Vector2.up
            };
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            var position = offset;
            if (placement == HudAnchor.BelowEquipment)
            {
                var equipment = app.Content != null ? app.Content.EquipmentHudRect : null;
                if (equipment != null)
                {
                    equipment.GetWorldCorners(corners);
                    position += (Vector2)root.InverseTransformPoint(corners[0]) - new Vector2(root.rect.xMin, root.rect.yMax);
                }
                else position += new Vector2(16, -330);
            }
            rect.anchoredPosition = position;
            rect.sizeDelta = Vector2.Max(Vector2.one, size);
            rect.localScale = Vector3.one * Mathf.Clamp(scale, .25f, 4);
        }

        string Format(string template)
        {
            var run = app.Session.Run; var content = app.Content;
            int seconds = Mathf.CeilToInt(Mathf.Max(0, content.director != null ? content.director.RemainingTime : 0));
            return (template ?? "").Replace("{wave}", run.Wave.ToString())
                .Replace("{maxWave}", run.MaxWave.ToString())
                .Replace("{health}", content.player.CurrentHealth.ToString("0", CultureInfo.InvariantCulture))
                .Replace("{maxHealth}", content.player.MaxHealth.ToString("0", CultureInfo.InvariantCulture))
                .Replace("{marrow}", run.Marrow.ToString())
                .Replace("{time}", content.director == null ? "休整中" : $"{seconds / 60:00}:{seconds % 60:00}")
                .Replace("{interaction}", InteractionHint())
                .Replace("{inventoryKey}", GameSettings.InventoryHint)
                .Replace("{seconds}", seconds.ToString());
        }

        string InteractionHint()
        {
            if (app.Content.kind != ContentKind.Rest) return "";
            string key = GameSettings.Display(GameBinding.Interact);
            if (app.CanInteractWithLaboratory()) return key + "：实验室装配";
            if (app.CanInteractWithShop()) return key + "：商店";
            return key + "：靠近商店或实验室互动";
        }

        void OnDestroy() { if (ownsSettings && settings != null) Destroy(settings); }
    }
}
