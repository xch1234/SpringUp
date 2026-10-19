using SpringUp.Organs;
using UnityEditor;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(PatternActuatorConfig))]
    public sealed class PatternActuatorConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            void Fields(params string[] names) { foreach (var name in names) EditorGUILayout.PropertyField(serializedObject.FindProperty(name)); }
            Fields("organId", "displayName", "targetingRange", "pattern");
            var pattern = (ActuatorPattern)serializedObject.FindProperty("pattern").enumValueIndex;
            Fields("damage");
            if (pattern != ActuatorPattern.Laser && pattern != ActuatorPattern.ChargedLaser && pattern != ActuatorPattern.Fog) Fields("speed");
            if (pattern != ActuatorPattern.Fog) Fields("hitRadius");
            if (pattern != ActuatorPattern.Summon && pattern != ActuatorPattern.Laser && pattern != ActuatorPattern.ChargedLaser) Fields("lifetime");
            switch (pattern)
            {
                case ActuatorPattern.Orbit: case ActuatorPattern.Spiral: Fields("orbitRadius", "angularSpeed"); break;
                case ActuatorPattern.Homing: Fields("angularSpeed"); break;
                case ActuatorPattern.Laser: Fields("range"); break;
                case ActuatorPattern.ChargedLaser: Fields("range", "chargeTime"); break;
                case ActuatorPattern.Fog: Fields("areaRadius", "interval"); break;
                case ActuatorPattern.Vampire: Fields("lifeSteal"); break;
                case ActuatorPattern.Triple: Fields("spreadAngle"); break;
                case ActuatorPattern.Sucker: Fields("interval", "slowMultiplier"); break;
                case ActuatorPattern.Missile: Fields("explosionDamage", "areaRadius"); break;
                case ActuatorPattern.Spore: Fields("driftDuration"); break;
                case ActuatorPattern.Summon: Fields("health", "healthDecay", "followDistance", "interval"); break;
            }
            serializedObject.ApplyModifiedProperties();
            if (!((PatternActuatorConfig)target).TryCreateDefinition(out _, out var error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
