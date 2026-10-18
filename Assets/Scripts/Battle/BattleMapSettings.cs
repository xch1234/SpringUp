using System;
using System.Collections.Generic;
using UnityEngine;

namespace TideBorne.Battle
{
    [CreateAssetMenu(menuName = "潮涌之躯/战斗地图配置", fileName = "BattleMapSettings")]
    public sealed class BattleMapSettings : ScriptableObject
    {
        [Min(3)] public int columns = 40;
        [Min(3)] public int rows = 40;
        [Min(.5f)] public float cellSize = 1.8f;
        [Min(.5f)] public float cameraHalfHeight = 10.125f;
        public bool randomizeOnEntry = true;
        public int seed = 12345;
        [Tooltip("中间格独立、等概率选图。空项会跳过；列表为空时使用九宫格的 5 号图。")]
        public Sprite[] centerVariants = Array.Empty<Sprite>();
        [Tooltip("按 1 到 9 的顺序，保留四角和四边。")]
        public Sprite[] borderSprites = new Sprite[9];
        public Material tileMaterial;

        public int Columns => Mathf.Clamp(columns, 3, 200);
        public int Rows => Mathf.Clamp(rows, 3, 200);
        public float CellSize => Mathf.Max(.5f, cellSize);
        public float CameraHalfHeight => Mathf.Max(.5f, cameraHalfHeight);
        public Vector2 WorldSize => new Vector2(Columns, Rows) * CellSize;

        public Sprite[] ValidCenterVariants()
        {
            var sprites = new List<Sprite>();
            if (centerVariants != null)
                foreach (var sprite in centerVariants)
                    if (sprite != null) sprites.Add(sprite);
            if (sprites.Count == 0 && borderSprites != null && borderSprites.Length > 4 && borderSprites[4] != null)
                sprites.Add(borderSprites[4]);
            return sprites.ToArray();
        }

        public bool Validate(out string error)
        {
            if (tileMaterial == null) { error = "请指定地砖材质。"; return false; }
            if (borderSprites == null || borderSprites.Length != 9)
            { error = "九宫格图片需要按 1–9 顺序填写 9 项。"; return false; }
            var candidates = new List<Sprite>(borderSprites);
            candidates.AddRange(ValidCenterVariants());
            foreach (var sprite in candidates)
            {
                if (sprite == null) { error = "九宫格图片存在空项。"; return false; }
                if (!Mathf.Approximately(sprite.rect.width, sprite.rect.height))
                { error = "地砖必须是正方形，避免拼接拉伸：" + sprite.name; return false; }
            }
            error = null; return true;
        }
    }
}
