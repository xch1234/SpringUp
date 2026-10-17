using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 战场用的物理层名，以及**只在代码里**确立的碰撞关系（不写 ProjectSettings）。
    ///
    /// **为什么需要分层**：策划要求「角色穿过敌人和木桩，但不能穿墙」，
    /// 同时「敌人之间保留碰撞体积」。这是一个纯粹的碰撞矩阵问题：
    ///
    /// | | 玩家 | 敌人 | 墙 |
    /// |---|---|---|---|
    /// | **玩家** | — | ❌ 穿过 | ✅ 被挡住 |
    /// | **敌人** | ❌ | ✅ 互相碰撞 | ✅ 被挡住 |
    ///
    /// **为什么不改 ProjectSettings**：本工程是多人共用仓库，`ProjectSettings/`
    /// 属于**两边都在改**的文件（远端 main 与本地 main 有 29 个文件双侧修改）。
    /// 用 `Physics2D.IgnoreLayerCollision` 在每次运行加载场景前初始化，
    /// 不依赖生成器执行过的临时状态，也不写共享物理配置。
    ///
    /// 代价：层名若没有写进 `TagManager.asset`，在 Physics2D 的碰撞矩阵界面上
    /// 只会看到 "Layer 8 / 9" 这种编号。代码里用 <see cref="Player"/> /
    /// <see cref="Enemy"/> 常量取名，所以不影响可读性。
    /// </summary>
    public static class BattleLayers
    {
        /// <summary>角色层。与敌人层互不相碰（穿过），与墙相碰（被挡住）。</summary>
        public const string Player = "TideBorne_Player";

        /// <summary>敌人层。彼此相碰（保留碰撞体积），与墙相碰。</summary>
        public const string Enemy = "TideBorne_Enemy";

        /// <summary>边界墙层。玩家和敌人都撞它。</summary>
        public const string Wall = "TideBorne_Wall";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeCollisionRules()
        {
            int player = LayerMask.NameToLayer(Player), enemy = LayerMask.NameToLayer(Enemy), wall = LayerMask.NameToLayer(Wall);
            if (player < 0 || enemy < 0 || wall < 0) return;
            Physics2D.IgnoreLayerCollision(player, enemy, true);
            Physics2D.IgnoreLayerCollision(player, wall, false);
            Physics2D.IgnoreLayerCollision(enemy, enemy, false);
            Physics2D.IgnoreLayerCollision(enemy, wall, false);
        }

        /// <summary>
        /// 取层号；层的名字不存在时退回 Default(0) 并告警。
        ///
        /// **为什么告警而不是静默**：退回 Default 会让"角色穿过敌人"失效
        /// （Default 上什么都有，大家又会互相撞），表现为角色被敌人卡住、木桩被推走。
        /// 这个症状很难反推到层名上，所以必须留一句话。
        /// </summary>
        public static int Resolve(string layerName)
        {
            var index = LayerMask.NameToLayer(layerName);
            if (index >= 0)
                return index;

            Debug.LogWarning("[BattleLayers] 找不到层 \"" + layerName + "\"，退回 Default(0)。" +
                             "碰撞关系可能不符合预期——请先跑一次菜单 Tools → 程序C → 生成战场灰盒场景" +
                             "（它会建立这三个层）。");
            return 0;
        }
    }
}
