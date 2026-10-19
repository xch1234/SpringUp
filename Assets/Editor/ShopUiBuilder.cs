using System.Linq;
using SpringUp.Flow;
using SpringUp.Laboratory;
using Tideflesh.Systems;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class ShopUiBuilder
    {
        public const string PrefabPath = "Assets/Resources/Menus/Shop.prefab";
        static readonly Color Ink = new Color(.82f, .92f, 1);

        // Migrate only removed controls; preserve the existing laboratory layout and bindings.
        public static void BuildFixedInventoryUi()
        {
            Build();
            var lab = PrefabUtility.LoadPrefabContents(FlowPresentationRepair.LabPath);
            try
            {
                foreach (var child in lab.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "Previous page" || t.name == "Next page" || t.name == "Page").ToArray())
                    Object.DestroyImmediate(child.gameObject);
                PrefabUtility.SaveAsPrefabAsset(lab, FlowPresentationRepair.LabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(lab); }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/SpringUp/Flow/Build Shop UI")]
        public static void Build()
        {
            var art = AssetDatabase.LoadAssetAtPath<OrganArtCatalog>(OrganUiBuilder.ArtPath);
            var root = Rect("Shop", null, 0, 0, 0, 0); root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var flow = root.gameObject.AddComponent<FlowWindow>(); flow.group = root.gameObject.AddComponent<CanvasGroup>();
            var fit = root.gameObject.AddComponent<MenuSettingsView>(); fit.designSize = new Vector2(1586, 992); fit.screenHeightFraction = .94f;
            var panel = Rect("Panel", root, 0, 0, 1586, 992); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            fit.panel = panel; flow.body = panel;
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/UI/Store.png").OfType<Sprite>().Single(s => s.name == "Store_0");
            var view = root.gameObject.AddComponent<FlowShopWindow>();
            // Titles, slot frames and the refresh caption are already painted into Store.png.
            Picture("Marrow", panel, 1125, 123, 48, 48, SpriteAt("Assets/Art/Items/marrow.png")).preserveAspect = true;
            view.balance = Label("Balance", panel, "0", 1190, 120, 240, 55, 32);
            view.refresh = OverlayControl(Rect("Refresh", panel, 480, 140, 352, 78));
            Picture("Refresh cost icon", view.refresh.transform, 370, 20, 36, 36, SpriteAt("Assets/Art/Items/marrow.png")).preserveAspect = true;
            Label("Refresh price", view.refresh.transform, "", 414, 15, 130, 48, 28);
            view.offerIcons = new OrganIconView[4]; view.offerButtons = new Button[4]; view.offerNames = new Text[4]; view.offerPrices = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var card = Rect("Offer " + i, panel, 133 + i * 234, 250, 211, 300);
                view.offerButtons[i] = OverlayControl(card);
                view.offerIcons[i] = OrganUiBuilder.Icon(card, "Organ", art, 49, 34, 105, false);
                view.offerNames[i] = Label("Name", card, "", 5, 154, 201, 43, 25, TextAnchor.MiddleCenter);
                view.offerPrices[i] = Label("Price", card, "", 20, 231, 166, 48, 28, TextAnchor.MiddleCenter);
            }
            view.inventoryIcons = new OrganIconView[Inventory.BackpackCapacity]; view.inventoryButtons = new Button[Inventory.BackpackCapacity];
            for (int i = 0; i < Inventory.BackpackCapacity; i++)
            {
                var rect = Rect("Inventory " + i, panel, 175 + i % 7 * 121.5f, 654 + i / 7 * 99, 74, 69);
                view.inventoryButtons[i] = OverlayControl(rect);
                view.inventoryIcons[i] = OrganUiBuilder.Icon(rect, "Organ", art, 3, 0, 68, false);
            }
            view.detailIcon = OrganUiBuilder.Icon(panel, "Selected organ", art, 1207, 243, 117, false);
            view.detailName = Label("Organ name", panel, "选择器官", 1100, 413, 330, 48, 30, TextAnchor.MiddleCenter);
            view.detailKind = Label("Organ kind", panel, "", 1100, 465, 330, 38, 23, TextAnchor.MiddleCenter);
            var viewport = Rect("Description viewport", panel, 1110, 518, 310, 205);
            var raycast = viewport.gameObject.AddComponent<Image>(); raycast.color = new Color(0, 0, 0, .01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            view.description = Label("Description", viewport, "", 0, 0, 310, 205, 25, TextAnchor.UpperLeft);
            var fitter = view.description.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = view.description.rectTransform; view.detailsScroll = scroll;
            view.price = Label("Trade price", panel, "", 1110, 737, 310, 44, 30, TextAnchor.MiddleCenter);
            view.trade = Button("Trade", panel, "购买", 1120, 798, 290, 66);
            view.feedback = Label("Transaction error", panel, "", 1100, 867, 330, 40, 20, TextAnchor.MiddleCenter);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath); Object.DestroyImmediate(root.gameObject);
            AssetDatabase.SaveAssets(); Debug.Log("SHOP_UI_BUILD_OK");
        }
        static Button OverlayControl(RectTransform rect)
        {
            var button = Control(rect, Color.white);
            var colors = button.colors; colors.normalColor = Color.clear;
            colors.highlightedColor = colors.selectedColor = new Color(.4f, .8f, 1, .12f);
            colors.pressedColor = new Color(.4f, .8f, 1, .22f);
            colors.disabledColor = new Color(0, 0, 0, .12f); button.colors = colors;
            return button;
        }
        static Sprite SpriteAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First();
        static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer = LayerMask.NameToLayer("UI");
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        static Image Picture(string name, Transform parent, float x, float y, float w, float h, Sprite sprite)
        { var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; return image; }
        static Text Label(string name, Transform parent, string caption, float x, float y, float w, float h, int size, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = caption; text.fontSize = size; text.color = Ink; text.alignment = alignment; text.raycastTarget = false; return text;
        }
        static Button Control(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>(); image.color = color;
            var button = rect.gameObject.AddComponent<MenuButton>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(.65f, .85f, 1); colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.4f, .7f, .9f); colors.disabledColor = new Color(.5f, .55f, .65f, .6f); button.colors = colors; return button;
        }
        static Button Button(string name, Transform parent, string caption, float x, float y, float w, float h)
        {
            var rect = Rect(name, parent, x, y, w, h); var button = Control(rect, new Color(.13f, .29f, .41f));
            var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(.4f, .65f, .82f); outline.effectDistance = new Vector2(1, -1);
            Label("Label", rect, caption, 0, 0, w, h, 26, TextAnchor.MiddleCenter); return button;
        }
    }
}
