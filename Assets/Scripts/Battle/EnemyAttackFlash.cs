using System.Collections;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 灰盒阶段的敌人开火表现：出手瞬间把方块闪一下。
    ///
    /// **为什么现在必须有**：初版 <see cref="EnemyAttack"/> 只扣血、只抛事件，没有任何视觉反馈，
    /// 实测表现是"看不到开火动作，只看到自己掉血"——玩家无法判断敌人什么时候在打自己。
    /// 正式版这里会换成美术 B 的特效模板（按载荷类型复用），本组件是**临时占位**，
    /// 灰盒验收通过后可以整体删掉。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyAttack))]
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
            if (_attack != null)
                _attack.Attacked += OnAttacked;
        }

        private void OnDisable()
        {
            if (_attack != null)
                _attack.Attacked -= OnAttacked;

            if (_renderer != null)
                _renderer.color = _baseColor;

            _running = null;
        }

        private void OnAttacked(EnemyAttack.AttackInfo info)
        {
            if (_renderer == null || !gameObject.activeInHierarchy)
                return;

            if (_running != null)
                StopCoroutine(_running);

            _running = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            var elapsed = 0f;
            while (elapsed < flashDuration)
            {
                elapsed += Time.deltaTime;

                // 0 → 1 → 0 的三角波：亮起再熄灭。
                var t = Mathf.PingPong(elapsed / (flashDuration * 0.5f), 1f);
                _renderer.color = Color.Lerp(_baseColor, flashColor, t);

                yield return null;
            }

            _renderer.color = _baseColor;
            _running = null;
        }
    }
}
