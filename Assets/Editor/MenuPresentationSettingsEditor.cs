using System.Linq;
using SpringUp.Flow;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(MenuPresentationSettings))]
    public sealed class MenuPresentationSettingsEditor : Editor
    {
        public const string AssetPath = "Assets/Resources/MenuPresentationSettings.asset";
        [MenuItem("Tools/SpringUp/主菜单与结算/编辑图片、布局和提示")]
        public static void SelectSettings()
        { Selection.activeObject = EnsureAsset(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MenuPresentationSettings EnsureAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MenuPresentationSettings>(AssetPath);
            if (settings != null) return settings;
            settings = CreateInstance<MenuPresentationSettings>();
            settings.cover.sprite = SpriteAt("封面"); settings.background.sprite = SpriteAt("背景");
            settings.defeat.sprite = SpriteAt("battle-fail"); settings.victory.sprite = SpriteAt("battle-victory");
            AssetDatabase.CreateAsset(settings, AssetPath); AssetDatabase.SaveAssets(); return settings;
        }
        static Sprite SpriteAt(string name) => AssetDatabase.LoadAllAssetsAtPath("Assets/Art/UI/" + name + ".png").OfType<Sprite>().FirstOrDefault();
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("从 Bootstrap 运行后，修改这里可实时查看。点击下方“保存配置”保留参数。图片请拖入 Sprite 子资源。封面自带按钮文字，三个点击区需与图片上的按钮对齐；打开“显示点击区域”可检查位置。", MessageType.Info);
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("主菜单按封面尺寸等比适配，整体缩放会同时缩放点击区。结算窗口和提示按参考尺寸等比缩放；提示偏移从窗口下边缘算起。背景透明度与颜色 A 相乘。闪烁使用真实时间，结算暂停时仍有效。", MessageType.Info);
            if (GUILayout.Button("保存配置")) AssetDatabase.SaveAssets();
        }
    }
}
