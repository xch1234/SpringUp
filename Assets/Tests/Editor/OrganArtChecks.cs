using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    public static class OrganArtChecks
    {
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated validation copy.");
            var art = OrganArtAuthoring.RefreshCatalog();
            if (art.entries.Length != 37) throw new InvalidOperationException("Expected all 37 supplied illustrations.");
            foreach (var entry in art.entries)
            {
                var path = AssetDatabase.GetAssetPath(entry.sprite);
                if (Path.GetFileNameWithoutExtension(path) != entry.definitionId || art.Find(entry.definitionId) != entry.sprite)
                    throw new InvalidOperationException("ID/art mismatch: " + entry.definitionId);
                if (AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count() != 1
                    || entry.sprite.rect.width != entry.sprite.texture.width || entry.sprite.rect.height != entry.sprite.texture.height)
                    throw new InvalidOperationException("Incomplete sliced artwork: " + path);
            }
            Debug.Log("[Organ Art Checks] PASS: 37 English filenames match JSON IDs, complete sprites and runtime catalog lookup.");
            BattleHudChecks.RunBatch();
        }
    }
}
