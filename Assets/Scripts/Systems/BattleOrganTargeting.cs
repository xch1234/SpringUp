using System.Collections.Generic;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEngine;

namespace Tideflesh.Systems
{
    // 只负责 A/C 的目标接线，不扣血，不把 C 的敌人射程当作玩家索敌距离。
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OrganPipelineController))]
    public sealed class BattleOrganTargeting : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private float attackRange = AttackTargeting.DefaultRange;
        [SerializeField, Tooltip("留空时通过 C 的 PlayerLocator 获取玩家。")]
        private Transform attackOrigin;
        private OrganPipelineController pipeline;
        private readonly List<AttackTarget> candidates = new List<AttackTarget>();

        public float AttackRange { get => attackRange; set => attackRange = value; }
        public Transform AttackOrigin { get => attackOrigin; set => attackOrigin = value; }

        private void OnEnable()
        {
            pipeline = GetComponent<OrganPipelineController>();
            if (pipeline.TargetSelector != null || pipeline.TargetValidator != null || pipeline.TargetSnapshotProvider != null
                || pipeline.TargetSelectorInRange != null || pipeline.TargetSnapshotInRange != null)
            {
                Debug.LogError("BattleOrganTargeting: 管道已有索敌接入，请使用独立战斗管道。", this);
                enabled = false;
                return;
            }
            pipeline.TargetSelector = SelectTarget;
            pipeline.TargetValidator = ValidateTarget;
            pipeline.TargetSnapshotProvider = Snapshot;
            pipeline.TargetSelectorInRange = SelectTargetInRange;
            pipeline.TargetSnapshotInRange = SnapshotInRange;
            pipeline.OriginProvider = OriginPosition;
        }

        private void OnDisable()
        {
            if (pipeline == null) return;
            if (pipeline.TargetSelector == SelectTarget) pipeline.TargetSelector = null;
            if (pipeline.TargetValidator == ValidateTarget) pipeline.TargetValidator = null;
            if (pipeline.TargetSnapshotProvider == Snapshot) pipeline.TargetSnapshotProvider = null;
            if (pipeline.TargetSelectorInRange == SelectTargetInRange) pipeline.TargetSelectorInRange = null;
            if (pipeline.TargetSnapshotInRange == SnapshotInRange) pipeline.TargetSnapshotInRange = null;
            if (pipeline.OriginProvider == OriginPosition) pipeline.OriginProvider = null;
        }

        public string SelectTarget() => SelectTargetInRange(attackRange);

        public string SelectTargetInRange(float range)
        {
            var origin = Origin();
            if (origin == null) return null;
            candidates.Clear();
            foreach (var enemy in EnemyRuntime.ActiveEnemies)
                if (Eligible(enemy, origin))
                    candidates.Add(new AttackTarget(enemy.TargetId, enemy.Position, true));
            return AttackTargeting.SelectNearest(candidates, origin.position, range);
        }

        public bool ValidateTarget(string targetId)
            => Snapshot(targetId).HasValue;

        public AttackTarget? Snapshot(string targetId) => SnapshotInRange(targetId, attackRange);

        public AttackTarget? SnapshotInRange(string targetId, float range)
        {
            var origin = Origin();
            if (origin == null || string.IsNullOrWhiteSpace(targetId)) return null;
            foreach (var enemy in EnemyRuntime.ActiveEnemies)
                if (Eligible(enemy, origin) && enemy.TargetId == targetId)
                {
                    var target = new AttackTarget(enemy.TargetId, enemy.Position, true);
                    return AttackTargeting.IsInRange(target, origin.position, range) ? target : (AttackTarget?)null;
                }
            return null;
        }

        private Transform Origin() => attackOrigin != null ? attackOrigin : PlayerLocator.Player;
        private Vector2? OriginPosition() => Origin() != null ? (Vector2?)Origin().position : null;
        private static bool Eligible(EnemyRuntime enemy, Transform origin)
            => enemy != null && enemy.isActiveAndEnabled && enemy.IsAlive
                && enemy.gameObject.scene == origin.gameObject.scene;
    }
}
