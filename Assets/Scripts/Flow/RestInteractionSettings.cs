using UnityEngine;

namespace SpringUp.Flow
{
    [CreateAssetMenu(menuName = "SpringUp/休整区交互范围")]
    public sealed class RestInteractionSettings : ScriptableObject
    {
        [Tooltip("相对船图：左下角 (0,0)，右上角 (1,1)。X/Y 为左下角，W/H 为宽高。")]
        [InspectorName("商店交互矩形")] public Rect shopArea = new Rect(.62f, .55f, .28f, .23f);
        [InspectorName("实验室交互矩形")] public Rect laboratoryArea = new Rect(.62f, .28f, .28f, .24f);
        [InspectorName("选中休整场景对象时显示范围")] public bool showSceneGizmos = true;
        public static bool Contains(Rect area, Vector2 point) => area.width > 0 && area.height > 0
            && point.x >= area.xMin && point.x <= area.xMax && point.y >= area.yMin && point.y <= area.yMax;
    }
}
