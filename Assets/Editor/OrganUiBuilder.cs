using System.Collections.Generic;
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
    // Explicit authoring command only. Runtime never overwrites hand-adjusted prefab layouts.
    public static class OrganUiBuilder
    {
        public const string HudPath = "Assets/Resources/EquipmentHud.prefab";
        public const string ArtPath = "Assets/Resources/OrganArtCatalog.asset";
        static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static Sprite SpriteAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First();
        [MenuItem("Tools/SpringUp/UI/Build Organ UI Assets")]
        public static void Build()
        {
            var art = OrganArtAuthoring.RefreshCatalog();
            BuildHud(art); BuildBackpack(art); AssetDatabase.SaveAssets();
            Debug.Log("[Organ UI] Assets built. Prefabs are now editable without runtime layout rebuilding.");
        }
        static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        static Image Image(string name, Transform parent, float x, float y, float w, float h, Color color, bool raycast = false)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = raycast; return image;
        }
        static Text Text(string name, Transform parent, string value, float x, float y, float w, float h, int size)
        {
            var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>();
            text.text = value; text.font = Font; text.fontSize = size; text.color = new Color(.8f, .9f, 1);
            text.raycastTarget = false; text.alignment = TextAnchor.MiddleLeft;
            return text;
        }
        static Button Button(string name, Transform parent, string value, float x, float y, float w, float h, int size = 23)
        {
            var image = Image(name, parent, x, y, w, h, new Color(.08f, .2f, .29f, .98f), true);
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Text("Label", image.transform, value, 0, 0, w, h, size).alignment = TextAnchor.MiddleCenter;
            return button;
        }
        public static OrganIconView Icon(Transform parent, string name, OrganArtCatalog art, float x, float y, float size, bool raycast)
        {
            var root = Image(name, parent, x, y, size, size, new Color(0, 0, 0, .01f), raycast);
            var view = root.gameObject.AddComponent<OrganIconView>(); view.art = art;
            view.icon = Image("Icon", root.transform, 3, 3, size - 6, size - 6, Color.white);
            view.icon.preserveAspect = true; view.icon.enabled = false;
            view.fallback = Text("Missing art label", root.transform, "", 2, 2, size - 4, size - 4, Mathf.Max(10, Mathf.RoundToInt(size * .21f)));
            view.fallback.alignment = TextAnchor.MiddleCenter;
            view.selection = Image("Selected", root.transform, 0, 0, size, size, new Color(.3f, .8f, 1, 1));
            view.selection.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            view.selection.type = UnityEngine.UI.Image.Type.Sliced; view.selection.fillCenter = false;
            view.selection.enabled = false;
            view.dropHint = Image("Drop hint", root.transform, 0, 0, size, size, Color.clear); view.dropHint.enabled = false;
            view.flash = Image("Activation flash", root.transform, 0, 0, size, size, Color.clear); view.flash.enabled = false;
            view.number = Text("Slot number", root.transform, "", 1, 0, size - 2, Mathf.Max(12, size * .25f), Mathf.Max(10, Mathf.RoundToInt(size * .22f)));
            view.number.alignment = TextAnchor.UpperLeft;
            view.number.gameObject.AddComponent<Outline>().effectColor = Color.black;
            return view;
        }
        static void BuildHud(OrganArtCatalog art)
        {
            const float scale = 530f / 1774f;
            var root = Rect("Equipment HUD", null, 16, 65, 530, 265);
            var hud = root.gameObject.AddComponent<RunEquipmentHud>();
            var background = Image("Artwork", root, 0, 0, 530, 265, Color.white);
            background.sprite = SpriteAt("Assets/Art/UI/HUD.png");
            var slots = new List<RunEquipmentHud.Slot>();
            for (int p = 0; p < 4; p++) for (int i = 0; i < (p == 3 ? 2 : 6); i++)
            {
                float x = p == 3 ? 204 : 645 + i * 157;
                float y = p == 3 ? 362 + i * 166 : 195 + p * 204;
                var view = Icon(root, ((LaboratoryPart)p) + " " + (i + 1), art, x * scale, y * scale, 112 * scale, false);
                slots.Add(new RunEquipmentHud.Slot { part = (LaboratoryPart)p, index = i, view = view });
            }
            hud.slots = slots.ToArray();
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, HudPath); Object.DestroyImmediate(root.gameObject);
        }
        static void BuildBackpack(OrganArtCatalog art)
        {
            var root = Rect("Laboratory Window", null, 0, 0, 1774, 1080);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f); root.localScale = Vector3.one * .72f;
            var window = root.gameObject.AddComponent<FlowWindow>(); window.group = root.gameObject.AddComponent<CanvasGroup>(); window.body = root;
            var backpack = root.gameObject.AddComponent<BackpackPresenter>();
            var binding = root.gameObject.AddComponent<FlowLaboratoryWindow>(); binding.backpack = backpack;
            // Only the actual panel blocks clicks; icons/overlays themselves do not swallow drop events.
            var background = Image("Artwork", root, 0, 0, 1774, 887, Color.white, true);
            background.sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/UI/Backpack.png")
                .OfType<Sprite>().Single(sprite => sprite.name == "Backpack_0");
            var slots = new List<OrganDragSlot>();
            for (int p = 0; p < 4; p++) for (int i = 0; i < (p == 3 ? 2 : 6); i++)
            {
                float x = p == 3 ? 182 : 522 + i * 114.5f;
                float y = p == 3 ? 299 + i * 121 : 173 + p * 141;
                var view = Icon(root, ((LaboratoryPart)p) + " " + (i + 1), art, x, y, 76, true);
                var slot = view.gameObject.AddComponent<OrganDragSlot>(); slot.view = view; slot.part = (LaboratoryPart)p; slot.index = i;
                slots.Add(slot);
            }
            backpack.equipmentSlots = slots.ToArray(); slots.Clear();
            var inventory = Image("Inventory drop area", root, 320, 593, 858, 225, Color.clear, true);
            inventory.gameObject.AddComponent<InventoryDropArea>().owner = backpack;
            for (int i = 0; i < Inventory.BackpackCapacity; i++)
            {
                var view = Icon(inventory.transform, "Inventory " + (i + 1), art, 25 + i % 7 * 120, 24 + i / 7 * 113, 76, true);
                var slot = view.gameObject.AddComponent<OrganDragSlot>(); slot.view = view; slot.inventory = true; slot.index = i; slots.Add(slot);
            }
            backpack.inventorySlots = slots.ToArray();
            backpack.detailIcon = Icon(root, "Selected organ", art, 1282, 175, 80, false);
            // Long descriptions remain reachable without reducing the font to fit.
            var viewport = Image("Details viewport", root, 1260, 285, 360, 400, Color.clear, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            backpack.detail = Text("Details", viewport.transform, "选择器官查看详情", 0, 0, 360, 480, 25);
            backpack.detail.alignment = TextAnchor.UpperLeft;
            var content = (RectTransform)backpack.detail.transform;
            content.anchorMax = new Vector2(1, 1); content.sizeDelta = new Vector2(0, 480);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            backpack.detailsScroll = scroll;
            binding.discardButton = Button("Discard selected", root, "丢弃选中器官", 1270, 700, 320, 42);
            backpack.dragGhost = Icon(root, "Drag ghost", art, 0, 0, 76, false);
            backpack.dragGhost.GetComponent<RectTransform>().pivot = new Vector2(.5f, .5f);
            backpack.dragGhost.gameObject.SetActive(false);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = LayerMask.NameToLayer("UI");
            root.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, FlowPresentationRepair.LabPath); Object.DestroyImmediate(root.gameObject);
        }
    }
}
