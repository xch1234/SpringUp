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
            if (art.entries.Length != 38) throw new InvalidOperationException("Expected all 38 supplied illustrations.");
            foreach (var entry in art.entries)
            {
                var path = AssetDatabase.GetAssetPath(entry.sprite);
                if (Path.GetFileNameWithoutExtension(path) != entry.definitionId || art.Find(entry.definitionId) != entry.sprite)
                    throw new InvalidOperationException("ID/art mismatch: " + entry.definitionId);
                if (entry.definitionId == "summon_follower")
                {
                    if (entry.sprite.name != "summon_follower_0" || AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count() != 10)
                        throw new InvalidOperationException("Summon animation must retain 10 frames and use frame 0 as icon.");
                    continue;
                }
                if (AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count() != 1
                    || entry.sprite.rect.width != entry.sprite.texture.width || entry.sprite.rect.height != entry.sprite.texture.height)
                    throw new InvalidOperationException("Incomplete sliced artwork: " + path);
            }
            Debug.Log("[Organ Art Checks] PASS: 38 English filenames match JSON IDs, single illustrations and summon frame icon.");
            BattleHudChecks.RunBatch();
        }
    }
}
