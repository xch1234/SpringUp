using System.Linq;
using SpringUp.Flow;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(RestInteractionSettings))]
    public sealed class RestInteractionSettingsEditor : Editor
    {
        public const string AssetPath = "Assets/Resources/RestInteractionSettings.asset";
        [MenuItem("Tools/SpringUp/休整区/编辑商店与实验室交互范围")]
        public static void SelectSettings()
        { Selection.activeObject = EnsureAsset(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static RestInteractionSettings EnsureAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RestInteractionSettings>(AssetPath);
            if (settings == null)
            {
                settings = CreateInstance<RestInteractionSettings>(); AssetDatabase.CreateAsset(settings, AssetPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("绿色为商店，蓝色为实验室。范围包含柜台贴图及其左侧一小段甲板；中间走道留空。玩家中心进入矩形后按交互键打开。运行时修改立即生效。", MessageType.Info);
            DrawDefaultInspector();
            var settings = (RestInteractionSettings)target;
            if (settings.shopArea.Overlaps(settings.laboratoryArea))
                EditorGUILayout.HelpBox("两个区域有重叠；重叠处按交互键将优先打开实验室。建议缩小高度或调整 Y。", MessageType.Warning);
            var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Backgrounds/ship.png").OfType<Sprite>().FirstOrDefault();
            if (sprite != null)
            {
                float width = Mathf.Max(100, EditorGUIUtility.currentViewWidth - 42);
                var rect = GUILayoutUtility.GetRect(width, width * sprite.rect.height / sprite.rect.width);
                var uv = sprite.rect; uv.x /= sprite.texture.width; uv.width /= sprite.texture.width;
                uv.y /= sprite.texture.height; uv.height /= sprite.texture.height;
                GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv);
                DrawArea(rect, settings.shopArea, new Color(.2f, 1, .4f, .35f), "商店");
                DrawArea(rect, settings.laboratoryArea, new Color(.2f, .6f, 1, .35f), "实验室");
            }
            if (GUILayout.Button("保存配置")) AssetDatabase.SaveAssets();
        }
        static void DrawArea(Rect image, Rect area, Color color, string label)
        {
            var rect = new Rect(image.x + area.x * image.width, image.y + (1 - area.yMax) * image.height,
                area.width * image.width, area.height * image.height);
            EditorGUI.DrawRect(rect, color); GUI.Label(rect, label, EditorStyles.boldLabel);
        }
    }
}
