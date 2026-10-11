using System;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人接口的对外信号与载荷。
    /// 与 <c>接口草案.md</c> §6.1 的信号表一一对应。
    ///
    /// **为什么是静态总线而不是每个敌人一个 C# 事件**：
    /// 订阅方（碰撞触发 / 击杀触发 / 表现层 / 受伤触发）需要在**任意敌人**出事时都收到通知，
    /// 用实例事件就得先找到那个实例，等于把「敌人注册表」的活推给每个订阅方。
    /// 集中一条总线更稳，也避免某个敌人被销毁时漏发事件。
    ///
    /// **谁抛**：<see cref="EnemyRuntime"/>（敌人侧）与 <see cref="PlayerRuntime"/>（玩家受伤侧）。
    /// **谁接**：程序 A 的触发分发、美术 B 的表现层、程序 B 的战斗内触发可视化。
    /// </summary>
    public static class EnemyEvents
    {
        /// <summary>`OnDamaged` 载荷：敌人 id、伤害值、来源器官 id、命中点。</summary>
        public struct DamagedInfo
        {
            public string enemyId;
            public float damage;
            public string sourceOrganId;
            public Vector2 hitPoint;
            public GameObject enemy;
        }

        /// <summary>`OnKilled` 载荷：敌人 id、死亡点、击杀者器官 id。</summary>
        public struct KilledInfo
        {
            public string enemyId;
            public Vector2 deathPoint;
            public string killerOrganId;
            public GameObject enemy;
        }

        /// <summary>`OnStatusApplied` 载荷：敌人 id、状态类型、层数、持续。</summary>
        public struct StatusAppliedInfo
        {
            public string enemyId;
            public string status;
            public int stacks;
            public float duration;
            public GameObject enemy;
        }

        /// <summary>`OnPlayerHurt` 载荷：伤害值、来源敌人 id。</summary>
        public struct PlayerHurtInfo
        {
            public float damage;
            public string sourceEnemyId;
        }

        /// <summary>敌人受伤。喂给碰撞触发与触发链。</summary>
        public static event Action<DamagedInfo> Damaged;

        /// <summary>敌人被击杀。喂给击杀触发。</summary>
        public static event Action<KilledInfo> Killed;

        /// <summary>敌人被施加状态。喂给表现层。</summary>
        public static event Action<StatusAppliedInfo> StatusApplied;

        /// <summary>玩家受伤。喂给受伤触发（玩家侧）。</summary>
        public static event Action<PlayerHurtInfo> PlayerHurt;

        public static void RaiseDamaged(DamagedInfo info)
        {
            var handler = Damaged;
            if (handler != null)
                handler(info);
        }

        public static void RaiseKilled(KilledInfo info)
        {
            var handler = Killed;
            if (handler != null)
                handler(info);
        }

        public static void RaiseStatusApplied(StatusAppliedInfo info)
        {
            var handler = StatusApplied;
            if (handler != null)
                handler(info);
        }

        public static void RaisePlayerHurt(PlayerHurtInfo info)
        {
            var handler = PlayerHurt;
            if (handler != null)
                handler(info);
        }

        /// <summary>
        /// 清空所有订阅。
        /// **只给运行时重载 / 退出播放模式用**：静态事件在编辑器里跨播放会话存活，
        /// 不清理会出现「第二个播放会话里同一个订阅被调两次」。
        /// </summary>
        public static void ResetAllSubscriptions()
        {
            Damaged = null;
            Killed = null;
            StatusApplied = null;
            PlayerHurt = null;
        }
    }
}
