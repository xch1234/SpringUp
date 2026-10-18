using System;
using UnityEngine;

namespace SpringUp.Organs
{
    // 新执行器共用的配置入口。只生成只读定义，不保存某次攻击的状态。
    public abstract class OrganActuatorConfig : ScriptableObject
    {
        [SerializeField] private string organId = "straight_projectile";
        [SerializeField] private string displayName = "直线子弹";
        [SerializeField, Min(0f)] private float damage = 5f;
        [SerializeField, HideInInspector] private float chargeCost = 20f;
        [SerializeField, Min(.1f), Tooltip("索敌范围：以玩家位置为中心的世界单位距离。只决定选谁攻击，不改变爆炸半径、弹体寿命或激光长度。")]
        private float targetingRange = 6f;
        protected float TargetingRange => targetingRange;
        public string DisplayName => displayName;
        protected string OrganId => organId;
        protected float Damage => damage;
        protected float ChargeCost => chargeCost;
        protected abstract OrganDefinition CreateDefinition();

        public bool TryCreateDefinition(out OrganDefinition definition, out string error)
        {
            definition = null;
            error = null;
            try { definition = CreateDefinition(); return true; }
            catch (ArgumentException exception)
            { error = DisplayName + "参数无效：" + exception.Message; return false; }
        }
    }
}
