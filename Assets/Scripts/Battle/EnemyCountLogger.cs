using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 灰盒阶段的刷怪诊断：定期把存活敌人数量打到 Console。
    ///
    /// **为什么需要**：实测出现过「能碰到敌人但看不到」的情况，
    /// 这时"敌人到底有没有生成"靠肉眼无法区分——是没生成，还是生成了但看不见。
    /// 这个组件把答案写成数字，避免下一次又在两种可能之间猜。
    /// 正式版不需要它，可随时删除。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/灰盒/刷怪诊断（EnemyCountLogger）")]
    public class EnemyCountLogger : MonoBehaviour
    {
        [Tooltip("打印间隔，秒。0 表示只在首次生成时打印一次。")]
        [SerializeField] private float interval = 5f;

        [Tooltip("出现第一个敌人时立刻打印一次，确认刷怪路径通了。")]
        [SerializeField] private bool logFirstSpawn = true;

        private float _remaining;
        private bool _loggedFirstSpawn;

        private void Start()
        {
            _remaining = interval;
        }

        private void Update()
        {
            if (logFirstSpawn && !_loggedFirstSpawn && EnemyRuntime.ActiveCount > 0)
            {
                _loggedFirstSpawn = true;
                Debug.Log("[EnemyCountLogger] 刷怪路径已通，场上出现第一个敌人。");
            }

            if (interval <= 0f)
                return;

            _remaining -= Time.deltaTime;
            if (_remaining > 0f)
                return;

            _remaining = interval;

            var first = EnemyRuntime.ActiveEnemies.Count > 0 ? EnemyRuntime.ActiveEnemies[0] : null;
            var positionText = first != null
                ? "，首个敌人位置 " + first.Position + " 距离玩家 " +
                  Vector2.Distance(first.Position, PlayerLocator.PlayerPosition).ToString("0.0") + " 米"
                : string.Empty;

            Debug.Log("[EnemyCountLogger] 场上敌人 " + EnemyRuntime.ActiveCount + " 个" + positionText);
        }
    }
}
