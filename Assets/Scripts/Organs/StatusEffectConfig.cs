using System;
using UnityEngine;

namespace SpringUp.Organs
{
    // 编辑用的独立状态资料卡；运行时读取不可变快照。
    [CreateAssetMenu(fileName = "NewStatus", menuName = "SpringUp/Status Config")]
    public sealed class StatusEffectConfig : ScriptableObject
    {
        [SerializeField] private StatusEffectKind kind;
        [SerializeField, Range(0f, 100f), Tooltip("命中后的施加概率。0 关闭，100 必定施加。")]
        private float chancePercent = 20f;
        [SerializeField, Min(0.05f), Tooltip("持续秒数；减速表示每减少一层的间隔。")]
        private float duration = 2f;
        [SerializeField, Min(0f), Tooltip("仅流血：每次扣血量。")]
        private float tickDamage = 2f;
        [SerializeField, Min(0.05f), Tooltip("仅流血：扣血间隔，单位秒。")]
        private float tickInterval = 1f;
        [SerializeField, Range(0f, 100f), Tooltip("仅减速：每层降低的基础移速百分比。")]
        private float slowPercentPerStack = 15f;
        [SerializeField, Range(1, 5)] private int maxStacks = 3;

        public StatusEffectKind Kind => kind;
        public bool TryCreateData(out StatusEffectData data, out string error)
        {
            data = null; error = null;
            try
            {
                data = new StatusEffectData(kind, chancePercent / 100f, duration,
                    kind == StatusEffectKind.Bleed ? tickDamage : 0f,
                    kind == StatusEffectKind.Bleed ? tickInterval : 1f,
                    kind == StatusEffectKind.Slow ? slowPercentPerStack / 100f : 0f,
                    kind == StatusEffectKind.Slow ? maxStacks : 1);
                return true;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                error = name + " 的状态参数无效：" + exception.ParamName;
                return false;
            }
        }
    }
}
