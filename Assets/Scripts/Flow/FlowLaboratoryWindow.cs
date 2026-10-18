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
        public void Bind(FlowApplication app)
        {
            run = app.Session.Run;
            equipment = new RunLaboratoryEquipment(run, app.Configuration.Definitions);
            foreach (var text in GetComponentsInChildren<Text>(true)) text.font = app.hud.font;
            run.LoadoutChanged += Changed;
            if (backpack != null)
            { backpack.ReadOnly = run.Stage != RunPhase.Rest; backpack.Bind(equipment); }
            else
            { presenter.ReadOnly = run.Stage != RunPhase.Rest; presenter.Bind(equipment); }
            if (closeButton != null)
            {
                closeButton.GetComponentInChildren<Text>().text = "关闭背包 · 返回游戏 [Esc / B / Tab]";
                closeButton.onClick.AddListener(app.CloseTop);
            }
            if (sellButton != null) sellButton.gameObject.SetActive(false);
            discardButton.interactable = run.Stage == RunPhase.Rest;
            discardButton.onClick.AddListener(() => RemoveSelected());
            RefreshStatus();
        }
        void Changed(InventorySnapshot snapshot) { equipment.Refresh(); RefreshStatus(); }
        public void RefreshInventory() { equipment?.Refresh(); if (run != null) RefreshStatus(); }
        void RefreshStatus()
        {
            if (status == null) return;
            status.text = (run.Stage == RunPhase.Rest ? "休整期 · 拖动调整装备" : "战斗中 · 仅可查看")
            + $" · 背包 {run.Inventory.UnequippedIds().Count} 件 · 骨髓 {run.Marrow}\n背包打开期间世界暂停；关闭后可攻击休整区木桩测试构筑。";
        }
        void RemoveSelected()
        {
            var id = backpack != null ? backpack.SelectedInstanceId : presenter.SelectedInstanceId;
            if (id == null) { if (status != null) status.text = "请先选择物品。"; return; }
            string error;
            bool ok = equipment.TryDiscard(id, out error);
            if (!ok && status != null) status.text = error;
        }
        void OnDestroy() { if (run != null) run.LoadoutChanged -= Changed; }
    }
}
