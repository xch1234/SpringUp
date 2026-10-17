using SpringUp.Organs;
using UnityEditor;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(DirectActuatorConfig))]
    public sealed class DirectActuatorConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            Field("organId"); Field("displayName"); Field("shape");
            var shape = (AttackShape)serializedObject.FindProperty("shape").enumValueIndex;
            if (shape == AttackShape.BlackHole)
            {
                Field("blackHoleRadius"); Field("blackHoleDuration"); Field("blackHolePullSpeed");
                EditorGUILayout.HelpBox("黑洞只吸引，不结算伤害或命中状态；持续时间独立于管道节拍。", MessageType.Info);
            }
            else
            {
                Field("damage");
                if (shape == AttackShape.Explosion) Field("explosionRadius");
                Field("statusEffect");
            }
            serializedObject.ApplyModifiedProperties();
            if (!((DirectActuatorConfig)target).TryCreateDefinition(out _, out var error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
