using System.Threading;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 船形可行走区域：一个长方形船身 + 一个等腰三角形船头，用一组静态墙体围出来。
    ///
    /// **为什么不用 PolygonCollider2D**：多边形碰撞体是实心的，玩家站在里面会一直和它重叠，
    /// 物理上会被往外推；这里需要的是"围一圈墙"，所以用 5 块带旋转的 BoxCollider2D 拼边界，
    /// 和场景里原来的边界墙是同一套东西，玩家移动的扫掠检测直接就能挡住。
    ///
    /// 参数改了（Inspector 或代码）会即时重建；Scene 视图里画青色轮廓，方便对照船体贴。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/船形可行走区域（ShipWalkBounds）")]
    public class ShipWalkBounds : MonoBehaviour
    {
        private const string EdgePrefix = "__ShipEdge_";
        private int rebuildRequested;

        [Tooltip("船体长方形尺寸：x = 船身长度，y = 船宽。")]
        [SerializeField] private Vector2 bodySize = new Vector2(30.8f, 20.7f);

        [Tooltip("船头三角形的高度：从长方形短边量到船头尖端的距离。")]
        [SerializeField, Min(0f)] private float bowLength = 13.3f;

        [Tooltip("船头朝左（-X）。取消勾选则船头朝右。")]
        [SerializeField] private bool bowFacesLeft = true;

        [Tooltip("边界墙厚度，米。只影响墙体本身的粗细，不影响可行走区域。")]
        [SerializeField, Min(0.05f)] private float wallThickness = 1f;

        /// <summary>当前多边形的本地坐标点（首尾闭合）。</summary>
        public Vector2[] Points { get { return BuildPoints(); } }

        private void OnEnable()
        {
            Interlocked.Exchange(ref rebuildRequested, 1);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= RebuildWhenReady;
            UnityEditor.EditorApplication.update += RebuildWhenReady;
#endif
            // 运行时在首次物理更新前补齐墙体；已有墙体会被复用。
            if (Application.IsPlaying(gameObject))
                Rebuild();
        }

        private void OnDisable()
        {
            Interlocked.Exchange(ref rebuildRequested, 0);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= RebuildWhenReady;
#endif
        }

        private void OnValidate()
        {
            // 此回调可能发生在加载线程或反序列化期间，不能修改对象层级。
            Interlocked.Exchange(ref rebuildRequested, 1);
        }

#if UNITY_EDITOR
        private void RebuildWhenReady()
        {
            if (this == null || !isActiveAndEnabled || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded
                || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating
                || UnityEditor.BuildPipeline.isBuildingPlayer
                || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode != Application.isPlaying)
                return;

            if (Interlocked.Exchange(ref rebuildRequested, 0) != 0)
                Rebuild();
        }
#endif

        /// <summary>按当前参数重建边界墙。</summary>
        [ContextMenu("重建边界")]
        public void Rebuild()
        {
            Interlocked.Exchange(ref rebuildRequested, 0);
            if (transform == null)
                return;

            var points = BuildPoints();
            var centroid = Vector2.zero;
            for (var i = 0; i < points.Length - 1; i++)
                centroid += points[i];
            centroid /= points.Length - 1;

            var wallLayer = BattleLayers.Resolve(BattleLayers.Wall);
            var walls = new Transform[points.Length - 1];

            for (var i = 0; i < points.Length - 1; i++)
            {
                var p = points[i];
                var q = points[i + 1];
                var edge = q - p;
                var length = edge.magnitude;
                if (length < 0.001f)
                    continue;

                var midpoint = (p + q) * 0.5f;
                var outward = new Vector2(-edge.y, edge.x).normalized;
                if (Vector2.Dot(outward, midpoint - centroid) < 0f)
                    outward = -outward;

                var wallTransform = transform.Find(EdgePrefix + i);
                if (wallTransform == null)
                {
                    wallTransform = new GameObject(EdgePrefix + i).transform;
                    wallTransform.SetParent(transform, false);
                }
                walls[i] = wallTransform;
                var wall = wallTransform.gameObject;
                if (wall.layer != wallLayer) wall.layer = wallLayer;
                if (!wall.activeSelf) wall.SetActive(true);

                Vector3 position = midpoint + outward * (wallThickness * 0.5f);
                var rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg);
                if (wallTransform.localPosition != position) wallTransform.localPosition = position;
                if (wallTransform.localRotation != rotation) wallTransform.localRotation = rotation;
                if (wallTransform.localScale != Vector3.one) wallTransform.localScale = Vector3.one;

                var box = wall.GetComponent<BoxCollider2D>();
                if (box == null) box = wall.AddComponent<BoxCollider2D>();
                var size = new Vector2(length + wallThickness * 2f, wallThickness);
                if (box.size != size) box.size = size;
                if (box.offset != Vector2.zero) box.offset = Vector2.zero;
                if (box.isTrigger) box.isTrigger = false;
                if (!box.enabled) box.enabled = true;
            }

            // 清理旧版本可能留下的重复墙，保留其他手工添加的子物体。
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(EdgePrefix) || System.Array.IndexOf(walls, child) >= 0)
                    continue;

                if (Application.IsPlaying(gameObject))
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>按参数生成五边形（长方形 + 等腰三角形船头），首尾闭合。</summary>
        private Vector2[] BuildPoints()
        {
            var half = bodySize * 0.5f;
            var dir = bowFacesLeft ? -1f : 1f;

            var tip = new Vector2(dir * (half.x + bowLength), 0f);
            var bowTop = new Vector2(dir * half.x, half.y);
            var sternTop = new Vector2(-dir * half.x, half.y);
            var sternBottom = new Vector2(-dir * half.x, -half.y);
            var bowBottom = new Vector2(dir * half.x, -half.y);

            return new[] { tip, bowTop, sternTop, sternBottom, bowBottom, tip };
        }

        private void OnDrawGizmos()
        {
            var points = BuildPoints();
            Gizmos.color = new Color(0.2f, 1f, 0.7f, 0.9f);

            for (var i = 0; i < points.Length - 1; i++)
            {
                var a = transform.TransformPoint(points[i]);
                var b = transform.TransformPoint(points[i + 1]);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawWireSphere(a, 0.18f);
            }
        }
    }
}
