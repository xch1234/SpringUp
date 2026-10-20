using System.Collections.Generic;
using SpringUp.Organs;
using UnityEngine;

namespace Tideflesh.Systems
{
    [RequireComponent(typeof(OrganPipelineController))]
    public sealed class BattleOrganEffects : MonoBehaviour
    {
        private sealed class Ring
        {
            public string Instance;
            public AreaEffectView View;
            public Vector2 Center;
            public float Radius, Duration, Remaining;
        }
        private readonly List<Ring> rings = new List<Ring>();
        private Material material;
        private AreaEffectVisualSettings areaSettings;
        private bool ownsAreaSettings;
        private Texture2D diskTexture;
        private Sprite diskSprite;
        private readonly Dictionary<string, AreaEffectView> fogViews = new Dictionary<string, AreaEffectView>();
        private OrganPipelineController pipeline;
        private BattleOrganCombat combat;
        private sealed class SummonView
        {
            public SpriteRenderer Renderer;
            public LineRenderer HealthBackground, HealthFill;
            public Vector2 LastPosition;
            public float Frame;
        }
        private SummonVisualSettings summonSettings;
        private readonly Dictionary<string, SummonView> summonViews = new Dictionary<string, SummonView>();
        public int SummonViewCount => summonViews.Count;
        private readonly Dictionary<string, LineRenderer> projectileViews = new Dictionary<string, LineRenderer>();
        private readonly Stack<LineRenderer> projectilePool = new Stack<LineRenderer>();
        private ProjectileVisualSettings projectileSettings;
        private readonly Dictionary<string, ProjectileSpriteView> spriteViews = new Dictionary<string, ProjectileSpriteView>();
        private readonly Stack<ProjectileSpriteView> spritePool = new Stack<ProjectileSpriteView>();
        private void OnEnable()
        {
            pipeline = GetComponent<OrganPipelineController>();
            combat = GetComponent<BattleOrganCombat>();
            summonSettings = Resources.Load<SummonVisualSettings>("SummonVisualSettings");
            projectileSettings = Resources.Load<ProjectileVisualSettings>("ProjectileVisualSettings");
            if (areaSettings == null)
            {
                areaSettings = Resources.Load<AreaEffectVisualSettings>("AreaEffectVisualSettings");
                if (areaSettings == null) { areaSettings = ScriptableObject.CreateInstance<AreaEffectVisualSettings>(); ownsAreaSettings = true; }
            }
            pipeline.AttackProduced += Show;
        }
        private AreaEffectView CreateAreaView(AreaEffectView.Kind kind)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            if (diskSprite == null)
            {
                const int size = 64;
                diskTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                diskTexture.wrapMode = TextureWrapMode.Clamp;
                var colors = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float r = new Vector2((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1).magnitude;
                        colors[y * size + x] = new Color(1, 1, 1, 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1, r)));
                    }
                diskTexture.SetPixels(colors); diskTexture.Apply();
                diskSprite = Sprite.Create(diskTexture, new Rect(0, 0, size, size), Vector2.one * .5f, size);
            }
            var go = new GameObject(kind + " visual"); go.transform.SetParent(transform, false);
            var view = go.AddComponent<AreaEffectView>();
            view.Initialize(kind, areaSettings, diskSprite, material);
            return view;
        }
        private void Show(CastEvent attack)
        {
            if (!attack.EffectCenter.HasValue || attack.Shape == AttackShape.SingleTarget) return;
            bool hole = attack.Shape == AttackShape.BlackHole;
            if (hole)
                for (int i = rings.Count - 1; i >= 0; i--)
                    if (rings[i].Instance == attack.OriginInstanceId) Remove(i);
            if (rings.Count >= 32) Remove(0);
            var view = CreateAreaView(hole ? AreaEffectView.Kind.Hole : AreaEffectView.Kind.Explosion);
            float duration = hole ? attack.BlackHole.Duration : Mathf.Max(.05f, areaSettings.explosionSeconds);
            view.Present(attack.EffectCenter.Value, attack.Radius, 0);
            rings.Add(new Ring { Instance = hole ? attack.OriginInstanceId : null, View = view,
                Center = attack.EffectCenter.Value, Radius = attack.Radius, Duration = duration, Remaining = duration });
        }
        private void Update()
        {
            RenderProjectiles();
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                rings[i].Remaining -= Time.deltaTime;
                if (rings[i].Remaining <= 0) Remove(i);
                else rings[i].View.Present(rings[i].Center, rings[i].Radius, Time.deltaTime, rings[i].Remaining / rings[i].Duration);
            }
        }
        private void Remove(int index)
        {
            if (rings[index].View != null) { rings[index].View.gameObject.SetActive(false); Destroy(rings[index].View.gameObject); }
            rings.RemoveAt(index);
        }
        private readonly List<EffectVisual> effectVisuals = new List<EffectVisual>();
        private void RenderProjectiles()
        {
            var visible = new HashSet<string>();
            var visibleSummons = new HashSet<string>();
            var visibleSprites = new HashSet<string>();
            var visibleFog = new HashSet<string>();
            if (combat != null) foreach (var execution in combat.Actuators.Active)
            {
                if (execution.IsFinished) continue;
                if (execution is FogExecution && execution.Context.Projectile is PatternActuatorSettings fog)
                {
                    string id = execution.Context.ExecutionId;
                    visibleFog.Add(id);
                    if (!fogViews.TryGetValue(id, out var view))
                    { view = CreateAreaView(AreaEffectView.Kind.Fog); fogViews.Add(id, view); }
                    view.Present(execution.Position, fog.AreaRadius, Time.deltaTime);
                    continue;
                }
                if (execution is SummonExecution summon && summonSettings != null && summonSettings.IsValid)
                {
                    RenderSummon(summon, visibleSummons);
                    continue;
                }
                effectVisuals.Clear();
                if (execution is IEffectVisuals effects) effects.GetVisuals(effectVisuals);
                else effectVisuals.Add(new EffectVisual(execution.Position, execution.Position, execution.Context.Projectile.HitRadius, Color.yellow));
                for (int index = 0; index < effectVisuals.Count; index++)
                {
                    var visual = effectVisuals[index];
                    string id = execution.Context.ExecutionId + "/" + (visual.Index >= 0 ? visual.Index : index);
                    var style = projectileSettings != null ? projectileSettings.For(execution.Context.Projectile) : null;
                    if (!visual.Line && style != null && style.IsValid)
                    {
                        visibleSprites.Add(id);
                        if (!spriteViews.TryGetValue(id, out var sprite))
                        {
                            if (spritePool.Count > 0) sprite = spritePool.Pop();
                            else
                            {
                                var go = new GameObject("Player projectile sprite"); go.transform.SetParent(transform, false);
                                sprite = go.AddComponent<ProjectileSpriteView>();
                            }
                            sprite.ResetView(); sprite.gameObject.SetActive(true); spriteViews.Add(id, sprite);
                        }
                        sprite.Present(style, visual.From, visual.Direction.sqrMagnitude > 0 ? visual.Direction : execution.Context.Direction,
                            visual.Radius, Time.deltaTime);
                        continue;
                    }
                    visible.Add(id);
                    if (!projectileViews.TryGetValue(id, out var line))
                    {
                        if (projectilePool.Count > 0) line = projectilePool.Pop();
                        else
                        {
                            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
                            var go = new GameObject("Organ effect view"); go.transform.SetParent(transform, false);
                            line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
                            line.sortingOrder = 20; line.useWorldSpace = true;
                        }
                        line.gameObject.SetActive(true); projectileViews.Add(id, line);
                    }
                    line.startColor = line.endColor = visual.Color;
                    line.loop = !visual.Line; line.positionCount = visual.Line ? 2 : 32;
                    line.widthMultiplier = visual.Line ? visual.Radius * 2 : Mathf.Min(.08f, visual.Radius * .65f);
                    if (visual.Line) { line.SetPosition(0, visual.From); line.SetPosition(1, visual.To); }
                    else for (int i = 0; i < line.positionCount; i++)
                    {
                        float angle = i * Mathf.PI * 2 / line.positionCount;
                        Vector2 point = visual.From + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * visual.Radius;
                        line.SetPosition(i, point);
                    }
                }
            }
            foreach (var id in new List<string>(fogViews.Keys)) if (!visibleFog.Contains(id))
            {
                var view = fogViews[id]; fogViews.Remove(id);
                view.gameObject.SetActive(false); Destroy(view.gameObject);
            }
            foreach (var id in new List<string>(projectileViews.Keys)) if (!visible.Contains(id))
            {
                var line = projectileViews[id]; projectileViews.Remove(id);
                line.gameObject.SetActive(false); projectilePool.Push(line);
            }
            foreach (var id in new List<string>(spriteViews.Keys)) if (!visibleSprites.Contains(id))
            {
                var sprite = spriteViews[id]; spriteViews.Remove(id);
                sprite.gameObject.SetActive(false); spritePool.Push(sprite);
            }
            foreach (var id in new List<string>(summonViews.Keys)) if (!visibleSummons.Contains(id))
            {
                var view = summonViews[id]; view.Renderer.gameObject.SetActive(false);
                Destroy(view.Renderer.gameObject); summonViews.Remove(id);
            }
        }
        private void RenderSummon(SummonExecution execution, HashSet<string> visible)
        {
            string id = execution.Context.ExecutionId; visible.Add(id);
            if (!summonViews.TryGetValue(id, out var view))
            {
                var go = new GameObject("Summon follower"); go.transform.SetParent(transform, false);
                var renderer = go.AddComponent<SpriteRenderer>(); renderer.sortingOrder = 3;
                view = new SummonView { Renderer = renderer, LastPosition = execution.Position };
                view.HealthBackground = CreateSummonHealthLine("Summon health background", go.transform, 30);
                view.HealthFill = CreateSummonHealthLine("Summon health fill", go.transform, 31);
                summonViews.Add(id, view);
            }
            float dx = execution.Position.x - view.LastPosition.x;
            if (summonSettings.flipWithMovement && Mathf.Abs(dx) > .0001f)
                view.Renderer.flipX = (dx < 0) != summonSettings.artworkFacesLeft;
            else if (!summonSettings.flipWithMovement) view.Renderer.flipX = false;
            view.LastPosition = execution.Position;
            view.Frame = Mathf.Repeat(view.Frame + Time.deltaTime * Mathf.Max(0, summonSettings.framesPerSecond), summonSettings.frames.Length);
            view.Renderer.sprite = summonSettings.frames[(int)view.Frame] ?? summonSettings.frames[0];
            view.Renderer.transform.SetPositionAndRotation(execution.Position, Quaternion.identity);
            float scale = Mathf.Max(.1f, summonSettings.width) / Mathf.Max(.001f, summonSettings.frames[0].bounds.size.x);
            view.Renderer.transform.localScale = Vector3.one * scale;
            RenderSummonHealth(execution, view);
        }
        private LineRenderer CreateSummonHealthLine(string name, Transform parent, int order)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = true;
            line.positionCount = 2; line.sortingOrder = order;
            return line;
        }
        private void RenderSummonHealth(SummonExecution execution, SummonView view)
        {
            bool show = summonSettings.showHealthBar;
            view.HealthBackground.enabled = show;
            float fraction = Mathf.Clamp01(execution.Health / execution.MaxHealth);
            view.HealthFill.enabled = show && fraction > 0;
            if (!show) return;
            float width = Mathf.Max(.1f, summonSettings.healthBarSize.x);
            float height = Mathf.Max(.02f, summonSettings.healthBarSize.y);
            // World-space geometry keeps the bar upright and independent of sprite scaling/flipping.
            Vector2 center = new Vector2(execution.Position.x, view.Renderer.bounds.max.y) + summonSettings.healthBarOffset;
            Vector2 left = center - Vector2.right * width * .5f;
            const float border = .02f;
            view.HealthBackground.widthMultiplier = height + border * 2;
            view.HealthBackground.startColor = view.HealthBackground.endColor = summonSettings.healthBarBackground;
            view.HealthBackground.SetPosition(0, left - Vector2.right * border);
            view.HealthBackground.SetPosition(1, left + Vector2.right * (width + border));
            view.HealthFill.widthMultiplier = height;
            view.HealthFill.startColor = view.HealthFill.endColor = summonSettings.healthBarColor;
            view.HealthFill.SetPosition(0, left);
            view.HealthFill.SetPosition(1, left + Vector2.right * width * fraction);
        }
        public void Clear()
        {
            foreach (var view in fogViews.Values) if (view != null)
            { view.gameObject.SetActive(false); Destroy(view.gameObject); }
            fogViews.Clear();
            foreach (var sprite in spriteViews.Values) if (sprite != null)
            { sprite.gameObject.SetActive(false); Destroy(sprite.gameObject); }
            spriteViews.Clear();
            while (spritePool.Count > 0) { var sprite = spritePool.Pop(); if (sprite != null) Destroy(sprite.gameObject); }
            foreach (var view in summonViews.Values) if (view.Renderer != null)
            { view.Renderer.gameObject.SetActive(false); Destroy(view.Renderer.gameObject); }
            summonViews.Clear();
            for (int i = rings.Count - 1; i >= 0; i--) Remove(i);
            foreach (var line in projectileViews.Values) if (line != null) Destroy(line.gameObject);
            projectileViews.Clear();
            while (projectilePool.Count > 0) { var line = projectilePool.Pop(); if (line != null) Destroy(line.gameObject); }
        }
        private void OnDisable() { if (pipeline != null) pipeline.AttackProduced -= Show; Clear(); }
        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (diskSprite != null) Destroy(diskSprite);
            if (diskTexture != null) Destroy(diskTexture);
            if (ownsAreaSettings && areaSettings != null) Destroy(areaSettings);
        }
    }
}
