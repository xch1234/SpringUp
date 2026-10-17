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
            Field("organId"); Field("displayName"); Field("condition"); Field("triggerTarget");
            switch ((TriggerOn)serializedObject.FindProperty("condition").enumValueIndex)
            {
                case TriggerOn.Hit: case TriggerOn.Debuff: Field("probability"); break;
                case TriggerOn.Started: Field("delay"); break;
                case TriggerOn.Kill: Field("killThreshold"); break;
                case TriggerOn.Stationary: Field("chargeRate"); Field("chargeThreshold"); break;
            }
            serializedObject.ApplyModifiedProperties();
            if (((OrganTriggerConfig)target).TryCreateDefinition(out var definition, out var error))
                EditorGUILayout.HelpBox(definition.Trigger.Description + (definition.TriggerTarget == TriggerTargetKind.SelfSlots
                    ? " 追加发动自身整条管道的执行器。" : " 追加发动同管道后续执行器。"), MessageType.Info);
            else EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
