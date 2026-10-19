using System.Linq;
using SpringUp.Flow;
using SpringUp.Laboratory;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class ShopUiBuilder
    {
        const string Backpack = "Assets/Art/UI/Backpack.png";
        public const string PrefabPath = "Assets/Resources/Menus/Shop.prefab";
        static Sprite slot;
        static OrganArtCatalog art;
        static readonly Color Ink = new Color(.82f, .92f, 1);
        [MenuItem("Tools/SpringUp/Flow/Build Shop UI")]
        public static void Build()
        {
            AddSlices();
            slot = AssetDatabase.LoadAllAssetsAtPath(Backpack).OfType<Sprite>().Single(s => s.name == "ShopSlot");
            art = AssetDatabase.LoadAssetAtPath<OrganArtCatalog>(OrganUiBuilder.ArtPath);
            var root = Rect("Shop", null, 0, 0, 0, 0); root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var flow = root.gameObject.AddComponent<FlowWindow>(); flow.group = root.gameObject.AddComponent<CanvasGroup>();
            var fit = root.gameObject.AddComponent<MenuSettingsView>(); fit.designSize = new Vector2(1440, 860); fit.screenHeightFraction = .94f;
            var panel = Rect("Panel", root, 0, 0, 1440, 860); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            fit.panel = panel; flow.body = panel;
            var bg = panel.gameObject.AddComponent<Image>(); bg.sprite = SpriteAt("Assets/Art/UI/Backdrop.png"); bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 2.5f;
            var view = root.gameObject.AddComponent<FlowShopWindow>();
            Picture("Crown", panel, 0, -8, 1440, 102, AssetDatabase.LoadAllAssetsAtPath(Backpack).OfType<Sprite>().Single(s => s.name == "ShopCrown"));
            flow.title = Label("Title", panel, "商店", 50, 105, 500, 50, 36);
            Picture("Marrow", panel, 1120, 105, 56, 56, SpriteAt("Assets/Art/Items/marrow.png")).preserveAspect = true;
            view.balance = Label("Balance", panel, "0", 1190, 105, 190, 56, 32);
            Line(panel, 50, 170, 1340, 2);
            Line(panel, 1008, 190, 2, 620);
            view.offerIcons = new OrganIconView[4]; view.offerButtons = new Button[4]; view.offerNames = new Text[4]; view.offerPrices = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var card = Rect("Offer " + i, panel, 50 + i * 230, 198, 210, 228);
                view.offerButtons[i] = Control(card, new Color(.055f, .11f, .18f));
                view.offerIcons[i] = Icon(card, "Organ", 35, 7, 140);
                view.offerNames[i] = Label("Name", card, "", 0, 156, 210, 40, 23, TextAnchor.MiddleCenter);
                view.offerPrices[i] = Label("Price", card, "", 0, 195, 210, 32, 26, TextAnchor.MiddleCenter);
            }
            view.refresh = Button("Refresh", panel, "刷新", 700, 443, 270, 58);
            Label("Inventory title", panel, "背包", 50, 464, 110, 40, 28);
            view.inventoryCount = Label("Inventory count", panel, "0", 170, 464, 130, 40, 23);
            Line(panel, 50, 516, 920, 2);
            view.inventoryIcons = new OrganIconView[14]; view.inventoryButtons = new Button[14];
            for (int i = 0; i < 14; i++)
            {
                var rect = Rect("Inventory " + i, panel, 50 + i % 7 * 132, 535 + i / 7 * 115, 108, 100);
                view.inventoryButtons[i] = Control(rect, Color.white); rect.GetComponent<Image>().sprite = slot;
                view.inventoryIcons[i] = OrganUiBuilder.Icon(rect, "Organ", art, 10, 8, 82, false);
            }
            view.emptyInventory = Label("Empty inventory", panel, "暂无器官", 50, 765, 300, 52, 22);
            view.previous = Button("Previous page", panel, "‹", 670, 765, 74, 52);
            view.pageLabel = Label("Page", panel, "1 / 1", 750, 765, 130, 52, 23, TextAnchor.MiddleCenter);
            view.next = Button("Next page", panel, "›", 892, 765, 74, 52);
            view.detailIcon = Icon(panel, "Selected organ", 1142, 205, 128);
            view.detailName = Label("Organ name", panel, "选择器官", 1045, 350, 330, 50, 28, TextAnchor.MiddleCenter);
            view.detailKind = Label("Organ kind", panel, "", 1045, 402, 330, 36, 20, TextAnchor.MiddleCenter);
            var viewport = Rect("Description viewport", panel, 1045, 452, 330, 186);
            var raycast = viewport.gameObject.AddComponent<Image>(); raycast.color = new Color(0, 0, 0, .01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            view.description = Label("Description", viewport, "", 0, 0, 330, 186, 22, TextAnchor.UpperLeft);
            var fitter = view.description.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = view.description.rectTransform; view.detailsScroll = scroll;
            view.price = Label("Trade price", panel, "", 1045, 654, 330, 44, 30, TextAnchor.MiddleCenter);
            view.trade = Button("Trade", panel, "购买", 1045, 714, 330, 70);
            view.feedback = Label("Transaction error", panel, "", 1045, 791, 330, 48, 18, TextAnchor.MiddleCenter);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath); Object.DestroyImmediate(root.gameObject);
            AssetDatabase.SaveAssets(); Debug.Log("SHOP_UI_BUILD_OK");
        }
        static void AddSlices()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Backpack);
            var factory = new SpriteDataProviderFactories(); factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var rects = provider.GetSpriteRects().ToList();
            void Add(string name, Rect rect)
            {
                var existing = rects.FirstOrDefault(s => s.name == name);
                if (existing != null) { existing.rect = rect; return; }
                rects.Add(new SpriteRect { name = name, rect = rect, pivot = new Vector2(.5f, .5f), alignment = SpriteAlignment.Center, spriteID = GUID.Generate() });
            }
            Add("ShopSlot", new Rect(508, 625, 103, 105)); Add("ShopCrown", new Rect(0, 762, 1774, 125));
            provider.SetSpriteRects(rects.ToArray());
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(s => new SpriteNameFileIdPair(s.name, s.spriteID)));
            provider.Apply(); importer.SaveAndReimport();
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
        static void Line(Transform parent, float x, float y, float w, float h)
        { Picture("Divider", parent, x, y, w, h, null).color = new Color(.25f, .43f, .59f, .7f); }
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
        static OrganIconView Icon(Transform parent, string name, float x, float y, float size)
        {
            var frame = Picture(name, parent, x, y, size, size, slot);
            return OrganUiBuilder.Icon(frame.transform, "Organ", art, 9, 9, size - 18, false);
        }
    }
}
