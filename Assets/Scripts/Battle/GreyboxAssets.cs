using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 灰盒阶段共用的占位素材。
    ///
    /// **为什么单独抽一个类**：初版把"造白方块精灵"这件事在
    /// <c>BattleGreyboxBuilder</c> 与 <c>EnemyFactory</c> 里各写了一遍，
    /// 结果两处的 PixelsPerUnit 不一致——构建器是 16，工厂是 1。
    /// 1 像素每单位意味着精灵只有 1 个单位大，缩到 0.7 后**几乎看不见**，
    /// 表现为"敌人能碰到但看不到"。共用一份就不可能出现这种漂移。
    /// </summary>
    public static class GreyboxAssets
    {
        /// <summary>灰盒方块的像素尺寸。16×16 配 16 PPU = 1 个世界单位，便于按整数摆放。</summary>
        public const int SquareTextureSize = 16;

        /// <summary>灰盒方块每单位像素数。1 单位 = 16 像素。</summary>
        public const float SquarePixelsPerUnit = 16f;

        private static Sprite s_SquareSprite;

        /// <summary>
        /// 共用的白色方块精灵（16×16，1 单位 = 16 像素，Point 过滤）。
        /// 首次调用时在内存里生成，之后复用。
        /// </summary>
        public static Sprite SquareSprite
        {
            get
            {
                if (s_SquareSprite == null)
                    s_SquareSprite = CreateSquareSprite();

                return s_SquareSprite;
            }
        }

        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(SquareTextureSize, SquareTextureSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.name = "GreyboxSquareTexture";

            var pixels = new Color32[SquareTextureSize * SquareTextureSize];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);

            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, SquareTextureSize, SquareTextureSize),
                new Vector2(0.5f, 0.5f),
                SquarePixelsPerUnit);
            sprite.name = "GreyboxSquare";
            return sprite;
        }
    }
}
