using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Flow
{
    // Cover artwork is 1448 x 1086. World cameras and all UI share its 4:3 viewport.
    [DefaultExecutionOrder(-900)]
    public sealed class GameViewport : MonoBehaviour
    {
        public static readonly Vector2 ReferenceSize = new Vector2(1448, 1086);
        public const float Aspect = 4f / 3f;
        readonly List<Camera> cameras = new List<Camera>();
        readonly List<RectTransform> bars = new List<RectTransform>();
        public RectTransform Content { get; private set; }
        public Rect Viewport { get; private set; } = new Rect(0, 0, 1, 1);

        public void Initialize(Canvas canvas)
        {
            var children = new List<Transform>();
            foreach (Transform child in canvas.transform) children.Add(child);
            Content = Rect("4 by 3 game viewport", canvas.transform);
            Content.gameObject.AddComponent<RectMask2D>();
            foreach (var child in children) child.SetParent(Content, false);
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            // Above nested canvases (including reward flights), and intercepts pointer events in bars.
            var overlay = Rect("Letterbox bars", canvas.transform);
            Stretch(overlay, Vector2.zero, Vector2.one);
            var layer = overlay.gameObject.AddComponent<Canvas>();
            layer.overrideSorting = true; layer.sortingOrder = 32760;
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            foreach (var name in new[] { "Left", "Right", "Bottom", "Top" })
            {
                var rect = Rect(name, overlay); var image = rect.gameObject.AddComponent<Image>();
                image.color = Color.black; image.raycastTarget = true; bars.Add(rect);
            }
            Refresh(Screen.width, Screen.height);
        }
        public void Register(Camera camera)
        {
            if (camera == null || cameras.Contains(camera)) return;
            cameras.Add(camera); Apply(camera);
        }
        public static Rect Calculate(int width, int height)
        {
            float ratio = Mathf.Max(1, width) / (float)Mathf.Max(1, height);
            if (ratio > Aspect)
            { float w = Aspect / ratio; return new Rect((1 - w) * .5f, 0, w, 1); }
            float h = ratio / Aspect; return new Rect(0, (1 - h) * .5f, 1, h);
        }
        void Update() { if (Content != null) Refresh(Screen.width, Screen.height); }
        public void Refresh(int width, int height)
        {
            Viewport = Calculate(width, height);
            Stretch(Content, Viewport.min, Viewport.max);
            Stretch(bars[0], Vector2.zero, new Vector2(Viewport.xMin, 1));
            Stretch(bars[1], new Vector2(Viewport.xMax, 0), Vector2.one);
            Stretch(bars[2], Vector2.zero, new Vector2(1, Viewport.yMin));
            Stretch(bars[3], new Vector2(0, Viewport.yMax), Vector2.one);
            for (int i = cameras.Count - 1; i >= 0; i--)
            { if (cameras[i] == null) cameras.RemoveAt(i); else Apply(cameras[i]); }
        }
        void Apply(Camera camera) { camera.rect = Viewport; camera.aspect = Aspect; }
        static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.gameObject.layer = LayerMask.NameToLayer("UI"); rect.SetParent(parent, false); return rect;
        }
        static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
