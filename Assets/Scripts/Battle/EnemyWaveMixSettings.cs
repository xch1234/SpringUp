using System;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>一波的敌人权重。0 表示该波不出现该类型。</summary>
    [Serializable]
    public sealed class EnemyWaveMix
    {
        [Tooltip("波次号，从 1 开始。")]
        public int wave;

        [Header("权重（0 = 该波不出现）")]
        public int normal;
        public int fast;
        public int keeper;
        public int chaser;
        public int medic;
        public int arsenal;

        /// <summary>取某种类型在本波的权重。</summary>
        public int WeightOf(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Normal: return normal;
                case EnemyArchetype.Fast: return fast;
                case EnemyArchetype.RangedKeeper: return keeper;
                case EnemyArchetype.RangedChaser: return chaser;
                case EnemyArchetype.Medic: return medic;
                case EnemyArchetype.Arsenal: return arsenal;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// 每波刷怪的权重表与全局刷怪参数。
    ///
    /// **刷怪模型**（2026-10-19 策划定）：所有波次统一每 3 秒出一个敌人，
    /// 出哪一种按当前波次的权重随机；波次不再有"计划总数"。
    /// 兵工厂 / 医疗兵另有"同时存活上限"，到上限后这一拍重新在其余类型里按权重抽。
    /// </summary>
    [CreateAssetMenu(menuName = "SpringUp/Enemy Wave Mix", fileName = "EnemyWaveMix")]
    public sealed class EnemyWaveMixSettings : ScriptableObject
    {
        [Tooltip("刷怪间隔，秒。所有波次统一。")]
        [Min(0.1f)] public float spawnIntervalSeconds = 3f;

        [Tooltip("兵工厂全场同时存活上限。0 = 不限。")]
        [Min(0)] public int arsenalAliveCap = 1;

        [Tooltip("医疗兵全场同时存活上限。0 = 不限。")]
        [Min(0)] public int medicAliveCap = 2;

        [Tooltip("每波权重表；找不到对应波次时沿用最后一行。")]
        public EnemyWaveMix[] waves = CreateDefaultWaves();

        /// <summary>取某一波的权重；没有配置时返回 null。</summary>
        public EnemyWaveMix ForWave(int waveNumber)
        {
            if (waves == null || waves.Length == 0)
                return null;

            for (var i = 0; i < waves.Length; i++)
            {
                if (waves[i] != null && waves[i].wave == waveNumber)
                    return waves[i];
            }

            return waves[waves.Length - 1];
        }

        /// <summary>某种类型的同时存活上限；0 表示不限。</summary>
        public int AliveCapFor(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Arsenal: return arsenalAliveCap;
                case EnemyArchetype.Medic: return medicAliveCap;
                default: return 0;
            }
        }

        /// <summary>从 Resources 取配置；没有资产时返回 null。</summary>
        public static EnemyWaveMixSettings Load()
        {
            return Resources.Load<EnemyWaveMixSettings>("EnemyWaveMix");
        }

        /// <summary>内存里的默认配置（资产缺失时的兜底，也供编辑器创建资产用）。</summary>
        public static EnemyWaveMixSettings CreateDefaultInstance()
        {
            var instance = CreateInstance<EnemyWaveMixSettings>();
            instance.waves = CreateDefaultWaves();
            return instance;
        }

        /// <summary>策划给的比例表；第 7 波已按 2026-10-19 修正为 1:1:3:2:3。</summary>
        public static EnemyWaveMix[] CreateDefaultWaves()
        {
            return new[]
            {
                new EnemyWaveMix { wave = 1, normal = 10 },
                new EnemyWaveMix { wave = 2, fast = 2, normal = 8 },
                new EnemyWaveMix { wave = 3, keeper = 2, fast = 3, normal = 5 },
                new EnemyWaveMix { wave = 4, chaser = 1, keeper = 2, fast = 3, normal = 4 },
                new EnemyWaveMix { wave = 5, medic = 1, chaser = 2, keeper = 2, fast = 2, normal = 3 },
                new EnemyWaveMix { wave = 6, arsenal = 1, medic = 1, chaser = 2, keeper = 2, fast = 2, normal = 2 },
                new EnemyWaveMix { wave = 7, arsenal = 1, medic = 1, chaser = 3, keeper = 2, fast = 3 },
            };
        }
    }
}
