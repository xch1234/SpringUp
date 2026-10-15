using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 找玩家并缓存。敌人、刷怪器、波次计时都要用，集中一处避免各自 <c>Find</c>。
    ///
    /// 用 <c>FindAnyObjectByType</c> 而不是 <c>FindObjectOfType</c>：
    /// 后者在 Unity 6 已标记过时（探针可另验）。也刻意不用
    /// <c>FindFirstObjectByType</c>——它在 6000.5 里同样已过时，
    /// 理由是"依赖实例 ID 顺序"，换成 FindAny 才是它推荐的替代。
    /// </summary>
    public static class PlayerLocator
    {
        private static Transform s_Cached;

        /// <summary>玩家 Transform，找不到时返回 null。</summary>
        public static Transform Player
        {
            get
            {
                if (s_Cached != null)
                    return s_Cached;

                var runtime = Object.FindAnyObjectByType<PlayerRuntime>();
                if (runtime != null)
                {
                    s_Cached = runtime.transform;
                    return s_Cached;
                }

                var found = GameObject.FindGameObjectWithTag("Player");
                if (found != null)
                    s_Cached = found.transform;

                return s_Cached;
            }
        }

        /// <summary>玩家所在位置；找不到时返回原点。</summary>
        public static Vector2 PlayerPosition
        {
            get
            {
                var player = Player;
                return player != null ? (Vector2)player.position : Vector2.zero;
            }
        }

        /// <summary>玩家是否可用。</summary>
        public static bool HasPlayer { get { return Player != null; } }

        /// <summary>清掉缓存。切换场景或玩家被重建后调用。</summary>
        public static void Bind(Transform player) => s_Cached = player;

        public static void Reset()
        {
            s_Cached = null;
        }
    }
}
