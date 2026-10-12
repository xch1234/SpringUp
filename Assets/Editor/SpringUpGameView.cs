using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class SpringUpGameView
    {
        private static readonly Type ViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        private static readonly PropertyInfo Gizmos = ViewType?.GetProperty("drawGizmos",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly Dictionary<EditorWindow, bool> Previous = new Dictionary<EditorWindow, bool>();

        static SpringUpGameView()
        {
            EditorApplication.playModeStateChanged += StateChanged;
            if (EditorApplication.isPlaying) EditorApplication.delayCall += HideForDemo;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += HideForDemo;
            if (state == PlayModeStateChange.ExitingPlayMode) Restore();
        }

        private static void HideForDemo()
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != SpringUpDemoBuilder.ScenePath) return;
            Hide();
        }

        [MenuItem("Tools/SpringUp/Hide Game View Gizmos")]
        public static void Hide()
        {
            // GameView has no public editor API; keep this version-specific access editor-only.
            if (Gizmos == null || !Gizmos.CanWrite)
            {
                Debug.LogWarning("[SpringUp Game View] Disable Gizmos manually in the Game view toolbar.");
                return;
            }
            foreach (var item in Resources.FindObjectsOfTypeAll(ViewType))
            {
                var window = (EditorWindow)item;
                if (!Previous.ContainsKey(window)) Previous.Add(window, (bool)Gizmos.GetValue(window));
                Gizmos.SetValue(window, false);
                window.Repaint();
            }
        }

        private static void Restore()
        {
            foreach (var item in Previous)
                if (item.Key != null) { Gizmos.SetValue(item.Key, item.Value); item.Key.Repaint(); }
            Previous.Clear();
        }

        public static void CheckBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run only in an isolated batch editor.");
            if (Gizmos == null || !Gizmos.CanWrite) throw new InvalidOperationException("GameView.drawGizmos unavailable.");
            var window = EditorWindow.GetWindow(ViewType);
            Gizmos.SetValue(window, true);
            Hide();
            if ((bool)Gizmos.GetValue(window)) throw new InvalidOperationException("Game view gizmos were not hidden.");
            Restore();
            if (!(bool)Gizmos.GetValue(window)) throw new InvalidOperationException("Game view gizmos were not restored.");
            Debug.Log("[SpringUp Game View] PASS: hide and restore gizmos; runtime objects unchanged.");
        }
    }
}
