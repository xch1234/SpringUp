using System.Collections;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人开火闪黄、实际受伤闪白。统一控制颜色，受伤反馈优先于开火反馈。
    /// 保留组件名和原字段，兼容已有敌人 prefab。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/灰盒/敌人攻击与受伤闪烁（EnemyAttackFlash）")]
    public class EnemyAttackFlash : MonoBehaviour
    {
        [Tooltip("闪烁时长，秒。")]
        [SerializeField] private float flashDuration = 0.15f;

        [Tooltip("闪烁到的最亮颜色。")]
        [SerializeField] private Color flashColor = new Color(1f, 0.95f, 0.6f, 1f);

        [Tooltip("实际受伤后闪色并恢复原色的时长，秒。")]
        [SerializeField, Min(0.01f)] private float damageFlashDuration = 0.16f;

        [Tooltip("受伤闪色；优先于开火闪色。")]
        [SerializeField] private Color damageFlashColor = Color.white;

        private EnemyAttack _attack;
        private SpriteRenderer _renderer;
        private Color _baseColor;
        private Coroutine _running;
        private bool _damageFlashing;

        private void Awake()
        {
            _attack = GetComponent<EnemyAttack>();
            _renderer = GetComponent<SpriteRenderer>();

            if (_renderer == null)
            {
                Debug.LogWarning("[EnemyAttackFlash] 同一物体上没有 SpriteRenderer，闪烁不会有可见效果。", this);
                enabled = false;
                return;
            }

            _baseColor = _renderer.color;
        }

        private void OnEnable()
        {
            if (_renderer == null) return;
            _baseColor = _renderer.color;
            if (_attack != null)
                _attack.Attacked += OnAttacked;
            EnemyEvents.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (_attack != null)
                _attack.Attacked -= OnAttacked;
            EnemyEvents.Damaged -= OnDamaged;

            if (_running != null)
                StopCoroutine(_running);

            if (_renderer != null)
                _renderer.color = _baseColor;

            _running = null;
            _damageFlashing = false;
        }

        private void OnAttacked(EnemyAttack.AttackInfo info)
        {
            if (_renderer == null || !isActiveAndEnabled || _damageFlashing)
                return;

            if (_running != null)
                StopCoroutine(_running);

            _running = StartCoroutine(FlashRoutine(false));
        }

        private void OnDamaged(EnemyEvents.DamagedInfo info)
        {
            if (info.enemy != gameObject || info.damage <= 0f || _renderer == null || !isActiveAndEnabled)
                return;

            if (_running != null)
                StopCoroutine(_running);
            _damageFlashing = true;
            _running = StartCoroutine(FlashRoutine(true));
        }

        private IEnumerator FlashRoutine(bool damaged)
        {
            var duration = Mathf.Max(0.01f, damaged ? damageFlashDuration : flashDuration);
            var color = damaged ? damageFlashColor : flashColor;
            color.a = _baseColor.a;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                // 受伤当帧立即亮起再淡回；开火仍使用三角波。
                var t = damaged ? 1f - elapsed / duration : Mathf.PingPong(elapsed / (duration * 0.5f), 1f);
                _renderer.color = Color.Lerp(_baseColor, color, t);

                yield return null;
                elapsed += Time.deltaTime;
            }

            _renderer.color = _baseColor;
            _running = null;
            _damageFlashing = false;
        }
    }
}
