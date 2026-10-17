using System;
using System.Collections.Generic;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tideflesh.Systems
{
    public sealed class BattleActuatorWorld : IEffectWorld
    {
        private readonly Scene scene;
        private readonly Action<CastEvent> damage;
        public Func<CastEvent, float> MeasuredDamage { get; set; }
        public Action<CastEvent> DebuffApplied { get; set; }
        public Vector2? OwnerPosition => PlayerLocator.Player != null && PlayerLocator.Player.gameObject.scene == scene
            ? (Vector2?)PlayerLocator.Player.position : null;
        public IReadOnlyList<AttackTarget> Targets
        {
            get
            {
                var values = new List<AttackTarget>();
                foreach (var enemy in EnemyRuntime.ActiveEnemies)
                    if (enemy != null && enemy.isActiveAndEnabled && enemy.IsAlive && enemy.gameObject.scene == scene)
                        values.Add(new AttackTarget(enemy.TargetId, enemy.Position, true));
                return values;
            }
        }
        public float Damage(CastEvent hit)
        {
            if (MeasuredDamage != null) return MeasuredDamage(hit);
            float before = 0; EnemyRuntime found = null;
            foreach (var enemy in EnemyRuntime.ActiveEnemies) if (enemy != null && enemy.TargetId == hit.TargetId && enemy.gameObject.scene == scene) { found = enemy; before = enemy.CurrentHealth; break; }
            damage(hit);
            return Mathf.Max(0, before - (found == null ? 0 : found.CurrentHealth));
        }
        public IReadOnlyList<AttackTarget> BeamTargets(Vector2 from, Vector2 to, float radius)
        {
            Physics2D.SyncTransforms();
            var result = new List<AttackTarget>(); var seen = new HashSet<string>();
            void Add(Collider2D collider)
            {
                var enemy = collider == null ? null : collider.GetComponentInParent<EnemyRuntime>();
                if (enemy != null && enemy.isActiveAndEnabled && enemy.IsAlive && enemy.gameObject.scene == scene && seen.Add(enemy.TargetId))
                    result.Add(new AttackTarget(enemy.TargetId, enemy.Position, true));
            }
            foreach (var collider in Physics2D.OverlapCircleAll(from, radius)) Add(collider);
            Vector2 delta = to - from;
            if (delta.sqrMagnitude > .000001f)
                foreach (var hit in Physics2D.CircleCastAll(from, radius, delta.normalized, delta.magnitude)) Add(hit.collider);
            return result;
        }
        public void NotifyDebuff(CastEvent cause) => DebuffApplied?.Invoke(cause);
        public float Heal(float amount, CastEvent? cause = null)
        {
            var player = OwnerPosition.HasValue ? PlayerLocator.Player.GetComponent<PlayerRuntime>() : null;
            return player != null && player.CurrentHealth > 0 ? player.Heal(amount, cause) : 0;
        }
        private sealed class SlowLease : IDisposable
        {
            private EnemyRuntime enemy; private readonly string target, source;
            public SlowLease(EnemyRuntime enemy, string source, float multiplier)
            { this.enemy = enemy; target = enemy.TargetId; this.source = source; enemy.SetAttachedSlow(source, multiplier); }
            public void Dispose() { if (enemy != null && enemy.TargetId == target) enemy.RemoveAttachedSlow(source); enemy = null; }
        }
        public IDisposable Slow(string targetId, string sourceId, float multiplier, CastEvent? cause = null)
        {
            foreach (var enemy in EnemyRuntime.ActiveEnemies)
                if (enemy != null && enemy.IsAlive && enemy.TargetId == targetId && enemy.gameObject.scene == scene)
                    { var lease = new SlowLease(enemy, sourceId, multiplier);
                      
                      return lease; }
            return null;
        }
        public BattleActuatorWorld(Scene scene, Action<CastEvent> damage)
        { this.scene = scene; this.damage = damage; }
        public bool Sweep(Vector2 from, Vector2 to, float radius, out ActuatorHit hit)
            => SweepInternal(from, to, radius, false, out hit);
        public bool SweepWall(Vector2 from, Vector2 to, float radius, out ActuatorHit hit)
            => SweepInternal(from, to, radius, true, out hit);
        private bool SweepInternal(Vector2 from, Vector2 to, float radius, bool wallsOnly, out ActuatorHit hit)
        {
            Physics2D.SyncTransforms();
            float nearest = float.PositiveInfinity;
            string targetId = null;
            bool found = false, wallFirst = false;
            Vector2 delta = to - from;
            void Consider(Collider2D collider, float distance)
            {
                if (collider == null || collider.gameObject.scene != scene || !collider.enabled) return;
                var enemy = collider.GetComponentInParent<EnemyRuntime>();
                bool wall = false;
                string id = null;
                if (enemy != null)
                {
                    if (wallsOnly) return;
                    if (!enemy.isActiveAndEnabled || !enemy.IsAlive) return;
                    id = enemy.TargetId;
                }
                else
                {
                    if (collider.isTrigger || collider.GetComponentInParent<PlayerRuntime>() != null) return;
                    // 兼容旧统一场景 Default 层静态边界和新版专用墙层。
                    wall = collider.gameObject.layer == LayerMask.NameToLayer(BattleLayers.Wall)
                        || collider.attachedRigidbody == null || collider.attachedRigidbody.bodyType == RigidbodyType2D.Static;
                    if (!wall) return;
                }
                if (distance > nearest || (distance == nearest && wallFirst)) return;
                nearest = distance; targetId = id; wallFirst = wall; found = true;
            }
            foreach (var collider in Physics2D.OverlapCircleAll(from, radius)) Consider(collider, 0);
            if (delta.sqrMagnitude > .0000001f)
                foreach (var result in Physics2D.CircleCastAll(from, radius, delta.normalized, delta.magnitude))
                    Consider(result.collider, result.distance);
            hit = found ? new ActuatorHit(targetId, from + delta.normalized * nearest) : default;
            return found;
        }
        public void ApplyHit(CastEvent hit) => damage(hit);
    }
}
