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
            presenter.ReadOnly = run.Stage != RunPhase.Rest;
            presenter.Bind(equipment);
            closeButton.GetComponentInChildren<Text>().text = "关闭背包 · 返回游戏 [Esc / B / Tab]";
            closeButton.onClick.AddListener(app.CloseTop);
            if (sellButton != null) sellButton.gameObject.SetActive(false);
            discardButton.interactable = run.Stage == RunPhase.Rest;
            discardButton.onClick.AddListener(() => RemoveSelected());
            RefreshStatus();
        }
        void Changed(InventorySnapshot snapshot) { equipment.Refresh(); RefreshStatus(); }
        void RefreshStatus() => status.text = (run.Stage == RunPhase.Rest ? "休整期：允许装卸、替换、丢弃。\n" : "战斗只读：休整期才能装卸、替换、丢弃。\n") + $"背包 {run.Inventory.UnequippedIds().Count}/{run.Inventory.UnequippedCapacity()} · 骨髓 {run.Marrow}\n超限时请找商人出售或在休整期丢弃物品，再进入下一波。\n\n选择背包物品，再选择对应部位槽位，点击安装/替换。\n只选择已装备槽位可拆卸。\n\n关闭背包后在休整地图攻击木桩，测试新构筑。\n背包打开期间世界暂停。";
        void RemoveSelected()
        {
            var id = presenter.SelectedInstanceId;
            if (id == null) { status.text = "请先选择物品。"; return; }
            string error;
            bool ok = equipment.TryDiscard(id, out error);
            if (!ok) status.text = error;
        }
        void OnDestroy() { if (run != null) run.LoadoutChanged -= Changed; }
    }
}
