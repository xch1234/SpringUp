using System;
using UnityEngine;

namespace SpringUp.Organs
{
    [CreateAssetMenu(menuName = "SpringUp/Trigger Config")]
    public sealed class OrganTriggerConfig : ScriptableObject
    {
        [SerializeField] private string organId = "trigger_hit";
        [SerializeField] private string displayName = "击中触发";
        [SerializeField] private TriggerOn condition = TriggerOn.Hit;
        [SerializeField] private TriggerTargetKind triggerTarget = TriggerTargetKind.FollowingSlots;
        [SerializeField, Range(0, 1)] private float probability = .3f;
        [SerializeField, Min(.01f)] private float delay = .5f;
        [SerializeField, Min(1)] private int killThreshold = 10;
        [SerializeField, Min(.01f)] private float chargeRate = 1;
        [SerializeField, Min(.01f)] private float chargeThreshold = 10;
        public string DisplayName => displayName;
        public bool TryCreateDefinition(out OrganDefinition definition, out string error)
        {
            definition = null; error = null;
            try
            {
                var settings = new TriggerSettings(condition, probability, delay, killThreshold, chargeRate, chargeThreshold);
                definition = new OrganDefinition(organId, displayName, OrganType.Trigger,
                    triggerOn: condition, triggerTarget: triggerTarget, trigger: settings);
                return true;
            }
            catch (ArgumentException ex) { error = displayName + ": " + ex.Message; return false; }
        }
    }

    public sealed class TriggerSettings
    {
        public TriggerOn Condition { get; }
        public float Probability { get; }
        public float Delay { get; }
        public int KillThreshold { get; }
        public float ChargeRate { get; }
        public float ChargeThreshold { get; }
        public TriggerSettings(TriggerOn condition, float probability = .3f, float delay = .5f,
            int killThreshold = 10, float chargeRate = 1, float chargeThreshold = 10)
        {
            if (!Enum.IsDefined(typeof(TriggerOn), condition)) throw new ArgumentOutOfRangeException(nameof(condition));
            if (!Finite(probability) || probability < 0 || probability > 1) throw new ArgumentOutOfRangeException(nameof(probability));
            if (!Finite(delay) || delay <= 0) throw new ArgumentOutOfRangeException(nameof(delay));
            if (killThreshold < 1) throw new ArgumentOutOfRangeException(nameof(killThreshold));
            if (!Finite(chargeRate) || chargeRate <= 0) throw new ArgumentOutOfRangeException(nameof(chargeRate));
            if (!Finite(chargeThreshold) || chargeThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(chargeThreshold));
            Condition = condition; Probability = probability; Delay = delay; KillThreshold = killThreshold;
            ChargeRate = chargeRate; ChargeThreshold = chargeThreshold;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public string Description => Condition switch
        {
            TriggerOn.Hit => $"实际命中后 {Probability:P0} 概率触发。",
            TriggerOn.Started => $"最近前级执行器开始 {Delay:g} 秒后触发。",
            TriggerOn.Kill => $"每击杀 {KillThreshold} 个敌人触发，扣除对应计数。",
            TriggerOn.Hurt => "实际受伤后触发。",
            TriggerOn.Heal => "实际回复生命后触发。",
            TriggerOn.Ended => "最近前级执行器正常结束后触发；清场取消不触发。",
            TriggerOn.Debuff => $"成功施加流血、眩晕或减速后 {Probability:P0} 概率触发。",
            TriggerOn.Critical => "实际暴击命中后触发。",
            _ => $"静止每秒积累 {ChargeRate:g} 点，满 {ChargeThreshold:g} 点消耗并触发；移动清零。"
        };
    }
}
