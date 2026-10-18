using System;
using SpringUp.Laboratory;
using SpringUp.Organs;
using UnityEngine;
using UnityEngine.UI;

namespace Tideflesh.Systems
{
    public sealed class RunEquipmentHud : MonoBehaviour
    {
        [Serializable] public struct Slot { public LaboratoryPart part; public int index; public OrganIconView view; }
        public Slot[] slots;
        public Text reportText;
        public Button reportToggle;
        public GameObject reportPanel;
        RunLaboratoryEquipment equipment;
        OrganCombatReport report;
        OrganPulseBinding pulses;
        int reportRevision = -1;
        public void BindReport(OrganCombatReport value) { report = value; reportRevision = -1; }
        public void BindPipeline(OrganPipelineController pipeline)
        {
            pulses?.Dispose();
            pulses = new OrganPulseBinding(pipeline, (part, index) =>
            {
                foreach (var slot in slots)
                    if ((int)slot.part == (int)part && slot.index == index) slot.view.Pulse();
            });
        }
        public static RunEquipmentHud Create(Transform parent, Font font)
        {
            var prefab = Resources.Load<RunEquipmentHud>("EquipmentHud");
            if (prefab == null) throw new InvalidOperationException("Missing EquipmentHud prefab. Run Tools/SpringUp/UI/Build Organ UI Assets.");
            var hud = Instantiate(prefab, parent, false);
            foreach (var text in hud.GetComponentsInChildren<Text>(true)) text.font = font;
            hud.reportToggle.onClick.AddListener(() => hud.reportPanel.SetActive(!hud.reportPanel.activeSelf));
            return hud;
        }
        public void Bind(RunLaboratoryEquipment source)
        {
            if (equipment != null) equipment.Changed -= Refresh;
            equipment = source;
            if (equipment != null) equipment.Changed += Refresh;
            Refresh();
        }
        void Refresh()
        {
            foreach (var slot in slots)
            {
                slot.view.ClearPulse();
                slot.view.Bind(equipment?.GetSlot(slot.part, slot.index), slot.index + 1);
            }
        }
        void Update()
        {
            if (report != null && reportPanel.activeInHierarchy && reportRevision != report.Revision)
            { reportRevision = report.Revision; reportText.text = report.Describe(); }
        }
        void OnDestroy()
        {
            if (equipment != null) equipment.Changed -= Refresh;
            pulses?.Dispose();
        }
    }
}
