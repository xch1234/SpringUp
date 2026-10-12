using System.Collections.Generic;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEngine;

namespace Tideflesh.Systems
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OrganPipelineController), typeof(BattleOrganTargeting))]
    public sealed class BattleOrganCombat : MonoBehaviour
    {
        private sealed class TargetState
        {
            public EnemyRuntime Enemy;
            public string Id;
            public readonly StatusEffectRuntime Status = new StatusEffectRuntime();
            public bool Valid => Enemy != null && Enemy.isActiveAndEnabled && Enemy.IsAlive && Enemy.TargetId == Id;
        }

        private OrganPipelineController pipeline;
        private readonly Dictionary<string, TargetState> states = new Dictionary<string, TargetState>();
        private readonly Queue<TriggerSignal> kills = new Queue<TriggerSignal>();
        private readonly List<BlackHoleBehaviour> holes = new List<BlackHoleBehaviour>();
        private bool resolving, draining;
        public int ActiveBlackHoles => holes.Count;

        private void OnEnable()
        {
            pipeline = GetComponent<OrganPipelineController>();
            pipeline.AttackProduced += Resolve;
            EnemyEvents.Killed += Killed;
        }

        private bool Eligible(EnemyRuntime enemy) => enemy != null && enemy.isActiveAndEnabled
            && enemy.IsAlive && enemy.gameObject.scene == gameObject.scene;

        public void Resolve(CastEvent attack)
        {
            if (!isActiveAndEnabled || attack.Context == null) return;
            if (attack.Shape == AttackShape.BlackHole)
            {
                if (!attack.EffectCenter.HasValue) return;
                holes.RemoveAll(h => h.Cause.OriginInstanceId == attack.OriginInstanceId);
                holes.Add(new BlackHoleBehaviour(attack));
                return;
            }
            var targets = new List<AttackTarget>();
            var enemies = new Dictionary<string, EnemyRuntime>();
            foreach (var enemy in EnemyRuntime.ActiveEnemies)
                if (Eligible(enemy))
                {
                    targets.Add(new AttackTarget(enemy.TargetId, enemy.Position, true));
                    enemies[enemy.TargetId] = enemy;
                }
            var hits = AttackResolver.Resolve(attack, targets, out _);
            resolving = true;
            try
            {
                foreach (var hit in hits)
                {
                    var enemy = enemies[hit.TargetId];
                    if (!Eligible(enemy) || enemy.TargetId != hit.TargetId) continue;
                    if (enemy.TakeAttack(hit) <= 0 || !Eligible(enemy) || enemy.TargetId != hit.TargetId) continue;
                    if (!states.TryGetValue(hit.TargetId, out var state))
                        states.Add(hit.TargetId, state = new TargetState { Enemy = enemy, Id = hit.TargetId });
                    if (state.Status.TryApply(hit, Random.value))
                    {
                        enemy.OrganSpeedMultiplier = state.Status.SpeedMultiplier;
                        enemy.ApplyStatus(hit.Status.Kind.ToString(), hit.Status.Kind == StatusEffectKind.Slow
                            ? state.Status.SlowStacks : 1, hit.Status.Duration);
                    }
                }
            }
            finally { resolving = false; }
            DrainKills();
        }

        private void Killed(EnemyEvents.KilledInfo info)
        {
            if (info.enemy == null || info.enemy.scene != gameObject.scene || !info.organAttack.HasValue) return;
            kills.Enqueue(new TriggerSignal(TriggerOn.Kill, info.organAttack.Value, info.deathPoint));
            if (!resolving) DrainKills();
        }

        private void DrainKills()
        {
            if (draining) return;
            draining = true;
            try { while (kills.Count > 0) pipeline.PublishSignal(kills.Dequeue()); }
            finally { draining = false; }
        }

        public void Advance(float deltaTime)
        {
            if (!isActiveAndEnabled) return;
            // 本帧新增的状态不提前结算；死亡通知在整个状态批次后发送。
            resolving = true;
            try
            {
                foreach (var state in new List<TargetState>(states.Values))
                {
                    if (!state.Valid) { states.Remove(state.Id); continue; }
                    state.Status.Tick(deltaTime, hit => state.Enemy.TakeAttack(hit), () => state.Valid);
                    if (state.Valid) state.Enemy.OrganSpeedMultiplier = state.Status.SpeedMultiplier;
                    else states.Remove(state.Id);
                }
            }
            finally { resolving = false; }
            DrainKills();
            foreach (var hole in holes)
            {
                foreach (var enemy in EnemyRuntime.ActiveEnemies)
                {
                    if (!Eligible(enemy) || enemy.Data == null || !enemy.Data.CanBeDisplaced) continue;
                    var position = hole.PullPosition(enemy.Position, deltaTime);
                    var body = enemy.GetComponent<Rigidbody2D>();
                    if (body != null) body.position = position;
                    else enemy.transform.position = new Vector3(position.x, position.y, enemy.transform.position.z);
                }
                hole.Tick(deltaTime);
            }
            holes.RemoveAll(h => !h.IsActive);
        }

        private void FixedUpdate() => Advance(Time.fixedDeltaTime);
        public void Clear()
        {
            foreach (var state in states.Values)
                if (state.Valid) state.Enemy.OrganSpeedMultiplier = 1f;
            states.Clear(); holes.Clear(); kills.Clear();
        }
        private void OnDisable()
        {
            if (pipeline != null) pipeline.AttackProduced -= Resolve;
            EnemyEvents.Killed -= Killed;
            Clear();
        }
    }
}
