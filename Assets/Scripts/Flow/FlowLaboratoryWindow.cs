using SpringUp.Laboratory;
using Tideflesh.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Flow
{
    // Owns only the view and its subscription. RunController remains the transaction authority.
    public sealed class FlowLaboratoryWindow : MonoBehaviour
    {
        public LaboratoryPresenter presenter;
        public BackpackPresenter backpack;
        public Text status;
        public Button closeButton, sellButton, discardButton;
        RunController run;
        RunLaboratoryEquipment equipment;
        FlowApplication owner;
        bool editable;
        public void Bind(FlowApplication app)
        {
            owner = app;
            run = app.Session.Run;
            equipment = new RunLaboratoryEquipment(run, app.Configuration.Definitions);
            // The artwork-only backpack prefab predates the access-status field.
            if (status == null && backpack != null)
            {
                var go = new GameObject("Equipment access status", typeof(RectTransform), typeof(Text));
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                status = go.GetComponent<Text>();
                var rect = status.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(80, -900); rect.sizeDelta = new Vector2(1320, 110);
                status.fontSize = 24; status.color = Color.white;
                status.alignment = TextAnchor.UpperLeft; status.raycastTarget = false;
                go.AddComponent<Outline>().effectColor = Color.black;
            }
            foreach (var text in GetComponentsInChildren<Text>(true)) text.font = app.hud.font;
            run.LoadoutChanged += Changed;
            if (backpack != null)
            { backpack.ReadOnly = !app.CanModifyEquipment; backpack.Bind(equipment); }
            else
            { presenter.ReadOnly = !app.CanModifyEquipment; presenter.Bind(equipment); }
            if (closeButton != null)
            {
                closeButton.GetComponentInChildren<Text>().text = $"关闭 · 返回游戏 [Esc / {GameSettings.InventoryHint} / 右键]";
                closeButton.onClick.AddListener(app.CloseTop);
            }
            if (sellButton != null) sellButton.gameObject.SetActive(false);
            editable = app.CanModifyEquipment;
            discardButton.interactable = editable;
            discardButton.onClick.AddListener(() => RemoveSelected());
            RefreshStatus();
        }
        void Update()
        {
            if (owner == null || run == null || editable == owner.CanModifyEquipment) return;
            editable = owner.CanModifyEquipment;
            if (backpack != null) backpack.ReadOnly = !editable;
            if (presenter != null) presenter.ReadOnly = !editable;
            if (discardButton != null) discardButton.interactable = editable;
            RefreshStatus();
        }
        void Changed(InventorySnapshot snapshot) { equipment.Refresh(); RefreshStatus(); }
        public void RefreshInventory() { equipment?.Refresh(); if (run != null) RefreshStatus(); }
        void RefreshStatus()
        {
            if (status == null) return;
            status.text = (editable ? "实验室 · 拖动调整装备" : "背包 · 仅可查看")
            + $" · 骨髓 {run.Marrow}\n"
            + (editable ? "装配期间世界暂停；关闭后可前往木桩测试构筑。" : $"仅在休整区靠近 LAB 柜台按 {GameSettings.Display(GameBinding.Interact)}，才能装配、拆卸或交换器官。")
            + $"\nEsc / {GameSettings.InventoryHint} / 鼠标右键：关闭并返回游戏";
        }
        void RemoveSelected()
        {
            if (owner == null || !owner.CanModifyEquipment) return;
            var id = backpack != null ? backpack.SelectedInstanceId : presenter.SelectedInstanceId;
            if (id == null) { if (status != null) status.text = "请先选择物品。"; return; }
            string error;
            bool ok = equipment.TryDiscard(id, out error);
            if (!ok && status != null) status.text = error;
        }
        void OnDestroy() { if (run != null) run.LoadoutChanged -= Changed; }
    }
}
