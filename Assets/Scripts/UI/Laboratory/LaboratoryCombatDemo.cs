using System;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Organs;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Serialization;

namespace SpringUp.Laboratory
{
    // 预览适配：共用器官节拍、命中、状态和执行器运行时，提供演示目标与 UI 表现。
    public sealed class LaboratoryCombatDemo : MonoBehaviour
    {
        public LaboratoryDemoBootstrap source;
        [NonSerialized] public DemoTarget[] targets;
        public Image[] targetImages;
        public Text[] targetLabels;
        public Text statusText, eventText, rulesText;
        public Button restartButton;
        [Min(1)] public float targetHealth = 30;
        [Min(.1f)] public float replenishDelay = 1.5f;
        public Vector2 firstPosition = new Vector2(.75f, 0), secondPosition = new Vector2(1.75f, 0);
        [FormerlySerializedAs("explosionCenter")] public Vector2 playerPosition = Vector2.zero;
        [HideInInspector] public float attackRange = AttackTargeting.DefaultRange;
        public OrganPipelineController Pipeline { get; private set; }
        public Action<OrganPipelineController> ConfigureTiming { get; set; }
        public int UnsupportedCount { get; private set; }
        public int TriggerCount { get; private set; }
        public int DeathCount { get; private set; }
        public IReadOnlyDictionary<string, string> InventoryOrigins => inventoryOrigins;
        public IReadOnlyList<BlackHoleBehaviour> BlackHoles => holes;
        public StatusEffectRuntime StatusAt(int index) => statuses[index];
        private ILaboratoryEquipment equipment;
        private readonly Queue<string> events = new Queue<string>();
        private readonly Queue<TriggerSignal> deaths = new Queue<TriggerSignal>();
        private readonly Dictionary<string, string> origins = new Dictionary<string, string>();
        private readonly Dictionary<string, string> inventoryOrigins = new Dictionary<string, string>();
        private readonly List<BlackHoleBehaviour> holes = new List<BlackHoleBehaviour>();
        private StatusEffectRuntime[] statuses;
        private float[] remaining, flashes;
        private bool resolving, draining;
        private int generation, supported;
        private Scene privateScene;
        private readonly ActuatorRunner actuators = new ActuatorRunner();
        private readonly IOrganActuator straightProjectile = new StraightProjectileActuator();
        private readonly List<EffectPreviewGraphic> projectileViews = new List<EffectPreviewGraphic>();
        private readonly List<Image> projectileMarkers = new List<Image>();
        public ActuatorRunner Actuators => actuators;
        public float PreviewHealth { get; private set; } = 50;
        public TorsoLoadout PreviewTorso { get; } = new TorsoLoadout();
        public float PreviewMaxHealth => 100 * PreviewTorso.MaxHealth;
        public Vector2 previewFacing = Vector2.right;
        private float previewInvulnerability;
        [Range(0, 1)] public float previewCriticalChance;
        [Min(1)] public float previewCriticalMultiplier = 2f;
        public bool PreviewMoving { get; set; }
        public void PreviewHurt()
        {
            if (Pipeline == null || PreviewHealth <= 0 || previewInvulnerability > 0) return;
            PreviewHealth = Mathf.Max(0, PreviewHealth - 5 * PreviewTorso.IncomingDamage);
            previewInvulnerability = .3f * PreviewTorso.Invulnerability;
            if (PreviewHealth > 0) Pipeline.NotifyOwner(TriggerOn.Hurt);
            Render();
        }
        public void PreviewHeal() { if (Pipeline != null && PreviewHealth > 0) { new PreviewWorld(this).Heal(5); Render(); } }
        private void ExecutionStarted(IActuatorExecution e) => Pipeline.NotifyExecutionStarted(e.Context.Cause);
        private void ExecutionEnded(IActuatorExecution e) => Pipeline.NotifyExecutionEnded(e.Context.Cause, e.EndReason == ActuatorEndReason.Cancelled);
        private readonly Dictionary<string, float> attachedSlows = new Dictionary<string, float>();

