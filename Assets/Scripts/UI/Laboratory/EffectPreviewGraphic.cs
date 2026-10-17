using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Laboratory
{
    [RequireComponent(typeof(CanvasRenderer), typeof(RectTransform))]
    public sealed class EffectPreviewGraphic : MaskableGraphic
    {
        private bool line;
        public void Configure(bool isLine, Color tint) { line = isLine; color = tint; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            if (line)
            {
                mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
                mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(0, 2, 3); return;
            }
            mesh.AddVert(rect.center, color, Vector2.zero);
            for (int i = 0; i <= 32; i++)
            {
                float angle = i * Mathf.PI * 2 / 32;
                mesh.AddVert(rect.center + new Vector2(Mathf.Cos(angle) * rect.width / 2, Mathf.Sin(angle) * rect.height / 2), color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
