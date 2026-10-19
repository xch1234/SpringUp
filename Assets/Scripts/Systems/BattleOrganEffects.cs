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
            public LineRenderer View;
            public float Remaining;
        }
        private readonly List<Ring> rings = new List<Ring>();
        private Material material;
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
        private void OnEnable()
        {
            pipeline = GetComponent<OrganPipelineController>();
            combat = GetComponent<BattleOrganCombat>();
            summonSettings = Resources.Load<SummonVisualSettings>("SummonVisualSettings");
            pipeline.AttackProduced += Show;
        }
        private void Show(CastEvent attack)
        {
            if (!attack.EffectCenter.HasValue || attack.Shape == AttackShape.SingleTarget) return;
            bool hole = attack.Shape == AttackShape.BlackHole;
            if (hole)
                for (int i = rings.Count - 1; i >= 0; i--)
                    if (rings[i].Instance == attack.OriginInstanceId) Remove(i);
            if (rings.Count >= 32) Remove(0);
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject(hole ? "Black hole" : "Explosion");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = true; line.loop = true;
            line.positionCount = 64; line.widthMultiplier = hole ? .09f : .13f; line.sortingOrder = 8;
            line.startColor = line.endColor = hole ? new Color(.4f, .85f, 1) : new Color(1, .65f, .2f);
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2 / 64;
                Vector2 position = attack.EffectCenter.Value + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * attack.Radius;
                line.SetPosition(i, new Vector3(position.x, position.y, 0));
            }
            rings.Add(new Ring { Instance = hole ? attack.OriginInstanceId : null, View = line,
                Remaining = hole ? attack.BlackHole.Duration : .35f });
        }
        private void Update()
        {
            RenderProjectiles();
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                rings[i].Remaining -= Time.deltaTime;
                if (rings[i].Remaining <= 0) Remove(i);
            }
        }
        private void Remove(int index)
        {
            if (rings[index].View != null) Destroy(rings[index].View.gameObject);
            rings.RemoveAt(index);
        }
        private readonly List<EffectVisual> effectVisuals = new List<EffectVisual>();
        private void RenderProjectiles()
        {
            var visible = new HashSet<string>();
            var visibleSummons = new HashSet<string>();
            if (combat != null) foreach (var execution in combat.Actuators.Active)
            {
                if (execution.IsFinished) continue;
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
                    string id = execution.Context.ExecutionId + "/" + index; visible.Add(id);
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
            foreach (var id in new List<string>(projectileViews.Keys)) if (!visible.Contains(id))
            {
                var line = projectileViews[id]; projectileViews.Remove(id);
                line.gameObject.SetActive(false); projectilePool.Push(line);
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
            foreach (var view in summonViews.Values) if (view.Renderer != null)
            { view.Renderer.gameObject.SetActive(false); Destroy(view.Renderer.gameObject); }
            summonViews.Clear();
            for (int i = rings.Count - 1; i >= 0; i--) Remove(i);
            foreach (var line in projectileViews.Values) if (line != null) Destroy(line.gameObject);
            projectileViews.Clear();
            while (projectilePool.Count > 0) { var line = projectilePool.Pop(); if (line != null) Destroy(line.gameObject); }
        }
        private void OnDisable() { if (pipeline != null) pipeline.AttackProduced -= Show; Clear(); }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
