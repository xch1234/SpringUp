using System.Collections.Generic;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEngine;

namespace Tideflesh.Systems
{
    public sealed class TorsoEquipmentRuntime : MonoBehaviour, IStatModifierSource
    {
        public TorsoLoadout Loadout { get; } = new TorsoLoadout();
        private PlayerRuntime player;
        private PlayerMotor motor;
        public void Configure(PlayerRuntime owner)
        {
            if (player != null) player.Stats.RemoveRuntimeSource(this);
            player = owner; motor = owner.GetComponent<PlayerMotor>(); player.Stats.AddRuntimeSource(this);
        }
        public void Apply(IEnumerable<TorsoSettings> values, OrganPipelineController pipeline)
        {
            Loadout.Set(values);
            if (player != null)
            {
                player.Stats.InvalidateModifiers(); player.RefreshStats();
                player.InvulnerabilityMultiplier = Loadout.Invulnerability;
                if (motor != null) motor.DirectionalSteering = Loadout.DirectionalMovement;
            }
            if (pipeline != null) { pipeline.TriggerProbabilityMultiplier = Loadout.Probability; pipeline.TriggerFrequencyMultiplier = Loadout.Frequency; }
        }
        public void CollectAdditiveModifiers(StatModifierCollector collector) { }
        public void CollectMultiplicativeModifiers(StatModifierCollector collector)
        {
            collector.Multiply(StatField.BaseMaxHealth, Loadout.MaxHealth);
            collector.Multiply(StatField.DamageMultiplier, Loadout.Damage);
            collector.Multiply(StatField.MoveSpeed, Loadout.MoveSpeed);
            collector.Multiply(StatField.TurnSpeed, Loadout.TurnSpeed);
            collector.Multiply(StatField.ViewRadius, Loadout.View);
            collector.Multiply(StatField.DamageTakenMultiplier, Loadout.IncomingDamage);
            collector.Multiply(StatField.CritChance, Loadout.Probability);
            collector.Multiply(StatField.BaseCooldown, 1 / Loadout.Frequency);
            collector.Multiply(StatField.BaseCycleDuration, 1 / Loadout.Frequency);
        }
        private void OnDestroy()
        {
            if (player != null) { player.Stats.RemoveRuntimeSource(this); player.InvulnerabilityMultiplier = 1; }
            if (motor != null) motor.DirectionalSteering = false;
        }
    }
}
