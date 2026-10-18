using UnityEngine;
using UnityEngine.EventSystems;

namespace SpringUp.Laboratory
{
    public sealed class InventoryDropArea : MonoBehaviour, IDropHandler
    {
        public BackpackPresenter owner;
        public void OnDrop(PointerEventData e) => owner?.DropToInventory();
    }
}
