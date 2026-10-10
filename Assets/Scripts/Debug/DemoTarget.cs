using System;
using UnityEngine;

namespace SpringUp.Organs
{
    // 临时测试敌人。以后由程序 C 的正式敌人接替。
    [DisallowMultipleComponent]
    public sealed class DemoTarget : MonoBehaviour
    {
        [SerializeField, Tooltip("每个目标必须使用不同编号，例如 target_a、target_b。")]
        private string targetId = "target_a";

        [SerializeField, Min(1f)] private float maxHealth = 20f;
        [SerializeField, Tooltip("取消勾选后不会被黑洞吸引，例如不能被推拉的 Boss。")]
        private bool canBeDisplaced = true;
        private float currentHealth;
        private SpriteRenderer spriteRenderer;
        private float hitFlashRemaining;

        public string TargetId => targetId;
        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public bool IsAlive => isActiveAndEnabled && currentHealth > 0f;
        public bool CanBeDisplaced => canBeDisplaced;

        public bool TryDisplace(Vector2 displacement)
        {
            if (!IsAlive || !canBeDisplaced || float.IsNaN(displacement.x) || float.IsInfinity(displacement.x)
                || float.IsNaN(displacement.y) || float.IsInfinity(displacement.y)) return false;
            transform.position += new Vector3(displacement.x, displacement.y, 0f);
            return true;
        }

        // 保留原攻击及链上下文，由接入方决定是否转发给触发器。
        public event Action<DemoTarget, CastEvent, float> Damaged;
        public event Action<DemoTarget, CastEvent> Killed;

        private void Awake() => ResetHealth();

        // 供测试脚本设置样例数据。重新启用物体不会自动复活。
        public void InitializeForDemo(string id, float health)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("目标编号不能为空。", nameof(id));
            if (float.IsNaN(health) || float.IsInfinity(health) || health <= 0f)
                throw new ArgumentOutOfRangeException(nameof(health));
            targetId = id;
            maxHealth = health;
            ResetHealth();
        }

        private void ResetHealth()
        {
            currentHealth = maxHealth;
            hitFlashRemaining = 0f;
            spriteRenderer = GetComponent<SpriteRenderer>();
            SetColor(Color.green);
        }

        public bool TryApplyAttack(CastEvent attack)
        {
            // 也拒绝 default(CastEvent)，防止未生成的攻击被误用。
            if (!IsAlive || string.IsNullOrWhiteSpace(targetId) || attack.TargetId != targetId
                || float.IsNaN(attack.Damage) || float.IsInfinity(attack.Damage) || attack.Damage <= 0f
                || string.IsNullOrWhiteSpace(attack.OriginInstanceId))
                return false;

            float appliedDamage = Mathf.Min(currentHealth, attack.Damage);
            currentHealth = Mathf.Max(0f, currentHealth - attack.Damage);
            bool killedByThisAttack = currentHealth == 0f;
            hitFlashRemaining = killedByThisAttack ? 0f : 0.2f;
            SetColor(killedByThisAttack ? Color.gray : Color.red);

            // 先更新血量，再发通知。死亡目标即使马上再收到攻击，也不会重复死亡。
            Debug.Log($"[拳头测试] {targetId} 扣血 {appliedDamage}，剩余 {currentHealth}/{maxHealth}。来源：{attack.OriginDefinitionId}，实例：{attack.OriginInstanceId}。链：{attack.ChainId}，深度：{attack.ChainDepth}", this);
            Damaged?.Invoke(this, attack, appliedDamage);
            if (killedByThisAttack)
            {
                Debug.Log($"[拳头测试] {targetId} 已死亡。", this);
                Killed?.Invoke(this, attack);
            }
            return true;
        }

        private void Update()
        {
            if (hitFlashRemaining <= 0f) return;
            hitFlashRemaining -= Time.deltaTime;
            if (hitFlashRemaining <= 0f && currentHealth > 0f) SetColor(Color.green);
        }

        private void SetColor(Color color)
        {
            if (spriteRenderer != null) spriteRenderer.color = color;
        }

        private void OnValidate()
        {
            if (float.IsNaN(maxHealth) || float.IsInfinity(maxHealth)) maxHealth = 20f;
            maxHealth = Mathf.Max(1f, maxHealth);
        }
    }
}
