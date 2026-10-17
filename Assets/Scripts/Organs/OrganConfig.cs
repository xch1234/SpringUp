using UnityEngine;

namespace SpringUp.Organs
{
    // A catalog entry binds exactly one behaviour config. Runtime state lives elsewhere.
    [CreateAssetMenu(fileName = "NewOrgan", menuName = "SpringUp/Organ Config")]
    public sealed class OrganConfig : ScriptableObject
    {
        [SerializeField] private OrganActuatorConfig actuator;
        [SerializeField] private OrganTriggerConfig trigger;
        [SerializeField] private TorsoOrganConfig torso;
        public OrganActuatorConfig Actuator => actuator;
        public OrganTriggerConfig Trigger => trigger;
        public TorsoOrganConfig Torso => torso;
        public string DisplayName => actuator != null ? actuator.DisplayName
            : trigger != null ? trigger.DisplayName : torso != null ? torso.DisplayName : name;

        public bool TryCreateDefinition(out OrganDefinition definition, out string error)
        {
            definition = null;
            if ((actuator != null ? 1 : 0) + (trigger != null ? 1 : 0) + (torso != null ? 1 : 0) != 1)
            { error = "同一器官只能关联一种行为配置（执行器、触发器或躯干），且不能缺失。"; return false; }
            if (actuator != null) return actuator.TryCreateDefinition(out definition, out error);
            if (trigger != null) return trigger.TryCreateDefinition(out definition, out error);
            return torso.TryCreateDefinition(out definition, out error);
        }
    }
}
