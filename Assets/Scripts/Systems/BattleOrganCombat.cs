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

        private static readonly HashSet<BattleOrganCombat> activeCombats = new HashSet<BattleOrganCombat>();
        private OrganPipelineController pipeline;
        private readonly Dictionary<string, TargetState> states = new Dictionary<string, TargetState>();
        private readonly Queue<TriggerSignal> kills = new Queue<TriggerSignal>();
        private readonly List<BlackHoleBehaviour> holes = new List<BlackHoleBehaviour>();
        private readonly ActuatorRunner actuators = new ActuatorRunner();
        private readonly IOrganActuator straightProjectile = new StraightProjectileActuator();
        private BattleActuatorWorld actuatorWorld;
        public ActuatorRunner Actuators => actuators;
        private bool resolving, draining, clearing;
        private PlayerRuntime triggerPlayer;
        public TorsoEquipmentRuntime Torso { get; set; }
        public OrganCombatReport Report { get; set; }
        private CastEvent ScaleTorsoDamage(CastEvent hit, EnemyRuntime enemy)
        {
            float scale = triggerPlayer != null ? triggerPlayer.Stats.DamageMultiplier : 1;
            if (Torso != null && triggerPlayer != null)
                scale *= Torso.Loadout.FrontMultiplier(triggerPlayer.transform.position, triggerPlayer.transform.right, enemy.Position);
            return TorsoLoadout.ScaleDamage(hit, scale);
        }
        private void ApplyTorsoHealing(float actual, CastEvent hit)
        {
            if (actual > 0 && Torso != null && triggerPlayer != null && triggerPlayer.CurrentHealth > 0)
                triggerPlayer.Heal(actual * Torso.Loadout.LifeSteal, hit);
        }
        [SerializeField, Min(1)] private float criticalMultiplier = 2f;
        private void BindTriggerPlayer()
        {
            var found = PlayerLocator.Player != null && PlayerLocator.Player.gameObject.scene == gameObject.scene
                ? PlayerLocator.Player.GetComponent<PlayerRuntime>() : null;
            if (found == triggerPlayer) return;
            UnbindTriggerPlayer(); triggerPlayer = found;
            if (found != null) { found.PlayerDamaged += OwnerHurt; found.PlayerHealed += OwnerHealed; }
        }
        private void UnbindTriggerPlayer()
        {
            if (triggerPlayer != null) { triggerPlayer.PlayerDamaged -= OwnerHurt; triggerPlayer.PlayerHealed -= OwnerHealed; }
            triggerPlayer = null;
        }
        private void OwnerHurt(float amount) { if (triggerPlayer != null && triggerPlayer.CurrentHealth > 0) pipeline.NotifyOwner(TriggerOn.Hurt); }
        private void OwnerHealed(float amount, CastEvent? cause)
        {
            if (resolving && cause.HasValue) kills.Enqueue(new TriggerSignal(TriggerOn.Heal, cause.Value));
            else pipeline.NotifyOwner(TriggerOn.Heal, cause);
        }
        private void ExecutionStarted(IActuatorExecution e)
        {
            if (e is SummonExecution summon) summon.Damaged += SummonHurt;
            pipeline.NotifyExecutionStarted(e.Context.Cause);
        }
        private void SummonHurt(SummonExecution summon, float amount)
        {
            var source = summon.Context.Cause;
            var cause = new CastEvent("owner", 0, source.OriginDefinitionId, source.OriginInstanceId,
                summon.NewActorContext(), effectCenter: summon.Position, allowZeroDamage: true);
            pipeline.PublishSignal(new TriggerSignal(TriggerOn.Hurt, cause, summon.Position));
        }
        private void ExecutionEnded(IActuatorExecution e) { if (e is SummonExecution summon) summon.Damaged -= SummonHurt; if (!clearing) pipeline.NotifyExecutionEnded(e.Context.Cause, e.EndReason == ActuatorEndReason.Cancelled); }
        public int ActiveBlackHoles => holes.Count;

        private void OnEnable()
        {
            activeCombats.Add(this);
            pipeline = GetComponent<OrganPipelineController>();
            pipeline.AttackProduced += Resolve;
            actuatorWorld = new BattleActuatorWorld(gameObject.scene, Resolve) { MeasuredDamage = ResolveDamage, DebuffApplied = hit => pipeline.PublishSignal(new TriggerSignal(TriggerOn.Debuff, hit, hit.EffectCenter)) };
            pipeline.ActuatorProduced += ExecuteActuator;
            EnemyEvents.Killed += Killed;
            actuators.Started += ExecutionStarted; actuators.Ended += ExecutionEnded;
            BindTriggerPlayer();
        }

        private bool Eligible(EnemyRuntime enemy) => enemy != null && enemy.isActiveAndEnabled
            && enemy.IsAlive && enemy.gameObject.scene == gameObject.scene;

        private void ExecuteActuator(ActuatorContext context)
        {
            if (context.Projectile is PatternActuatorSettings settings && settings.Pattern == ActuatorPattern.Summon)
                foreach (var active in actuators.Active)
                    if (active is SummonExecution && !active.IsFinished
                        && active.Context.Cause.OriginInstanceId == context.Cause.OriginInstanceId) return;
            var world = context.Cause.Context.ActorPosition == null ? actuatorWorld
                : new BattleActuatorWorld(gameObject.scene, Resolve) { MeasuredDamage = ResolveDamage,
                    DebuffApplied = actuatorWorld.DebuffApplied, ActorPosition = context.Cause.Context.ActorPosition };
            actuators.Start(straightProjectile, context, world);
        }

        public void Resolve(CastEvent attack)
            => ResolveDamage(attack);

        public float ResolveDamage(CastEvent attack)
        {
            if (!isActiveAndEnabled || attack.Context == null) return 0;
            if (attack.Shape == AttackShape.BlackHole)
            {
                if (!attack.EffectCenter.HasValue) return 0;
                holes.RemoveAll(h => h.Cause.OriginInstanceId == attack.OriginInstanceId);
                holes.Add(new BlackHoleBehaviour(attack));
                return 0;
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
            float actualDamage = 0;
            resolving = true;
            try
            {
                foreach (var rawHit in hits)
                {
                    var hit = CriticalDamage.Roll(ScaleTorsoDamage(rawHit, enemies[rawHit.TargetId]), triggerPlayer != null ? triggerPlayer.Stats.CritChance : 0,
                        criticalMultiplier, Random.value, out bool critical);
                    var enemy = enemies[hit.TargetId];
                    if (!Eligible(enemy) || enemy.TargetId != hit.TargetId) continue;
                    float actual = enemy.TakeAttack(hit); actualDamage += actual;
                    Report?.RecordDamage(hit, actual);
                    ApplyTorsoHealing(actual, hit);
                    if (actual > 0)
                    {
                        kills.Enqueue(new TriggerSignal(TriggerOn.Hit, hit, enemy.Position));
                        if (critical) kills.Enqueue(new TriggerSignal(TriggerOn.Critical, hit, enemy.Position));
                    }
                    if (actual <= 0 || !Eligible(enemy) || enemy.TargetId != hit.TargetId) continue;
                    if (!states.TryGetValue(hit.TargetId, out var state))
                        states.Add(hit.TargetId, state = new TargetState { Enemy = enemy, Id = hit.TargetId });
                    if (state.Status.TryApply(hit, Random.value, Torso?.Loadout.Probability ?? 1))
                    {
                        kills.Enqueue(new TriggerSignal(TriggerOn.Debuff, hit, enemy.Position));
                        enemy.OrganSpeedMultiplier = state.Status.SpeedMultiplier;
                        enemy.ApplyStatus(hit.Status.Kind.ToString(), hit.Status.Kind == StatusEffectKind.Slow
                            ? state.Status.SlowStacks : 1, hit.Status.Duration);
                    }
                }
            }
            finally { resolving = false; }
            DrainKills();
            return actualDamage;
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
            BindTriggerPlayer();
            pipeline.AdvanceTriggers(deltaTime);
            // 本帧新增的状态不提前结算；死亡通知在整个状态批次后发送。
            resolving = true;
            try
            {
                foreach (var state in new List<TargetState>(states.Values))
                {
                    if (!state.Valid) { states.Remove(state.Id); continue; }
                    state.Status.Tick(deltaTime, hit =>
                    {
                        var scaled = ScaleTorsoDamage(hit, state.Enemy);
                        float actual = state.Enemy.TakeAttack(scaled);
                        Report?.RecordDamage(scaled, actual); ApplyTorsoHealing(actual, scaled);
                    }, () => state.Valid);
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
            foreach (var hole in holes.ToArray()) if (!hole.IsActive) { holes.Remove(hole); pipeline.NotifyExecutionEnded(hole.Cause); }
            actuators.Advance(deltaTime);
        }

        // Return the nearest living companion in the projectile's scene; caller compares it with the player.
        public static SummonExecution SweepSummons(UnityEngine.SceneManagement.Scene scene, Vector2 from, Vector2 to,
            float radius, out float fraction)
        {
            fraction = float.PositiveInfinity;
            SummonExecution nearest = null;
            foreach (var combat in activeCombats)
            {
                if (combat == null || !combat.isActiveAndEnabled || combat.gameObject.scene != scene) continue;
                foreach (var execution in combat.actuators.Active)
                    if (execution is SummonExecution summon && !summon.IsFinished
                        && ProjectileGeometry.SegmentCircle(from, to, summon.Position, radius + summon.HitRadius, out float t) && t < fraction)
                    { nearest = summon; fraction = t; }
            }
            return nearest;
        }

        private void FixedUpdate() => Advance(Time.fixedDeltaTime);
        public void Clear()
        {
            clearing = true;
            try { actuators.Clear(); } finally { clearing = false; }
            pipeline?.ResetTriggers();
            foreach (var state in states.Values)
                if (state.Valid) state.Enemy.OrganSpeedMultiplier = 1f;
            states.Clear(); holes.Clear(); kills.Clear();
        }
        private void OnDisable()
        {
            activeCombats.Remove(this);
            if (pipeline != null) pipeline.AttackProduced -= Resolve;
            if (pipeline != null) pipeline.ActuatorProduced -= ExecuteActuator;
            EnemyEvents.Killed -= Killed;
            actuators.Started -= ExecutionStarted; actuators.Ended -= ExecutionEnded;
            UnbindTriggerPlayer();
            Clear();
        }
    }
}
