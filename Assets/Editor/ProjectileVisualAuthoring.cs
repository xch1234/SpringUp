using System;
using System.Linq;
using SpringUp.Organs;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(ProjectileVisualSettings))]
    public sealed class ProjectileVisualAuthoring : Editor
    {
        public const string AssetPath = "Assets/Resources/ProjectileVisualSettings.asset";
        [MenuItem("Tools/SpringUp/玩家子弹/编辑贴图与动画")]
        public static void SelectSettings()
        { Selection.activeObject = EnsureAsset(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static ProjectileVisualSettings EnsureAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ProjectileVisualSettings>(AssetPath);
            if (settings != null) return settings;
            settings = CreateInstance<ProjectileVisualSettings>();
            settings.orbit.frames = Import("Assets/Art/Items/子弹.png", "player_orbit", 1);
            settings.flying.frames = Import("Assets/Art/Items/发射子弹.png", "player_shot", 5);
            AssetDatabase.CreateAsset(settings, AssetPath); AssetDatabase.SaveAssets(); return settings;
        }
        // Trim export padding and place each pivot on the projectile head, not the smoke trail.
        static Sprite[] Import(string path, string prefix, int count)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 4096;
            importer.spritePixelsPerUnit = 100; importer.isReadable = true; importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); var pixels = texture.GetPixels32();
            var factory = new SpriteDataProviderFactories(); factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var old = provider.GetSpriteRects().OrderBy(r => r.rect.x).ToArray();
            var frames = new SpriteRect[count]; int cell = texture.width / count;
            for (int i = 0; i < count; i++)
            {
                int left = cell, right = -1, bottom = texture.height, top = -1;
                int goldLeft = cell, goldRight = -1, goldBottom = texture.height, goldTop = -1;
                for (int y = 0; y < texture.height; y++) for (int x = 0; x < cell; x++)
                {
                    var c = pixels[y * texture.width + i * cell + x];
                    if (c.a <= 32) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                    if (c.a > 100 && c.r > 150 && c.g > 100 && c.b < 170)
                    { goldLeft = Math.Min(goldLeft, x); goldRight = Math.Max(goldRight, x); goldBottom = Math.Min(goldBottom, y); goldTop = Math.Max(goldTop, y); }
                }
                if (right < left || goldRight < goldLeft) throw new InvalidOperationException("No projectile head in " + path);
                left = Math.Max(0, left - 2); right = Math.Min(cell - 1, right + 2);
                bottom = Math.Max(0, bottom - 2); top = Math.Min(texture.height - 1, top + 2);
                var rect = new Rect(i * cell + left, bottom, right - left + 1, top - bottom + 1);
                frames[i] = new SpriteRect { name = prefix + "_" + i, rect = rect, alignment = SpriteAlignment.Custom,
                    pivot = new Vector2(((goldLeft + goldRight + 1) * .5f - left) / rect.width, ((goldBottom + goldTop + 1) * .5f - bottom) / rect.height),
                    spriteID = i < old.Length ? old[i].spriteID : GUID.Generate() };
            }
            provider.SetSpriteRects(frames);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(frames.Select(f => new SpriteNameFileIdPair(f.name, f.spriteID)));
            provider.Apply(); importer.isReadable = false; importer.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("运行时修改可实时生效。弹头直径按碰撞直径×显示倍数计算，拖尾不计入碰撞。帧率为 0 时显示第一帧。默认原图朝右；可用朝向修正调整其他贴图。缺少图片时回退为原有圆环。", MessageType.Info);
            DrawDefaultInspector();
            if (GUILayout.Button("保存配置")) AssetDatabase.SaveAssets();
        }
    }
}
