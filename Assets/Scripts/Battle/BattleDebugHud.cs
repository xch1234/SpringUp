using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 灰盒调试 HUD：屏幕左上角显示镜头与敌人的关键数字。
    ///
    /// **为什么需要**：连续几轮排查都卡在"看不到就说不清"上——
    /// 「镜头到底有没有跟随」「敌人是被卡住还是没刷出来」这两类问题，
    /// 靠肉眼描述会来回猜；把数字摆到屏幕上，一眼就能判定，而且能回报具体数值。
    ///
    /// 显示三类信息：
    /// 1. **镜头 vs 玩家偏移**：跟随正常时应恒定为 `offset`（默认 (0,0,-10)）。
    ///    若 Z 以外的分量随时间变大，就是跟随没生效——这是"跟随"的硬判据。
    /// 2. **敌人最近距离 / 数量**：判断"刷出来后迟迟不出现"是生成问题还是卡住问题。
    /// 3. **帧率**：帧率极低时 `deltaTime` 变大会让移动看起来异常，顺手排除这个变量。
    ///
    /// **边界**：与 <see cref="WaveHud"/> 一样属灰盒诊断件，正式 HUD 归程序 B，
    /// 接手时整个文件删掉即可。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/灰盒/调试 HUD（BattleDebugHud）")]
    public class BattleDebugHud : MonoBehaviour
    {
        [Tooltip("界面边距，像素。")]
        [SerializeField] private float margin = 16f;

        [Tooltip("字号缩放。高分辨率下需要调大。")]
        [SerializeField] private float fontScale = 1.4f;

        [Tooltip("文字颜色。")]
        [SerializeField] private Color textColor = new Color(0.85f, 1f, 0.85f, 0.95f);

        [Tooltip("背景颜色（含透明度）。")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);

        [Tooltip("判定「离得还远」的距离阈值，米。超过它才有「卡住」一说。")]
        [SerializeField] private float stuckDistanceThreshold = 8f;

        [Tooltip("判定「没在动」的速度阈值，米/秒。")]
        [SerializeField] private float stuckSpeedThreshold = 0.2f;

        private GUIStyle _style;
        private float _smoothedDeltaTime;

        /// <summary>跟随目标与相机的偏移。跟随正常时应恒等于相机 offset。</summary>
        public Vector3 TargetOffset { get; private set; }

        /// <summary>场上敌人中离玩家最近的距离；没有敌人时为 -1。</summary>
        public float NearestEnemyDistance { get; private set; }

        /// <summary>
        /// 是否疑似有敌人被卡住：某个敌人离玩家还远，但实际速度接近 0。
        ///
        /// **这是"敌人迟迟不出现"的硬判据**：光看距离分不清「没刷出来」和「刷出来了但卡住」，
        /// 加上速度就能分开——离得远且不动 = 卡住；离得远但在动 = 只是还在路上。
        /// </summary>
        public bool SuspectEnemyStuck { get; private set; }

        /// <summary>被判定为疑似卡住的那个敌人到玩家的距离；无则为 -1。</summary>
        public float StuckCandidateDistance { get; private set; } = -1f;

        private void Update()
        {
            // 平滑一下帧率读数，避免数字乱跳。
            _smoothedDeltaTime = Mathf.Lerp(_smoothedDeltaTime <= 0f ? Time.unscaledDeltaTime : _smoothedDeltaTime,
                Time.unscaledDeltaTime, 0.1f);

            UpdateCameraOffset();
            UpdateNearestEnemyDistance();
        }

        private void UpdateCameraOffset()
        {
            var camera = Camera.main;
            var player = PlayerLocator.Player;

            TargetOffset = camera != null && player != null
                ? camera.transform.position - player.position
                : Vector3.zero;
        }

        private void UpdateNearestEnemyDistance()
        {
            var nearest = float.MaxValue;
            SuspectEnemyStuck = false;
            StuckCandidateDistance = -1f;

            var playerPosition = PlayerLocator.PlayerPosition;
            var enemies = EnemyRuntime.ActiveEnemies;

            for (var i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive)
                    continue;

                var distance = Vector2.Distance(enemy.Position, playerPosition);
                if (distance < nearest)
                    nearest = distance;

                // 只把"离得还远"的算作疑似卡住：已到攻击距离的敌人本来就该停下。
                if (distance > stuckDistanceThreshold && GetEnemySpeed(enemy) < stuckSpeedThreshold)
                {
                    SuspectEnemyStuck = true;
                    if (distance > StuckCandidateDistance)
                        StuckCandidateDistance = distance;
                }
            }

            NearestEnemyDistance = nearest == float.MaxValue ? -1f : nearest;
        }

        private static float GetEnemySpeed(EnemyRuntime enemy)
        {
            var body = enemy.GetComponent<Rigidbody2D>();
            return body != null ? body.linearVelocity.magnitude : 0f;
        }

        private void OnGUI()
        {
            EnsureStyle();
            DrawBox(BuildLines());
        }

        private string[] BuildLines()
        {
            var camera = Camera.main;
            var player = PlayerLocator.Player;

            var cameraText = camera != null
                ? camera.transform.position.ToString("0.00")
                : "(无相机)";
            var playerText = player != null
                ? player.position.ToString("0.00")
                : "(无玩家)";

            var offsetText = TargetOffset.ToString("0.00") +
                             (IsFollowOffsetOk() ? "  ✅跟随中" : "  ❌偏移异常");

            var follow = camera != null ? camera.GetComponent<CameraFollow>() : null;
            var followText = follow == null
                ? "相机上没有 CameraFollow"
                : "挂于 " + follow.OwnerName +
                  (string.IsNullOrEmpty(follow.OwnerParentName) ? "" : "(父:" + follow.OwnerParentName + ")") +
                  "，目标 " + (follow.HasTarget ? follow.TargetName : "(空)") +
                  "，LateUpdate x" + follow.LateUpdateCount;

            var nearestText = NearestEnemyDistance < 0f
                ? "无"
                : NearestEnemyDistance.ToString("0.0") + " 米";

            var stuckText = SuspectEnemyStuck
                ? "⚠ 疑似卡住 " + StuckCandidateDistance.ToString("0.0") + " 米"
                : "无";

            return new[]
            {
                "相机 " + cameraText,
                "玩家 " + playerText,
                "偏移 " + offsetText,
                "跟随 " + followText,
                "敌人 " + EnemyRuntime.ActiveCount + " 个，最近 " + nearestText,
                "卡住 " + stuckText,
                "帧率 " + (Time.unscaledDeltaTime > 0f ? (1f / Time.unscaledDeltaTime).ToString("0") : "?")
            };
        }

        /// <summary>
        /// 跟随是否正常：相机与玩家的偏移在 X/Y 上应接近 0（只留 Z 的相机距离）。
        /// 阈值 1 米——跟随正常时偏移恒为 (0,0,-10)，根本不会接近 1 米。
        /// </summary>
        private bool IsFollowOffsetOk()
        {
            var planar = new Vector2(TargetOffset.x, TargetOffset.y);
            return planar.magnitude < 1f;
        }

        private void DrawBox(string[] lines)
        {
            const float LineHeight = 20f;
            const float Padding = 10f;

            var lineHeight = LineHeight * fontScale;
            var boxWidth = 330f * fontScale;
            var boxHeight = lineHeight * lines.Length + Padding * 2f;

            var rect = new Rect(margin, margin, boxWidth, boxHeight);

            var previousColor = GUI.color;
            GUI.color = backgroundColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;

            for (var i = 0; i < lines.Length; i++)
            {
                var lineRect = new Rect(
                    rect.x + Padding,
                    rect.y + Padding + lineHeight * i,
                    rect.width - Padding * 2f,
                    lineHeight);

                GUI.Label(lineRect, lines[i], _style);
            }
        }

        private void EnsureStyle()
        {
            if (_style != null)
                return;

            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(13f * fontScale),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = textColor }
            };
        }
    }
}
