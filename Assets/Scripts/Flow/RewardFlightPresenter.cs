using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace SpringUp.Flow
{
    // Pure presentation. Stores screen-space origins, never an enemy, camera or reward callback.
    public sealed class RewardFlightPresenter : MonoBehaviour
    {
        sealed class Flight
        {
            public Image image;
            public Vector2 startUV;
            public float elapsed;
        }
        readonly List<Flight> active = new List<Flight>();
        readonly Stack<Flight> idle = new Stack<Flight>();
        RewardFlightSettings settings;
        PauseService pause;
        RectTransform layer;
        Canvas canvas;
        public RectTransform Receiver { get; private set; }
        public int ActiveCount => active.Count;
        public static RewardFlightPresenter Create(Transform uiRoot, RewardFlightSettings settings, PauseService pause)
        {
            if (uiRoot == null || settings == null) return null;
            var root = new GameObject("Reward flights", typeof(RectTransform), typeof(Canvas), typeof(RewardFlightPresenter));
            root.transform.SetParent(uiRoot, false);
            var presenter = root.GetComponent<RewardFlightPresenter>();
            presenter.settings = settings; presenter.pause = pause;
            presenter.layer = (RectTransform)root.transform;
            presenter.layer.anchorMin = Vector2.zero; presenter.layer.anchorMax = Vector2.one;
            presenter.layer.offsetMin = presenter.layer.offsetMax = Vector2.zero;
            presenter.canvas = uiRoot.GetComponentInParent<Canvas>();
            var overlay = root.GetComponent<Canvas>(); overlay.overrideSorting = true; overlay.sortingOrder = 100;
            var receiver = new GameObject("Reward receiver", typeof(RectTransform));
            receiver.transform.SetParent(root.transform, false);
            presenter.Receiver = (RectTransform)receiver.transform;
            presenter.Receiver.anchorMin = presenter.Receiver.anchorMax = settings.receiverAnchor;
            presenter.Receiver.anchoredPosition = Vector2.zero;
            presenter.Receiver.sizeDelta = Vector2.zero;
            return presenter;
        }
        public void Spawn(Vector2 worldPosition, Camera battleCamera, bool organ)
        {
            if (battleCamera == null || canvas == null || settings == null) return;
            var screen = battleCamera.WorldToScreenPoint(worldPosition);
            if (screen.z < 0) { screen.x = Screen.width - screen.x; screen.y = Screen.height - screen.y; }
            var screenPoint = new Vector2(Mathf.Clamp(screen.x, 0, Screen.width), Mathf.Clamp(screen.y, 0, Screen.height));
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screenPoint, uiCamera, out var local)) return;
            var rect = layer.rect;
            if (rect.width <= 0 || rect.height <= 0) return;
            SpawnAtViewport(new Vector2(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x), Mathf.InverseLerp(rect.yMin, rect.yMax, local.y)), organ);
        }
        // Also allows deterministic lifecycle tests without depending on a battle camera.
        public void SpawnAtViewport(Vector2 origin, bool organ)
        {
            if (settings == null || active.Count >= Mathf.Max(1, settings.maxConcurrent)) return;
            var sprite = organ ? settings.organSprite : settings.marrowSprite;
            if (sprite == null) return; // Missing artwork only skips presentation; rewards are already committed.
            Flight flight;
            if (idle.Count > 0) flight = idle.Pop();
            else
            {
                var go = new GameObject("Reward icon", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(layer, false);
                flight = new Flight { image = go.GetComponent<Image>() };
                flight.image.raycastTarget = false;
                flight.image.preserveAspect = true;
            }
            flight.startUV = new Vector2(Mathf.Clamp01(origin.x), Mathf.Clamp01(origin.y)); flight.elapsed = 0;
            flight.image.sprite = sprite;
            flight.image.color = organ ? settings.organColor : settings.marrowColor;
            flight.image.rectTransform.sizeDelta = Vector2.one * Mathf.Max(1, settings.size);
            flight.image.gameObject.SetActive(true);
            active.Add(flight); Draw(flight);
        }
        void Update() => Advance(Time.unscaledDeltaTime);
        public void Advance(float delta)
        {
            if (pause != null && pause.PausesRewardAnimations) return;
            float duration = Mathf.Max(0, settings.holdSeconds) + Mathf.Max(.01f, settings.flightSeconds) + Mathf.Max(0, settings.arrivalSeconds);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var flight = active[i]; flight.elapsed += Mathf.Max(0, delta);
                if (flight.elapsed >= duration)
                {
                    flight.image.gameObject.SetActive(false); idle.Push(flight); active.RemoveAt(i);
                }
                else Draw(flight);
            }
        }
        void Draw(Flight flight)
        {
            var rect = layer.rect;
            var start = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, flight.startUV.x), Mathf.Lerp(rect.yMin, rect.yMax, flight.startUV.y));
            var end = (Vector2)layer.InverseTransformPoint(Receiver.position);
            float t = Mathf.Clamp01((flight.elapsed - Mathf.Max(0, settings.holdSeconds)) / Mathf.Max(.01f, settings.flightSeconds));
            float progress = settings.flightProgress != null ? Mathf.Clamp01(settings.flightProgress.Evaluate(t)) : t * t;
            flight.image.rectTransform.anchoredPosition = Vector2.Lerp(start, end, progress) + Vector2.up * (4 * progress * (1 - progress) * settings.arcHeight);
            float arrival = Mathf.Clamp01((flight.elapsed - Mathf.Max(0, settings.holdSeconds) - Mathf.Max(.01f, settings.flightSeconds)) / Mathf.Max(.001f, settings.arrivalSeconds));
            flight.image.rectTransform.localScale = Vector3.one * (1 - arrival);
        }
        public void Clear()
        {
            foreach (var flight in active) { flight.image.gameObject.SetActive(false); idle.Push(flight); }
            active.Clear();
        }
    }
}
