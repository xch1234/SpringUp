using System;
using UnityEngine;

namespace SpringUp.Organs
{
    [CreateAssetMenu(menuName = "SpringUp/玩家子弹外观配置")]
    public sealed class ProjectileVisualSettings : ScriptableObject
    {
        [Serializable]
        public sealed class Style
        {
            [InspectorName("动画帧（顺序播放）")] public Sprite[] frames;
            [InspectorName("每秒帧数"), Min(0)] public float framesPerSecond = 12;
            [InspectorName("原图弹头直径（像素，不含拖尾）"), Min(1)] public float bodyPixels = 114;
            [InspectorName("显示直径 / 碰撞直径"), Min(.1f)] public float sizeMultiplier = 1.5f;
            [InspectorName("颜色与透明度")] public Color color = Color.white;
            [InspectorName("跟随飞行方向旋转")] public bool rotate = true;
            [InspectorName("朝向修正（度）")] public float angleOffset;
            [InspectorName("显示层级")] public int sortingOrder = 20;
            public bool IsValid => frames != null && frames.Length > 0 && frames[0] != null;
        }
        [InspectorName("环绕弹")] public Style orbit = new Style { framesPerSecond = 0, bodyPixels = 422, rotate = false };
        [InspectorName("飞行弹（直线、螺旋、追踪、三连发）")] public Style flying = new Style();
        public Style For(StraightProjectileSettings settings)
        {
            if (!(settings is PatternActuatorSettings pattern)) return flying;
            switch (pattern.Pattern)
            {
                case ActuatorPattern.Orbit: return orbit;
                case ActuatorPattern.Spiral:
                case ActuatorPattern.Homing:
                case ActuatorPattern.Triple: return flying;
                default: return null;
            }
        }
    }
}
