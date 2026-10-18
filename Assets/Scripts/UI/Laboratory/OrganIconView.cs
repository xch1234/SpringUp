using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Laboratory
{
    // Presentation only: combat and previews can both call Pulse without owning a combat report.
    public sealed class OrganIconView : MonoBehaviour
    {
        public OrganArtCatalog art;
        public Image icon, selection, dropHint, flash;
        public Text fallback, number;
        public float pulseDuration = .35f;
        public Color pulseColor = new Color(1, .72f, .2f, .65f);
        public Color validColor = new Color(.2f, 1, .6f, .3f);
        public Color invalidColor = new Color(1, .2f, .2f, .3f);
        float until;
        public string InstanceId { get; private set; }
        public void Bind(LaboratoryItem item, int slotNumber = 0)
        {
            if (InstanceId != item?.InstanceId) ClearPulse();
            InstanceId = item?.InstanceId;
            icon.sprite = item == null ? null : art?.Find(item.DefinitionId);
            icon.enabled = icon.sprite != null;
            fallback.text = item == null ? "" : icon.sprite != null ? "" : item.Name;
            number.text = slotNumber > 0 ? slotNumber.ToString() : "";
            if (item == null) SetSelected(false);
        }
        public void SetSelected(bool value) => selection.enabled = value;
        public void SetDropHint(bool? valid)
        {
            dropHint.enabled = valid.HasValue;
            if (valid.HasValue) dropHint.color = valid.Value ? validColor : invalidColor;
        }
        public void Pulse() { until = Time.unscaledTime + Mathf.Max(.01f, pulseDuration); Tick(); }
        public void ClearPulse() { until = 0; if (flash != null) flash.enabled = false; }
        void Update() => Tick();
        void Tick()
        {
            flash.enabled = Time.unscaledTime < until;
            if (flash.enabled)
            {
                var color = pulseColor;
                color.a *= Mathf.Clamp01((until - Time.unscaledTime) / Mathf.Max(.01f, pulseDuration));
                flash.color = color;
            }
        }
        void OnDisable() { ClearPulse(); SetDropHint(null); }
    }
}
