namespace TideBorne.Battle
{
    /// <summary>
    /// <see cref="PlayerStatsBase"/> 中可被器官与躯干升级树改动的字段。
    /// 取值与 <c>接口草案.md</c> §5.2 的字段表一一对应；新增字段必须同步该表。
    /// </summary>
    public enum StatField
    {
        /// <summary>全局节拍基准，单位秒。执行模型用。</summary>
        BaseCooldown = 0,

        /// <summary>基础血量，单位点。</summary>
        BaseMaxHealth = 1,

        /// <summary>移速，单位米/秒。</summary>
        MoveSpeed = 2,

        /// <summary>转向速度，单位度/秒。</summary>
        TurnSpeed = 3,

        /// <summary>直线冲锋速度，单位米/秒。</summary>
        ChargeSpeed = 4,

        /// <summary>可见范围，单位米。</summary>
        ViewRadius = 5,

        /// <summary>拾取范围，单位米。</summary>
        PickupRadius = 6,

        /// <summary>伤害乘区，倍率。</summary>
        DamageMultiplier = 7,

        /// <summary>暴击率，0–1。</summary>
        CritChance = 8,

        /// <summary>固定减伤，单位点。</summary>
        Armor = 9,

        /// <summary>回血速度，单位点/秒。</summary>
        HealthRegen = 10,

        /// <summary>受伤倍率。</summary>
        DamageTakenMultiplier = 11,

        /// <summary>背包格，单位格。</summary>
        InventorySlots = 12,

        /// <summary>髓质收益，倍率。</summary>
        MedullaGain = 13
    }

    /// <summary>
    /// 对单个 <see cref="StatField"/> 的一条修改。
    /// 加法区处理「视野 −30%」这类绝对增减，乘法区处理「暴击率 ×3」这类倍率
    /// （见 <c>接口草案.md</c> §5.4 甲，暂定两层）。
    /// </summary>
    public readonly struct StatModifier
    {
        /// <summary>被修改的字段。</summary>
        public readonly StatField Field;

        /// <summary>加法区的增量，或乘法区的倍率。</summary>
        public readonly float Value;

        public StatModifier(StatField field, float value)
        {
            Field = field;
            Value = value;
        }
    }

    /// <summary>
    /// 能向 <see cref="PlayerStatsBase"/> 提供修改的来源。
    /// 器官（程序 A）与躯干升级树各实现一份，取值时由 <see cref="PlayerStatsBase"/> 汇总。
    /// </summary>
    public interface IStatModifierSource
    {
        /// <summary>加法区：最终值先叠加这些增量。</summary>
        void CollectAdditiveModifiers(StatModifierCollector collector);

        /// <summary>乘法区：加法区之后按这些倍率相乘。</summary>
        void CollectMultiplicativeModifiers(StatModifierCollector collector);
    }
}
