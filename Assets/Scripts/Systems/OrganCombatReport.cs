using System.Collections.Generic;
using System.Linq;
using SpringUp.Organs;
using UnityEngine;

namespace Tideflesh.Systems
{
    // Wave-local, bounded diagnostics. Never retain enemies or shared mutable CastContexts.
    public sealed class OrganCombatReport : MonoBehaviour
    {
        public const int HistoryLimit = 64, SourceLimit = 128;
        public readonly struct Entry
        {
            public readonly string Source, Chain, Message;
            public readonly int Depth;
            public Entry(string source, CastContext context, string message)
            { Source = source; Chain = context?.ChainId; Depth = context?.Depth ?? 0; Message = message; }
        }
        private static readonly BodyPart[] Parts = { BodyPart.Head, BodyPart.Hand, BodyPart.Leg };
        private readonly Queue<Entry> history = new();
        private readonly Dictionary<string, float> damage = new();
        private readonly Dictionary<string, string> names = new();
        private readonly float[,] flashes = new float[3, BodyRuntime.SlotCount];
        private OrganPipelineController pipeline;
        public IReadOnlyCollection<Entry> History => history;
        public IReadOnlyDictionary<string, float> Damage => damage;
        public string Latest { get; private set; } = "等待器官发动";
        public int Revision { get; private set; }
        public bool IsFlashing(BodyPart part, int slot) => (int)part >= 0 && (int)part < 3 && slot >= 0 && slot < BodyRuntime.SlotCount
            && Time.unscaledTime < flashes[(int)part, slot];
        public void Bind(OrganPipelineController value)
        {
            Unbind(); pipeline = value;
            if (pipeline == null) return;
            pipeline.AttackProduced += Attack;
            pipeline.ActuatorProduced += Actuator;
            pipeline.TriggerActivated += Trigger;
            pipeline.TriggerBlocked += Blocked;
        }
        private void Unbind()
        {
            if (pipeline == null) return;
            pipeline.AttackProduced -= Attack; pipeline.ActuatorProduced -= Actuator;
            pipeline.TriggerActivated -= Trigger; pipeline.TriggerBlocked -= Blocked;
            pipeline = null;
        }
        private void Add(string source, CastContext context, string message)
        {
            if (history.Count == HistoryLimit) history.Dequeue();
            history.Enqueue(new Entry(source, context, message)); Latest = message; Revision++;
        }
        private void Pulse(BodyPart part, int slot) => flashes[(int)part, slot] = Time.unscaledTime + .35f;
        private void Attack(CastEvent hit)
        {
            foreach (BodyPart part in Parts)
                for (int slot = 0; slot < BodyRuntime.SlotCount; slot++)
                {
                    var organ = pipeline.GetBody(part).GetSlot(slot);
                    if (organ?.InstanceId != hit.OriginInstanceId) continue;
                    Pulse(part, slot);
                    Add(organ.InstanceId, hit.Context, Position(part, slot) + " " + organ.Definition.DisplayName + " 发动");
                    return;
                }
        }
        private static string Position(BodyPart part, int slot) => (part == BodyPart.Head ? "头" : part == BodyPart.Hand ? "手" : "腿") + (slot + 1);
        private void Actuator(ActuatorContext value) => Attack(value.Cause);
        private void Trigger(BodyRuntime body, int slot, SpringUp.Organs.OrganInstance organ, CastContext context)
        {
            Pulse(body.Part, slot);
            Add(organ.InstanceId, context, Position(body.Part, slot) + " " + organ.Definition.DisplayName + " 触发连锁（链深 " + context.Depth + "）");
        }
        private void Blocked(BodyRuntime body, int slot, SpringUp.Organs.OrganInstance organ, CastContext context, TriggerBlockReason reason)
            => Add(organ.InstanceId, context, Position(body.Part, slot) + " 连锁保护：" + reason);
        public void RecordDamage(CastEvent hit, float actual)
        {
            if (!(actual > 0) || float.IsInfinity(actual) || string.IsNullOrEmpty(hit.OriginDefinitionId)) return;
            string id = hit.OriginDefinitionId;
            if (!damage.ContainsKey(id) && damage.Count >= SourceLimit - 1) id = "__other";
            damage.TryGetValue(id, out float previous); damage[id] = previous + actual;
            Revision++;
        }
        public void SetNames(IEnumerable<SpringUp.Organs.OrganDefinition> definitions)
        { names.Clear(); foreach (var d in definitions) names[d.Id] = d.DisplayName; }
        public string Describe()
        {
            var rows = damage.OrderByDescending(x => x.Value).Take(6)
                .Select(x => (names.TryGetValue(x.Key, out var name) ? name : x.Key == "__other" ? "其他" : x.Key) + "：" + x.Value.ToString("0.#"));
            return "本波实际伤害（同种器官合计，前六名）\n" + string.Join("  /  ", rows)
                + "\n最近发动顺序（相同编号表示同一连锁）\n" + string.Join("\n", history.Reverse().Take(4).Reverse().Select(x => "[" + (x.Chain == null ? "—" : x.Chain.Substring(0, System.Math.Min(6, x.Chain.Length))) + "] " + x.Message));
        }
        public void ResetWave()
        { history.Clear(); damage.Clear(); System.Array.Clear(flashes, 0, flashes.Length); Latest = "等待器官发动"; Revision++; }
        private void OnDestroy() => Unbind();
    }
}
