using System;
using System.IO;
using System.Linq;
using SpringUp.Flow;
using SpringUp.Laboratory;
using SpringUp.Organs;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class ShopUiChecks
    {
        static void Require(bool pass, string message) { if (!pass) throw new Exception("Shop UI: " + message); }
        public static void RunBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var config = new RunConfiguration(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Organs/organs.json").text,
                AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Waves/waves.json").text, OrganCatalog.LoadDefault());
            var run = new RunController(config.Organs, config.Waves, null, () => 0, config.Definitions.Keys);
            run.StartRun(); run.CreditKill("shop-ui-funds", 5000); run.CombatTimerFinished();
            var view = Create(run, config);
            var offer = run.GetShopOffers()[0]; int balance = run.Marrow, count = run.Inventory.UnequippedIds().Count;
            view.SelectOffer(0); view.trade.onClick.Invoke();
            Require(run.Marrow == balance - offer.Price && run.Inventory.UnequippedIds().Count == count + 1 && offer.Purchased, "Purchase updates inventory and exact quoted balance");
            Require(!view.trade.interactable && view.trade.GetComponentInChildren<Text>().text == "购买", "Buying does not turn the same button into sell");
            view.trade.onClick.Invoke(); Require(run.Inventory.UnequippedIds().Count == count + 1, "Double purchase ignored");
            view.Unbind(); Object.DestroyImmediate(view.gameObject); view = Create(run, config);
            Require(ReferenceEquals(offer, run.GetShopOffers()[0]) && view.offerPrices[0].text == "已售出", "Reopen preserves sold stock");
            for (int i = 0; i < 30; i++) run.Inventory.Spawn("rust_knife");
            view.Refresh(); view.ChangePage(999); Require(!view.next.interactable && view.previous.interactable, "Inventory paging clamps");
            view.SelectInventory(0); string selected = view.inventoryIcons[0].InstanceId;
            balance = run.Marrow; int sale = run.Inventory.SalePrice(selected); count = run.Inventory.UnequippedIds().Count;
            view.trade.onClick.Invoke(); Require(run.Marrow == balance + sale && !run.Inventory.UnequippedIds().Contains(selected), "Sale removes selected instance and credits price");
            Require(!view.trade.gameObject.activeSelf, "Sale clears selection");
            view.trade.onClick.Invoke(); Require(run.Inventory.UnequippedIds().Count == count - 1, "Second click does not sell the next item");
            view.ChangePage(-999); view.SelectOffer(1); int refreshPrice = run.ShopRefreshPrice; balance = run.Marrow;
            view.refresh.onClick.Invoke(); Require(run.Marrow == balance - refreshPrice && !view.trade.gameObject.activeSelf, "Refresh charges once and clears old offer selection");
            Require(!ReferenceEquals(offer, run.GetShopOffers()[0]), "Refresh replaces offers");
            Require(view.GetComponentsInChildren<OrganDragSlot>(true).Length == 0, "No drag handlers in shop");
            Require(!view.GetComponentsInChildren<Button>(true).Any(b => b.name == "Close" || b.name == "返回"), "No redundant return button");
            view.SelectOffer(0);
            Capture(view, 1440, 900, "shop-1440.png"); Capture(view, 1280, 720, "shop-1280.png");
            view.SelectInventory(0); Capture(view, 1440, 900, "shop-sell.png");
            view.Unbind(); Object.DestroyImmediate(view.gameObject);

            var waves = new[] { new WaveDefinition { wave = 1, duration_sec = 60, marrow_reward = 0 }, new WaveDefinition { wave = 2, duration_sec = 60 } };
            var poor = new RunController(config.Organs, waves.ToList(), null, () => 0, config.Definitions.Keys);
            poor.StartRun(); poor.CombatTimerFinished(); view = Create(poor, config); view.SelectOffer(0);
            Require(!view.trade.interactable && !view.refresh.interactable && poor.Marrow == 0, "Insufficient funds disable controls");
            view.trade.onClick.Invoke(); view.refresh.onClick.Invoke(); Require(poor.Marrow == 0 && !poor.GetShopOffers()[0].Purchased, "Disabled transactions cannot mutate state");
            Capture(view, 1440, 900, "shop-empty.png"); view.Unbind(); Object.DestroyImmediate(view.gameObject);
            Debug.Log("SHOP_UI_CHECKS_PASS: purchase, sale, duplicate clicks, sold-stock persistence, refresh, pagination, insufficient funds, no dragging, layout.");
        }
        static FlowShopWindow Create(RunController run, RunConfiguration config)
        {
            var root = Object.Instantiate(Resources.Load<FlowWindow>("Menus/Shop"));
            var view = root.GetComponent<FlowShopWindow>(); view.Bind(run, config); return view;
        }
        static void Capture(FlowShopWindow view, int width, int height, string filename)
        {
            Directory.CreateDirectory("Logs/ShopUI");
            var camera = new GameObject("UI capture").AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 450;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f, .028f, .045f);
            var canvas = new GameObject("UI", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            canvas.scaleFactor = Mathf.Min(width / 1440f, height / 900f);
            view.transform.SetParent(canvas.transform, false);
            var font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 24);
            foreach (var text in view.GetComponentsInChildren<Text>(true)) { text.font = font; text.SetAllDirty(); }
            var rt = new RenderTexture(width, height, 24); var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                // In EditMode the camera's target size reaches the Canvas on its first render.
                camera.Render(); Canvas.ForceUpdateCanvases(); view.GetComponent<MenuSettingsView>().Fit();
                foreach (var text in view.GetComponentsInChildren<Text>(true)) text.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = rt;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes("Logs/ShopUI/" + filename, image.EncodeToPNG()); Object.DestroyImmediate(image);
                foreach (var button in view.GetComponentsInChildren<Button>())
                {
                    var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                    foreach (var corner in corners) { var point = camera.WorldToViewportPoint(corner); Require(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1, "Button within viewport: " + button.name); }
                }
            }
            finally
            {
                view.transform.SetParent(null, false); RenderTexture.active = previous;
                Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(canvas.gameObject); Object.DestroyImmediate(font); rt.Release(); Object.DestroyImmediate(rt);
            }
        }
    }
}
