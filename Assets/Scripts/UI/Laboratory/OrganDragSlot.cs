using UnityEngine;
using UnityEngine.EventSystems;

namespace SpringUp.Laboratory
{
    public sealed class OrganDragSlot : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public OrganIconView view;
        public bool inventory;
        public LaboratoryPart part;
        public int index;
        [System.NonSerialized] public BackpackPresenter owner;
        public LaboratoryItem Item { get; private set; }
        public void Bind(LaboratoryItem item) { Item = item; view.Bind(item, inventory ? 0 : index + 1); }
        public void OnPointerClick(PointerEventData e)
        { if (e.button == PointerEventData.InputButton.Left && !e.dragging) owner?.Select(Item?.InstanceId); }
        public void OnBeginDrag(PointerEventData e)
        { if (e.button == PointerEventData.InputButton.Left) owner?.BeginDrag(this, e); }
        public void OnDrag(PointerEventData e) => owner?.MoveDrag(e);
        public void OnEndDrag(PointerEventData e) => owner?.CancelDrag();
        public void OnDrop(PointerEventData e) => owner?.Drop(this);
        public void OnPointerEnter(PointerEventData e) { if (owner != null && owner.IsDragging) view.SetDropHint(owner.CanDrop(this)); }
        public void OnPointerExit(PointerEventData e) { if (owner != null && owner.IsDragging) view.SetDropHint(owner.CanDrop(this) ? true : (bool?)null); }
    }
}
