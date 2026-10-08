using System.Collections.Generic;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人 prefab 注册表：按 <see cref="EnemyArchetype"/> 取 prefab。
    ///
    /// **为什么从 Resources 按名加载，而不是静态注册**：静态字段跨不过域重载，
    /// 需要有人在一个确定的时机把 prefab 塞进去——那就是个初始化顺序问题，
    /// 而初始化顺序问题在本项目已经踩过两次（池原型的精灵引用、静态池的跨会话残留）。
    /// 从 <c>Assets/Resources/Enemies/&lt;名字&gt;.prefab</c> 按名加载没有这个问题，
    /// 而且**美术新增一种敌人只需往那个目录扔一个 prefab，不用改代码**。
    /// </summary>
    public static class EnemyPrefabLibrary
    {
        /// <summary>敌人 prefab 的 Resources 目录。</summary>
        public const string EnemyResourcesFolder = "Enemies";

        private static readonly Dictionary<EnemyArchetype, GameObject> s_Cache =
            new Dictionary<EnemyArchetype, GameObject>();

        private static bool s_LoadedAll;

        /// <summary>取某个原型的 prefab；找不到返回 null。</summary>
        public static GameObject Get(EnemyArchetype archetype)
        {
            GameObject cached;
            if (s_Cache.TryGetValue(archetype, out cached) && cached != null)
                return cached;

            if (!s_LoadedAll)
                LoadAll();

            if (s_Cache.TryGetValue(archetype, out cached) && cached != null)
                return cached;

            // 单个补一次：LoadAll 之后仍没有，可能是资产在本次会话中刚被创建。
            var single = Resources.Load<GameObject>(EnemyResourcesFolder + "/" + archetype);
            if (single != null)
            {
                s_Cache[archetype] = single;
                return single;
            }

            Debug.LogWarning("[EnemyPrefabLibrary] 找不到 " + archetype + " 的 prefab。" +
                             "期望路径：Assets/Resources/" + EnemyResourcesFolder + "/" + archetype + ".prefab");
            return null;
        }

        /// <summary>把 Resources/Enemies 下所有 prefab 读进缓存，按文件名匹配枚举名。</summary>
        public static void LoadAll()
        {
            s_LoadedAll = true;

            var all = Resources.LoadAll<GameObject>(EnemyResourcesFolder);
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[EnemyPrefabLibrary] Resources/" + EnemyResourcesFolder +
                                 " 下没有 prefab。请跑一次菜单 Tools/程序C/生成战场灰盒场景。");
                return;
            }

            foreach (var prefab in all)
            {
                if (prefab == null)
                    continue;

                EnemyArchetype archetype;
                if (System.Enum.TryParse(prefab.name, out archetype))
                    s_Cache[archetype] = prefab;
            }
        }

        /// <summary>清空缓存。域重载或资产变更后调用。</summary>
        public static void Clear()
        {
            s_Cache.Clear();
            s_LoadedAll = false;
        }

        /// <summary>已缓存的条目数，供诊断。</summary>
        public static int CachedCount { get { return s_Cache.Count; } }
    }
}
