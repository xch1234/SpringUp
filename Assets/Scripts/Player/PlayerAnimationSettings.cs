using UnityEngine;

namespace TideBorne.Battle
{
    [CreateAssetMenu(menuName = "SpringUp/主角动画配置")]
    public sealed class PlayerAnimationSettings : ScriptableObject
    {
        [Header("方向帧（按播放顺序排列）")]
        [InspectorName("朝右帧")] public Sprite[] rightFrames;
        [InspectorName("朝左帧")] public Sprite[] leftFrames;
        [Header("播放速度")]
        [InspectorName("移动帧率"), Min(0)] public float moveFramesPerSecond = 10;
        [InspectorName("待机帧率"), Min(0), Tooltip("使用同一组帧缓慢呼吸；设为 0 时停在第一帧。")]
        public float idleFramesPerSecond = 4;
        [InspectorName("疾跑动画倍率"), Min(.1f)] public float sprintAnimationMultiplier = 1.5f;
        [Header("显示（不改变碰撞体和攻击方向）")]
        [InspectorName("整帧高度"), Min(.1f), Tooltip("世界单位，包含精灵帧的透明留白。")]
        public float height = 1.5f;
        [InspectorName("显示偏移")] public Vector2 offset;
        [InspectorName("初始朝左")] public bool initiallyFacingLeft;

        public bool HasFrames => rightFrames != null && rightFrames.Length > 0 && rightFrames[0] != null
            && leftFrames != null && leftFrames.Length > 0 && leftFrames[0] != null;
    }
}
