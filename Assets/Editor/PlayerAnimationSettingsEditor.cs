using TideBorne.Battle;
using UnityEditor;

namespace SpringUp.EditorChecks
{
    public static class PlayerAnimationSettingsEditor
    {
        [MenuItem("Tools/SpringUp/主角/编辑主角动画")]
        public static void SelectSettings()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<PlayerAnimationSettings>("Assets/Resources/PlayerAnimationSettings.asset");
            if (Selection.activeObject != null) EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}
