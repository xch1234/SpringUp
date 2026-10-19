using System;
using UnityEngine;

namespace SpringUp.Flow
{
    public enum HudAnchor { TopLeft, TopRight, BottomLeft, BottomRight, Center, BelowEquipment }

    [Serializable]
    public sealed class HealthBarStyle
    {
        public bool showValue = true;
        public Color fillColor = new Color(.88f, .055f, .075f, 1);
        public Color emptyColor = new Color(.18f, .025f, .035f, 1);
        [Range(0, 12)] public float padding = 3;
    }

    [Serializable]
    public sealed class EquipmentHudStyle
    {
        public bool visible = true;
        public Sprite backgroundSprite;
        public Color backgroundColor = Color.white;
        public HudAnchor anchor = HudAnchor.TopLeft;
        public Vector2 offset = new Vector2(16, -65);
        public Vector2 size = new Vector2(530, 265);
        [Range(.25f, 4)] public float scale = 1;
        [Range(0, 1)] public float opacity = 1;
    }

    [Serializable]
    public sealed class HudImageStyle
    {
        public string name = "图片";
        public bool visible = true;
        public Sprite sprite;
        public HudAnchor anchor = HudAnchor.BelowEquipment;
        public Vector2 offset = new Vector2(0, -12);
        public Vector2 size = new Vector2(42, 42);
        [Range(.25f, 4)] public float scale = 1;
        [Range(0, 1)] public float opacity = 1;
        public Color color = Color.white;
        public bool preserveAspect = true;
    }

    [Serializable]
    public sealed class HudTextStyle
    {
        public bool visible = true;
        public HudAnchor anchor = HudAnchor.TopRight;
        public Vector2 offset;
        public Vector2 size = new Vector2(300, 40);
        [Range(.25f, 4)] public float scale = 1;
        [Range(0, 1)] public float opacity = 1;
        [Range(8, 96)] public int fontSize = 28;
        public Color textColor = Color.white;
        public Color backgroundColor = new Color(.025f, .065f, .09f, .65f);
        public TextAnchor alignment = TextAnchor.MiddleRight;
        public bool bold = true;
        public bool outline = true;
        [TextArea(2, 4)] public string text;

        public HudTextStyle(string text, float y) { this.text = text; offset = new Vector2(-24, y); }
    }

    [CreateAssetMenu(menuName = "潮涌之躯/战斗 HUD 配置", fileName = "BattleHudSettings")]
    public sealed class BattleHudSettings : ScriptableObject
    {
        public bool hideWithWindows = true;
        [InspectorName("显示右下角调试面板")] public bool showDebugPanel = false;
        public HudTextStyle wave = new HudTextStyle("波次 {wave}/{maxWave}", -24);
        public HudTextStyle time = new HudTextStyle("剩余时间 {time}", -72);
        public HudTextStyle health = new HudTextStyle("生命 {health}/{maxHealth}", -120) { alignment = TextAnchor.MiddleCenter };
        public HealthBarStyle healthBar = new HealthBarStyle();
        public EquipmentHudStyle equipment = new EquipmentHudStyle();
        public HudImageStyle[] images = Array.Empty<HudImageStyle>();
        public HudTextStyle marrow = new HudTextStyle("骨髓 {marrow}", 0)
        {
            anchor = HudAnchor.BelowEquipment, offset = new Vector2(0, -12),
            size = new Vector2(300, 42), alignment = TextAnchor.MiddleLeft,
            textColor = new Color(1, .85f, .4f, 1)
        };
        public HudTextStyle help = new HudTextStyle("器官自动攻击 {interaction}  {inventoryKey}：背包  Esc：菜单", 0)
        {
            visible = false,
            anchor = HudAnchor.BottomLeft, offset = new Vector2(24, 20),
            size = new Vector2(900, 32), alignment = TextAnchor.MiddleLeft,
            fontSize = 18, bold = false, backgroundColor = Color.clear
        };
    }
}
