using System.Collections.Generic;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 「属性」表：可被改的**上限与系数**，与「运行时状态」（<see cref="PlayerRuntime"/>）分开，不合并。
    /// 字段与 <c>接口草案.md</c> §5.2 的 <c>PlayerStats</c> 表一一对应。
    ///
    /// 谁是基础值的唯一来源：躯干与躯干升级树（见 §5.2「谁能改」列）。
    /// 本组件只负责**取值**：基础值经加法区、乘法区两层得到最终值。
    /// 器官（程序 A）与躯干升级树各自实现 <see cref="IStatModifierSource"/> 挂到 <see cref="ModifierSources"/>。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战场/玩家属性（PlayerStats）")]
    public class PlayerStatsBase : MonoBehaviour
    {
        [Header("基础值（唯一来源：躯干与躯干升级树）")]
        [Tooltip("全局节拍基准，秒。执行模型用；下限由 clampBaseCooldown 保护。")]
        [SerializeField] private float baseCooldown = 1f;

        [Tooltip("基础血量，点。")]
        [SerializeField] private float baseMaxHealth = 100f;

        [Tooltip("移速，米/秒。")]
        [SerializeField] private float moveSpeed = 5f;

        [Tooltip("转向速度，度/秒。")]
        [SerializeField] private float turnSpeed = 360f;

        [Tooltip("直线冲锋速度，米/秒。")]
        [SerializeField] private float chargeSpeed = 0f;

        [Tooltip("可见范围，米。")]
        [SerializeField] private float viewRadius = 10f;

        [Tooltip("拾取范围，米。")]
        [SerializeField] private float pickupRadius = 2f;

        [Tooltip("伤害乘区，倍率。")]
        [SerializeField] private float damageMultiplier = 1f;

        [Tooltip("暴击率，0–1，最终值会被夹到这个区间。")]
        [SerializeField] private float critChance = 0f;

        [Tooltip("固定减伤，点。")]
        [SerializeField] private float armor = 0f;

        [Tooltip("回血速度，点/秒。")]
        [SerializeField] private float healthRegen = 0f;

        [Tooltip("受伤倍率。")]
        [SerializeField] private float damageTakenMultiplier = 1f;

        [Tooltip("背包格，格。归程序 D 使用。")]
        [SerializeField] private int inventorySlots = 5;

        [Tooltip("髓质收益，倍率。归程序 D 使用。")]
        [SerializeField] private float medullaGain = 1f;

        [Header("边界规则（接口草案 §5.5）")]
        [Tooltip("baseCooldown 的下限，防止器官把节拍压到 0。")]
        [SerializeField] private float minBaseCooldown = 0.05f;

        [Tooltip("moveSpeed 的下限，必须大于 0。")]
        [SerializeField] private float minMoveSpeed = 0.1f;

        [Header("修改来源（器官 / 躯干升级树实现 IStatModifierSource）")]
        [Tooltip("挂在此处的组件会在每次取值时向属性表提交修改。为空即表示无修改。")]
        [SerializeField] private MonoBehaviour[] modifierSourceComponents = new MonoBehaviour[0];

        private readonly StatModifierCollector _collector = new StatModifierCollector();
        private readonly List<IStatModifierSource> _sources = new List<IStatModifierSource>();

        /// <summary>
        /// 取值缓存；每帧被取值的字段第一次访问时重算一次。
        /// **必须用 NaN 初始化**：<see cref="Read"/> 用 <c>IsNaN</c> 判断"尚未计算"，
        /// 而 <c>new float[n]</c> 的初值是 0——那会让所有属性在第一次访问时直接返回 0，
        /// 基础值永远读不到（表现为「所有属性恒为 0」）。
        /// </summary>
        private readonly float[] _cache = CreateEmptyCache();

        /// <summary>收集器是否已按当前来源重建。</summary>
        private bool _collectorDirty = true;

        /// <summary>加法区 + 乘法区两层结构（接口草案 §5.4 甲）。</summary>
        private StatModifierCollector ModifierCollector
        {
            get
            {
                if (_collectorDirty)
                    RebuildCollector();

                return _collector;
            }
        }

        /// <summary>最终值：全局节拍基准，秒。</summary>
        public float BaseCooldown { get { return Read(StatField.BaseCooldown); } }

        /// <summary>最终值：基础血量，点。</summary>
        public float BaseMaxHealth { get { return Read(StatField.BaseMaxHealth); } }

        /// <summary>最终值：移速，米/秒。</summary>
        public float MoveSpeed { get { return Read(StatField.MoveSpeed); } }

        /// <summary>最终值：转向速度，度/秒。</summary>
        public float TurnSpeed { get { return Read(StatField.TurnSpeed); } }

        /// <summary>最终值：直线冲锋速度，米/秒。</summary>
        public float ChargeSpeed { get { return Read(StatField.ChargeSpeed); } }

        /// <summary>最终值：可见范围，米。</summary>
        public float ViewRadius { get { return Read(StatField.ViewRadius); } }

        /// <summary>最终值：拾取范围，米。</summary>
        public float PickupRadius { get { return Read(StatField.PickupRadius); } }

        /// <summary>最终值：伤害乘区，倍率。</summary>
        public float DamageMultiplier { get { return Read(StatField.DamageMultiplier); } }

        /// <summary>最终值：暴击率，已夹到 0–1。</summary>
        public float CritChance { get { return Read(StatField.CritChance); } }

        /// <summary>最终值：固定减伤，点。</summary>
        public float Armor { get { return Read(StatField.Armor); } }

        /// <summary>最终值：回血速度，点/秒。</summary>
        public float HealthRegen { get { return Read(StatField.HealthRegen); } }

        /// <summary>最终值：受伤倍率。</summary>
        public float DamageTakenMultiplier { get { return Read(StatField.DamageTakenMultiplier); } }

        /// <summary>最终值：背包格，四舍五入到整数。</summary>
        public int InventorySlots { get { return Mathf.RoundToInt(Read(StatField.InventorySlots)); } }

        /// <summary>最终值：髓质收益，倍率。</summary>
        public float MedullaGain { get { return Read(StatField.MedullaGain); } }

        /// <summary>
        /// 属性发生变化时触发，载荷是属性快照。
        /// 变更通知走事件而不是轮询，见 <c>接口草案.md</c> §5.4 丙（实验室要实时预览数值）。
        /// </summary>
        public event System.Action<PlayerStatsSnapshot> StatsChanged;

        /// <summary>建一个全部为 NaN 的取值缓存，表示"每个字段都还没算过"。</summary>
        private static float[] CreateEmptyCache()
        {
            var cache = new float[StatModifierCollector.StatFieldCount];
            for (var i = 0; i < cache.Length; i++)
                cache[i] = float.NaN;

            return cache;
        }

        /// <summary>取某个字段的最终值。新增字段时这里的分支必须同步。</summary>
        public float GetStat(StatField field)
        {
            return Read(field);
        }

        /// <summary>当前属性快照，供通知与预览使用。</summary>
        public PlayerStatsSnapshot CreateSnapshot()
        {
            var snapshot = new PlayerStatsSnapshot
            {
                baseCooldown = BaseCooldown,
                baseMaxHealth = BaseMaxHealth,
                moveSpeed = MoveSpeed,
                turnSpeed = TurnSpeed,
                chargeSpeed = ChargeSpeed,
                viewRadius = ViewRadius,
                pickupRadius = PickupRadius,
                damageMultiplier = DamageMultiplier,
                critChance = CritChance,
                armor = Armor,
                healthRegen = HealthRegen,
                damageTakenMultiplier = DamageTakenMultiplier,
                inventorySlots = InventorySlots,
                medullaGain = MedullaGain
            };
            return snapshot;
        }

        /// <summary>让下一次取值重新收集修改；装 / 拆器官后调用。</summary>
        public void InvalidateModifiers()
        {
            _collectorDirty = true;
        }

        /// <summary>在编辑器里拖动数值时立即失效缓存，避免看到旧值。</summary>
        private void OnValidate()
        {
            _collectorDirty = true;
        }

        private void Awake()
        {
            _collectorDirty = true;
        }

        private void RebuildCollector()
        {
            _collector.Clear();
            _sources.Clear();

            if (modifierSourceComponents != null)
            {
                for (var i = 0; i < modifierSourceComponents.Length; i++)
                {
                    var source = modifierSourceComponents[i] as IStatModifierSource;
                    if (source != null)
                        _sources.Add(source);
                }
            }

            for (var i = 0; i < _sources.Count; i++)
                _collector.AddSource(_sources[i]);

            for (var i = 0; i < _cache.Length; i++)
                _cache[i] = float.NaN;

            _collectorDirty = false;
        }

        private float Read(StatField field)
        {
            var index = (int)field;
            var cached = _cache[index];
            if (!float.IsNaN(cached))
                return cached;

            var value = Clamp(field, ModifierCollector.Apply(field, ReadBaseValue(field)));
            _cache[index] = value;
            return value;
        }

        private float ReadBaseValue(StatField field)
        {
            switch (field)
            {
                case StatField.BaseCooldown: return baseCooldown;
                case StatField.BaseMaxHealth: return baseMaxHealth;
                case StatField.MoveSpeed: return moveSpeed;
                case StatField.TurnSpeed: return turnSpeed;
                case StatField.ChargeSpeed: return chargeSpeed;
                case StatField.ViewRadius: return viewRadius;
                case StatField.PickupRadius: return pickupRadius;
                case StatField.DamageMultiplier: return damageMultiplier;
                case StatField.CritChance: return critChance;
                case StatField.Armor: return armor;
                case StatField.HealthRegen: return healthRegen;
                case StatField.DamageTakenMultiplier: return damageTakenMultiplier;
                case StatField.InventorySlots: return inventorySlots;
                case StatField.MedullaGain: return medullaGain;
                default:
                    Debug.LogError("[PlayerStatsBase] 未处理的属性字段：" + field, this);
                    return 0f;
            }
        }

        /// <summary>边界规则（接口草案 §5.5）：只夹有明确下限 / 区间的字段。</summary>
        private float Clamp(StatField field, float value)
        {
            switch (field)
            {
                case StatField.MoveSpeed:
                    return Mathf.Max(value, minMoveSpeed);
                case StatField.BaseCooldown:
                    return Mathf.Max(value, minBaseCooldown);
                case StatField.CritChance:
                    return Mathf.Clamp01(value);
                default:
                    return value;
            }
        }

        /// <summary>比较两个快照是否有实质差异，用于决定要不要发变更事件。</summary>
        internal static bool AreSnapshotsEqual(PlayerStatsSnapshot left, PlayerStatsSnapshot right)
        {
            const float Epsilon = 0.0001f;

            if (left.inventorySlots != right.inventorySlots)
                return false;

            return Mathf.Abs(left.baseCooldown - right.baseCooldown) <= Epsilon
                   && Mathf.Abs(left.baseMaxHealth - right.baseMaxHealth) <= Epsilon
                   && Mathf.Abs(left.moveSpeed - right.moveSpeed) <= Epsilon
                   && Mathf.Abs(left.turnSpeed - right.turnSpeed) <= Epsilon
                   && Mathf.Abs(left.chargeSpeed - right.chargeSpeed) <= Epsilon
                   && Mathf.Abs(left.viewRadius - right.viewRadius) <= Epsilon
                   && Mathf.Abs(left.pickupRadius - right.pickupRadius) <= Epsilon
                   && Mathf.Abs(left.damageMultiplier - right.damageMultiplier) <= Epsilon
                   && Mathf.Abs(left.critChance - right.critChance) <= Epsilon
                   && Mathf.Abs(left.armor - right.armor) <= Epsilon
                   && Mathf.Abs(left.healthRegen - right.healthRegen) <= Epsilon
                   && Mathf.Abs(left.damageTakenMultiplier - right.damageTakenMultiplier) <= Epsilon
                   && Mathf.Abs(left.medullaGain - right.medullaGain) <= Epsilon;
        }

        /// <summary>供 <see cref="PlayerRuntime"/> 在血量被夹取后广播变更。</summary>
        internal void RaiseStatsChanged(PlayerStatsSnapshot snapshot)
        {
            var handler = StatsChanged;
            if (handler != null)
                handler(snapshot);
        }
    }
}
