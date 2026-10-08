using System;
using UnityEngine;

namespace SpringUp.Organs
{
    public enum HandOrganKind
    {
        [InspectorName("拳头")] Fist = 0,
        [InspectorName("钢管")] SteelPipe = 1,
        [InspectorName("多肢触手")] MultiTentacle = 2,
        [InspectorName("锈刀")] RustKnife = 3,
        [InspectorName("粘液腺")] SlimeGland = 4,
        [InspectorName("坍缩体")] CollapseBody = 5,
        [InspectorName("TNT")] Tnt = 6
    }

    // 编辑用的资料。开局转换成运行时快照，不把战斗状态写回资源。
    [CreateAssetMenu(fileName = "NewOrgan", menuName = "SpringUp/Organ Config")]
    public sealed class OrganConfig : ScriptableObject
    {
        [SerializeField, Tooltip("七种手部器官的独立效果。停止 Play 后修改配置。")]
        private HandOrganKind kind;

        [SerializeField, Min(0f), Tooltip("基础伤害。0 表示不造成攻击伤害。多肢触手和坍缩体不使用此值；黑洞只吸引，不扣血。")]
        private float damage = 5f;

        [Header("范围爆炸：仅 TNT 使用")]
        [SerializeField, Min(0f), Tooltip("爆炸半径，单位为 Unity 世界单位。以角色为中心，包含边界，每个敌人只扣一次血。")]
        private float explosionRadius = 2f;

        [Header("黑洞：仅坍缩体使用")]
        [SerializeField, Min(0f), Tooltip("吸引半径，包含边界。0 只覆盖中心点。")]
        private float blackHoleRadius = 3f;
        [SerializeField, Min(0.05f), Tooltip("黑洞存在的秒数。默认 5 秒。同一个器官再次发动会替换自己的旧黑洞。")]
        private float blackHoleDuration = 5f;
        [SerializeField, Min(0f), Tooltip("吸引速度，世界单位/秒。0 表示只显示黑洞，不移动敌人。")]
        private float blackHolePullSpeed = 2f;

        [Header("状态：钢管 / 锈刀 / 粘液腺使用")]
        [SerializeField, Range(0f, 100f), Tooltip("每次直接命中的施加概率。0 表示关闭附带状态，100 表示必定施加。")]
        private float statusChancePercent = 20f;
        [SerializeField, Min(0.05f), Tooltip("持续秒数；粘液腺表示每减少一层的间隔。重复施加会刷新倒计时。")]
        private float statusDuration = 2f;
        [Header("流血：仅锈刀使用")]
        [SerializeField, Min(0f), Tooltip("每次流血扣血量；不是每秒伤害。")]
        private float bleedDamage = 2f;
        [SerializeField, Min(0.05f), Tooltip("两次流血扣血之间的秒数。刷新状态不会推迟已经排好的下一次扣血。")]
        private float bleedInterval = 1f;
        [Header("减速：仅粘液腺使用")]
        [SerializeField, Range(0f, 100f), Tooltip("每层减少基础移速的百分比。三层 15 表示共减速 45%。")]
        private float slowPercentPerStack = 15f;
        [SerializeField, Range(1, 5), Tooltip("最大层数。每过一个 Status Duration 减少一层，直到归零。")]
        private int maxSlowStacks = 3;

        public HandOrganKind Kind => kind;
        public float Damage => damage;
        public string DisplayName
        {
            get
            {
                switch (kind)
                {
                    case HandOrganKind.Fist: return "拳头";
                    case HandOrganKind.SteelPipe: return "钢管";
                    case HandOrganKind.MultiTentacle: return "多肢触手";
                    case HandOrganKind.RustKnife: return "锈刀";
                    case HandOrganKind.SlimeGland: return "粘液腺";
                    case HandOrganKind.CollapseBody: return "坍缩体";
                    case HandOrganKind.Tnt: return "TNT";
                    default: return "未知器官";
                }
            }
        }

        public bool TryCreateDefinition(out OrganDefinition definition, out string error)
        {
            definition = null;
            error = null;
            if (!Enum.IsDefined(typeof(HandOrganKind), kind))
                error = "器官种类无效。";
            else if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f)
                error = DisplayName + "的伤害必须是有限的非负数。";
            else if (kind == HandOrganKind.CollapseBody && (float.IsNaN(blackHoleRadius)
                || float.IsInfinity(blackHoleRadius) || blackHoleRadius < 0f))
                error = "黑洞半径必须是有限的非负数。";
            else if (kind == HandOrganKind.Tnt && (float.IsNaN(explosionRadius)
                || float.IsInfinity(explosionRadius) || explosionRadius < 0f))
                error = "TNT 的爆炸半径必须是有限的非负数。";
            if (error != null) return false;

            bool trigger = kind == HandOrganKind.MultiTentacle;
            string id = trigger ? "multi_tentacle" : kind == HandOrganKind.Fist ? "fist"
                : kind == HandOrganKind.SteelPipe ? "steel_pipe" : kind == HandOrganKind.RustKnife ? "rust_knife"
                : kind == HandOrganKind.Tnt ? "tnt" : kind == HandOrganKind.CollapseBody ? "collapse_body" : "slime_gland";
            StatusEffectData status = null;
            BlackHoleData blackHole = null;
            try
            {
                if (kind == HandOrganKind.CollapseBody)
                    blackHole = new BlackHoleData(blackHoleDuration, blackHolePullSpeed);
                else if (kind == HandOrganKind.SteelPipe)
                    status = new StatusEffectData(StatusEffectKind.Stun, statusChancePercent / 100f, statusDuration);
                else if (kind == HandOrganKind.RustKnife)
                    status = new StatusEffectData(StatusEffectKind.Bleed, statusChancePercent / 100f,
                        statusDuration, bleedDamage, bleedInterval);
                else if (kind == HandOrganKind.SlimeGland)
                    status = new StatusEffectData(StatusEffectKind.Slow, statusChancePercent / 100f,
                        statusDuration, slowPerStack: slowPercentPerStack / 100f, maxStacks: maxSlowStacks);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                error = DisplayName + "的参数无效：" + exception.ParamName + "。请检查概率、时间、伤害、速度或层数。";
                return false;
            }
            definition = new OrganDefinition(id, DisplayName, BodyPart.Hand,
                trigger ? OrganType.Trigger : OrganType.Actuator, trigger || blackHole != null ? 0f : damage, status: status,
                shape: blackHole != null ? AttackShape.BlackHole : kind == HandOrganKind.Tnt ? AttackShape.Explosion : AttackShape.SingleTarget,
                radius: blackHole != null ? blackHoleRadius : kind == HandOrganKind.Tnt ? explosionRadius : 0f,
                blackHole: blackHole);
            return true;
        }
    }
}
