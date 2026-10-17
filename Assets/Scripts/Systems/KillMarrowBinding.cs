using System;
using TideBorne.Battle;
namespace Tideflesh.Systems
{
    // Owned by the current scene driver, disposed before unload / run replacement.
    public sealed class KillMarrowBinding : IDisposable
    {
        readonly RunController run;
        public KillMarrowBinding(RunController run) { this.run = run; EnemyEvents.DeathCommitted += Killed; }
        void Killed(EnemyEvents.KilledInfo info)
        {
            if (run.TryCreditKill(info.targetId, info.baseMarrow, out _)) MarrowDeathVisual.Spawn(info.deathPoint);
        }
        public void Dispose() { EnemyEvents.DeathCommitted -= Killed; }
    }
}

