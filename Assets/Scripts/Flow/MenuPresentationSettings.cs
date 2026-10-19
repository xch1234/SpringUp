using System;
using UnityEngine;

namespace SpringUp.Flow
{
    [CreateAssetMenu(menuName = "SpringUp/主菜单与结算界面配置")]
    public sealed class MenuPresentationSettings : ScriptableObject
    {
        public const string ResourcePath = "MenuPresentationSettings";
        [Serializable]
        public sealed class Artwork
        {
            [InspectorName("图片（Sprite）")] public Sprite sprite;
            [InspectorName("位置偏移")] public Vector2 position;
            [InspectorName("尺寸")] public Vector2 size = new Vector2(860, 440);
            [InspectorName("颜色")] public Color tint = Color.white;
            [InspectorName("透明度"), Range(0, 1)] public float opacity = 1;
            [InspectorName("保持图片比例")] public bool preserveAspect = true;
        }
        [Serializable]
        public sealed class HitArea
        {
            [InspectorName("中心位置")] public Vector2 position;
            [InspectorName("尺寸")] public Vector2 size = new Vector2(459, 132);
            public HitArea(float y) { position = new Vector2(0, y); }
        }
        [Header("主菜单（以封面中心为原点，X 向右，Y 向上）")]
        [InspectorName("封面")] public Artwork cover = new Artwork { size = new Vector2(1448, 1086) };
        [InspectorName("封面与点击区整体缩放"), Min(.1f)] public float menuScale = 1;
        [InspectorName("屏幕底色")] public Color menuBacking = new Color(.015f, .035f, .08f, 1);
        [InspectorName("开始游戏点击区")] public HitArea start = new HitArea(-68);
        [InspectorName("退出游戏点击区")] public HitArea quit = new HitArea(-217);
        [InspectorName("设置点击区")] public HitArea settings = new HitArea(-366);
        [InspectorName("显示点击区域（调试）")] public bool showHitAreas;
        [InspectorName("悬停/键盘选中颜色")] public Color hover = new Color(.5f, .8f, 1, .18f);
        [Header("结算背景")]
        [InspectorName("背景图")] public Artwork background = new Artwork { size = new Vector2(1448, 1086) };
        [InspectorName("背景铺满（裁切边缘）；关闭则完整显示")] public bool backgroundFill = true;
        [InspectorName("背景底色")] public Color settlementBacking = new Color(.015f, .025f, .06f, 1);
        [Header("结算布局（参考尺寸内等比缩放）")]
        [InspectorName("参考尺寸")] public Vector2 settlementReferenceSize = new Vector2(1440, 900);
        [InspectorName("失败窗口")] public Artwork defeat = new Artwork { position = new Vector2(0, 50) };
        [InspectorName("胜利窗口")] public Artwork victory = new Artwork { position = new Vector2(0, 50) };
        [Header("窗口下方提示")]
        [InspectorName("提示文字")] public string hintText = "点击任意位置返回标题";
        [InspectorName("字体（留空使用游戏中文字体）")] public Font hintFont;
        [InspectorName("字号"), Min(1)] public int hintFontSize = 32;
        [InspectorName("文字颜色")] public Color hintColor = new Color(.85f, .94f, 1, 1);
        [InspectorName("文字框底色")] public Color hintBacking = new Color(0, .03f, .08f, .35f);
        [InspectorName("文字框尺寸")] public Vector2 hintSize = new Vector2(720, 72);
        [InspectorName("相对窗口下边缘偏移")] public Vector2 hintOffset = new Vector2(0, -70);
        [InspectorName("启用闪烁")] public bool blink = true;
        [InspectorName("闪烁周期（秒）"), Min(.1f)] public float blinkPeriod = 1.6f;
        [InspectorName("最低透明度"), Range(0, 1)] public float blinkMin = .25f;
        [InspectorName("最高透明度"), Range(0, 1)] public float blinkMax = 1;
        [InspectorName("结算防误触时间（秒）"), Min(0)] public float inputDelay = .35f;

        public float HintAlpha(float elapsed) => !blink ? blinkMax : Mathf.Lerp(blinkMin, blinkMax,
            .5f + .5f * Mathf.Cos(elapsed * Mathf.PI * 2 / Mathf.Max(.1f, blinkPeriod)));
        public static MenuPresentationSettings Load() => Resources.Load<MenuPresentationSettings>(ResourcePath);
    }
}
