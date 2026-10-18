using System;
using System.IO;
using System.Linq;
using SpringUp.Flow;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpringUp.EditorChecks
{
    public static class MenuUiBuilder
    {
        static Sprite backdrop, button, pause;
        static readonly Color Ink = new Color(.82f, .92f, 1);
        [MenuItem("Tools/SpringUp/Flow/Rebuild Menu UI")]
        public static void Build()
        {
            backdrop = Import("Backdrop"); button = Import("Button"); pause = Import("ESC");
            Directory.CreateDirectory("Assets/Resources/Menus");
            MainMenu(); Pause(); Settings(); Confirm(); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("MENU_UI_BUILD_OK");
        }
        static Sprite Import(string name)
        {
            string path = "Assets/Art/UI/" + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = name == "ESC" ? SpriteImportMode.Single : SpriteImportMode.Multiple;
            importer.spritePivot = new Vector2(.5f, .5f); importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096; importer.isReadable = name != "ESC"; importer.SaveAndReimport();
            if (name != "ESC")
            {
                // Crop the opaque black export padding in import metadata; keep the source PNG untouched.
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); var pixels = texture.GetPixels32();
                int left = texture.width, bottom = texture.height, right = 0, top = 0;
                for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
                {
                    var c = pixels[y * texture.width + x]; if (c.a <= 20 || c.r + c.g + c.b <= 55) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                }
                var factory = new SpriteDataProviderFactories(); factory.Init();
                var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
                var existing = provider.GetSpriteRects().FirstOrDefault();
                var sprite = new SpriteRect { name = name, rect = new Rect(left, bottom, right - left + 1, top - bottom + 1),
                    pivot = new Vector2(.5f, .5f), alignment = SpriteAlignment.Center,
                    border = Vector4.one * (name == "Button" ? 65 : 40), spriteID = existing != null ? existing.spriteID : GUID.Generate() };
                provider.SetSpriteRects(new[] { sprite });
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(new[] { new SpriteNameFileIdPair(sprite.name, sprite.spriteID) });
                provider.Apply(); importer.isReadable = false; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
        }
        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        static Image Picture(RectTransform rect, Sprite sprite, Color color)
        { var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) { image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = sprite.name == "Button" ? 8 : 2.5f; } return image; }
        static Text Label(string name, Transform parent, string text, Vector2 size, Vector2 pos, int fontSize = 24, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var rect = Rect(name, parent, size, pos); var label = rect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.text = text;
            label.fontSize = fontSize; label.color = Ink; label.alignment = align; label.raycastTarget = false;
            return label;
        }
        static Button Button(string name, Transform parent, Vector2 size, Vector2 pos, bool art = true)
        {
            var rect = Rect(name, parent, size, pos);
            var image = Picture(rect, art ? button : null, art ? Color.white : Color.clear); image.raycastTarget = true;
            var control = rect.gameObject.AddComponent<MenuButton>(); control.targetGraphic = image;
            var colors = control.colors; colors.normalColor = Color.white;
            colors.highlightedColor = art ? new Color(.78f, .9f, 1) : new Color(.55f, .78f, 1, .15f);
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = art ? new Color(.5f, .7f, .9f) : new Color(.4f, .7f, 1, .28f);
            control.colors = colors;
            if (!art) { image.color = Color.white; colors.normalColor = Color.clear; control.colors = colors; }
            if (art) Label("Label", rect, name, new Vector2(size.x * .86f, size.y * .56f), Vector2.zero);
            return control;
        }
        static MenuSettingsView Root(string name, Vector2 design, float fraction)
        {
            var rect = Rect(name, null, Vector2.zero, Vector2.zero); Stretch(rect);
            var flow = rect.gameObject.AddComponent<FlowWindow>(); flow.group = rect.gameObject.AddComponent<CanvasGroup>();
            var menu = rect.gameObject.AddComponent<MenuSettingsView>(); menu.designSize = design; menu.screenHeightFraction = fraction;
            menu.panel = Rect("Panel", rect, design, Vector2.zero); flow.body = menu.panel;
            return menu;
        }
        static void Save(MenuSettingsView view, string name)
        { PrefabUtility.SaveAsPrefabAsset(view.gameObject, "Assets/Resources/Menus/" + name + ".prefab"); Object.DestroyImmediate(view.gameObject); }
        static void MainMenu()
        {
            var view = Root("MainMenu", new Vector2(1440, 900), 1);
            // The background is independently replaceable in this prefab.
            var bg = Rect("Backdrop", view.transform, Vector2.zero, Vector2.zero); Stretch(bg); bg.SetAsFirstSibling();
            Picture(bg, backdrop, Color.white);
            view.buttons = new[] {
                Button("开始游戏", view.panel, new Vector2(430, 90), new Vector2(0, 60)),
                Button("设置", view.panel, new Vector2(430, 90), new Vector2(0, -60)),
                Button("退出", view.panel, new Vector2(430, 90), new Vector2(0, -180)) };
            Save(view, "MainMenu");
        }
        static void Pause()
        {
            var view = Root("Pause", new Vector2(620, 620 * 1369f / 1149), .64f);
            Picture(view.panel, pause, Color.white);
            view.buttons = new Button[3]; string[] names = { "继续游戏", "设置", "回到主菜单" };
            float[] centers = { .29f, .516f, .742f };
            for (int i = 0; i < 3; i++)
                view.buttons[i] = Button(names[i], view.panel, new Vector2(420, 119), new Vector2(0, (.5f - centers[i]) * view.designSize.y), false);
            Save(view, "Pause");
        }
        static Slider Volume(string name, Transform parent, float y, out Text value)
        {
            Label(name + " label", parent, name, new Vector2(210, 40), new Vector2(-215, y), 24, TextAnchor.MiddleLeft);
            var rect = Rect(name, parent, new Vector2(290, 36), new Vector2(75, y));
            var slider = rect.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1;
            var track = Rect("Track", rect, new Vector2(270, 8), Vector2.zero); Picture(track, null, new Color(.12f, .24f, .36f)).raycastTarget = true;
            var fillArea = Rect("Fill area", rect, new Vector2(270, 8), Vector2.zero);
            var fill = Rect("Fill", fillArea, Vector2.zero, Vector2.zero); Stretch(fill); Picture(fill, null, new Color(.4f, .7f, .94f));
            var handleArea = Rect("Handle area", rect, new Vector2(270, 0), Vector2.zero);
            var handle = Rect("Handle", handleArea, new Vector2(18, 28), Vector2.zero); var image = Picture(handle, null, Ink); image.raycastTarget = true;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = image;
            slider.value = .5f;
            value = Label(name + " value", parent, "50%", new Vector2(80, 40), new Vector2(285, y), 22);
            return slider;
        }
        static void Confirm()
        {
            var view = Root("Confirm", new Vector2(660, 330), .7f);
            Picture(view.panel, backdrop, Color.white);
            view.GetComponent<FlowWindow>().title = Label("Title", view.panel, "确认放弃", new Vector2(540, 50), new Vector2(0, 95), 30);
            Label("Message", view.panel, "确定放弃本局并返回主菜单吗？\n当前进度不会保存。", new Vector2(560, 90), new Vector2(0, 15), 24);
            view.buttons = new[] {
                Button("取消", view.panel, new Vector2(240, 52), new Vector2(-145, -95)),
                Button("放弃本局", view.panel, new Vector2(240, 52), new Vector2(145, -95)) };
            Save(view, "Confirm");
        }
        static void Settings()
        {
            var view = Root("Settings", new Vector2(860, 760), .92f); Picture(view.panel, backdrop, Color.white);
            view.GetComponent<FlowWindow>().title = Label("Title", view.panel, "设置", new Vector2(500, 50), new Vector2(0, 305), 32);
            view.music = Volume("音乐音量", view.panel, 235, out view.musicValue);
            view.sfx = Volume("音效音量", view.panel, 180, out view.sfxValue);
            Label("Keyboard title", view.panel, "按键绑定", new Vector2(640, 32), new Vector2(0, 120), 22, TextAnchor.MiddleLeft);
            view.bindings = new Button[GameSettings.Labels.Length];
            for (int i = 0; i < view.bindings.Length; i++)
            {
                float y = 73 - 43 * i;
                Label("Binding label " + i, view.panel, GameSettings.Labels[i], new Vector2(240, 36), new Vector2(-200, y), 22, TextAnchor.MiddleLeft);
                view.bindings[i] = Button("绑定", view.panel, new Vector2(290, 36), new Vector2(150, y));
                var text = view.bindings[i].GetComponentInChildren<Text>(); text.rectTransform.sizeDelta = new Vector2(250, 32); text.fontSize = 21;
            }
            view.hint = Label("Hint", view.panel, "方向键 / Tab 为备用键；Esc / 鼠标右键返回。", new Vector2(660, 48), new Vector2(0, -240), 18);
            view.buttons = new[] { Button("恢复默认", view.panel, new Vector2(260, 52), new Vector2(-160, -300)), Button("返回", view.panel, new Vector2(260, 52), new Vector2(160, -300)) };
            Save(view, "Settings");
        }
    }
}
