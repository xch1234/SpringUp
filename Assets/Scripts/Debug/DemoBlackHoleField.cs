using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    // 演示接入：发现敌人、执行位移、显示范围。正式敌人接入时复用 BlackHoleBehaviour。
    [DisallowMultipleComponent]
    public sealed class DemoBlackHoleField : MonoBehaviour
    {
        private readonly List<BlackHoleBehaviour> fields = new List<BlackHoleBehaviour>();
        public int ActiveCount => fields.Count;
        public int SpawnCount { get; private set; }
        public float LongestRemaining
        {
            get { float remaining = 0f; foreach (var field in fields) remaining = Mathf.Max(remaining, field.Remaining); return remaining; }
        }

        public void Spawn(CastEvent attack, Vector2 center)
        {
            if (!isActiveAndEnabled) return;
            var field = new BlackHoleBehaviour(attack, center);
            // 同一器官实例只保留一个黑洞；不同槽位的实例可以各有一个。
            fields.RemoveAll(old => old.Cause.OriginInstanceId == attack.OriginInstanceId);
            fields.Add(field);
            SpawnCount++;
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (!isActiveAndEnabled || fields.Count == 0) return;
            var targets = new List<DemoTarget>();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                targets.AddRange(root.GetComponentsInChildren<DemoTarget>(false));
            foreach (BlackHoleBehaviour field in fields)
            {
                foreach (DemoTarget target in targets)
                {
                    if (target == null || !target.IsAlive || !target.CanBeDisplaced) continue;
                    Vector2 before = target.transform.position;
                    target.TryDisplace(field.PullPosition(before, deltaTime) - before);
                }
                field.Tick(deltaTime);
            }
            fields.RemoveAll(field => !field.IsActive);
        }

        public void Clear() { fields.Clear(); SpawnCount = 0; }
        private void OnDisable() => Clear();

        private void OnGUI()
        {
            Camera camera = Camera.main;
            if (camera == null || Event.current.type != EventType.Repaint) return;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = new Color(0.7f, 0.3f, 1f, 0.9f);
            foreach (BlackHoleBehaviour field in fields)
            {
                for (int i = 0; i < 64; i++)
                {
                    float a = i * Mathf.PI * 2f / 64f, b = (i + 1) * Mathf.PI * 2f / 64f;
                    Vector3 start = camera.WorldToScreenPoint(field.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * field.Cause.Radius);
                    Vector3 end = camera.WorldToScreenPoint(field.Center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * field.Cause.Radius);
                    if (start.z <= 0f || end.z <= 0f) continue;
                    Vector2 p = new Vector2(start.x, Screen.height - start.y), q = new Vector2(end.x, Screen.height - end.y);
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(q.y - p.y, q.x - p.x) * Mathf.Rad2Deg, p);
                    GUI.DrawTexture(new Rect(p.x, p.y - 1f, Vector2.Distance(p, q), 2f), Texture2D.whiteTexture);
                    GUI.matrix = previousMatrix;
                }
                Vector3 screen = camera.WorldToScreenPoint(field.Center);
                if (screen.z > 0f) GUI.Label(new Rect(screen.x - 40f, Screen.height - screen.y - 12f, 150f, 24f), $"● 黑洞 {field.Remaining:0.0}s");
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
    }
}
