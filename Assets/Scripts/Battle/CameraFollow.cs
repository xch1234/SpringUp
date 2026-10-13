using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 俯视 2D 跟随相机。挂在 Main Camera 上，LateUpdate 跟随目标。
    ///
    /// 不用 Cinemachine：本项目只需要俯视跟随，Cinemachine 会额外引入包依赖
    /// （交接文档 §6 的切片 2 也把它列为「未必需要」）。
    ///
    /// **为什么带自诊断**：曾出现过"场景文件里 target 序列化正确、运行时也没有任何告警，
    /// 但镜头就是不跟随"的情况。光看序列化数据无法区分下面几种可能，所以本组件
    /// 把自己运行时的真实状态暴露出来（<see cref="IsFollowing"/> / <see cref="TargetName"/> /
    /// <see cref="LateUpdateCount"/>），由 HUD 显示，用不着再猜：
    /// - 组件在不在这个物体上
    /// - target 到底绑没绑上
    /// - LateUpdate 到底跑没跑
    /// - 有没有别的东西在覆盖相机位置
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/俯视跟随相机（CameraFollow）")]
    public class CameraFollow : MonoBehaviour
    {
        private const string DefaultTargetTag = "Player";

        /// <summary>跟随偏移的默认值。z 必须为负，否则相机跑到 2D 平面背后。</summary>
        public static readonly Vector3 DefaultOffset = new Vector3(0f, 0f, -10f);

        [Tooltip("跟随目标。留空则按 targetTag 查找。")]
        [SerializeField] private Transform target;

        [Tooltip("目标为空时按此 Tag 查找。Player 是 Unity 内置 Tag。")]
        [SerializeField] private string targetTag = DefaultTargetTag;

        [Tooltip("相对目标的偏移。z 必须是负数，否则相机跑到 2D 平面背后。")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

        [Tooltip("跟随平滑时间，秒。0 表示硬跟随。**当前实现为硬跟随，此字段保留但未使用。**")]
        [SerializeField] private float smoothTime = 0.12f;

        [Tooltip("每帧最大跟随距离，0 表示不限。**当前实现为硬跟随，此字段保留但未使用。**")]
        [SerializeField] private float maxSpeed = 0f;

        [Header("地图边界限制")]
        [Tooltip("开启后，相机不会让画面越过地图边界：目标靠近边缘时相机停住，回到中间再继续跟。")]
        [SerializeField] private bool clampToMapBounds = true;

        [Tooltip("地图中心的**世界坐标**（x,y）。地图以原点居中时留 (0,0)。")]
        [SerializeField] private Vector2 mapCenter = Vector2.zero;

        [Tooltip("可通行区域的半宽/半高（米），与 BattleGreyboxBuilder.PlayableHalfExtents 一致。")]
        [SerializeField] private Vector2 mapHalfExtents = new Vector2(36f, 36f);

        // ---- 自诊断字段（只读，供 HUD 显示）----

        /// <summary>
        /// 上一次 <c>LateUpdate</c> 里相机是否被地图边界卡住了。
        ///
        /// **为什么需要它**：卡住时相机与目标的偏移**本来就**会变大（这正是设计意图），
        /// 而 HUD 原来把"偏移大"一律判成跟随故障，于是在地图边缘会误报。
        /// 暴露这个状态之后，"偏移大"就能区分成"边界限制生效"和"真的坏了"两种情况。
        /// </summary>
        public bool IsBoundedByMap { get; private set; }

        /// <summary>边界限制是否开启。</summary>
        public bool ClampEnabled { get { return clampToMapBounds; } }

        /// <summary>是否已经拿到跟随目标。</summary>
        public bool HasTarget { get { return target != null; } }

        /// <summary>当前跟随目标。</summary>
        public Transform Target
        {
            get { return target; }
            set { target = value; }
        }

        /// <summary>目标名字；无目标时为空串。用于确认绑的是不是预期的那个对象。</summary>
        public string TargetName { get { return target != null ? target.name : string.Empty; } }

        /// <summary><c>LateUpdate</c> 已经执行过的次数。为 0 说明它根本没跑。</summary>
        public int LateUpdateCount { get; private set; }

        /// <summary>本组件所在物体的名字。用于确认脚本挂在了哪个物体上。</summary>
        public string OwnerName { get { return gameObject.name; } }

        /// <summary>本组件所在物体的父物体名字；没有父物体时为空串。</summary>
        public string OwnerParentName
        {
            get { return transform.parent != null ? transform.parent.name : string.Empty; }
        }

        /// <summary>跟随是否正在生效：有目标，且 LateUpdate 至少跑过一次。</summary>
        public bool IsFollowing { get { return target != null && LateUpdateCount > 0; } }

        private bool _reportedMissingTarget;
        private Camera _camera;

        private void Awake()
        {
            EnsureTarget();

            if (target == null)
            {
                Debug.LogWarning("[CameraFollow] 没有跟随目标：target 未赋值，且按 Tag \"" + targetTag +
                                 "\" 也找不到物体。镜头不会移动。", this);
            }
        }

        private void LateUpdate()
        {
            LateUpdateCount++;

            if (target == null)
            {
                EnsureTarget();
                if (target == null)
                {
                    // 只报一次，避免每帧刷屏。
                    if (!_reportedMissingTarget)
                    {
                        _reportedMissingTarget = true;
                        Debug.LogWarning("[CameraFollow] 仍然找不到跟随目标，镜头保持不动。", this);
                    }

                    // 没有目标时把"受限"状态清掉，免得 HUD 一直显示上一次停留的旧状态。
                    IsBoundedByMap = false;
                    return;
                }
            }

            var desired = target.position + offset;

            if (clampToMapBounds && ClampToBounds(ref desired))
                IsBoundedByMap = true;
            else
                IsBoundedByMap = false;

            transform.position = desired;
        }

        /// <summary>
        /// 把相机中心夹到「画面不越界」的范围内，返回值表示**是否真的被夹住了**。
        ///
        /// 推导只有一步：画面半宽/半高是 <c>可见范围 ÷ 2</c>，所以相机中心最多只能离地图中心
        /// <c>地图半宽 − 画面半宽</c> 那么远。这正是"目标靠近边缘时相机停住"的来源——
        /// 目标继续往外走，相机中心已经顶到上限，于是镜头不动、目标在画面里继续移向边缘。
        ///
        /// 例：地图半宽 36、相机 orthographicSize 10.125 配 16:9 → 画面半宽 18、半高 10.125，
        /// 于是相机中心 x 被夹到 ±18、y 被夹到 ±25.875。目标 x 超过 18 之后相机就不再横移。
        ///
        /// **不夹会怎样**：相机中心一路跟到 ±36，画面有一半落在地图外，露出背景色（黑边）。
        ///
        /// **地图比画面还小时**（余量为负）没有合法位置，直接把相机放到地图中心——
        /// 这是唯一不会让画面偏向一侧的选择。
        /// </summary>
        /// <returns>是否被边界夹住（即期望位置与最终位置不同）。</returns>
        private bool ClampToBounds(ref Vector3 position)
        {
            var camera = _camera != null ? _camera : (_camera = GetComponent<Camera>());
            if (camera == null || !camera.orthographic)
                return false;

            // 画面在世界里的半宽/半高。aspect 取运行时真值——窗口比例变了也能自适应。
            var halfHeight = camera.orthographicSize;
            var halfWidth = halfHeight * camera.aspect;

            // **这里刻意不额外留余量。** 试过往外扩 0.675 米（角色碰撞体半边长），
            // 能让贴墙的角色整个留在画面里——但画面同时就越过了地图边界 0.675 米，
            // 又露出地图外的黑边，正好是这个功能要消灭的东西。两者在数学上不能同时满足：
            // 角色的渲染宽度约 2.15 米（1×1 白方块按碰撞体缩放到 1.35 后，精灵本身还带边距），
            // 要整个看见它就必须让画面越过墙。
            // 取舍：**保证不露黑边**（用户要的），代价是贴墙时角色最外侧约 0.4 米被切掉。
            // 这个代价很轻——墙本身有 1 米厚且可见，玩家顶到墙时身体本来就贴着画面边缘。
            var marginX = mapHalfExtents.x - halfWidth;
            var marginY = mapHalfExtents.y - halfHeight;

            var original = position;

            position.x = marginX > 0f
                ? Mathf.Clamp(position.x, mapCenter.x - marginX, mapCenter.x + marginX)
                : mapCenter.x;

            position.y = marginY > 0f
                ? Mathf.Clamp(position.y, mapCenter.y - marginY, mapCenter.y + marginY)
                : mapCenter.y;

            // 用极小容差比较：浮点上"夹了但没变"不该被算成受限，
            // 否则站在边界内侧一点点的位置上 HUD 会一直显示"受限"。
            return Mathf.Abs(position.x - original.x) > 0.0001f
                || Mathf.Abs(position.y - original.y) > 0.0001f;
        }

        private void EnsureTarget()
        {
            if (target != null)
                return;

            if (!string.IsNullOrEmpty(targetTag))
            {
                var found = GameObject.FindGameObjectWithTag(targetTag);
                if (found != null)
                {
                    target = found.transform;
                    return;
                }
            }

            // 兜底：Tag 可能被改过或不存在。直接按组件找玩家，比 Tag 可靠。
            var runtime = FindAnyObjectByType<PlayerRuntime>();
            if (runtime != null)
                target = runtime.transform;
        }
    }
}
