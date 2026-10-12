using UnityEngine;
using UnityEngine.UI;

namespace Tideflesh.Systems
{
    public sealed class RunUIFont : MonoBehaviour
    {
        private Font font;
        private void Start()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
            var driver = GetComponent<SpringUpRunDriver>();
            foreach (var text in driver.runStatus.canvas.GetComponentsInChildren<Text>(true)) text.font = font;
        }
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
