using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Laboratory
{
    // 只负责左侧装配；选择物品不会暂停或重置右侧战斗。
    public sealed class LaboratoryPresenter : MonoBehaviour
    {
        public LaboratoryDemoBootstrap source;
        public Transform[] partRoots;
        public Transform inventoryRoot;
        public OrganCardView slotPrefab, inventoryPrefab;
        public Text selectionText, inventoryTitle;
        public Button installButton, removeButton;
        public Font fontOverride;
        private ILaboratoryEquipment equipment;
        private string candidateId;
        private int part = 1, slot = -1;
        private Font font;
        private bool ownsFont;
        public static readonly string[] PartNames = { "头部", "手部", "腿部", "躯干" };
        private LaboratoryItem Candidate => equipment.Inventory.FirstOrDefault(x => x.InstanceId == candidateId);
        private void OnEnable() { if (source != null) InitializeView(); }
        private void Start() { if (equipment == null) InitializeView(); }
        public void InitializeView()
        {
            if (equipment != null) equipment.Changed -= EquipmentChanged;
            equipment = source.Equipment;
            equipment.Changed += EquipmentChanged;
            if (font == null)
            {
                font = fontOverride;
                if (font == null)
                {
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 18);
                    ownsFont = true;
                }
            }
            foreach (var text in GetComponentsInChildren<Text>(true)) text.font = font;
            installButton.onClick.RemoveListener(Install); installButton.onClick.AddListener(Install);
            removeButton.onClick.RemoveListener(Remove); removeButton.onClick.AddListener(Remove);
            Refresh();
        }
        public void SelectSlot(int body, int index)
        {
            if (body < 0 || body > 3 || index < 0 || index >= equipment.SlotCount((LaboratoryPart)body)) return;
            slot = part == body && slot == index ? -1 : index;
            part = body; Refresh();
        }
        public void SelectInventory(string id) { candidateId = candidateId == id ? null : id; Refresh(); }
        private void EquipmentChanged() { candidateId = null; slot = -1; Refresh(); }
        public void Refresh()
        {
            if (equipment == null) return;
            for (int p = 0; p < 4; p++)
            {
                var views = Sync(partRoots[p], equipment.SlotCount((LaboratoryPart)p), slotPrefab);
                for (int s = 0; s < views.Count; s++)
                {
                    int body = p, index = s;
                    var item = equipment.GetSlot((LaboratoryPart)p, s);
                    views[s].caption.font = font;
                    views[s].Bind($"{(part == p && slot == s ? "> " : "")}{s + 1}\n{item?.Name ?? "空"}", () => SelectSlot(body, index));
                }
            }
            var cards = Sync(inventoryRoot, equipment.Inventory.Count, inventoryPrefab);
            for (int i = 0; i < cards.Count; i++)
            {
                var item = equipment.Inventory[i];
                cards[i].caption.font = font;
                cards[i].Bind($"{(candidateId == item.InstanceId ? "> " : "")}{item.Name}\n{PartNames[(int)item.Part]}", () => SelectInventory(item.InstanceId));
            }
            var candidate = Candidate;
            var target = equipment.GetSlot((LaboratoryPart)part, slot);
            inventoryTitle.text = $"背包 · {equipment.Inventory.Count} 件";
            installButton.interactable = candidate != null && slot >= 0 && candidate.Part == (LaboratoryPart)part;
            removeButton.interactable = candidate == null && target != null;
            installButton.GetComponentInChildren<Text>().text = target == null ? "安装" : "替换";
            selectionText.text = $"物品：{candidate?.Name ?? "未选择"}    槽位：{(slot < 0 ? "未选择" : PartNames[part] + " " + (slot + 1))}\n" +
                (candidate?.Description ?? target?.Description ?? "选择物品和对应槽位安装；只选择已装备槽位可拆下。");
        }
        private void Install()
        {
            if (!installButton.interactable) return;
            if (!equipment.TryEquip(candidateId, (LaboratoryPart)part, slot, out var error)) selectionText.text = error;
        }
        private void Remove()
        {
            if (!removeButton.interactable) return;
            if (!equipment.TryUnequip((LaboratoryPart)part, slot, out var error)) selectionText.text = error;
        }
        private static List<OrganCardView> Sync(Transform root, int count, OrganCardView prefab)
        {
            var cards = root.Cast<Transform>().Select(x => x.GetComponent<OrganCardView>()).ToList();
            while (cards.Count > count)
            {
                var last = cards[cards.Count - 1]; cards.RemoveAt(cards.Count - 1);
                last.transform.SetParent(null); last.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(last.gameObject); else DestroyImmediate(last.gameObject);
            }
            while (cards.Count < count) cards.Add(Instantiate(prefab, root));
            return cards;
        }
        private void OnDisable()
        {
            if (equipment != null) equipment.Changed -= EquipmentChanged;
            equipment = null;
            if (installButton != null) installButton.onClick.RemoveListener(Install);
            if (removeButton != null) removeButton.onClick.RemoveListener(Remove);
            if (ownsFont && font != null) { if (Application.isPlaying) Destroy(font); else DestroyImmediate(font); }
            font = null; ownsFont = false;
        }
    }
}
