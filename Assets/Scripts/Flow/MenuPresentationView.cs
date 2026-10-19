using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpringUp.Flow
{
    // All geometry is authored in reference pixels and scaled as a single group.
    public sealed class MenuPresentationView : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        MenuPresentationSettings config;
        RectTransform layout;
        Image backing, artwork, background, hintBox;
        Text hint;
        CanvasGroup hintGroup;
        Button[] buttons;
        Font defaultFont;
        bool settlement, won, consumed, validPress;
        float openedAt;
        Action returnToTitle;

        public static FlowWindow Create(Transform parent, bool settlement, Font font)
        {
            var root = Rect(settlement ? "Settlement artwork" : "MainMenu artwork", parent);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var flow = root.gameObject.AddComponent<FlowWindow>();
            flow.group = root.gameObject.AddComponent<CanvasGroup>(); flow.body = root;
            var view = root.gameObject.AddComponent<MenuPresentationView>();
            view.config = MenuPresentationSettings.Load();
            if (view.config == null) throw new InvalidOperationException("Missing Resources/MenuPresentationSettings.asset");
            view.settlement = settlement; view.defaultFont = font;
            view.backing = root.gameObject.AddComponent<Image>(); view.backing.raycastTarget = true;
            if (settlement) view.background = Picture("Background", root);
            view.layout = Rect("Reference layout", root);
            view.artwork = Picture("Artwork", view.layout);
            if (settlement)
            {
                view.hintBox = Picture("Return hint", view.layout);
                view.hintGroup = view.hintBox.gameObject.AddComponent<CanvasGroup>();
                var textRect = Rect("Text", view.hintBox.transform);
                textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;
                view.hint = textRect.gameObject.AddComponent<Text>();
                view.hint.alignment = TextAnchor.MiddleCenter; view.hint.raycastTarget = false;
                view.hint.supportRichText = false;
            }
            else
            {
                view.buttons = new[] { view.HitButton("Start"), view.HitButton("Settings"), view.HitButton("Quit") };
                root.gameObject.AddComponent<MenuSettingsView>().buttons = view.buttons;
            }
            view.Refresh(); return flow;
        }
        public void BindSettlement(bool victory, Action onReturn)
        { won = victory; returnToTitle = onReturn; openedAt = Time.unscaledTime; consumed = validPress = false; Refresh(); }
        public void OnPointerDown(PointerEventData e)
        {
            validPress = settlement && !consumed && e.button == PointerEventData.InputButton.Left &&
                Time.unscaledTime - openedAt >= Mathf.Max(0, config.inputDelay) &&
                GetComponent<CanvasGroup>().interactable;
        }
        public void OnPointerClick(PointerEventData e)
        {
            if (!validPress || consumed || e.button != PointerEventData.InputButton.Left) return;
            validPress = false; consumed = true; returnToTitle?.Invoke();
        }
        void LateUpdate() { if (config != null) Refresh(); }
        public void Refresh()
        {
            var screen = ((RectTransform)transform).rect.size;
            backing.color = settlement ? config.settlementBacking : config.menuBacking;
            var design = settlement ? config.settlementReferenceSize : config.cover.size;
            design = new Vector2(Mathf.Max(1, design.x), Mathf.Max(1, design.y));
            layout.sizeDelta = design;
            layout.localScale = Vector3.one * Mathf.Min(screen.x / design.x, screen.y / design.y);
            if (!settlement) layout.localScale *= Mathf.Max(.1f, config.menuScale);
            var art = settlement ? (won ? config.victory : config.defeat) : config.cover;
            Apply(artwork, art);
            if (!settlement)
            {
                layout.anchoredPosition = config.cover.position;
                artwork.rectTransform.anchoredPosition = Vector2.zero;
                ApplyHit(buttons[0], config.start); ApplyHit(buttons[1], config.settings); ApplyHit(buttons[2], config.quit);
                return;
            }
            Apply(background, config.background);
            var size = config.background.size;
            float x = screen.x / Mathf.Max(1, size.x), y = screen.y / Mathf.Max(1, size.y);
            background.rectTransform.localScale = Vector3.one * (config.backgroundFill ? Mathf.Max(x, y) : Mathf.Min(x, y));
            hintBox.rectTransform.sizeDelta = config.hintSize;
            hintBox.rectTransform.anchoredPosition = art.position + new Vector2(0, -art.size.y * .5f) + config.hintOffset;
            hintBox.color = config.hintBacking;
            hint.text = config.hintText; hint.font = config.hintFont != null ? config.hintFont : defaultFont;
            hint.fontSize = Mathf.Max(1, config.hintFontSize); hint.color = config.hintColor;
            hintGroup.alpha = config.HintAlpha(Time.unscaledTime - openedAt);
        }
        void ApplyHit(Button button, MenuPresentationSettings.HitArea area)
        {
            var rect = (RectTransform)button.transform; rect.anchoredPosition = area.position; rect.sizeDelta = area.size;
            var colors = button.colors;
            colors.normalColor = config.showHitAreas ? new Color(0, 1, .5f, .25f) : Color.clear;
            colors.highlightedColor = colors.selectedColor = config.hover;
            colors.pressedColor = new Color(config.hover.r, config.hover.g, config.hover.b, Mathf.Clamp01(config.hover.a * 2));
            button.colors = colors;
        }
        static void Apply(Image image, MenuPresentationSettings.Artwork art)
        {
            image.sprite = art.sprite; image.enabled = art.sprite != null;
            image.preserveAspect = art.preserveAspect;
            var color = art.tint; color.a *= art.opacity; image.color = color;
            image.rectTransform.anchoredPosition = art.position; image.rectTransform.sizeDelta = art.size;
        }
        Button HitButton(string name)
        {
            var image = Picture(name, layout); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<MenuButton>(); button.targetGraphic = image; return button;
        }
        static Image Picture(string name, Transform parent)
        { var image = Rect(name, parent).gameObject.AddComponent<Image>(); image.raycastTarget = false; return image; }
        static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.gameObject.layer = LayerMask.NameToLayer("UI"); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); return rect;
        }
    }
}
