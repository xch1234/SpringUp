using System.Collections.Generic;
using UnityEngine;

namespace SpringUp.Organs
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OrganPipelineController))]
    public sealed class HandOrganDemo : MonoBehaviour
    {
        [SerializeField, Tooltip("Element 0 到 5 对应第 1 到 6 槽。拖入配置资源；None 是空槽。停止 Play 后修改。")]
        private OrganConfig[] slots = new OrganConfig[BodyRuntime.SlotCount];
        [SerializeField, Tooltip("只用于拳头等单目标攻击。TNT 不依赖此列表，会检查当前场景的全部启用敌人。敌人编号必须唯一。")]
        private DemoTarget[] targets = new DemoTarget[0];
        [SerializeField, Tooltip("TNT 的爆炸中心。拖入角色物体；留空时使用本物体的位置，例如 HandStatusDemo。每次爆炸读取当前位置。")]
        private Transform explosionOrigin;
        [SerializeField, Tooltip("黑洞生成位置。拖入角色；留空使用本物体。生成后黑洞留在原地，不跟随角色。")]
        private Transform blackHoleOrigin;

        private readonly List<DemoTarget> connectedTargets = new List<DemoTarget>();
        private readonly HashSet<DemoTarget> observedTargets = new HashSet<DemoTarget>();
        private readonly List<DemoTarget> battleTargets = new List<DemoTarget>();
        private readonly OrganInstance[] installed = new OrganInstance[BodyRuntime.SlotCount];
        private OrganPipelineController controller;
        private bool connected;
        private bool initializationPending;
        private readonly Queue<TriggerSignal> pendingKills = new Queue<TriggerSignal>();
        private int damageBatchDepth;
        private bool flushingKills;
        private string message = "等待运行";
        private string chainMessage = "等待击杀触发";
        public int TriggerCount { get; private set; }
        public int ExplosionCount { get; private set; }
        public int LastExplosionHitCount { get; private set; }

        private void OnEnable()
        {
            // OnEnable 时其他组件可能还没完成启用，不能在这里判断其运行状态。
            initializationPending = true;
        }

        private void Start() => InitializeIfPending();

        // Start 只调用一次；运行中重新启用本组件时，在下一帧重新连接。
        private void Update() => InitializeIfPending();

        private void InitializeIfPending()
        {
            if (!Application.isPlaying || !initializationPending) return;
            initializationPending = false;
            if (!TryStartDemo(out string error))
                Debug.LogWarning("[手部测试] " + error, this);
        }

        // 也供编辑器检查调用。先全部校验，再安装，避免半套配置开始攻击。
        public bool TryStartDemo(out string error)
        {
            initializationPending = false;
            Disconnect();
            controller = GetComponent<OrganPipelineController>();
            error = null;
            if (GetComponent<OrganPipelineDemo>() != null)
                error = "请先移除同物体上的 OrganPipelineDemo，只保留一个测试入口。";
            else if (controller == null || !controller.isActiveAndEnabled)
                error = "请启用 OrganPipelineController。";
            else if (controller.TargetSelector != null)
                error = "管道已有其他目标选择器，请使用独立的测试物体。";
            else if (slots == null || slots.Length != BodyRuntime.SlotCount)
                error = "Slots 的 Size 必须为 6。";

            var definitions = new OrganDefinition[BodyRuntime.SlotCount];
            if (error == null)
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    if (!slots[i].TryCreateDefinition(out definitions[i], out error)) break;
                }

            var selectedTargets = new List<DemoTarget>();
            var ids = new HashSet<string>();
            if (error == null && targets != null)
                foreach (DemoTarget target in targets)
                {
                    if (target == null) continue;
                    if (string.IsNullOrWhiteSpace(target.TargetId) || !ids.Add(target.TargetId))
                    {
                        error = "Targets 中的目标编号为空或重复，请修改 DemoTarget 的 Target Id。";
                        break;
                    }
                    selectedTargets.Add(target);
                }
            bool hasArea = false;
            bool hasBlackHole = false;
            foreach (OrganDefinition definition in definitions)
            {
                if (definition != null && definition.Shape != AttackShape.SingleTarget) hasArea = true;
                if (definition?.Shape == AttackShape.BlackHole) hasBlackHole = true;
            }
            if (error == null && selectedTargets.Count == 0 && !hasArea)
                error = "请把场景中的 DemoTarget 拖入 Targets 列表。";
            if (error == null && hasBlackHole && GetComponent<DemoBlackHoleField>() is DemoBlackHoleField field && !field.enabled)
                error = "请启用 DemoBlackHoleField，才能演示黑洞。";
            bool needsStatusTarget = false;
            foreach (OrganDefinition definition in definitions)
                if (definition?.Status != null && definition.Status.Chance > 0f) needsStatusTarget = true;
            if (error == null && needsStatusTarget)
                foreach (DemoTarget target in selectedTargets)
                {
                    DemoCombatTarget combat = target.GetComponent<DemoCombatTarget>();
                    if (combat == null || !combat.enabled)
                    {
                        error = "有状态器官时，每个目标都需启用 DemoCombatTarget。请使用 HandStatusDemo 场景。";
                        break;
                    }
                }
            if (error != null)
            {
                message = error;
                return false;
            }

            // 本入口只安装手部；不修改头、腿，也不重置测试敌人的生命。
            if (hasBlackHole && GetComponent<DemoBlackHoleField>() == null) gameObject.AddComponent<DemoBlackHoleField>();
            BodyRuntime hand = controller.GetBody(BodyPart.Hand);
            for (int i = 0; i < installed.Length; i++)
            {
                installed[i] = definitions[i] == null ? null : new OrganInstance(definitions[i]);
                hand.SetSlot(i, installed[i]);
            }
            connectedTargets.AddRange(selectedTargets);
            foreach (DemoTarget target in connectedTargets) ObserveTarget(target);
            controller.TargetSelector = SelectTarget;
            controller.AttackProduced += ApplyAttack;
            controller.TriggerActivated += OnTrigger;
            controller.TriggerBlocked += OnBlocked;
            connected = true;
            TriggerCount = 0;
            ExplosionCount = 0;
            LastExplosionHitCount = 0;
            message = "已读取配置，等待攻击。修改数值后请重新 Play。";
            chainMessage = "等待击杀触发";
            return true;
        }

        private void OnDisable() => StopDemo();

        public void SetLoadout(OrganConfig[] newSlots, DemoTarget[] newTargets)
        {
            if (newSlots == null || newSlots.Length != BodyRuntime.SlotCount)
                throw new System.ArgumentException("Slots 必须为六槽。", nameof(newSlots));
            StopDemo();
            slots = (OrganConfig[])newSlots.Clone();
            targets = newTargets == null ? new DemoTarget[0] : (DemoTarget[])newTargets.Clone();
            // Awake 设置装备后，仍交给原有的 Start/Update 安全初始化。
            initializationPending = true;
        }

        // 普通清理入口，运行时和编辑器检查共用；不要用 SendMessage 模拟生命周期。
        public void StopDemo()
        {
            initializationPending = false;
            Disconnect();
        }

        private void Disconnect()
        {
            if (connected && controller != null)
            {
                controller.AttackProduced -= ApplyAttack;
                controller.TriggerActivated -= OnTrigger;
                controller.TriggerBlocked -= OnBlocked;
                if (controller.TargetSelector == SelectTarget) controller.TargetSelector = null;
                BodyRuntime hand = controller.GetBody(BodyPart.Hand);
                for (int i = 0; i < installed.Length; i++)
                {
                    if (installed[i] != null && ReferenceEquals(hand.GetSlot(i), installed[i]))
                        hand.SetSlot(i, null);
                    installed[i] = null;
                }
            }
            foreach (DemoTarget target in observedTargets)
                if (target != null)
                {
                    target.Killed -= OnKilled;
                    target.GetComponent<DemoCombatTarget>()?.ClearStatuses();
                }
            connectedTargets.Clear();
            observedTargets.Clear();
            battleTargets.Clear();
            pendingKills.Clear();
            GetComponent<DemoExplosionView>()?.Clear();
            GetComponent<DemoBlackHoleField>()?.Clear();
            connected = false;
        }

        private string SelectTarget()
        {
            foreach (DemoTarget target in connectedTargets)
                if (target != null && target.IsAlive) return target.TargetId;
            message = "没有存活目标；停止后重新 Play 可重新测试。";
            return null;
        }

        private void ApplyAttack(CastEvent attack)
        {
            if (attack.Shape == AttackShape.BlackHole)
            {
                Vector2 holeCenter = blackHoleOrigin != null ? blackHoleOrigin.position : transform.position;
                GetComponent<DemoBlackHoleField>()?.Spawn(attack, holeCenter);
                CollectBattleTargets();
                message = $"坍缩体：生成位置 {holeCenter}，半径 {attack.Radius}，持续 {attack.BlackHole.Duration} 秒，只吸引不扣血";
                return;
            }
            bool explosion = attack.Shape == AttackShape.Explosion;
            List<DemoTarget> candidates = explosion ? CollectBattleTargets() : new List<DemoTarget>(connectedTargets);
            var snapshot = new List<AttackTarget>();
            var targetsById = new Dictionary<string, DemoTarget>();
            foreach (DemoTarget target in candidates)
            {
                if (target == null || !target.IsAlive) continue;
                if (string.IsNullOrWhiteSpace(target.TargetId) || targetsById.ContainsKey(target.TargetId))
                {
                    message = "场景中有敌人编号为空或重复，请给每个 DemoTarget 设置不同的 Target Id。";
                    Debug.LogWarning("[手部测试] " + message, this);
                    return;
                }
                targetsById.Add(target.TargetId, target);
                snapshot.Add(new AttackTarget(target.TargetId, target.transform.position, true));
            }
            Vector2 origin = explosionOrigin != null ? explosionOrigin.position : transform.position;
            List<CastEvent> hits = AttackResolver.Resolve(attack, snapshot, origin, out Vector2 center);
            if (!explosion && hits.Count == 0) return;
            if (explosion)
            {
                ExplosionCount++;
                GetComponent<DemoExplosionView>()?.Show(center, attack.Radius);
            }
            int applied = 0;
            bool completed = false;
            damageBatchDepth++;
            try
            {
                foreach (CastEvent hit in hits)
                {
                    if (!connected) break;
                    DemoTarget target = targetsById[hit.TargetId];
                    if (target == null) continue;
                    ObserveTarget(target);
                    DemoCombatTarget combat = target.GetComponent<DemoCombatTarget>();
                    bool accepted = combat != null && combat.isActiveAndEnabled
                        ? combat.TryApplyAttack(hit) : target.TryApplyAttack(hit);
                    if (accepted) applied++;
                }
                if (explosion) LastExplosionHitCount = applied;
                message = explosion ? $"TNT 爆炸 #{ExplosionCount}：中心 {center}，半径 {attack.Radius}，命中 {applied} 个，伤害 {attack.Damage}"
                    : $"{attack.OriginDefinitionId} → {attack.TargetId}，伤害 {attack.Damage}，链深度 {attack.ChainDepth}";
                completed = true;
            }
            finally
            {
                damageBatchDepth--;
                if (!completed) pendingKills.Clear();
                else if (damageBatchDepth == 0) FlushKills();
            }
        }

        private void ObserveTarget(DemoTarget target)
        {
            if (observedTargets.Add(target)) target.Killed += OnKilled;
        }

        private List<DemoTarget> CollectBattleTargets()
        {
            battleTargets.Clear();
            // 每次爆炸重新发现敌人，覆盖运行中生成的目标；不使用主目标列表筛掉旁边的敌人。
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (DemoTarget target in root.GetComponentsInChildren<DemoTarget>(false))
                    if (target.isActiveAndEnabled) battleTargets.Add(target);
            return new List<DemoTarget>(battleTargets);
        }

        private void OnKilled(DemoTarget target, CastEvent attack)
        {
            pendingKills.Enqueue(new TriggerSignal(TriggerOn.Kill, attack));
            if (damageBatchDepth == 0) FlushKills();
        }

        private void FlushKills()
        {
            if (flushingKills) return;
            flushingKills = true;
            try
            {
                // 一次范围攻击扣完血后再触发。流血等单独发来的死亡通知仍可立即处理。
                while (connected && pendingKills.Count > 0)
                    controller.PublishSignal(pendingKills.Dequeue());
            }
            finally { pendingKills.Clear(); flushingKills = false; }
        }

        private void OnTrigger(BodyRuntime body, int index, OrganInstance organ, CastContext context)
        {
            TriggerCount++;
            chainMessage = $"多肢触手发动 {TriggerCount} 次，额外执行手部，链深度 {context.Depth}";
            Debug.Log("[手部测试] " + chainMessage, this);
        }

        private void OnBlocked(BodyRuntime body, int index, OrganInstance organ,
            CastContext context, TriggerBlockReason reason)
        {
            chainMessage = reason == TriggerBlockReason.DepthLimit
                ? "已到链深度上限，停止追加。" : "本条链的这个触手已发动，停止重复。";
        }

        private void OnGUI()
        {
            List<DemoTarget> displayedTargets = battleTargets.Count > 0 ? battleTargets : connectedTargets;
            GUILayout.BeginArea(new Rect(15, 15, 720, 290 + displayedTargets.Count * 48), GUI.skin.box);
            GUILayout.Label("手部器官：配置伤害、状态与击杀链");
            GUILayout.Label("已支持眩晕、流血、减速、TNT 爆炸和黑洞吸引。修改配置后重新 Play。");
            GUILayout.Label(message);
            DemoBlackHoleField field = GetComponent<DemoBlackHoleField>();
            if (field != null) GUILayout.Label($"当前黑洞：{field.ActiveCount}，最长剩余：{field.LongestRemaining:0.0} 秒");
            if (connected && controller != null)
            {
                BodyRuntime hand = controller.GetBody(BodyPart.Hand);
                for (int i = 0; i < BodyRuntime.SlotCount; i++)
                {
                    OrganInstance organ = hand.GetSlot(i);
                    string label = organ == null ? "空" : organ.Definition.DisplayName
                        + (organ.Definition.Shape == AttackShape.BlackHole ? " / 持续吸引，不扣血"
                            : organ.Definition.Type == OrganType.Actuator ? $" / 伤害 {organ.Definition.Damage}" : " / 击杀触发");
                    GUILayout.Label($"{(hand.LastExecutedSlot == i ? ">" : " ")} 第 {i + 1} 槽：{label}");
                }
            }
            GUILayout.Label($"触发次数：{TriggerCount}。{chainMessage}");
            foreach (DemoTarget target in displayedTargets)
                if (target != null)
                {
                    GUILayout.Label($"{target.TargetId}：{target.CurrentHealth}/{target.MaxHealth}");
                    DemoCombatTarget combat = target.GetComponent<DemoCombatTarget>();
                    if (combat != null) GUILayout.Label(combat.StatusSummary);
                }
            GUILayout.EndArea();
        }
    }
}
