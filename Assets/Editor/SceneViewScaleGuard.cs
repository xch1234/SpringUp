using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    // Unity can persist an orthographic Scene view size as float.Epsilon after extreme zoom.
    // That makes the picking projection invalid (IsMatrixValid), even after Play mode exits.
    [InitializeOnLoad]
    public static class SceneViewScaleGuard
    {
        static SceneViewScaleGuard()
        {
            EditorApplication.delayCall += RepairOpenViews;
            SceneView.beforeSceneGui += view => Repair(view);
            EditorApplication.playModeStateChanged += state =>
            { if (state == PlayModeStateChange.EnteredEditMode) RepairOpenViews(); };
        }
        static void RepairOpenViews()
        {
            foreach (SceneView view in SceneView.sceneViews) Repair(view);
        }
        public static bool Repair(SceneView view)
        {
            if (view == null || (Finite(view.size) && view.size >= .001f)) return false;
            var pivot = view.pivot;
            if (!Finite(pivot.x) || !Finite(pivot.y) || !Finite(pivot.z)) pivot = Vector3.zero;
            view.LookAtDirect(pivot, view.rotation, 10f);
            view.Repaint();
            return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
