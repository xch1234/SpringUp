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
        private void OnEnable()
        {
            pipeline = GetComponent<OrganPipelineController>();
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
        public void Clear() { for (int i = rings.Count - 1; i >= 0; i--) Remove(i); }
        private void OnDisable() { if (pipeline != null) pipeline.AttackProduced -= Show; Clear(); }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
