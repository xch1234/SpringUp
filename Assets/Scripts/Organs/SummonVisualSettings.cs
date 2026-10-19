using UnityEngine;

namespace SpringUp.Organs
{
    [CreateAssetMenu(menuName = "SpringUp/召唤物外观配置")]
    public sealed class SummonVisualSettings : ScriptableObject
    {
        [InspectorName("动画帧（按顺序）")] public Sprite[] frames;
        [InspectorName("动画帧率"), Min(0)] public float framesPerSecond = 10;
        [InspectorName("显示宽度"), Min(.1f)] public float width = 1.4f;
        [InspectorName("移动时左右翻转")] public bool flipWithMovement = true;
        [InspectorName("原图朝左")] public bool artworkFacesLeft = true;
        [Header("头顶血条")]
        [InspectorName("显示血条")] public bool showHealthBar = true;
        [InspectorName("血条偏移（相对贴图顶部）")] public Vector2 healthBarOffset = new Vector2(0, .12f);
        [InspectorName("血条尺寸（世界单位）")] public Vector2 healthBarSize = new Vector2(.9f, .1f);
        [InspectorName("血条颜色")] public Color healthBarColor = new Color(.9f, .08f, .1f, 1);
        [InspectorName("血条底色")] public Color healthBarBackground = new Color(.12f, .02f, .025f, .9f);
        public bool IsValid => frames != null && frames.Length > 0 && frames[0] != null;
    }
}
