using System.Collections.Generic;
using System.Linq;
using SpringUp.Laboratory;
using Tideflesh.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Flow
{
    public sealed class FlowShopWindow : MonoBehaviour
    {
        public OrganIconView[] offerIcons = System.Array.Empty<OrganIconView>(), inventoryIcons = System.Array.Empty<OrganIconView>();
        public Button[] offerButtons = System.Array.Empty<Button>(), inventoryButtons = System.Array.Empty<Button>();
        public Text[] offerNames, offerPrices;
        public OrganIconView detailIcon;
        public Text balance, pageLabel, inventoryCount, emptyInventory, detailName, detailKind, description, price, feedback;
        public Button trade, refresh, previous, next;
        public ScrollRect detailsScroll;
        RunController run;
        RunConfiguration configuration;
        RunLaboratoryEquipment equipment;
        IReadOnlyList<ShopOffer> offers;
        IReadOnlyList<LaboratoryItem> inventory;
        ShopOffer selectedOffer;
        string selectedInstance;
        int page;
        bool trading;
        static readonly Color Ink = new Color(.82f, .92f, 1);
        static readonly Color Unaffordable = new Color(1, .48f, .43f);

        public void Bind(FlowApplication app) => Bind(app.Session.Run, app.Configuration);
        public void Bind(RunController controller, RunConfiguration config)
        {
            Unbind(); run = controller; configuration = config;
            equipment = new RunLaboratoryEquipment(run, configuration.Definitions);
            for (int i = 0; i < offerButtons.Length; i++) { int index = i; offerButtons[i].onClick.AddListener(() => SelectOffer(index)); }
            for (int i = 0; i < inventoryButtons.Length; i++) { int index = i; inventoryButtons[i].onClick.AddListener(() => SelectInventory(index)); }
            trade.onClick.AddListener(Trade); refresh.onClick.AddListener(RefreshOffers);
            previous.onClick.AddListener(() => ChangePage(-1)); next.onClick.AddListener(() => ChangePage(1));
            run.LoadoutChanged += OnInventoryChanged; run.BalanceChanged += OnBalanceChanged;
            Refresh();
        }
        void OnInventoryChanged(InventorySnapshot snapshot) { if (!trading) Refresh(); }
        void OnBalanceChanged(int value) { if (!trading) Refresh(); }
        LaboratoryItem OfferItem(ShopOffer offer)
            => new LaboratoryItem("shop:" + offer.OrganId, configuration.Definitions[offer.OrganId]);
        public void SelectOffer(int index)
        {
            if (offers == null || index < 0 || index >= offers.Count) return;
            selectedOffer = offers[index]; selectedInstance = null; feedback.text = ""; RefreshDetails();
            detailsScroll.verticalNormalizedPosition = 1;
        }
        public void SelectInventory(int index)
        {
            int position = page * inventoryIcons.Length + index;
            if (inventory == null || position < 0 || position >= inventory.Count) return;
            selectedInstance = inventory[position].InstanceId; selectedOffer = null; feedback.text = ""; RefreshDetails();
            detailsScroll.verticalNormalizedPosition = 1;
        }
        public void ChangePage(int offset)
        { page += offset; selectedInstance = null; feedback.text = ""; Refresh(); }
        public void Refresh()
        {
            if (run == null) return;
            offers = run.GetShopOffers(); inventory = equipment.Inventory;
            int pages = Mathf.Max(1, Mathf.CeilToInt(inventory.Count / (float)inventoryIcons.Length));
            page = Mathf.Clamp(page, 0, pages - 1);
            balance.text = run.Marrow.ToString(); inventoryCount.text = inventory.Count.ToString();
            emptyInventory.gameObject.SetActive(inventory.Count == 0);
            pageLabel.text = $"{page + 1} / {pages}"; previous.interactable = page > 0; next.interactable = page + 1 < pages;
            refresh.interactable = run.Stage == RunPhase.Rest && run.Marrow >= run.ShopRefreshPrice && offers.Count > 0;
            refresh.GetComponentInChildren<Text>().text = $"刷新  {run.ShopRefreshPrice}";
            for (int i = 0; i < offerIcons.Length; i++)
            {
                bool exists = i < offers.Count;
                offerButtons[i].interactable = exists; offerIcons[i].Bind(exists ? OfferItem(offers[i]) : null);
                offerNames[i].text = exists ? configuration.Organs[offers[i].OrganId].name : "";
                offerPrices[i].text = !exists ? "" : offers[i].Purchased ? "已售出" : offers[i].Price.ToString();
                offerPrices[i].color = exists && !offers[i].Purchased && offers[i].Price > run.Marrow ? Unaffordable : Ink;
                offerIcons[i].icon.color = exists && offers[i].Purchased ? new Color(1, 1, 1, .35f) : Color.white;
            }
            for (int i = 0; i < inventoryIcons.Length; i++)
            {
                int index = page * inventoryIcons.Length + i;
                inventoryIcons[i].Bind(index < inventory.Count ? inventory[index] : null);
                inventoryButtons[i].interactable = index < inventory.Count;
            }
            if (selectedOffer != null && !offers.Contains(selectedOffer)) selectedOffer = null;
            if (selectedInstance != null && !inventory.Any(item => item.InstanceId == selectedInstance)) selectedInstance = null;
            RefreshDetails();
        }
        void RefreshDetails()
        {
            var item = selectedOffer != null ? OfferItem(selectedOffer) : inventory.FirstOrDefault(entry => entry.InstanceId == selectedInstance);
            detailIcon.Bind(item);
            detailName.text = item?.Name ?? "选择器官"; detailKind.text = item == null ? "" : item.Kind + " · " + item.MountLabel;
            description.text = item?.Description ?? "";
            bool buying = selectedOffer != null;
            int amount = item == null ? 0 : buying ? selectedOffer.Price : run.Inventory.SalePrice(item.InstanceId);
            price.text = item == null ? "" : buying && selectedOffer.Purchased ? "已售出" : amount.ToString();
            price.color = buying && !selectedOffer.Purchased && amount > run.Marrow ? Unaffordable : Ink;
            trade.gameObject.SetActive(item != null);
            trade.GetComponentInChildren<Text>().text = buying ? "购买" : "出售";
            trade.interactable = item != null && run.Stage == RunPhase.Rest && (!buying || (!selectedOffer.Purchased && run.Marrow >= amount));
            for (int i = 0; i < offerIcons.Length; i++) offerIcons[i].SetSelected(i < offers.Count && ReferenceEquals(selectedOffer, offers[i]));
            foreach (var icon in inventoryIcons) icon.SetSelected(selectedInstance != null && icon.InstanceId == selectedInstance);
        }
        public void Trade()
        {
            if (trading || run == null || !trade.interactable) return;
            trading = true; feedback.text = "";
            try
            {
                if (selectedOffer != null)
                {
                    var before = new HashSet<string>(run.Inventory.UnequippedIds());
                    var result = run.TryPurchaseShopOffer(selectedOffer);
                    if (result.Ok)
                    {
                        var ids = run.Inventory.UnequippedIds();
                        int added = ids.FindIndex(id => !before.Contains(id));
                        if (added >= 0) page = added / inventoryIcons.Length;
                    }
                    else if (result.Reason != "insufficient_marrow") feedback.text = SpringUp.UI.ChineseText.Error(result.Reason);
                }
                else if (selectedInstance != null && run.Inventory.UnequippedIds().Contains(selectedInstance))
                {
                    string id = selectedInstance; selectedInstance = null;
                    if (!equipment.TrySell(id, out var error)) feedback.text = error;
                }
            }
            finally { trading = false; Refresh(); }
        }
        public void RefreshOffers()
        {
            if (trading || run == null || !refresh.interactable) return;
            trading = true; feedback.text = "";
            try
            {
                var result = run.TryRefreshShop();
                if (result.Ok) selectedOffer = null;
                else if (result.Reason != "insufficient_marrow") feedback.text = SpringUp.UI.ChineseText.Error(result.Reason);
            }
            finally { trading = false; Refresh(); }
        }
        public void Unbind()
        {
            if (run != null) { run.LoadoutChanged -= OnInventoryChanged; run.BalanceChanged -= OnBalanceChanged; }
            foreach (var button in offerButtons.Concat(inventoryButtons)) button.onClick.RemoveAllListeners();
            foreach (var button in new[] { trade, refresh, previous, next }) if (button != null) button.onClick.RemoveAllListeners();
            run = null; selectedOffer = null; selectedInstance = null; page = 0;
        }
        void OnDisable() => Unbind();
        void OnDestroy() => Unbind();
    }
}
