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
            // 边界墙在编辑期已经烘进场景；运行时直接复用，避免每次加载都重建一遍。
            if (Application.isPlaying && HasWalls())
                return;

            Rebuild();
        }

        private bool HasWalls()
        {
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name.StartsWith(EdgePrefix))
                    return true;
            }

            return false;
        }

        private void OnValidate()
        {
            Rebuild();
        }

        /// <summary>按当前参数重建边界墙。</summary>
        [ContextMenu("重建边界")]
        public void Rebuild()
        {
            if (transform == null)
                return;

            // 先清掉旧的墙，避免改参数后叠加出一堆。
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name.StartsWith(EdgePrefix))
                    DestroyImmediate(child.gameObject);
            }

            var points = BuildPoints();
            var centroid = Vector2.zero;
            for (var i = 0; i < points.Length - 1; i++)
                centroid += points[i];
            centroid /= points.Length - 1;

            var wallLayer = BattleLayers.Resolve(BattleLayers.Wall);

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

                var wall = new GameObject(EdgePrefix + i);
                wall.layer = wallLayer;

                var wallTransform = wall.transform;
                wallTransform.SetParent(transform, false);
                wallTransform.localPosition = midpoint + outward * (wallThickness * 0.5f);
                wallTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg);
                wallTransform.localScale = Vector3.one;

                var box = wall.AddComponent<BoxCollider2D>();
                box.size = new Vector2(length + wallThickness * 2f, wallThickness);
                box.offset = Vector2.zero;
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
