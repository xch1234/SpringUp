using UnityEngine;

namespace SpringUp.Organs
{
    // Visual-only. The pivot is the gold projectile head; smoke never changes its hit radius.
    public sealed class ProjectileSpriteView : MonoBehaviour
    {
        public SpriteRenderer Renderer { get; private set; }
        float age;
        Vector2 previous, direction;
        bool started;
        public void ResetView()
        {
            if (Renderer == null) Renderer = gameObject.AddComponent<SpriteRenderer>();
            age = 0; started = false; Renderer.flipX = Renderer.flipY = false;
        }
        public void Present(ProjectileVisualSettings.Style style, Vector2 position, Vector2 initialDirection, float radius, float dt)
        {
            if (!started) { direction = initialDirection; started = true; }
            else
            {
                var delta = position - previous;
                if (delta.sqrMagnitude > .0000001f) direction = delta.normalized;
                age += Mathf.Max(0, dt);
            }
            previous = position;
            int frame = (int)Mathf.Repeat(age * Mathf.Max(0, style.framesPerSecond), style.frames.Length);
            var sprite = style.frames[frame] != null ? style.frames[frame] : style.frames[0];
            Renderer.sprite = sprite; Renderer.color = style.color; Renderer.sortingOrder = style.sortingOrder;
            float angle = style.angleOffset + (style.rotate ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, angle));
            transform.localScale = Vector3.one * (radius * 2 * Mathf.Max(.1f, style.sizeMultiplier) * sprite.pixelsPerUnit / Mathf.Max(1, style.bodyPixels));
        }
    }
}
