using System;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Organs;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpringUp.Laboratory
{
    // 只接线：A 负责节拍、命中、状态、黑洞寿命及触发链。这里不执行位移。
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
        public Vector2 explosionCenter = Vector2.zero;
        public OrganPipelineController Pipeline { get; private set; }
        public int UnsupportedCount { get; private set; }
        public int TriggerCount { get; private set; }
        public int DeathCount { get; private set; }
        public IReadOnlyDictionary<string, string> InventoryOrigins => inventoryOrigins;
        public IReadOnlyList<BlackHoleBehaviour> BlackHoles => holes;
        public StatusEffectRuntime StatusAt(int index) => statuses[index];
        private ILaboratoryEquipment equipment;
        private readonly Queue<string> events = new Queue<string>();
        private readonly Queue<CastEvent> deaths = new Queue<CastEvent>();
        private readonly Dictionary<string, string> origins = new Dictionary<string, string>();
        private readonly Dictionary<string, string> inventoryOrigins = new Dictionary<string, string>();
        private readonly List<BlackHoleBehaviour> holes = new List<BlackHoleBehaviour>();
        private StatusEffectRuntime[] statuses;
        private float[] remaining, flashes;
        private bool resolving, draining;
        private int generation, supported;
        private Scene privateScene;
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
            if (rulesText != null) rulesText.text = $"真实配置结算预览；爆炸中心 ({explosionCenter.x:g}, {explosionCenter.y:g})。\n固定目标，不展示移动；非完整战场模拟。";
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
            for (int p = 0; p < 4; p++)
                for (int s = 0; s < equipment.SlotCount((LaboratoryPart)p); s++)
                {
                    var item = equipment.GetSlot((LaboratoryPart)p, s);
                    if (item == null) continue;
                    var definition = Resolve(item);
                    if (definition == null || s >= BodyRuntime.SlotCount) { UnsupportedCount++; continue; }
                    var instance = new OrganInstance(definition);
                    Pipeline.GetBody(definition.Part).SetSlot(s, instance);
                    origins.Add(instance.InstanceId, $"{LaboratoryPresenter.PartNames[p]}{s + 1} {item.Name}");
                    inventoryOrigins.Add(instance.InstanceId, item.InstanceId);
                    supported++;
                }
            Pipeline.TargetSelector = () => targets.FirstOrDefault(t => t.IsAlive)?.TargetId;
            Pipeline.AttackProduced += Attack; Pipeline.SlotExecuted += Step;
            Pipeline.TriggerActivated += Trigger; Pipeline.TriggeredSlotExecuted += ExtraStep; Pipeline.TriggerBlocked += Blocked;
            Record("等待节拍；单目标按 A → B 选择存活目标。");
            Render();
        }
        private void Replenish(int i)
        {
            statuses[i].Clear(); remaining[i] = flashes[i] = 0;
            targets[i].transform.position = i == 0 ? firstPosition : secondPosition;
            // 每一代都是新身份，旧攻击不能命中新目标；初始化不发死亡或奖励信号。
            targets[i].InitializeForDemo((i == 0 ? "A" : "B") + "#" + (++generation), targetHealth);
        }
        private void Attack(CastEvent attack)
        {
            if (attack.Shape == AttackShape.BlackHole)
            {
                holes.RemoveAll(h => h.Cause.OriginInstanceId == attack.OriginInstanceId);
                holes.Add(new BlackHoleBehaviour(attack, explosionCenter));
                Record($"{Origin(attack)}：生成黑洞 {attack.BlackHole.Duration:g}s"); return;
            }
            var snapshot = targets.Select(t => new AttackTarget(t.TargetId, t.transform.position, t.IsAlive)).ToArray();
            var hits = AttackResolver.Resolve(attack, snapshot, explosionCenter, out _);
            resolving = true;
            try
            {
                foreach (var hit in hits)
                {
                    int i = Array.FindIndex(targets, t => t.TargetId == hit.TargetId);
                    if (i >= 0 && targets[i].TryApplyAttack(hit) && targets[i].IsAlive)
                        statuses[i].TryApply(hit, UnityEngine.Random.value);
                }
                if (attack.Shape == AttackShape.Explosion) Record($"{Origin(attack)}：范围命中 {hits.Count} 个，半径 {attack.Radius:g}");
            }
            finally { resolving = false; }
            DrainDeaths();
        }
        private string Origin(CastEvent attack) => origins.TryGetValue(attack.OriginInstanceId, out var name) ? name : attack.OriginDefinitionId;
        private void Step(BodyRuntime body, int slot, OrganInstance organ) => Record($"节拍：{origins[organ.InstanceId]}");
        private void ExtraStep(BodyRuntime body, int slot, OrganInstance organ, CastContext context)
            => Record($"追加执行：{origins[organ.InstanceId]}");
        private void Trigger(BodyRuntime body, int slot, OrganInstance organ, CastContext context)
        {
            TriggerCount++; Record($"击杀触发：{origins[organ.InstanceId]} → 本部位");
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
            deaths.Enqueue(attack);
            if (!resolving) DrainDeaths();
        }
        private void DrainDeaths()
        {
            if (draining) return;
            draining = true;
            try { while (deaths.Count > 0) Pipeline.PublishSignal(new TriggerSignal(TriggerOn.Kill, deaths.Dequeue())); }
            finally { draining = false; }
        }
        // 独立推进状态和补充，不改变管道游标；检查也调用此入口。
        public void Advance(float deltaTime)
        {
            if (equipment == null || Pipeline == null) return;
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
                    statuses[i].Tick(deltaTime, hit => targets[index].TryApplyAttack(hit), () => targets[index].IsAlive);
                }
            }
            finally { resolving = false; }
            DrainDeaths();
            for (int i = holes.Count - 1; i >= 0; i--)
            {
                holes[i].Tick(deltaTime);
                if (!holes[i].IsActive) { Record($"{Origin(holes[i].Cause)}：黑洞到期"); holes.RemoveAt(i); }
            }
            Render();
        }
        private void Record(string value)
        {
            events.Enqueue(value);
            while (events.Count > 6) events.Dequeue();
            eventText.text = "最近事件（最多 6 条）\n" + string.Join("\n", events);
        }
        private void Update() => Advance(Time.deltaTime);
        private void Render()
        {
            var lines = new List<string> { $"当前目标状态 · 有效装备 {supported} / 占位 {UnsupportedCount}" };
            for (int i = 0; i < 2; i++)
            {
                var s = statuses[i];
                lines.Add($"{(i == 0 ? "A" : "B")}：" + (!targets[i].IsAlive ? $"死亡，补充倒计时 {remaining[i]:0.0}s" :
                    $"流血 {s.BleedRemaining:0.0}s · 眩晕 {s.StunRemaining:0.0}s\n减速 {s.SlowStacks} 层 / {(1 - s.SlowMultiplier) * 100:0.#}% / 下次减层 {s.SlowRemaining:0.0}s"));
            }
            lines.Add("黑洞：此预览不展示位移（目标固定）");
            if (holes.Count == 0) lines.Add("无存续黑洞");
            foreach (var h in holes)
            {
                string names = string.Join("、", targets.Where(t => t.IsAlive && ((Vector2)t.transform.position - h.Center).sqrMagnitude <= h.Cause.Radius * h.Cause.Radius).Select(t => t == targets[0] ? "A" : "B"));
                lines.Add($"{Origin(h.Cause)} {h.Remaining:0.0}s / {h.Cause.BlackHole.Duration:g}s · 半径 {h.Cause.Radius:g} · 范围内 { (names.Length == 0 ? "无" : names)}");
            }
            statusText.text = string.Join("\n", lines); RenderTargets();
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
            if (Pipeline == null) return;
            Pipeline.enabled = false; Pipeline.TargetSelector = null;
            Pipeline.AttackProduced -= Attack; Pipeline.SlotExecuted -= Step;
            Pipeline.TriggerActivated -= Trigger; Pipeline.TriggeredSlotExecuted -= ExtraStep; Pipeline.TriggerBlocked -= Blocked;
            Pipeline.gameObject.SetActive(false); Release(Pipeline.gameObject); Pipeline = null;
        }
        private static void Release(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
        private void Disconnect()
        {
            if (equipment != null) equipment.Changed -= Restart;
            if (restartButton != null) restartButton.onClick.RemoveListener(Restart);
            equipment = null; DisposePipeline(); holes.Clear(); deaths.Clear();
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