        private sealed class PreviewWorld : IEffectWorld
        {
            private readonly LaboratoryCombatDemo demo;
            public PreviewWorld(LaboratoryCombatDemo demo) { this.demo = demo; }
            public Vector2? OwnerPosition => demo.playerPosition;
            public IReadOnlyList<AttackTarget> Targets => demo.targets.Select(t => new AttackTarget(t.TargetId, t.transform.position, t.IsAlive)).ToArray();
            public bool SweepWall(Vector2 from, Vector2 to, float radius, out ActuatorHit hit) { hit = default; return false; }
            public IReadOnlyList<AttackTarget> BeamTargets(Vector2 from, Vector2 to, float radius)
                => Targets.Where(t => t.IsAlive && ProjectileGeometry.SegmentCircle(from, to, t.Position, radius + .2f, out _)).ToArray();
            public float Damage(CastEvent hit)
            {
                var target = demo.targets.FirstOrDefault(t => t.TargetId == hit.TargetId && t.IsAlive);
                if (target == null) return 0;
                return demo.ResolvePreview(hit);
            }
            public void NotifyDebuff(CastEvent cause) => demo.Pipeline.PublishSignal(new TriggerSignal(TriggerOn.Debuff, cause));
            public float Heal(float amount, CastEvent? cause = null)
            {
                float before = demo.PreviewHealth;
                demo.PreviewHealth = Mathf.Min(demo.PreviewMaxHealth, before + amount);
                float actual = demo.PreviewHealth - before;
                if (actual > 0)
                {
                    if (demo.resolving && cause.HasValue) demo.deaths.Enqueue(new TriggerSignal(TriggerOn.Heal, cause.Value, demo.playerPosition));
                    else demo.Pipeline.NotifyOwner(TriggerOn.Heal, cause);
                }
                demo.Record($"吸血回复 {demo.PreviewHealth - before:g}；预览角色 {demo.PreviewHealth:g}/{demo.PreviewMaxHealth:g}");
                return actual;
            }
            private sealed class Lease : IDisposable
            {
                private Action release;
                public Lease(Action release) { this.release = release; }
                public void Dispose() { var action = release; release = null; action?.Invoke(); }
            }
            public IDisposable Slow(string targetId, string sourceId, float multiplier, CastEvent? cause = null)
            {
                string key = targetId + "/" + sourceId;
                demo.attachedSlows[key] = multiplier;

                return new Lease(() => demo.attachedSlows.Remove(key));
            }
            public bool Sweep(Vector2 from, Vector2 to, float radius, out ActuatorHit hit)
            {
                float nearest = float.PositiveInfinity; string id = null;
                foreach (var target in demo.targets)
                    if (target != null && target.IsAlive && ProjectileGeometry.SegmentCircle(from, to,
                        target.transform.position, radius + .2f, out float fraction) && fraction < nearest)
                    { nearest = fraction; id = target.TargetId; }
                hit = id == null ? default : new ActuatorHit(id, Vector2.Lerp(from, to, nearest));
                return id != null;
            }
            public void ApplyHit(CastEvent hit) => demo.Attack(hit);
        }
        private void ExecuteActuator(ActuatorContext context)
        {
            actuators.Start(straightProjectile, context, new PreviewWorld(this));
            Record($"{Origin(context.Cause)}：开始执行");
        }
        private void Start() { if (equipment == null) Initialize(); }
        private void OnEnable() { if (source != null) Initialize(); }
        public void Initialize()
        {
            Disconnect();
            equipment = source.Equipment;
            equipment.Changed += Restart;
            // 独立场景避免 A 的场景目标发现器看到实验室目标；不注册 C 的全局事件。
            if (Application.isPlaying) privateScene = SceneManager.CreateScene("Laboratory-" + Guid.NewGuid().ToString("N"));
            targets = new DemoTarget[2]; statuses = new StatusEffectRuntime[2];
            remaining = new float[2]; flashes = new float[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Laboratory target " + i);
                if (privateScene.IsValid()) SceneManager.MoveGameObjectToScene(go, privateScene);
                targets[i] = go.AddComponent<DemoTarget>();
                statuses[i] = new StatusEffectRuntime();
                targets[i].Damaged += Damaged; targets[i].Killed += Killed;
            }
            restartButton.onClick.AddListener(Restart);
            Restart();
        }
        public static OrganDefinition Resolve(LaboratoryItem item) => item?.Definition;
        public void Restart()
        {
            if (equipment == null) return;
            DisposePipeline();
            var torsoSettings = new List<TorsoSettings>();
            for (int slot = 0; slot < equipment.SlotCount(LaboratoryPart.Torso); slot++)
                torsoSettings.Add(equipment.GetSlot(LaboratoryPart.Torso, slot)?.Definition?.Torso);
            PreviewTorso.Set(torsoSettings); PreviewHealth = Mathf.Min(50, PreviewMaxHealth); previewInvulnerability = 0;
            if (rulesText != null) rulesText.text = "范围效果中心：触发目标位置。";
            events.Clear(); deaths.Clear(); holes.Clear(); origins.Clear(); inventoryOrigins.Clear();
            TriggerCount = DeathCount = UnsupportedCount = supported = 0;
            for (int i = 0; i < 2; i++) Replenish(i);
            if (!string.IsNullOrEmpty(source.ConfigurationError))
            {
                statusText.text = "配置错误，预览已停止：\n" + source.ConfigurationError;
                Record("请修复真实配置后重新进入 Play。"); RenderTargets(); return;
            }
            var runner = new GameObject("Equipment preview pipeline");
            runner.transform.SetParent(transform, false);
            Pipeline = runner.AddComponent<OrganPipelineController>();
            Pipeline.UseUnscaledTime = true;
            ConfigureTiming?.Invoke(Pipeline);
            if (ConfigureTiming == null) Pipeline.StepInterval /= PreviewTorso.Frequency;
            Pipeline.TriggerProbabilityMultiplier = PreviewTorso.Probability; Pipeline.TriggerFrequencyMultiplier = PreviewTorso.Frequency;
            for (int p = 0; p < 4; p++)
                for (int s = 0; s < equipment.SlotCount((LaboratoryPart)p); s++)
                {
                    var item = equipment.GetSlot((LaboratoryPart)p, s);
                    if (item == null) continue;
                    var definition = Resolve(item);
                    if (definition?.Torso != null && p == (int)LaboratoryPart.Torso) { supported++; continue; }
                    if (definition == null || p == (int)LaboratoryPart.Torso || s >= BodyRuntime.SlotCount) { UnsupportedCount++; continue; }
                    var instance = new OrganInstance(definition, item.InstanceId);
                    var actualPart = p == (int)LaboratoryPart.Head ? BodyPart.Head
                        : p == (int)LaboratoryPart.Hand ? BodyPart.Hand : BodyPart.Leg;
                    Pipeline.GetBody(actualPart).SetSlot(s, instance);
                    origins.Add(instance.InstanceId, $"{LaboratoryPresenter.PartNames[p]}{s + 1} {item.Name}");
                    inventoryOrigins.Add(instance.InstanceId, item.InstanceId);
                    supported++;
                }
            Pipeline.TargetSelector = SelectTarget;
            Pipeline.TargetValidator = ValidateTarget;
            Pipeline.TargetSnapshotProvider = Snapshot;
            Pipeline.TargetSelectorInRange = SelectTargetInRange;
            Pipeline.TargetSnapshotInRange = SnapshotInRange;
            Pipeline.OriginProvider = () => playerPosition;
            Pipeline.MovingProvider = () => PreviewMoving;
            actuators.Started += ExecutionStarted; actuators.Ended += ExecutionEnded;
            Pipeline.ActuatorProduced += ExecuteActuator;
            Pipeline.AttackProduced += Attack; Pipeline.SlotExecuted += Step;
            Pipeline.TriggerActivated += Trigger; Pipeline.TriggeredSlotExecuted += ExtraStep; Pipeline.TriggerBlocked += Blocked;
            Record("等待节拍；仅攻击索敌范围内最近的存活目标。");
            Render();
        }
        private string SelectTarget() => SelectTargetInRange(attackRange);

