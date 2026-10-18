using System;
using System.Collections.Generic;
using System.IO;
using SpringUp.Laboratory;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    // Rebuild only the ID-to-art mapping; preserve hand-edited UI prefabs.
    public static class OrganArtAuthoring
    {
        [Serializable] public sealed class Data { public Row[] organs = Array.Empty<Row>(); }
        [Serializable] public sealed class Row { public string id = "", icon = ""; }
        public const string JsonPath = "Assets/Data/Organs/organs.json";

        [MenuItem("Tools/SpringUp/UI/刷新器官图片绑定")]
        public static void RefreshMenu() => RefreshCatalog();

        public static OrganArtCatalog RefreshCatalog()
        {
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(JsonPath));
            var entries = new List<OrganArtCatalog.Entry>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in data.organs)
            {
                if (!ids.Add(row.id)) throw new InvalidOperationException("Duplicate organ ID: " + row.id);
                // Placeholder paths represent artwork that has not been supplied yet.
                if (!row.icon.StartsWith("Assets/Art/Organs/", StringComparison.Ordinal)) continue;
                if (Path.GetFileNameWithoutExtension(row.icon) != row.id || !File.Exists(row.icon))
                    throw new InvalidOperationException("Organ image must exist and use its JSON ID: " + row.icon);
                var importer = AssetImporter.GetAtPath(row.icon) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing texture importer: " + row.icon);
                // Some supplied icons were automatically sliced into isolated pixels/parts.
                // Single mode imports the whole illustration, including disconnected details.
                if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
                    || importer.filterMode != FilterMode.Point || importer.mipmapEnabled
                    || importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.alphaIsTransparency)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.alphaIsTransparency = true;
                    importer.SaveAndReimport();
                }
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(row.icon);
                if (sprite == null) throw new InvalidOperationException("Unable to load organ Sprite: " + row.icon);
                entries.Add(new OrganArtCatalog.Entry { definitionId = row.id, sprite = sprite });
            }
            var art = AssetDatabase.LoadAssetAtPath<OrganArtCatalog>(OrganUiBuilder.ArtPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<OrganArtCatalog>();
                AssetDatabase.CreateAsset(art, OrganUiBuilder.ArtPath);
            }
            art.entries = entries.ToArray(); EditorUtility.SetDirty(art); AssetDatabase.SaveAssets();
            Debug.Log("[Organ Art] Bound " + entries.Count + " complete sprites by JSON ID.");
            return art;
        }
    }
}
