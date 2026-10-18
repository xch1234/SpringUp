using System.Linq;
using Tideflesh.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpringUp.Laboratory
{
    public sealed class BackpackPresenter : MonoBehaviour
    {
        public OrganDragSlot[] equipmentSlots, inventorySlots;
        public OrganIconView detailIcon, dragGhost;
        public Text detail, pageLabel, message;
        public Button previousPage, nextPage;
        public ScrollRect detailsScroll;
        public bool ReadOnly { get; set; }
        public string SelectedInstanceId { get; private set; }
        public bool IsDragging => draggingId != null;
        RunLaboratoryEquipment equipment;
        int page;
        string draggingId;
        LaboratoryPart fromPart;
        int fromIndex;
        bool fromInventory;
        LaboratoryItem draggingItem;
        public void Bind(RunLaboratoryEquipment source)
        {
            Unbind(); equipment = source; equipment.Changed += Refresh;
            foreach (var slot in equipmentSlots.Concat(inventorySlots)) slot.owner = this;
            previousPage.onClick.AddListener(Previous); nextPage.onClick.AddListener(Next);
            Refresh();
        }
        void Previous() => ChangePage(-1);
        void Next() => ChangePage(1);
        public void ChangePage(int offset) { CancelDrag(); page += offset; Refresh(); }
        public void Select(string id)
        {
            SelectedInstanceId = id; RefreshSelection();
            Canvas.ForceUpdateCanvases();
            if (detailsScroll != null) detailsScroll.verticalNormalizedPosition = 1;
        }
        public void Refresh()
        {
            CancelDrag();
            if (equipment == null) return;
            var items = equipment.Inventory;
            int pages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)inventorySlots.Length));
            page = Mathf.Clamp(page, 0, pages - 1);
            foreach (var slot in equipmentSlots) slot.Bind(equipment.GetSlot(slot.part, slot.index));
            for (int i = 0; i < inventorySlots.Length; i++)
            {
                int position = page * inventorySlots.Length + i;
                inventorySlots[i].Bind(position < items.Count ? items[position] : null);
            }
            pageLabel.text = $"{page + 1} / {pages} 页 · {items.Count} 件";
            previousPage.interactable = page > 0; nextPage.interactable = page + 1 < pages;
            RefreshSelection();
        }
        void RefreshSelection()
        {
            if (equipment == null) return;
            var selectedSlot = equipmentSlots.FirstOrDefault(s => s.Item != null && s.Item.InstanceId == SelectedInstanceId);
            var item = selectedSlot?.Item ?? equipment.Inventory.FirstOrDefault(x => x.InstanceId == SelectedInstanceId);
            if (item == null) SelectedInstanceId = null;
            foreach (var slot in equipmentSlots.Concat(inventorySlots))
                slot.view.SetSelected(item != null && slot.Item?.InstanceId == item.InstanceId);
            detailIcon.Bind(item);
            detail.text = item == null ? "选择器官查看详情\n\n拖到装备槽：装配 / 替换\n拖回背包区域：卸下\n装备之间拖动：移动 / 交换\n\n同一行从左向右执行。" :
                $"{item.Name}\n{item.Kind} · {item.MountLabel}\n位置：{(selectedSlot == null ? "背包" : LaboratoryPresenter.PartNames[(int)selectedSlot.part] + "第 " + (selectedSlot.index + 1) + " 槽")}\n\n{item.Description}";
        }
        public void BeginDrag(OrganDragSlot source, PointerEventData e)
        {
            CancelDrag(); Select(source.Item?.InstanceId);
            if (ReadOnly) { message.text = "战斗中仅可查看，休整期间才能调整装备。"; return; }
            if (source.Item == null) return;
            draggingId = source.Item.InstanceId; draggingItem = source.Item;
            fromInventory = source.inventory; fromPart = source.part; fromIndex = source.index;
            dragGhost.Bind(draggingItem); dragGhost.gameObject.SetActive(true); dragGhost.transform.SetAsLastSibling();
            foreach (var slot in equipmentSlots.Concat(inventorySlots)) slot.view.SetDropHint(CanDrop(slot) ? true : (bool?)null);
            message.text = "拖到亮起的槽位放置；拖回背包卸下；松开在其他区域取消。";
            MoveDrag(e);
        }
        public void MoveDrag(PointerEventData e)
        {
            if (!IsDragging) return;
            var parent = (RectTransform)dragGhost.transform.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var local))
                dragGhost.transform.localPosition = local;
        }
        public bool CanDrop(OrganDragSlot target)
        {
            if (!IsDragging || ReadOnly) return false;
            if (target.inventory) return !fromInventory;
            if (!draggingItem.CanEquip(target.part)) return false;
            return fromInventory || target.Item == null || target.Item.CanEquip(fromPart);
        }
        public void Drop(OrganDragSlot target)
        {
            if (!IsDragging) return;
            if (target.inventory) { DropToInventory(); return; }
            if (!CanDrop(target)) { Finish(false, "器官与目标部位不匹配，无法放置或交换。"); return; }
            string id = draggingId;
            bool ok = fromInventory ? equipment.TryEquip(id, target.part, target.index, out var error)
                : equipment.TryMove(id, fromPart, fromIndex, target.part, target.index, out error);
            Finish(ok, error, id);
        }
        public void DropToInventory()
        {
            if (!IsDragging) return;
            if (ReadOnly || fromInventory) { CancelDrag(); return; }
            string id = draggingId;
            bool ok = equipment.TryUnequipInstance(id, fromPart, fromIndex, out var error);
            if (ok)
            {
                int position = equipment.Inventory.ToList().FindIndex(x => x.InstanceId == id);
                page = Mathf.Max(0, position / inventorySlots.Length);
            }
            Finish(ok, error, id);
        }
        void Finish(bool ok, string error, string id = null)
        {
            CancelDrag();
            if (ok) { SelectedInstanceId = id; Refresh(); }
            message.text = ok ? "装备已更新。" : error;
        }
        public void CancelDrag()
        {
            if (IsDragging && message != null) message.text = "已取消拖动。";
            draggingId = null; draggingItem = null;
            if (dragGhost != null) dragGhost.gameObject.SetActive(false);
            if (equipmentSlots != null) foreach (var slot in equipmentSlots) slot.view.SetDropHint(null);
            if (inventorySlots != null) foreach (var slot in inventorySlots) slot.view.SetDropHint(null);
        }
        // Deliberately visual-only. A future preview uses OrganPulseBinding against its own pipeline.
        [ContextMenu("Preview selected icon pulse")]
        public void PreviewSelectedPulse()
        {
            foreach (var slot in equipmentSlots.Concat(inventorySlots))
                if (SelectedInstanceId != null && slot.Item?.InstanceId == SelectedInstanceId) slot.view.Pulse();
            if (SelectedInstanceId != null) detailIcon.Pulse();
        }
        void Unbind()
        {
            if (equipment != null) equipment.Changed -= Refresh;
            equipment = null;
            previousPage.onClick.RemoveListener(Previous); nextPage.onClick.RemoveListener(Next);
        }
        void OnDisable() { CancelDrag(); Unbind(); }
    }
}
