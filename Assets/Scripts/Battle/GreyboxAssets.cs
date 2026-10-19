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

        /// <summary>
        /// 玩家方块的世界边长，米。**这是所有角色尺寸的基准**，敌人都按它的比例算。
        ///
        /// 2026-10-13 从 0.8 提到 1.35（×1.6875）：那天相机视野从 21.33 × 12 米
        /// 拉大到 **36 × 20.25 米**（长边 36 米 = 地图 72 米的一半）。
        /// 视野放大后原来的方块看起来小了一圈，所以角色与敌人**同倍**放大，
        /// 让"方块占屏幕的比例"回到拉大之前——也就是观感不变、地图看得更多。
        ///
        /// 1.6875 就是新旧半高之比 <c>10.125 ÷ 6</c>。要再调视野大小的话，
        /// 这个数按同一比例改，玩家和敌人就不会走形。
        /// </summary>
        public const float PlayerWorldSize = 1.35f;

        /// <summary>
        /// 玩家身体中心的小碰撞框，略小于人物可见轮廓；转向时也不伸出贴图两侧。
        /// </summary>
        public const float PlayerColliderSize = .65f;

        /// <summary>
        /// 灰盒敌人相对玩家的尺寸比例。保持与玩家同倍缩放，
        /// 所以玩家放大时敌人自动跟着放大，不用改两处。
        /// </summary>
        public const float EnemySizeRelativeToPlayer = 1f;

        /// <summary>灰盒敌人的视觉边长，米。</summary>
        public const float EnemyWorldSize = PlayerWorldSize * EnemySizeRelativeToPlayer;

        /// <summary>弹体的视觉边长，米。同样跟着角色一起放大。</summary>
        public const float ProjectileWorldSize = 0.25f * ScaleUpFactor;

        /// <summary>
        /// 尺寸放大倍数（1.6875）。这个常数唯一的用途是给那些**不该按玩家比例算**的
        /// 小物件（弹体）一个统一的放大系数，免得再散落一堆 1.6875。
        /// </summary>
        public const float ScaleUpFactor = 1.6875f;

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
