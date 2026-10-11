using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 静态状态的生命周期兜底。
    ///
    /// <see cref="EnemyEvents"/> 的事件是**静态**的，所以在编辑器里会跨播放会话存活。
    /// 如果某个订阅方在 <c>OnDisable</c> 里漏了解绑（或者对象被销毁时没走到解绑），
    /// 第二个播放会话里就会看到同一个回调被调用两次——这类 bug 极难定位，
    /// 因为「上一次会话的残留」不在任何 Inspector 里。
    ///
    /// **进入播放模式前**清空：静态订阅、玩家缓存、对象池。
    ///
    /// **退出播放模式时也清一次池**：池里的对象是"场景外的活跃物体"，
    /// 编辑模式下不会自动销毁，不清会在 Hierarchy 里堆积——
    /// 实测出现过一大片不会自动消失的灰色残留物。
    /// </summary>
    public static class BattleEventBusLifetime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            EnemyEvents.ResetAllSubscriptions();
            PlayerLocator.Reset();
            // 静态池同样跨播放会话存活；不清会留下上一次会话已销毁对象的引用，
            // 而且复用对象会带着上一次的位置与状态。
            SimplePool.Clear();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void HookPlayModeStateChanged()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                SimplePool.Clear();
        }
#endif
    }
}
