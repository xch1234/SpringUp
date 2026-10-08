using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    // 临时画圆表示实际判定范围，不负责伤害，不需要美术素材或碰撞体。
    [DisallowMultipleComponent]
    public sealed class DemoExplosionView : MonoBehaviour
    {
        private struct Ring { public Vector2 Center; public float Radius; public float Until; }
        private readonly List<Ring> rings = new List<Ring>();
        public void Show(Vector2 center, float radius)
        {
            if (rings.Count == 8) rings.RemoveAt(0);
            rings.Add(new Ring { Center = center, Radius = radius, Until = Time.unscaledTime + 0.5f });
        }
        public void Clear() => rings.Clear();
        private void OnDisable() => Clear();
        private void OnGUI()
        {
            Camera camera = Camera.main;
            if (camera == null || Event.current.type != EventType.Repaint) return;
            rings.RemoveAll(ring => ring.Until <= Time.unscaledTime);
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.65f, 0.1f, 0.9f);
            foreach (Ring ring in rings)
            {
                for (int i = 0; i < 64; i++)
                {
                    float a = i * Mathf.PI * 2f / 64f;
                    float b = (i + 1) * Mathf.PI * 2f / 64f;
                    Vector3 start = camera.WorldToScreenPoint(ring.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring.Radius);
                    Vector3 end = camera.WorldToScreenPoint(ring.Center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * ring.Radius);
                    if (start.z <= 0f || end.z <= 0f) continue;
                    Vector2 p = new Vector2(start.x, Screen.height - start.y);
                    Vector2 q = new Vector2(end.x, Screen.height - end.y);
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(q.y - p.y, q.x - p.x) * Mathf.Rad2Deg, p);
                    GUI.DrawTexture(new Rect(p.x, p.y - 1f, Vector2.Distance(p, q), 2f), Texture2D.whiteTexture);
                    GUI.matrix = previousMatrix;
                }
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
    }
}
