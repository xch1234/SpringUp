using System;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>一波的生命周期。</summary>
    public enum WaveState
    {
        /// <summary>还没开始。</summary>
        Idle = 0,

        /// <summary>战斗中。</summary>
        Running = 1,

        /// <summary>已结算（时间到），等待下一波。</summary>
        Complete = 2,

        /// <summary>全部波次跑完。</summary>
        AllWavesFinished = 3
    }

    /// <summary>
    /// 波次计时与流程。
    ///
    /// **波次结束条件已定**（2026-10-06 策划口述，对应 `策划案.md` 待定问题 2）：
    /// 60 秒到点即结算，**并清除场上残余敌人**。
    /// 清除是「清除」不是「消灭」——**不产生掉落物**，因此走
    /// <see cref="EnemyRuntime.ClearBattlefield"/>，绝不会发 `OnKilled`。
    ///
    /// 这条区分不是洁癖：若清场走击杀路径，「击杀触发」类器官
    /// （多肢触手 / 蜘蛛巢）会在非击杀时被误触发，白送收益，
    /// 与 `策划案.md`「掉落要与当前 build 加权」的经济设计直接冲突。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/波次计时（WaveDirector）")]
    public class WaveDirector : MonoBehaviour
    {
        [Header("节奏")]
        [Tooltip("每波时长，秒。策划案定为 60。")]
        [SerializeField] private float waveDuration = 60f;

        [Tooltip("总波次数量。策划案定为 7（第 7 波为 Boss）。")]
        [SerializeField] private int totalWaves = 7;

        [Tooltip("每波结束到下一波开始之间的间隔，秒。0 表示立即接上。")]
        [SerializeField] private float intervalBetweenWaves = 0f;

        [Header("刷怪")]
        [Tooltip("刷怪器。留空则自动取本物体或场景上的 EnemySpawner。")]
        [SerializeField] private EnemySpawner spawner;

        [Tooltip("每波敌人数量。下标 0 对应第 1 波；数量不足时沿用最后一个值。")]
        [SerializeField] private int[] enemiesPerWave = new int[] { 8, 12, 16, 20, 24, 28, 1 };

        [Header("行为开关")]
        [Tooltip("是否开局自动开第一波。")]
        [SerializeField] private bool autoStart = true;

        [Tooltip("是否在每波到点时清除场上残余敌人。按已定规则应为开。")]
        [SerializeField] private bool clearEnemiesOnWaveEnd = true;

        [Tooltip("是否打印波次日志，便于灰盒阶段肉眼核对。")]
        [SerializeField] private bool logWaveProgress = true;

        private float _elapsed;
        private float _intervalRemaining;

        /// <summary>当前波次号，从 1 开始；未开始时为 0。</summary>
        public int CurrentWave { get; private set; }

        /// <summary>当前状态。</summary>
        public WaveState State { get; private set; }

        /// <summary>本波剩余时间，秒。</summary>
        public float RemainingTime
        {
            get
            {
                if (State != WaveState.Running)
                    return 0f;

                return Mathf.Max(0f, waveDuration - _elapsed);
            }
        }

        /// <summary>每波时长，秒。</summary>
        public float WaveDuration { get { return waveDuration; } }

        /// <summary>总波次数量。</summary>
        public int TotalWaves { get { return totalWaves; } }

        /// <summary>一波开始。</summary>
        public event Action<int> WaveStarted;

        /// <summary>一波结算，参数是波次号与被清除的敌人数量。</summary>
        public event Action<int, int> WaveCompleted;

        /// <summary>全部波次结束。</summary>
        public event Action AllWavesCompleted;

        private void Awake()
        {
            if (spawner == null)
                spawner = GetComponent<EnemySpawner>();

            if (spawner == null)
                spawner = FindFirstObjectByType<EnemySpawner>();

            if (spawner == null)
                Debug.LogWarning("[WaveDirector] 场景里没有 EnemySpawner，本波次只会计时、不会刷怪。", this);

            State = WaveState.Idle;
            CurrentWave = 0;
        }

        private void Start()
        {
            if (autoStart)
                StartNextWave();
        }

        private void Update()
        {
            switch (State)
            {
                case WaveState.Running:
                    TickRunning();
                    break;

                case WaveState.Complete:
                    TickInterval();
                    break;
            }
        }

        private void TickRunning()
        {
            _elapsed += Time.deltaTime;

            if (spawner != null)
                spawner.Tick(Time.deltaTime, waveDuration);

            if (_elapsed >= waveDuration)
                CompleteCurrentWave();
        }

        private void TickInterval()
        {
            if (intervalBetweenWaves <= 0f)
                return;

            _intervalRemaining -= Time.deltaTime;
            if (_intervalRemaining <= 0f)
                StartNextWave();
        }

        /// <summary>开始下一波。已跑完全部波次时返回 false。</summary>
        public bool StartNextWave()
        {
            if (CurrentWave >= totalWaves)
            {
                State = WaveState.AllWavesFinished;
                return false;
            }

            if (CurrentWave == 0)
                PlayerLocator.Reset();

            CurrentWave++;
            _elapsed = 0f;
            State = WaveState.Running;

            var planned = GetEnemyCountForWave(CurrentWave);
            if (spawner != null)
                spawner.BeginWave(planned, waveDuration);

            if (logWaveProgress)
            {
                Debug.Log("[WaveDirector] 第 " + CurrentWave + "/" + totalWaves + " 波开始，计划刷 " +
                          planned + " 个，时长 " + waveDuration + " 秒。");
            }

            var handler = WaveStarted;
            if (handler != null)
                handler(CurrentWave);

            return true;
        }

        /// <summary>
        /// 结算当前波。时间到点时调用；也可由外部提前调用（例如调试跳过）。
        /// 会按 <see cref="clearEnemiesOnWaveEnd"/> 清除残余敌人——不发击杀信号、不掉落。
        /// </summary>
        public void CompleteCurrentWave()
        {
            if (State != WaveState.Running)
                return;

            State = WaveState.Complete;
            _intervalRemaining = intervalBetweenWaves;

            var cleared = 0;
            if (clearEnemiesOnWaveEnd)
                cleared = EnemyRuntime.ClearBattlefield();

            if (logWaveProgress)
            {
                Debug.Log("[WaveDirector] 第 " + CurrentWave + " 波结束，清除残余敌人 " + cleared +
                          " 个（不计击杀、不掉落）。");
            }

            var handler = WaveCompleted;
            if (handler != null)
                handler(CurrentWave, cleared);

            if (CurrentWave >= totalWaves)
            {
                State = WaveState.AllWavesFinished;

                var allDone = AllWavesCompleted;
                if (allDone != null)
                    allDone();

                if (logWaveProgress)
                    Debug.Log("[WaveDirector] 全部 " + totalWaves + " 波结束。");
            }
        }

        /// <summary>取某一波的敌人数量；数组不够长时沿用最后一个值。</summary>
        public int GetEnemyCountForWave(int waveNumber)
        {
            if (enemiesPerWave == null || enemiesPerWave.Length == 0)
                return 0;

            var index = Mathf.Clamp(waveNumber - 1, 0, enemiesPerWave.Length - 1);
            return Mathf.Max(0, enemiesPerWave[index]);
        }
    }
}
