using System;
using UnityEngine;

namespace SpringUp.Laboratory
{
    [CreateAssetMenu(menuName = "SpringUp/UI/Organ Art Catalog")]
    public sealed class OrganArtCatalog : ScriptableObject
    {
        [Serializable] public struct Entry { public string definitionId; public Sprite sprite; }
        public Entry[] entries = Array.Empty<Entry>();
        public Sprite Find(string id)
        {
            foreach (var entry in entries) if (entry.definitionId == id) return entry.sprite;
            return null;
        }
    }
}
