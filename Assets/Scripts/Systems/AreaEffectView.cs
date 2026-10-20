using UnityEngine;

namespace Tideflesh.Systems
{
    // Presentation only: shared resources belong to BattleOrganEffects.
    public sealed class AreaEffectView : MonoBehaviour
    {
        public enum Kind { Explosion, Fog, Hole }
        private Kind kind;
        private AreaEffectVisualSettings settings;
        private SpriteRenderer[] disks;
        private LineRenderer[] lines;
        private float age;

        public void Initialize(Kind value, AreaEffectVisualSettings config, Sprite disk, Material material)
        {
            kind = value; settings = config;
            disks = new SpriteRenderer[kind == Kind.Fog ? 5 : 1];
            for (int i = 0; i < disks.Length; i++)
            {
                var child = new GameObject("Soft disk"); child.transform.SetParent(transform, false);
                disks[i] = child.AddComponent<SpriteRenderer>();
                disks[i].sprite = disk; disks[i].sharedMaterial = material;
                disks[i].sortingOrder = settings.sortingOrder;
            }
            lines = new LineRenderer[kind == Kind.Hole ? 2 : 1];
            for (int i = 0; i < lines.Length; i++)
            {
                var child = new GameObject("Area arc"); child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>(); lines[i] = line; line.sharedMaterial = material;
                line.useWorldSpace = false; line.positionCount = 64;
                line.sortingOrder = settings.sortingOrder + 1;
            }
        }

        public void Present(Vector2 center, float radius, float deltaTime, float remainingFraction = 1)
        {
            age += deltaTime;
            transform.position = center;
            float fade = Mathf.Clamp01(remainingFraction);
            if (kind == Kind.Explosion)
            {
                float progress = 1 - fade;
                Disk(0, Vector2.zero, radius * (.15f + progress * .5f), settings.flashColor, fade * fade);
                Arc(0, radius * Mathf.Lerp(.15f, 1, progress), settings.explosionColor, fade, 0, false);
            }
            else if (kind == Kind.Fog)
            {
                Disk(0, Vector2.zero, radius, settings.fogColor, 1);
                float phase = age * settings.fogAnimationSpeed;
                for (int i = 1; i < disks.Length; i++)
                {
                    float angle = phase + i * Mathf.PI * .5f;
                    Disk(i, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * .35f,
                        radius * (.35f + .05f * Mathf.Sin(phase * 1.5f + i)), settings.fogColor, .65f);
                }
                Arc(0, radius, settings.fogColor, .8f, 0, false);
            }
            else
            {
                Disk(0, Vector2.zero, radius * .45f, settings.holeColor, Mathf.Min(1, fade * 5));
                for (int i = 0; i < lines.Length; i++)
                    Arc(i, radius, settings.swirlColor, Mathf.Min(1, fade * 5),
                        -age * settings.holeAnimationSpeed + i * Mathf.PI, true);
            }
        }

        private void Disk(int i, Vector2 offset, float radius, Color color, float opacity)
        {
            disks[i].transform.localPosition = offset;
            disks[i].transform.localScale = Vector3.one * radius * 2;
            color.a *= opacity; disks[i].color = color;
        }
        private void Arc(int index, float radius, Color color, float opacity, float phase, bool spiral)
        {
            var line = lines[index]; line.loop = !spiral;
            line.widthMultiplier = Mathf.Max(.01f, settings.lineWidth);
            color.a *= opacity; line.startColor = line.endColor = color;
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (float)(line.positionCount - 1);
                float angle = phase + t * Mathf.PI * (spiral ? 2.5f : 2);
                float r = radius * (spiral ? Mathf.Lerp(1, .08f, t) : 1);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0));
            }
        }
    }
}
