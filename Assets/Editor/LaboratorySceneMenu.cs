using UnityEditor;
using UnityEditor.SceneManagement;

namespace SpringUp.LaboratoryEditor
{
    // 场景文件是唯一布局来源；编辑器菜单只负责打开，不再生成或覆盖布局。
    public static class LaboratorySceneMenu
    {
        public const string ScenePath = "Assets/Scenes/LaboratoryDemo.unity";

        [MenuItem("Tools/SpringUp/Open Laboratory Scene")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
