using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 灰盒诊断 HUD：屏幕右上角显示波次倒计时与场上敌人数。
    ///
    /// **为什么需要**：波次计时在做完之前一直"看不到"，于是无法验收——
    /// 看不到的东西没法判断对不对。把倒计时摆到屏幕上，验收就变成肉眼一眼的事。
    ///
    /// **边界说明**：正式 HUD 属程序 B（`任务拆解.md` 程序 D 负责外框与 HUD）。
    /// 这里用即时模式的 <c>OnGUI</c> 是最小实现——不依赖 Canvas、TextMeshPro 字体资产与
    /// EventSystem，也就不会和程序 B 将来的界面打架。程序 B 接手时**整个文件删掉即可**，
    /// 不需要改动任何别的代码（它只读 <see cref="WaveDirector"/> 的公开属性，不反向依赖）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/灰盒/波次 HUD（WaveHud）")]
    public class WaveHud : MonoBehaviour
    {
        [Tooltip("波次计时。留空则自动在场景里找。")]
        [SerializeField] private WaveDirector director;

        [Tooltip("界面边距，像素。")]
        [SerializeField] private float margin = 16f;

        [Tooltip("字号缩放。高分辨率下需要调大。")]
        [SerializeField] private float fontScale = 1.6f;

        [Tooltip("文字颜色。")]
        [SerializeField] private Color textColor = new Color(1f, 1f, 1f, 0.95f);

        [Tooltip("背景颜色（含透明度），保证文字在任何画面上都读得清。")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);

        private GUIStyle _style;

        /// <summary>波次计时器。</summary>
        public WaveDirector Director
        {
            get
            {
                if (director == null)
                    director = FindFirstObjectByType<WaveDirector>();

                return director;
            }
        }

        private void OnGUI()
        {
            // GUI.skin 只在 OnGUI 里可靠，不能在 Awake/字段初始化里取。
            EnsureStyle();

            var waveDirector = Director;
            if (waveDirector == null)
            {
                DrawBox(new[] { "找不到 WaveDirector" });
                return;
            }

            DrawBox(BuildLines(waveDirector));
        }

        /// <summary>拼出要显示的几行文字。抽出来便于单独核对内容。</summary>
        private static string[] BuildLines(WaveDirector waveDirector)
        {
            var lines = new string[3];

            lines[0] = "第 " + waveDirector.CurrentWave + " / " + waveDirector.TotalWaves + " 波";

            switch (waveDirector.State)
            {
                case WaveState.Running:
                    // 保留一位小数：验收"60 秒到点"时，整秒跳变不够直观。
                    lines[1] = "倒计时 " + waveDirector.RemainingTime.ToString("0.0") + " 秒";
                    break;

                case WaveState.Complete:
                    lines[1] = "本波已结算";
                    break;

                case WaveState.AllWavesFinished:
                    lines[1] = "全部波次结束";
                    break;

                default:
                    lines[1] = "尚未开始";
                    break;
            }

            lines[2] = "场上敌人 " + EnemyRuntime.ActiveCount;

            return lines;
        }

        private void DrawBox(string[] lines)
        {
            const float LineHeight = 22f;
            const float Padding = 10f;

            var lineHeight = LineHeight * fontScale;
            var boxWidth = 230f * fontScale;
            var boxHeight = lineHeight * lines.Length + Padding * 2f;

            var rect = new Rect(
                Screen.width - boxWidth - margin,
                margin,
                boxWidth,
                boxHeight);

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
                fontSize = Mathf.RoundToInt(14f * fontScale),
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = textColor }
            };
        }
    }
}
