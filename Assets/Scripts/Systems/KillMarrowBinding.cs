using System;
using TideBorne.Battle;
using UnityEngine;
namespace Tideflesh.Systems
{
    // Old demos can omit presentation; economic settlement has no UI dependency.
    public sealed class KillMarrowBinding : IDisposable
    {
        readonly RunController run;
        readonly Action<KillRewardResult, Vector2> present;
        public KillMarrowBinding(RunController run, Action<KillRewardResult, Vector2> present = null)
        { this.run = run; this.present = present; EnemyEvents.DeathCommitted += Killed; }
        void Killed(EnemyEvents.KilledInfo info)
        {
            var reward = run.ResolveKill(info.targetId, info.baseMarrow, info.rewardEligible);
            if (!reward.Accepted || present == null) return;
            try { present(reward, info.deathPoint); }
            catch (Exception error) { Debug.LogException(error); } // Never interrupt death or undo committed rewards.
        }
        public void Dispose() => EnemyEvents.DeathCommitted -= Killed;
    }
}