        private string SelectTargetInRange(float range)
        {
            var snapshot = new List<AttackTarget>();
            foreach (var target in targets)
                if (target != null)
                    snapshot.Add(new AttackTarget(target.TargetId, target.transform.position, target.IsAlive));
            return AttackTargeting.SelectNearest(snapshot, playerPosition, range);
        }
        private bool ValidateTarget(string id)
            => Snapshot(id).HasValue;

        private AttackTarget? Snapshot(string id) => SnapshotInRange(id, attackRange);

        private AttackTarget? SnapshotInRange(string id, float range)
        {
            foreach (var target in targets)
                if (target != null && target.TargetId == id)
                {
                    var snapshot = new AttackTarget(id, target.transform.position, target.IsAlive);
                    if (AttackTargeting.IsInRange(snapshot, playerPosition, range)) return snapshot;
                }
            return null;
        }
        private void Replenish(int i)
        {
            statuses[i].Clear(); remaining[i] = flashes[i] = 0;
            targets[i].transform.position = i == 0 ? firstPosition : secondPosition;
            // 每一代都是新身份，旧攻击不能命中新目标；初始化不发死亡或奖励信号。
            targets[i].InitializeForDemo((i == 0 ? "A" : "B") + "#" + (++generation), targetHealth);
        }
        private void Attack(CastEvent attack) => ResolvePreview(attack);
        private CastEvent ScalePreview(CastEvent hit)
        {
            var target = targets.FirstOrDefault(t => t.TargetId == hit.TargetId);
            float scale = PreviewTorso.Damage;
            if (target != null) scale *= PreviewTorso.FrontMultiplier(playerPosition, previewFacing, target.transform.position);
            return TorsoLoadout.ScaleDamage(hit, scale);
        }
        private float ResolvePreview(CastEvent attack)
        {
            if (attack.Shape == AttackShape.BlackHole)
            {
                if (!attack.EffectCenter.HasValue) return 0;
                holes.RemoveAll(h => h.Cause.OriginInstanceId == attack.OriginInstanceId);
                holes.Add(new BlackHoleBehaviour(attack));
                Record($"{Origin(attack)}：生成黑洞 {attack.BlackHole.Duration:g}s"); return 0;
            }
            var snapshot = targets.Select(t => new AttackTarget(t.TargetId, t.transform.position, t.IsAlive)).ToArray();
            var hits = AttackResolver.Resolve(attack, snapshot, out _);
            float actualDamage = 0;
            resolving = true;
            try
            {
                foreach (var rawHit in hits)
                {
                    var hit = CriticalDamage.Roll(ScalePreview(rawHit), previewCriticalChance * PreviewTorso.Probability, previewCriticalMultiplier, UnityEngine.Random.value, out bool critical);
                    int i = Array.FindIndex(targets, t => t.TargetId == hit.TargetId);
                    float before = i >= 0 ? targets[i].CurrentHealth : 0;
                    if (i >= 0 && targets[i].TryApplyAttack(hit))
                    {
                        float actual = Mathf.Max(0, before - targets[i].CurrentHealth); actualDamage += actual;
                        if (actual > 0 && PreviewTorso.LifeSteal > 0) new PreviewWorld(this).Heal(actual * PreviewTorso.LifeSteal, hit);
                        deaths.Enqueue(new TriggerSignal(TriggerOn.Hit, hit, targets[i].transform.position));
                        if (critical) deaths.Enqueue(new TriggerSignal(TriggerOn.Critical, hit, targets[i].transform.position));
                        if (targets[i].IsAlive && statuses[i].TryApply(hit, UnityEngine.Random.value, PreviewTorso.Probability))
                            deaths.Enqueue(new TriggerSignal(TriggerOn.Debuff, hit, targets[i].transform.position));
                    }
                }
                if (attack.Shape == AttackShape.Explosion) Record($"{Origin(attack)}：范围命中 {hits.Count} 个，半径 {attack.Radius:g}");
            }
            finally { resolving = false; }
            DrainDeaths();
            return actualDamage;
        }
        private string Origin(CastEvent attack) => origins.TryGetValue(attack.OriginInstanceId, out var name) ? name : attack.OriginDefinitionId;
        private void Step(BodyRuntime body, int slot, OrganInstance organ) => Record($"节拍：{origins[organ.InstanceId]}");
        private void ExtraStep(BodyRuntime body, int slot, OrganInstance organ, CastContext context)
            => Record($"追加执行：{origins[organ.InstanceId]}");
        private void Trigger(BodyRuntime body, int slot, OrganInstance organ, CastContext context)
        {
            TriggerCount++; Record($"条件触发：{origins[organ.InstanceId]} → "
                + "紧邻后一个执行器");
        }
        private void Blocked(BodyRuntime body, int slot, OrganInstance organ, CastContext context, TriggerBlockReason reason)
            => Record($"{origins[organ.InstanceId]}：" + (reason == TriggerBlockReason.DepthLimit ? "达到链深上限" : "同链已发动"));
        private void Damaged(DemoTarget target, CastEvent attack, float damage)
        {
            flashes[Array.IndexOf(targets, target)] = Time.unscaledTime + .25f;
            Record($"{Origin(attack)} → {target.TargetId} 扣血 {damage:g}");
        }
        private void Killed(DemoTarget target, CastEvent attack)
        {
            int i = Array.IndexOf(targets, target);
            statuses[i].Clear(); remaining[i] = replenishDelay; DeathCount++;
            Record($"{target.TargetId} 死亡，{replenishDelay:g}s 后补充");
            deaths.Enqueue(new TriggerSignal(TriggerOn.Kill, attack, target.DeathPosition));
            if (!resolving) DrainDeaths();
        }
        private void DrainDeaths()
        {
            if (draining) return;
            draining = true;
            try { while (deaths.Count > 0) Pipeline.PublishSignal(deaths.Dequeue()); }
            finally { draining = false; }
        }
        // 独立推进状态和补充，不改变管道游标；检查也调用此入口。
        public void Advance(float deltaTime)
        {
            if (equipment == null || Pipeline == null) return;
            previewInvulnerability = Mathf.Max(0, previewInvulnerability - deltaTime);
            Pipeline.AdvanceTriggers(deltaTime);
            for (int i = 0; i < 2; i++)
            {
                if (!targets[i].IsAlive)
                {
                    remaining[i] -= deltaTime;
                    if (remaining[i] <= 0) { Replenish(i); Record($"{targets[i].TargetId} 满血补充"); }
                }

            }
            // 状态批次结束再处理追加，避免本帧新附加的流血立刻多走整帧时间。
            resolving = true;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    int index = i;
                    statuses[i].Tick(deltaTime, hit =>
                    {
                        var scaled = ScalePreview(hit); float before = targets[index].CurrentHealth;
                        targets[index].TryApplyAttack(scaled);
                        float actual = Mathf.Max(0, before - targets[index].CurrentHealth);
                        if (actual > 0 && PreviewTorso.LifeSteal > 0) new PreviewWorld(this).Heal(actual * PreviewTorso.LifeSteal, scaled);
                    }, () => targets[index].IsAlive);
                }
            }
            finally { resolving = false; }
            DrainDeaths();
            var holeSnapshot = holes.ToArray();
            foreach (var hole in holeSnapshot) hole.Tick(deltaTime);
            foreach (var hole in holeSnapshot)
                if (!hole.IsActive && holes.Remove(hole))
                { var cause = hole.Cause; Record($"{Origin(cause)}：黑洞到期"); Pipeline.NotifyExecutionEnded(cause); }
            actuators.Advance(deltaTime);
            Render();
        }
        private void Record(string value)
        {
            events.Enqueue(value);
            while (events.Count > 6) events.Dequeue();
            eventText.text = "最近事件（最多 6 条）\n" + string.Join("\n", events);
        }
        private void Update() => Advance(Time.unscaledDeltaTime);
        private void Render()
        {
            if (rulesText != null && Pipeline != null)
            {
                var progress = new List<string>();
                foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
                    for (int slot = 0; slot < BodyRuntime.SlotCount; slot++)
                    {
                        var organ = Pipeline.GetBody(part).GetSlot(slot); var t = organ?.Definition.Trigger;
                        if (t == null || (t.Condition != TriggerOn.Kill && t.Condition != TriggerOn.Stationary)) continue;
                        progress.Add($"{part}{slot+1}: {Pipeline.TriggerProgress(part, slot):0.#}/{(t.Condition == TriggerOn.Kill ? t.KillThreshold : t.ChargeThreshold):g}");
                    }
                rulesText.text = progress.Count == 0 ? "只触发紧邻后一个器官（不自动攻击）；攻击条件监听紧邻前级。" : string.Join(" · ", progress.Take(3)) + (progress.Count > 3 ? " …" : "");
            }
            var lines = new List<string> { $"当前目标状态 · 有效装备 {supported} / 占位 {UnsupportedCount}" };
            for (int i = 0; i < 2; i++)
            {
                var s = statuses[i];
                lines.Add($"{(i == 0 ? "A" : "B")}：" + (!targets[i].IsAlive ? $"死亡，补充倒计时 {remaining[i]:0.0}s" :
                    $"流血 {s.BleedRemaining:0.0}s · 眩晕 {s.StunRemaining:0.0}s\n减速 {s.SlowStacks} 层 / {(1 - s.SlowMultiplier) * 100:0.#}% / 下次减层 {s.SlowRemaining:0.0}s"));
            }
            lines.Add($"预览角色 {PreviewHealth:0.#}/{PreviewMaxHealth:g} · 无敌 {previewInvulnerability:0.0}s · 吸盘 {attachedSlows.Count}");
            foreach (var execution in actuators.Active)
                if (execution is SummonExecution summon) lines.Add($"召唤物生命 {summon.Health:0.#}");
            lines.Add($"躯干：伤害×{PreviewTorso.Damage:g} 频率×{PreviewTorso.Frequency:g} 视野×{PreviewTorso.View:g}");
            if (holes.Count == 0) lines.Add("无存续黑洞");
            foreach (var h in holes)
            {
                string names = string.Join("、", targets.Where(t => t.IsAlive && ((Vector2)t.transform.position - h.Center).sqrMagnitude <= h.Cause.Radius * h.Cause.Radius).Select(t => t == targets[0] ? "A" : "B"));
                lines.Add($"{Origin(h.Cause)} {h.Remaining:0.0}s / {h.Cause.BlackHole.Duration:g}s · 半径 {h.Cause.Radius:g} · 范围内 { (names.Length == 0 ? "无" : names)}");
            }
            statusText.text = string.Join("\n", lines); RenderTargets();
            RenderProjectiles();
        }
        private RectTransform effectViewport;
        private GameObject triggerControls;
        private void CreateTriggerControls(Transform parent)
        {
            if (triggerControls != null) return;
            triggerControls = new GameObject("Trigger preview controls", typeof(RectTransform));
            triggerControls.transform.SetParent(parent, false);
            var root = (RectTransform)triggerControls.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            if (rulesText != null) { rulesText.fontSize = 15; rulesText.rectTransform.sizeDelta = new Vector2(560, 25); }
            root.anchoredPosition = new Vector2(20, -94); root.sizeDelta = new Vector2(560, 30);
            string[] names = { "受伤 -5", "回复 +5", "移动：关", "暴击：关" };
            for (int i = 0; i < names.Length; i++)
            {
                var go = new GameObject(names[i], typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(root, false);
                var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(i * 140, 0); rect.sizeDelta = new Vector2(132, 28);
                go.GetComponent<Image>().color = new Color(.15f, .25f, .32f);
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text)); labelGo.transform.SetParent(rect, false);
                var label = labelGo.GetComponent<Text>(); label.font = statusText.font; label.fontSize = 15; label.color = Color.white;
                label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false; label.text = names[i];
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                int index = i;
                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    if (index == 0) PreviewHurt();
                    else if (index == 1) PreviewHeal();
                    else if (index == 2) { PreviewMoving = !PreviewMoving; label.text = PreviewMoving ? "移动：开" : "移动：关"; }
                    else { previewCriticalChance = previewCriticalChance > 0 ? 0 : 1; label.text = previewCriticalChance > 0 ? "暴击：必定" : "暴击：关"; }
                });
            }
        }
        private readonly List<EffectVisual> effectVisuals = new List<EffectVisual>();
        private void RenderProjectiles()
        {
            CreateTriggerControls(targetImages[0].transform.parent);
            if (effectViewport == null)
            {
                var parent = (RectTransform)targetImages[0].transform.parent;
                CreateTriggerControls(parent);
                var go = new GameObject("Effect preview viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
                go.transform.SetParent(parent, false); effectViewport = (RectTransform)go.transform;
                effectViewport.anchorMin = effectViewport.anchorMax = effectViewport.pivot = new Vector2(0, 1);
                effectViewport.anchoredPosition = new Vector2(20, -130); effectViewport.sizeDelta = new Vector2(560, 140);
                go.GetComponent<Image>().color = new Color(.04f, .08f, .12f); go.GetComponent<Image>().raycastTarget = false;
                for (int i = 0; i < 2; i++)
                {
                    targetImages[i].rectTransform.anchoredPosition = new Vector2(30 + 280 * i, -280);
                    targetImages[i].rectTransform.sizeDelta = new Vector2(240, 64);
                    var text = targetLabels[i]; text.fontSize = 16; text.alignment = TextAnchor.MiddleCenter;
                    text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                    text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
                }
                statusText.rectTransform.anchoredPosition = new Vector2(20, -356); statusText.rectTransform.sizeDelta = new Vector2(560, 230);
                eventText.rectTransform.anchoredPosition = new Vector2(20, -602); eventText.rectTransform.sizeDelta = new Vector2(560, 160);
                for (int i = 0; i < 3; i++)
                {
                    var markerObject = new GameObject("Effect marker " + i, typeof(RectTransform), typeof(Image));
                    markerObject.transform.SetParent(effectViewport, false);
                    var marker = markerObject.GetComponent<Image>(); marker.raycastTarget = false;
                    marker.color = i == 0 ? Color.cyan : Color.green; marker.rectTransform.sizeDelta = Vector2.one * 7;
                    projectileMarkers.Add(marker);
                }
            }
            const float scale = 32;
            Vector2 Map(Vector2 position) => new Vector2(100, -70) + (position - playerPosition) * scale;
            for (int i = 0; i < projectileMarkers.Count; i++)
            {
                projectileMarkers[i].gameObject.SetActive(i == 0 || targets[i-1].IsAlive);
                projectileMarkers[i].rectTransform.localPosition = Map(i == 0 ? playerPosition : (Vector2)targets[i-1].transform.position);
            }
            effectVisuals.Clear();
            foreach (var execution in actuators.Active)
                if (execution is IEffectVisuals effects) effects.GetVisuals(effectVisuals);
                else effectVisuals.Add(new EffectVisual(execution.Position, execution.Position, execution.Context.Projectile.HitRadius, Color.yellow));
            for (int i = 0; i < effectVisuals.Count; i++)
            {
                if (i == projectileViews.Count)
                {
                    var go = new GameObject("Preview projectile", typeof(RectTransform), typeof(CanvasRenderer), typeof(EffectPreviewGraphic));
                    go.transform.SetParent(effectViewport, false);
                    var view = go.GetComponent<EffectPreviewGraphic>(); view.raycastTarget = false; projectileViews.Add(view);
                }
                var visual = effectVisuals[i]; var image = projectileViews[i];
                Vector2 from = Map(visual.From), to = Map(visual.To);
                image.gameObject.SetActive(true); image.Configure(visual.Line, visual.Color);
                image.rectTransform.localPosition = visual.Line ? (from + to) / 2 : from;
                image.rectTransform.localRotation = Quaternion.Euler(0, 0, visual.Line ? Mathf.Atan2(to.y-from.y, to.x-from.x) * Mathf.Rad2Deg : 0);
                image.rectTransform.sizeDelta = visual.Line ? new Vector2(Vector2.Distance(from, to), Mathf.Max(2, visual.Radius * 2 * scale))
                    : Vector2.one * Mathf.Max(4, visual.Radius * 2 * scale);
            }
            for (int i = effectVisuals.Count; i < projectileViews.Count; i++) projectileViews[i].gameObject.SetActive(false);
        }
        private void RenderTargets()
        {
            for (int i = 0; i < 2; i++)
            {
                var t = targets[i];
                targetLabels[i].text = $"目标 {t.TargetId}\n{t.CurrentHealth:0.##} / {t.MaxHealth:0.##} · {(t.IsAlive ? "存活" : "死亡")}\n位置 ({t.transform.position.x:g}, {t.transform.position.y:g})";
                targetImages[i].color = !t.IsAlive ? new Color32(80, 85, 90, 255) :
                    Time.unscaledTime < flashes[i] ? new Color32(175, 65, 65, 255) : new Color32(45, 115, 80, 255);
            }
        }
        private void DisposePipeline()
        {
            actuators.Started -= ExecutionStarted; actuators.Ended -= ExecutionEnded;
            Pipeline?.ResetTriggers(); PreviewMoving = false; previewCriticalChance = 0;
            if (triggerControls != null) Release(triggerControls); triggerControls = null;
            actuators.Clear();
            attachedSlows.Clear(); PreviewHealth = 50;
            foreach (var view in projectileViews) if (view != null) view.gameObject.SetActive(false);
            foreach (var view in projectileMarkers) if (view != null) view.gameObject.SetActive(false);
            if (Pipeline == null) return;
            Pipeline.enabled = false; Pipeline.TargetSelector = null;
            Pipeline.TargetValidator = null; Pipeline.TargetSnapshotProvider = null;
            Pipeline.TargetSelectorInRange = null; Pipeline.TargetSnapshotInRange = null;
            Pipeline.AttackProduced -= Attack; Pipeline.SlotExecuted -= Step;
            Pipeline.ActuatorProduced -= ExecuteActuator;
            Pipeline.TriggerActivated -= Trigger; Pipeline.TriggeredSlotExecuted -= ExtraStep; Pipeline.TriggerBlocked -= Blocked;
            Pipeline.gameObject.SetActive(false); Release(Pipeline.gameObject); Pipeline = null;
        }
        private static void Release(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
        private void Disconnect()
        {
            if (equipment != null) equipment.Changed -= Restart;
            if (restartButton != null) restartButton.onClick.RemoveListener(Restart);
            equipment = null; DisposePipeline(); holes.Clear(); deaths.Clear();
            foreach (var view in projectileViews) if (view != null) Release(view.gameObject);
            foreach (var view in projectileMarkers) if (view != null) Release(view.gameObject);
            projectileMarkers.Clear();
            if (effectViewport != null) Release(effectViewport.gameObject);
            if (triggerControls != null) Release(triggerControls); triggerControls = null;
            effectViewport = null;
            projectileViews.Clear();
            if (statuses != null) foreach (var status in statuses) status.Clear();
            if (targets != null) foreach (var t in targets) if (t != null)
            { t.Damaged -= Damaged; t.Killed -= Killed; t.gameObject.SetActive(false); Release(t.gameObject); }
            targets = null;
            if (privateScene.IsValid() && privateScene.isLoaded) SceneManager.UnloadSceneAsync(privateScene);
            privateScene = default;
        }
        private void OnDisable() => Disconnect();
    }
}
