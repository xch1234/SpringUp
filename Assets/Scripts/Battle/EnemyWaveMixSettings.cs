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
        [Tooltip("本波同时存活上限（含出生预警）；至少为 1。")]
        [Min(1)] [InspectorName("同时存活上限")] public int aliveCap = 16;
        [Tooltip("累计生成达到此数后固定用兜底间隔；0 按旧刷怪量 × 额度倍率计算。")]
        [Min(0)] [InspectorName("累计减速阈值（0 为自动）")] public int slowAfterCount;

        [Header("敌人属性倍率（出生时应用，范围 0.01–100）")]
        [Min(.01f), InspectorName("敌人血量倍率")] public float healthMultiplier = 1f;
        [Min(.01f), InspectorName("敌人攻击频率倍率"), Tooltip("攻击间隔除以倍率，包括接触伤害；不影响治疗、产兵或首次攻击延迟。")]
        public float attackFrequencyMultiplier = 1f;
        [Min(.01f), InspectorName("敌人伤害倍率")] public float damageMultiplier = 1f;
        [Min(.01f), InspectorName("敌人移速倍率")] public float moveSpeedMultiplier = 1f;

        public static float ValidMultiplier(float value)
            => float.IsNaN(value) || float.IsInfinity(value) || value <= 0 ? 1f : Mathf.Clamp(value, .01f, 100f);
        public void ValidateMultipliers()
        {
            healthMultiplier = ValidMultiplier(healthMultiplier);
            attackFrequencyMultiplier = ValidMultiplier(attackFrequencyMultiplier);
            damageMultiplier = ValidMultiplier(damageMultiplier);
            moveSpeedMultiplier = ValidMultiplier(moveSpeedMultiplier);
        }

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
    /// 每波敌人权重、存活上限与动态刷怪节奏。累计额度为减速阈值，不是停止生成的硬上限。
    /// </summary>
    [CreateAssetMenu(menuName = "SpringUp/Enemy Wave Mix", fileName = "EnemyWaveMix")]
    public sealed class EnemyWaveMixSettings : ScriptableObject
    {
        [Tooltip("刷怪间隔，秒。所有波次统一。")]
        [Min(0.1f)] public float spawnIntervalSeconds = 3f;

        [Header("动态刷怪（倍率作用于生成间隔）")]
        [InspectorName("启用动态刷怪")] public bool dynamicSpawning = true;
        [Range(0, 1)] public float veryLowThreshold = .2f;
        [Range(0, 1)] public float lowThreshold = .5f;
        [Range(0, 1)] public float normalThreshold = .7f;
        [Min(.1f)] public float veryLowIntervalMultiplier = .5f;
        [Min(.1f)] public float lowIntervalMultiplier = .7f;
        [Min(.1f)] public float normalIntervalMultiplier = 1f;
        [Min(.1f)] public float crowdedIntervalMultiplier = 2f;
        [Min(.1f)] [InspectorName("额度耗尽后间隔倍率")] public float fallbackIntervalMultiplier = 2f;
        [Min(0)] [InspectorName("旧数量的额度倍率")] public float totalBudgetMultiplier = 1.5f;

        public int SlowAfterCount(int wave, float spawnDuration)
        {
            int configured = ForWave(wave)?.slowAfterCount ?? 0;
            if (configured > 0) return configured;
            // Old Tick stops before the cutoff, including a spawn exactly at its boundary.
            int baseline = Mathf.Max(0, Mathf.CeilToInt(spawnDuration / Mathf.Max(.1f, spawnIntervalSeconds)) - 1);
            return Mathf.Max(1, Mathf.FloorToInt(baseline * totalBudgetMultiplier));
        }
        public float IntervalMultiplier(int wave, int occupied, int scheduledTotal, int slowAfter)
        {
            if (!dynamicSpawning) return 1f;
            if (scheduledTotal >= slowAfter) return Mathf.Max(.1f, fallbackIntervalMultiplier);
            float ratio = (float)occupied / Mathf.Max(1, ForWave(wave)?.aliveCap ?? 16);
            float low = Mathf.Clamp01(veryLowThreshold);
            float middle = Mathf.Clamp(lowThreshold, low, 1);
            float high = Mathf.Clamp(normalThreshold, middle, 1);
            return Mathf.Max(.1f, ratio < low ? veryLowIntervalMultiplier : ratio < middle
                ? lowIntervalMultiplier : ratio < high ? normalIntervalMultiplier : crowdedIntervalMultiplier);
        }

        [Tooltip("兵工厂全场同时存活上限。0 = 不限。")]
        [Min(0)] public int arsenalAliveCap = 1;

        [Tooltip("医疗兵全场同时存活上限。0 = 不限。")]
        [Min(0)] public int medicAliveCap = 2;

        [Tooltip("每波权重表；找不到对应波次时沿用最后一行。")]
        public EnemyWaveMix[] waves = CreateDefaultWaves();

        void OnValidate()
        {
            if (waves != null) foreach (var wave in waves) wave?.ValidateMultipliers();
        }

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
