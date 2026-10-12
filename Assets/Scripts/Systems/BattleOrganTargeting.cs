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
        [SerializeField, Min(0f), Tooltip("玩家器官自动索敌距离；独立于 TNT/黑洞效果半径。")]
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
            if (pipeline.TargetSelector != null || pipeline.TargetValidator != null || pipeline.TargetSnapshotProvider != null)
            {
                Debug.LogError("BattleOrganTargeting: 管道已有索敌接入，请使用独立战斗管道。", this);
                enabled = false;
                return;
            }
            pipeline.TargetSelector = SelectTarget;
            pipeline.TargetValidator = ValidateTarget;
            pipeline.TargetSnapshotProvider = Snapshot;
        }

        private void OnDisable()
        {
            if (pipeline == null) return;
            if (pipeline.TargetSelector == SelectTarget) pipeline.TargetSelector = null;
            if (pipeline.TargetValidator == ValidateTarget) pipeline.TargetValidator = null;
            if (pipeline.TargetSnapshotProvider == Snapshot) pipeline.TargetSnapshotProvider = null;
        }

        public string SelectTarget()
        {
            var origin = Origin();
            if (origin == null) return null;
            candidates.Clear();
            foreach (var enemy in EnemyRuntime.ActiveEnemies)
                if (Eligible(enemy, origin))
                    candidates.Add(new AttackTarget(enemy.TargetId, enemy.Position, true));
            return AttackTargeting.SelectNearest(candidates, origin.position, attackRange);
        }

        public bool ValidateTarget(string targetId)
            => Snapshot(targetId).HasValue;

        public AttackTarget? Snapshot(string targetId)
        {
            var origin = Origin();
            if (origin == null || string.IsNullOrWhiteSpace(targetId)) return null;
            foreach (var enemy in EnemyRuntime.ActiveEnemies)
                if (Eligible(enemy, origin) && enemy.TargetId == targetId)
                {
                    var target = new AttackTarget(enemy.TargetId, enemy.Position, true);
                    return AttackTargeting.IsInRange(target, origin.position, attackRange) ? target : (AttackTarget?)null;
                }
            return null;
        }

        private Transform Origin() => attackOrigin != null ? attackOrigin : PlayerLocator.Player;
        private static bool Eligible(EnemyRuntime enemy, Transform origin)
            => enemy != null && enemy.isActiveAndEnabled && enemy.IsAlive
                && enemy.gameObject.scene == origin.gameObject.scene;
    }
}
