using System;

namespace TideBorne.Battle
{
    /// <summary>
    /// <see cref="PlayerStatsBase"/> 的最终值快照。
    /// 字段名沿用 <c>接口草案.md</c> §5.2 的表，便于与文档对照。
    /// </summary>
    [Serializable]
    public struct PlayerStatsSnapshot
    {
        public float baseCooldown;
        public float baseMaxHealth;
        public float moveSpeed;
        public float turnSpeed;
        public float chargeSpeed;
        public float viewRadius;
        public float pickupRadius;
        public float damageMultiplier;
        public float critChance;
        public float armor;
        public float healthRegen;
        public float damageTakenMultiplier;
        public int inventorySlots;
        public float medullaGain;

        public override string ToString()
        {
            return "baseCooldown=" + baseCooldown
                   + " baseMaxHealth=" + baseMaxHealth
                   + " moveSpeed=" + moveSpeed
                   + " turnSpeed=" + turnSpeed
                   + " chargeSpeed=" + chargeSpeed
                   + " viewRadius=" + viewRadius
                   + " pickupRadius=" + pickupRadius
                   + " damageMultiplier=" + damageMultiplier
                   + " critChance=" + critChance
                   + " armor=" + armor
                   + " healthRegen=" + healthRegen
                   + " damageTakenMultiplier=" + damageTakenMultiplier
                   + " inventorySlots=" + inventorySlots
                   + " medullaGain=" + medullaGain;
        }
    }
}
