using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    // 世界适配负责实际伤害、生命上限与各来源的减速贡献，执行器不直接修改场景对象。
    public interface IEffectWorld : IActuatorWorld
    {
        Vector2? OwnerPosition { get; }
        IReadOnlyList<AttackTarget> Targets { get; }
        float Damage(CastEvent hit);
        float Heal(float amount, CastEvent? cause = null);
        IDisposable Slow(string targetId, string sourceId, float multiplier, CastEvent? cause = null);
        void NotifyDebuff(CastEvent cause);
        bool SweepWall(Vector2 from, Vector2 to, float radius, out ActuatorHit hit);
        IReadOnlyList<AttackTarget> BeamTargets(Vector2 from, Vector2 to, float radius);
    }
    public readonly struct EffectVisual
    {
        public readonly Vector2 From, To;
        public readonly float Radius;
        public readonly Color Color;
        public readonly bool Line;
        public EffectVisual(Vector2 from, Vector2 to, float radius, Color color, bool line = false)
        { From = from; To = to; Radius = radius; Color = color; Line = line; }
    }
    public interface IEffectVisuals { void GetVisuals(List<EffectVisual> output); }

    public abstract class EffectExecution : IActuatorExecution, IEffectVisuals
    {
        public ActuatorContext Context { get; }
        public Vector2 Position { get; protected set; }
        public ActuatorEndReason EndReason { get; protected set; }
        public bool IsFinished => EndReason != ActuatorEndReason.None;
        protected readonly IEffectWorld World;
        protected readonly PatternActuatorSettings Settings;
        protected float Age;
        protected virtual float Duration => Settings.Lifetime;
        protected EffectExecution(ActuatorContext context, IEffectWorld world)
        { Context = context; World = world; Settings = (PatternActuatorSettings)context.Projectile; Position = context.SpawnPosition; }
        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // 限制单段移动和旋转角度；曲线也做逐段扫掠，不能用一条跨圆心的长弦替代轨迹。
            while (!IsFinished && deltaTime > .000001f)
            {
                float step = Mathf.Min(deltaTime, Mathf.Min(.02f, Mathf.Min(5 / Settings.AngularSpeed, Duration - Age)));
                if (step <= .000001f) { Finish(ActuatorEndReason.Expired); break; }
                Age += step; deltaTime -= step; Advance(step);
                if (!IsFinished && Age >= Duration - .000001f) Finish(ActuatorEndReason.Expired);
            }
        }
        protected abstract void Advance(float dt);
        protected virtual void Finish(ActuatorEndReason reason) { if (!IsFinished) EndReason = reason; }
        public void Cancel() => Finish(ActuatorEndReason.Cancelled);
        protected bool Find(string id, out AttackTarget target)
        {
            foreach (var candidate in World.Targets)
                if (candidate.IsAlive && candidate.Id == id) { target = candidate; return true; }
            target = default; return false;
        }
        protected float Hit(string id, Vector2 point, float? damage = null)
        {
            float amount = damage ?? Context.Cause.Damage;
            if (amount <= 0) return 0;
            var cause = Context.Cause;
            return World.Damage(new CastEvent(id, amount, cause.OriginDefinitionId, cause.OriginInstanceId,
                cause.Context, cause.Status, effectCenter: point));
        }
        protected void Area(float radius, float damage)
        {
            // 快照后再结算，击杀回调可以改变世界或取消本次执行。
            var targets = new List<AttackTarget>(World.Targets);
            foreach (var target in targets)
            {
                if (EndReason == ActuatorEndReason.Cancelled) break;
                if (target.IsAlive && (target.Position - Position).sqrMagnitude <= radius * radius) Hit(target.Id, target.Position, damage);
            }
        }
        public virtual void GetVisuals(List<EffectVisual> output)
        { if (!IsFinished) output.Add(new EffectVisual(Position, Position, Settings.HitRadius, Color.yellow)); }
        protected static Vector2 Rotate(Vector2 direction, float angle)
        { float r = angle * Mathf.Deg2Rad; return new Vector2(direction.x * Mathf.Cos(r) - direction.y * Mathf.Sin(r), direction.x * Mathf.Sin(r) + direction.y * Mathf.Cos(r)); }
    }

    // 环绕、螺旋、追踪、吸血针、导弹、孢子共用首次碰撞和结束处理。
    public sealed class MovingEffectExecution : EffectExecution
    {
        private Vector2 direction;
        private bool exploded;
        private float explosionAge;
        public MovingEffectExecution(ActuatorContext context, IEffectWorld world) : base(context, world)
        {
            direction = context.Direction;
            if (Settings.Pattern == ActuatorPattern.Orbit) Position += direction * Settings.OrbitRadius;
        }
        protected override void Advance(float dt)
        {
            if (exploded) { explosionAge += dt; if (explosionAge >= .15f) Finish(ActuatorEndReason.Hit); return; }
            Vector2 next = Position;
            switch (Settings.Pattern)
            {
                case ActuatorPattern.Orbit:
                    if (!World.OwnerPosition.HasValue) { Cancel(); return; }
                    next = World.OwnerPosition.Value + Rotate(Context.Direction, Settings.AngularSpeed * Age) * Settings.OrbitRadius;
                    break;
                case ActuatorPattern.Spiral:
                    next = Context.SpawnPosition + Context.Direction * (Settings.Speed * Age)
                        + (Rotate(Context.Direction, Settings.AngularSpeed * Age) - Context.Direction) * Settings.OrbitRadius;
                    break;
                case ActuatorPattern.Homing:
                    if (Find(Context.Cause.TargetId, out var target))
                    {
                        Vector2 desired = (target.Position - Position).normalized;
                        float angle = Vector2.SignedAngle(direction, desired);
                        direction = Rotate(direction, Mathf.Clamp(angle, -Settings.AngularSpeed * dt, Settings.AngularSpeed * dt));
                    }
                    next += direction * (Settings.Speed * dt); break;
                case ActuatorPattern.Spore:
                    float moving = Mathf.Clamp(Settings.DriftDuration - (Age - dt), 0, dt);
                    next += direction * (Settings.Speed * moving); break;
                default: next += direction * (Settings.Speed * dt); break;
            }
            if (!World.Sweep(Position, next, Settings.HitRadius, out var hit)) { Position = next; return; }
            Position = hit.Position;
            if (hit.TargetId == null) { Finish(ActuatorEndReason.Blocked); return; }
            if (Settings.Pattern == ActuatorPattern.Missile)
            {
                exploded = true; // 先标记，伤害回调不能让本弹再次命中。
                Hit(hit.TargetId, Position);
                if (!IsFinished) Area(Settings.AreaRadius, Settings.ExplosionDamage);
            }
            else
            {
                Finish(ActuatorEndReason.Hit);
                float actual = Hit(hit.TargetId, Position);
                if (Settings.Pattern == ActuatorPattern.Vampire) World.Heal(actual * Settings.LifeSteal, Context.Cause);
            }
        }
        public override void GetVisuals(List<EffectVisual> output)
        {
            if (!IsFinished) output.Add(new EffectVisual(Position, Position, exploded ? Settings.AreaRadius : Settings.HitRadius,
                exploded ? new Color(1, .4f, .1f) : Settings.Pattern == ActuatorPattern.Spore ? Color.green : Color.yellow));
        }
    }

    public sealed class TripleProjectileExecution : EffectExecution
    {
        private readonly List<IActuatorExecution> shots = new List<IActuatorExecution>();
        public TripleProjectileExecution(ActuatorContext context, IEffectWorld world) : base(context, world)
        {
            for (int i = -1; i <= 1; i++) shots.Add(new StraightProjectileExecution(new ActuatorContext(context.Cause,
                context.SourcePart, context.SourceSlotIndex, context.SpawnPosition, Rotate(context.Direction, i * Settings.SpreadAngle / 2),
                new StraightProjectileSettings(Settings.Speed, Settings.HitRadius, Settings.Lifetime)), world));
        }
        protected override void Advance(float dt)
        {
            bool done = true;
            foreach (var shot in shots) { if (IsFinished) break; shot.Tick(dt); done &= shot.IsFinished; }
            if (done && !IsFinished) Finish(ActuatorEndReason.Expired);
        }
        protected override void Finish(ActuatorEndReason reason)
        { base.Finish(reason); foreach (var shot in shots) shot.Cancel(); }
        public override void GetVisuals(List<EffectVisual> output)
        { foreach (var shot in shots) if (!shot.IsFinished) output.Add(new EffectVisual(shot.Position, shot.Position, Settings.HitRadius, Color.yellow)); }
    }

    public sealed class BeamExecution : EffectExecution
    {
        private bool fired;
        private Vector2 end;
        private float Charge => Settings.Pattern == ActuatorPattern.ChargedLaser ? Settings.ChargeTime : 0;
        protected override float Duration => Charge + .2f;
        public BeamExecution(ActuatorContext context, IEffectWorld world) : base(context, world)
        { end = Position + Context.Direction * Settings.Range; }
        protected override void Advance(float dt)
        {
            if (fired || Age < Charge) return;
            fired = true;
            if (World.SweepWall(Position, end, Settings.HitRadius, out var wall)) end = wall.Position;
            var targets = World.BeamTargets(Position, end, Settings.HitRadius);
            foreach (var target in targets)
            {
                if (IsFinished) break;
                if (target.IsAlive) Hit(target.Id, target.Position);
            }
        }
        public override void GetVisuals(List<EffectVisual> output)
        { if (!IsFinished) output.Add(new EffectVisual(Position, end, fired ? Settings.HitRadius : .025f, fired ? Color.cyan : Color.gray, true)); }
    }

    public sealed class FogExecution : EffectExecution
    {
        private float nextTick;
        public FogExecution(ActuatorContext context, IEffectWorld world) : base(context, world) { }
        protected override void Advance(float dt)
        { if (Age >= nextTick) { nextTick += Settings.Interval; Area(Settings.AreaRadius, Context.Cause.Damage); } }
        public override void GetVisuals(List<EffectVisual> output)
        { if (!IsFinished) output.Add(new EffectVisual(Position, Position, Settings.AreaRadius, new Color(.3f, 1, .3f, .6f))); }
    }

    public sealed class SuckerExecution : EffectExecution
    {
        private string targetId;
        private IDisposable slow;
        private float nextTick;
        public SuckerExecution(ActuatorContext context, IEffectWorld world) : base(context, world) { }
        protected override void Advance(float dt)
        {
            if (targetId == null)
            {
                Vector2 next = Position + Context.Direction * (Settings.Speed * dt);
                if (!World.Sweep(Position, next, Settings.HitRadius, out var hit)) { Position = next; return; }
                Position = hit.Position;
                if (hit.TargetId == null) { Finish(ActuatorEndReason.Blocked); return; }
                targetId = hit.TargetId;
                slow = World.Slow(targetId, Context.ExecutionId, Settings.SlowMultiplier, Context.Cause);
                if (slow != null && Settings.SlowMultiplier < 1) World.NotifyDebuff(Context.Hit(targetId, Position));
                if (IsFinished) return;
                nextTick = Age;
            }
            if (!Find(targetId, out var target)) { Finish(ActuatorEndReason.Expired); return; }
            Position = target.Position;
            if (Age >= nextTick) { nextTick += Settings.Interval; Hit(targetId, Position); }
            if (!IsFinished && !Find(targetId, out _)) Finish(ActuatorEndReason.Expired);
        }
        protected override void Finish(ActuatorEndReason reason)
        { base.Finish(reason); slow?.Dispose(); slow = null; }
        public override void GetVisuals(List<EffectVisual> output)
        { if (!IsFinished) output.Add(new EffectVisual(Position, Position, targetId == null ? Settings.HitRadius : .3f, Color.magenta)); }
    }

    public sealed class SummonExecution : EffectExecution
    {
        public float Health { get; private set; }
        protected override float Duration => Settings.Health / Settings.HealthDecay;
        public SummonExecution(ActuatorContext context, IEffectWorld world) : base(context, world) { Health = Settings.Health; }
        public void TakeDamage(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (IsFinished) return;
            Health = Mathf.Max(0, Health - amount); if (Health <= .00001f) Finish(ActuatorEndReason.Expired);
        }
        protected override void Advance(float dt)
        {
            if (!World.OwnerPosition.HasValue) { Cancel(); return; }
            Vector2 destination = World.OwnerPosition.Value - Context.Direction * Settings.FollowDistance;
            Position = Vector2.MoveTowards(Position, destination, Settings.Speed * dt);
            TakeDamage(Settings.HealthDecay * dt);
        }
        public override void GetVisuals(List<EffectVisual> output)
        { if (!IsFinished) output.Add(new EffectVisual(Position, Position, .3f, Color.Lerp(Color.red, Color.cyan, Health / Settings.Health))); }
    }

    public static class EffectExecutionFactory
    {
        public static IActuatorExecution Create(ActuatorContext context, IActuatorWorld world)
        {
            if (!(context.Projectile is PatternActuatorSettings settings)) return new StraightProjectileExecution(context, world);
            var effects = world as IEffectWorld ?? throw new ArgumentException("世界未提供执行器效果接口。");
            switch (settings.Pattern)
            {
                case ActuatorPattern.Triple: return new TripleProjectileExecution(context, effects);
                case ActuatorPattern.Laser: case ActuatorPattern.ChargedLaser: return new BeamExecution(context, effects);
                case ActuatorPattern.Fog: return new FogExecution(context, effects);
                case ActuatorPattern.Sucker: return new SuckerExecution(context, effects);
                case ActuatorPattern.Summon: return new SummonExecution(context, effects);
                default: return new MovingEffectExecution(context, effects);
            }
        }
    }
}
