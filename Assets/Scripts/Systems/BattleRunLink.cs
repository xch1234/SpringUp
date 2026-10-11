using System;

namespace Tideflesh.Systems
{
    /// <summary>
    /// 把战场的「这一波时间到了」和「实验室允许开下一波」接到 <see cref="RunController"/>。
    /// 不负责刷怪，也不负责界面。
    /// </summary>
    public sealed class BattleRunLink
    {
        public RunController Run { get; }

        public BattleRunLink(RunController run)
        {
            Run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public void Begin() => Run.StartRun();

        public void NotifyWaveTimerFinished(int wave)
        {
            if (Run.Phase != "combat")
                throw new RunException("timer only valid in combat");
            if (wave != Run.Wave)
                throw new RunException("wave mismatch: battle " + wave + ", run " + Run.Wave);
            Run.CombatTimerFinished();
        }

        public bool TryStartNextWave(out int nextWave)
        {
            if (!Run.CanStartNextWave())
            {
                nextWave = 0;
                return false;
            }

            Run.StartNextWave();
            nextWave = Run.Wave;
            return true;
        }
    }
}
