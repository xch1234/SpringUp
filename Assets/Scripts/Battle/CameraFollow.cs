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

        // ---- 自诊断字段（只读，供 HUD 显示）----

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

                    return;
                }
            }

            var desired = target.position + offset;
            transform.position = desired;
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
            var runtime = FindFirstObjectByType<PlayerRuntime>();
            if (runtime != null)
                target = runtime.transform;
        }
    }
}
