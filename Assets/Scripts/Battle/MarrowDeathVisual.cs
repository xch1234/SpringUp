using System.Collections.Generic;
using UnityEngine;
namespace TideBorne.Battle
{
    // Presentation only: no collision, pickup, money callback or reference to an enemy.
    public sealed class MarrowDeathVisual : MonoBehaviour
    {
        static readonly List<MarrowDeathVisual> active = new List<MarrowDeathVisual>();
        float remaining;
        public static void Spawn(Vector2 point)
        {
            float duration = EnemyCatalog.Load().marrowVisualLifetime;
            if (duration <= 0) return;
            var go = new GameObject("Marrow · cosmetic");
            go.transform.position = point;
            go.transform.localScale = Vector3.one * 0.25f;
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = GreyboxAssets.SquareSprite;
            sprite.color = new Color(0.8f, 1f, 0.65f);
            sprite.sortingOrder = 5;
            go.AddComponent<MarrowDeathVisual>().remaining = duration;
        }
        void OnEnable() => active.Add(this);
        void OnDestroy() => active.Remove(this);
        void Update() { remaining -= Time.deltaTime; if (remaining <= 0) Destroy(gameObject); }
        public static void Clear()
        {
            foreach (var visual in active.ToArray())
                if (visual != null) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); }
            active.Clear();
        }
    }
}
