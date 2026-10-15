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
        public Text status;
        public Button closeButton, sellButton, discardButton;
        RunController run;
        RunLaboratoryEquipment equipment;
        public void Bind(FlowApplication app)
        {
            run = app.Session.Run;
            equipment = new RunLaboratoryEquipment(run, app.Configuration.Definitions);
            run.LoadoutChanged += Changed;
            presenter.Bind(equipment);
            closeButton.onClick.AddListener(app.CloseTop);
            sellButton.onClick.AddListener(() => RemoveSelected(true));
            discardButton.onClick.AddListener(() => RemoveSelected(false));
            RefreshStatus();
        }
        void Changed(InventorySnapshot snapshot) { equipment.Refresh(); RefreshStatus(); }
        void RefreshStatus() => status.text = $"背包 {run.Inventory.UnequippedIds().Count}/{run.Inventory.UnequippedCapacity()} · 骨髓 {run.Marrow}\n超限时请出售或丢弃物品，再进入下一波。\n\n选择背包物品，再选择对应部位槽位，点击安装/替换。\n只选择已装备槽位可拆卸。\n\n关闭背包后在休整地图攻击木桩，测试新构筑。\n背包打开期间世界暂停。";
        void RemoveSelected(bool sell)
        {
            var id = presenter.SelectedInstanceId;
            if (id == null) { status.text = "请先选择物品。"; return; }
            string error;
            bool ok = sell ? equipment.TrySell(id, out error) : equipment.TryDiscard(id, out error);
            if (!ok) status.text = error;
        }
        void OnDestroy() { if (run != null) run.LoadoutChanged -= Changed; }
    }
}
