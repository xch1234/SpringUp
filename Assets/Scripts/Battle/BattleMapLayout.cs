using System;
using System.Collections.Generic;
using UnityEngine;

namespace TideBorne.Battle
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public sealed class BattleMapLayout : MonoBehaviour
    {
        public BattleMapSettings settings;
        public Transform terrain;
        public Transform walls;
        public Camera battleCamera;
        public EnemySpawner[] spawners;
        readonly Dictionary<Texture, Material> materials = new Dictionary<Texture, Material>();
        public Vector2 HalfExtents { get; private set; }
        public Vector2 Center => terrain != null ? (Vector2)terrain.position : (Vector2)transform.position;
        public int AppliedSeed { get; private set; }
        bool built;

        void Awake() => Rebuild();

        // Explicit rebuilding avoids hierarchy mutations during OnValidate / scene deserialization.
        public void Rebuild()
        {
            if (settings == null || terrain == null || walls == null || battleCamera == null) return;
            if (!settings.Validate(out var error)) { Debug.LogError("[BattleMapLayout] " + error, this); return; }
            // Keep one layout per scene entry; resizing does not reroll it.
            if (!built) AppliedSeed = settings.randomizeOnEntry && Application.isPlaying ? Guid.NewGuid().GetHashCode() : settings.seed;
            built = true;
            HalfExtents = settings.WorldSize * .5f;
            BuildTiles();
            ConfigureWalls();
            battleCamera.GetComponent<CameraFollow>()?.ConfigureBounds(Center, HalfExtents);
            battleCamera.orthographicSize = settings.CameraHalfHeight;
            if (spawners != null)
                foreach (var spawner in spawners)
                    if (spawner != null) spawner.ConfigurePlayArea(Center, HalfExtents);
        }

        void BuildTiles()
        {
            var random = new System.Random(AppliedSeed);
            var variants = settings.ValidCenterVariants();
            int count = settings.Columns * settings.Rows;
            // Reuse the baked tile objects; resizing does not require scaling the map artwork.
            var tiles = new List<Transform>();
            foreach (Transform child in terrain) tiles.Add(child);
            for (int i = 0; i < count; i++)
            {
                int x = i % settings.Columns, y = i / settings.Columns;
                int row = y == settings.Rows - 1 ? 0 : y == 0 ? 2 : 1;
                int column = x == 0 ? 0 : x == settings.Columns - 1 ? 2 : 1;
                var sprite = row == 1 && column == 1 ? variants[random.Next(variants.Length)] : settings.borderSprites[row * 3 + column];
                var tile = i < tiles.Count ? tiles[i] : new GameObject().transform;
                tile.SetParent(terrain, false); tile.name = $"Tile_{x:00}_{y:00}";
                tile.localRotation = Quaternion.identity;
                float scale = settings.CellSize / sprite.bounds.size.x;
                tile.localScale = new Vector3(scale, scale, 1);
                // Correct custom sprite pivots so every image still occupies exactly one grid cell.
                tile.localPosition = new Vector3((x + .5f) * settings.CellSize - HalfExtents.x,
                    (y + .5f) * settings.CellSize - HalfExtents.y, 0) - sprite.bounds.center * scale;
                var renderer = tile.GetComponent<SpriteRenderer>();
                if (renderer == null) renderer = tile.gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite; renderer.color = Color.white; renderer.sortingOrder = -100;
                if (!materials.TryGetValue(sprite.texture, out var material))
                {
                    material = new Material(settings.tileMaterial) { mainTexture = sprite.texture, hideFlags = HideFlags.DontSave };
                    materials.Add(sprite.texture, material);
                }
                renderer.sharedMaterial = material;
                tile.gameObject.SetActive(true);
            }
            for (int i = count; i < tiles.Count; i++)
            {
                tiles[i].gameObject.SetActive(false);
                tiles[i].SetParent(null, true);
                if (Application.isPlaying) Destroy(tiles[i].gameObject);
                else DestroyImmediate(tiles[i].gameObject);
            }
        }

        void ConfigureWalls()
        {
            SetWall("Wall_Top", new Vector2(0, HalfExtents.y + .5f), new Vector2(HalfExtents.x * 2 + 2, 1));
            SetWall("Wall_Bottom", new Vector2(0, -HalfExtents.y - .5f), new Vector2(HalfExtents.x * 2 + 2, 1));
            SetWall("Wall_Left", new Vector2(-HalfExtents.x - .5f, 0), new Vector2(1, HalfExtents.y * 2 + 2));
            SetWall("Wall_Right", new Vector2(HalfExtents.x + .5f, 0), new Vector2(1, HalfExtents.y * 2 + 2));
        }

        void SetWall(string name, Vector2 offset, Vector2 size)
        {
            var wall = walls.Find(name);
            if (wall == null) return;
            wall.position = new Vector3(Center.x + offset.x, Center.y + offset.y, wall.position.z);
            wall.rotation = Quaternion.identity; wall.localScale = new Vector3(size.x, size.y, 1);
            var collider = wall.GetComponent<BoxCollider2D>();
            collider.size = Vector2.one; collider.offset = Vector2.zero;
        }

        public Vector2 ClampInside(Vector2 position, float margin = 1f)
        {
            var available = Vector2.Max(Vector2.zero, HalfExtents - Vector2.one * margin);
            return new Vector2(Mathf.Clamp(position.x, Center.x - available.x, Center.x + available.x),
                Mathf.Clamp(position.y, Center.y - available.y, Center.y + available.y));
        }

        void OnDestroy()
        {
            foreach (var material in materials.Values)
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            materials.Clear();
        }
    }
}
