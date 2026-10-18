using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpringUp.Flow
{
    // Restored/default focus is not a hover. Only explicit keyboard navigation keeps a focus highlight.
    public sealed class MenuButton : Button
    {
        bool hovered, keyboardFocus;
        public override void OnPointerEnter(PointerEventData eventData)
        { hovered = true; base.OnPointerEnter(eventData); }
        public override void OnPointerExit(PointerEventData eventData)
        { hovered = false; base.OnPointerExit(eventData); }
        public override void OnSelect(BaseEventData eventData)
        { keyboardFocus = eventData is AxisEventData; base.OnSelect(eventData); }
        public override void OnDeselect(BaseEventData eventData)
        { keyboardFocus = false; base.OnDeselect(eventData); }
        protected override void OnCanvasGroupChanged()
        {
            base.OnCanvasGroupChanged();
            if (!IsInteractable()) { hovered = false; keyboardFocus = false; }
        }
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (state == SelectionState.Highlighted && !hovered) state = SelectionState.Normal;
            if (state == SelectionState.Selected && !keyboardFocus)
                state = hovered ? SelectionState.Highlighted : SelectionState.Normal;
            base.DoStateTransition(state, instant);
        }
        protected override void OnDisable()
        { hovered = keyboardFocus = false; base.OnDisable(); }
    }
}
