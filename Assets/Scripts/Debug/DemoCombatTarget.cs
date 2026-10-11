using UnityEngine;

namespace SpringUp.Organs
{
    // 临时状态及移动演示；正式敌人由程序 C 接入 StatusEffectRuntime。
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DemoTarget))]
    public sealed class DemoCombatTarget : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("测试敌人的基础移动速度，单位/秒。")]
        private float moveSpeed = 1f;
        [SerializeField, Min(0.1f), Tooltip("在出生点上下各移动多远。只是测试移动，不是正式敌人 AI。")]
        private float travelDistance = 1.5f;

        private DemoTarget target;
        private float travelled;
        public StatusEffectRuntime Statuses { get; } = new StatusEffectRuntime();
        public float CurrentSpeed => isActiveAndEnabled && Target.IsAlive ? moveSpeed * Statuses.SpeedMultiplier : 0f;
        private DemoTarget Target
        {
            get
            {
                if (target == null) target = GetComponent<DemoTarget>();
                return target;
            }
        }

        private void OnEnable() => Target.Killed += OnKilled;
        private void OnDisable()
        {
            Target.Killed -= OnKilled;
            ClearStatuses();
        }
        private void OnKilled(DemoTarget dead, CastEvent attack) => ClearStatuses();
        public void ClearStatuses() => Statuses.Clear();

        public bool TryApplyAttack(CastEvent attack)
        {
            if (!isActiveAndEnabled || !Target.TryApplyAttack(attack)) return false;
            if (Target.IsAlive && attack.Status != null)
                Statuses.TryApply(attack, Random.value);
            return true;
        }

        private void Update() => Advance(Time.deltaTime);

        // 检查工具也走同一个入口，不用模拟 Unity 生命周期消息。
        public void Advance(float deltaTime)
        {
            if (!isActiveAndEnabled) return;
            float movingSeconds = Statuses.MovementSeconds(deltaTime);
            Statuses.Tick(deltaTime, attack => Target.TryApplyAttack(attack), () => Target.IsAlive);
            if (!Target.IsAlive) return;
            float before = Mathf.PingPong(travelled + travelDistance, travelDistance * 2f);
            travelled += moveSpeed * movingSeconds;
            float after = Mathf.PingPong(travelled + travelDistance, travelDistance * 2f);
            // 只增加本帧的主动移动，保留黑洞等外力已经造成的位移，避免下一帧跳回出生点。
            transform.position += Vector3.up * (after - before);
        }

        public string StatusSummary => $"速度 {CurrentSpeed:0.00} | 眩晕 {Statuses.StunRemaining:0.0}s"
            + $" | 流血 {Statuses.BleedRemaining:0.0}s | 减速 {Statuses.SlowStacks} 层 / 下次减层 {Statuses.SlowRemaining:0.0}s";

        private void OnValidate()
        {
            if (float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed)) moveSpeed = 1f;
            if (float.IsNaN(travelDistance) || float.IsInfinity(travelDistance)) travelDistance = 1.5f;
            moveSpeed = Mathf.Max(0f, moveSpeed);
            travelDistance = Mathf.Max(0.1f, travelDistance);
        }
    }
}
