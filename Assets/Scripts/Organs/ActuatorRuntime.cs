using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    public enum ActuatorEndReason { None, Hit, Blocked, Expired, Cancelled }

    public sealed class ActuatorContext
    {
        public string ExecutionId { get; } = Guid.NewGuid().ToString("N");
        public CastEvent Cause { get; }
        public BodyPart SourcePart { get; }
        public int SourceSlotIndex { get; }
        public Vector2 SpawnPosition { get; }
        public Vector2 Direction { get; }
        public StraightProjectileSettings Projectile { get; }
        public ActuatorContext(CastEvent cause, BodyPart part, int slot, Vector2 position,
            Vector2 direction, StraightProjectileSettings projectile)
        {
            if (cause.Context == null) throw new ArgumentException("缺少攻击来源。", nameof(cause));
            if (!Enum.IsDefined(typeof(BodyPart), part)) throw new ArgumentOutOfRangeException(nameof(part));
            if (slot < 0 || slot >= BodyRuntime.SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!CastEvent.IsFinite(position) || !CastEvent.IsFinite(direction) || direction.sqrMagnitude < .000001f)
                throw new ArgumentException("无效起点或方向。");
            Cause = cause; SourcePart = part; SourceSlotIndex = slot;
            SpawnPosition = position; Direction = direction.normalized;
            Projectile = projectile ?? throw new ArgumentNullException(nameof(projectile));
        }
        public CastEvent Hit(string targetId, Vector2 position) => new CastEvent(targetId, Cause.Damage,
            Cause.OriginDefinitionId, Cause.OriginInstanceId, Cause.Context, Cause.Status, effectCenter: position);
    }

    public readonly struct ActuatorHit
    {
        // TargetId 为 null 表示墙体阻挡；Position 是碰撞时子弹中心。
        public string TargetId { get; }
        public Vector2 Position { get; }
        public ActuatorHit(string targetId, Vector2 position) { TargetId = targetId; Position = position; }
    }

    public interface IActuatorWorld
    {
        bool Sweep(Vector2 from, Vector2 to, float radius, out ActuatorHit hit);
        void ApplyHit(CastEvent hit);
    }

    public interface IOrganActuator
    {
        IActuatorExecution Create(ActuatorContext context, IActuatorWorld world);
    }

    public interface IActuatorExecution
    {
        ActuatorContext Context { get; }
        Vector2 Position { get; }
        bool IsFinished { get; }
        ActuatorEndReason EndReason { get; }
        void Tick(float deltaTime);
        void Cancel();
    }

    public sealed class StraightProjectileActuator : IOrganActuator
    {
        public IActuatorExecution Create(ActuatorContext context, IActuatorWorld world)
            => EffectExecutionFactory.Create(context, world);
    }

    // 一次执行一个实例；没有 MonoBehaviour，也不读取按键、充能或全局时间。
    public sealed class StraightProjectileExecution : IActuatorExecution
    {
        private readonly IActuatorWorld world;
        private float remaining;
        public ActuatorContext Context { get; }
        public Vector2 Position { get; private set; }
        public ActuatorEndReason EndReason { get; private set; }
        public bool IsFinished => EndReason != ActuatorEndReason.None;
        public StraightProjectileExecution(ActuatorContext context, IActuatorWorld world)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            Position = context.SpawnPosition; remaining = context.Projectile.Lifetime;
        }
        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (IsFinished || deltaTime == 0) return;
            float step = Mathf.Min(deltaTime, remaining);
            Vector2 next = Position + Context.Direction * (Context.Projectile.Speed * step);
            if (world.Sweep(Position, next, Context.Projectile.HitRadius, out var hit))
            {
                Position = hit.Position;
                // 先终止再扣血，避免击杀追加/清场回调重入本发子弹。
                EndReason = hit.TargetId == null ? ActuatorEndReason.Blocked : ActuatorEndReason.Hit;
                if (hit.TargetId != null) world.ApplyHit(Context.Hit(hit.TargetId, Position));
                return;
            }
            Position = next; remaining -= step;
            if (remaining <= 0) EndReason = ActuatorEndReason.Expired;
        }
        public void Cancel() { if (!IsFinished) EndReason = ActuatorEndReason.Cancelled; }
    }

    public sealed class ActuatorRunner
    {
        private readonly List<IActuatorExecution> active = new List<IActuatorExecution>();
        public IReadOnlyList<IActuatorExecution> Active { get; }
        public event Action<IActuatorExecution> Started;
        public event Action<IActuatorExecution> Ended;
        public ActuatorRunner() { Active = active.AsReadOnly(); }
        public void Start(IOrganActuator actuator, ActuatorContext context, IActuatorWorld world)
        {
            var execution = actuator.Create(context, world);
            active.Add(execution); Started?.Invoke(execution);
        }
        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            foreach (var execution in active.ToArray())
            {
                execution.Tick(deltaTime);
                if (execution.IsFinished && active.Remove(execution)) Ended?.Invoke(execution);
            }
        }
        public void Clear()
        {
            var snapshot = active.ToArray(); active.Clear();
            foreach (var execution in snapshot) { execution.Cancel(); Ended?.Invoke(execution); }
        }
    }

    // 预览提供圆形目标；实战使用 Physics2D 的实际碰撞形状。
    public static class ProjectileGeometry
    {
        public static bool SegmentCircle(Vector2 from, Vector2 to, Vector2 center, float radius, out float fraction)
        {
            fraction = 0;
            Vector2 offset = from - center, delta = to - from;
            float c = offset.sqrMagnitude - radius * radius;
            if (c <= 0) return true;
            float a = delta.sqrMagnitude, b = Vector2.Dot(offset, delta);
            if (a <= .0000001f || b > 0) return false;
            float discriminant = b * b - a * c;
            if (discriminant < 0) return false;
            fraction = (-b - Mathf.Sqrt(discriminant)) / a;
            return fraction >= 0 && fraction <= 1;
        }
    }
}
