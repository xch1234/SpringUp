using SpringUp.Organs;
using UnityEditor;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(OrganTriggerConfig))]
    public sealed class OrganTriggerConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            Field("organId"); Field("displayName"); Field("condition");
            switch ((TriggerOn)serializedObject.FindProperty("condition").enumValueIndex)
            {
                case TriggerOn.Hit: case TriggerOn.Debuff: Field("probability"); break;
                case TriggerOn.Started: Field("delay"); break;
                case TriggerOn.Kill: Field("killThreshold"); break;
                case TriggerOn.Stationary: Field("chargeRate"); Field("chargeThreshold"); break;
            }
            serializedObject.ApplyModifiedProperties();
            if (((OrganTriggerConfig)target).TryCreateDefinition(out var definition, out var error))
                EditorGUILayout.HelpBox(definition.Trigger.Description + " 只发动紧邻后一个执行器；该执行器不参与自动循环。攻击条件只监听紧邻前一个器官。", MessageType.Info);
            else EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
