using System.Collections.Generic;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 极简对象池：按"原型 + 键"复用实例，避免高频创建/销毁造成 GC 抖动。
    ///
    /// **为什么需要**：初版每发子弹、每个敌人都走 <c>new GameObject</c> + <c>Destroy</c>。
    /// 类幸存者一局要发射几百上千发子弹，每次创建/销毁都有一次托管堆分配，
    /// 结果是帧率锯齿与 GC 尖峰——这是必须修的性能缺陷，不是风格偏好。
    ///
    /// 用法：<see cref="Rent"/> 取一个（池空则按原型实例化），<see cref="Return"/> 还回去。
    /// 归还时对象被置为非激活并挂在池根下，取用时重新激活。
    ///
    /// **不做的事**：不做容量上限、不做按时间回收、不做跨场景清理。
    /// 灰盒阶段够用；正式版若要精细化，替换本类即可，调用点不用改。
    /// </summary>
    public static class SimplePool
    {
        private static readonly Dictionary<string, Stack<GameObject>> s_Pools =
            new Dictionary<string, Stack<GameObject>>();

        private static Transform s_Root;

        /// <summary>池根节点。归还的对象挂在这里，便于在 Hierarchy 里查看。</summary>
        private static Transform Root
        {
            get
            {
                if (s_Root == null)
                {
                    var rootObject = new GameObject("[Pool]");
                    s_Root = rootObject.transform;
                }

                return s_Root;
            }
        }

        /// <summary>
        /// 取一个实例。池里有就复用，没有就 <c>Instantiate</c> 原型。
        ///
        /// **必须把实例从池根摘出来**：归还时对象被挂到 <c>[Pool]</c> 节点下，
        /// 而挂在非场景根节点下的物体会因为其父节点无渲染上下文而不可见——
        /// 曾因此出现"子弹发射了但看不见"。取用时把父节点置空即可。
        /// </summary>
        /// <param name="prototype">原型物体；为 null 时返回 null。</param>
        /// <param name="key">池键。同一原型可用于多个池；为空时用原型名。</param>
        public static GameObject Rent(GameObject prototype, string key)
        {
            if (prototype == null)
                return null;

            var resolvedKey = string.IsNullOrEmpty(key) ? prototype.name : key;
            var pool = GetPool(resolvedKey);

            while (pool.Count > 0)
            {
                var candidate = pool.Pop();

                // 场景切换等原因可能把对象销毁掉，跳过空引用。
                if (candidate != null)
                {
                    candidate.transform.SetParent(null, false);
                    candidate.SetActive(true);
                    return candidate;
                }
            }

            var instance = Object.Instantiate(prototype);
            instance.name = prototype.name;

            // **必须显式激活**：原型通常是 SetActive(false) 的（否则它会以"场上单位"的身份
            // 混进游戏逻辑），而 Instantiate **会复制原型的非激活状态**。
            // 漏掉这一行，新建出来的实例就是非激活的——存在、位置对、不报错，但不可见。
            // 复用分支本来就有 SetActive(true)，所以只有"首次新建"的那些会坏，极难察觉。
            instance.transform.SetParent(null, false);
            instance.SetActive(true);

            return instance;
        }

        /// <summary>把实例还回池中：置为非激活并挂到池根下。</summary>
        public static void Return(GameObject instance, string key)
        {
            if (instance == null)
                return;

            var resolvedKey = string.IsNullOrEmpty(key) ? instance.name : key;
            instance.SetActive(false);
            instance.transform.SetParent(Root, false);

            GetPool(resolvedKey).Push(instance);
        }

        /// <summary>某个键当前缓存的实例数量。测试与诊断用。</summary>
        public static int CountInPool(string key)
        {
            Stack<GameObject> pool;
            return s_Pools.TryGetValue(key, out pool) ? pool.Count : 0;
        }

        /// <summary>清空所有池并把缓存对象销毁。退出播放模式或切换场景时用。</summary>
        public static void Clear()
        {
            foreach (var pool in s_Pools.Values)
            {
                while (pool.Count > 0)
                {
                    var instance = pool.Pop();
                    if (instance != null)
                        Object.Destroy(instance);
                }
            }

            s_Pools.Clear();
            s_Root = null;
        }

        private static Stack<GameObject> GetPool(string key)
        {
            Stack<GameObject> pool;
            if (s_Pools.TryGetValue(key, out pool))
                return pool;

            pool = new Stack<GameObject>();
            s_Pools[key] = pool;
            return pool;
        }
    }
}
