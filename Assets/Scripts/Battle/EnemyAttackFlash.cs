using System.Collections;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人开火闪黄。
    ///
    /// 受伤反馈已经交给 <see cref="HitFlashSkin"/> 的红色蒙皮，这里**不再处理受伤**：
    /// 两边都改同一个 <see cref="SpriteRenderer.color"/> 时会互相覆盖，
    /// 而且白色闪与红色蒙皮叠在一起会发粉。
    ///
    /// 保留组件名与原有字段，兼容已有敌人 prefab（prefab 里只序列化了这两个字段）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/灰盒/敌人开火闪烁（EnemyAttackFlash）")]
    public class EnemyAttackFlash : MonoBehaviour
    {
        [Tooltip("闪烁时长，秒。")]
        [SerializeField] private float flashDuration = 0.15f;

        [Tooltip("闪烁到的最亮颜色。")]
        [SerializeField] private Color flashColor = new Color(1f, 0.95f, 0.6f, 1f);

        private EnemyAttack _attack;
        private SpriteRenderer _renderer;
        private Color _baseColor;
        private Coroutine _running;

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
        }

        private void OnDisable()
        {
            if (_attack != null)
                _attack.Attacked -= OnAttacked;

            if (_running != null)
                StopCoroutine(_running);

            if (_renderer != null)
                _renderer.color = _baseColor;

            _running = null;
        }

        private void OnAttacked(EnemyAttack.AttackInfo info)
        {
            if (_renderer == null || !isActiveAndEnabled)
                return;

            if (_running != null)
                StopCoroutine(_running);

            _running = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            var duration = Mathf.Max(0.01f, flashDuration);
            var color = flashColor;
            color.a = _baseColor.a;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                // 三角波：亮起再回到原色。
                var t = Mathf.PingPong(elapsed / (duration * 0.5f), 1f);
                _renderer.color = Color.Lerp(_baseColor, color, t);

                yield return null;
                elapsed += Time.deltaTime;
            }

            _renderer.color = _baseColor;
            _running = null;
        }
    }
}
