using UnityEngine;
using UnityEngine.UI;
namespace SpringUp.Flow
{
    // The prefab owns layout; application supplies business callbacks.
    public sealed class FlowWindow : MonoBehaviour
    {
        public Text title;
        public Transform body;
        public CanvasGroup group;
        public Text AddText(string caption)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(body, false);
            var text = go.GetComponent<Text>(); text.font = title.font; text.fontSize = 18;
            text.color = Color.white; text.text = caption;
            // Text supplies its wrapped preferred height after the layout assigns its width.
            go.GetComponent<LayoutElement>().minHeight = 28;
            return text;
        }
        public Button AddButton(string caption, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(body, false);
            go.GetComponent<LayoutElement>().preferredHeight = 34;
            go.GetComponent<Image>().color = new Color(.14f, .3f, .34f);
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)labelGo.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<Text>(); label.font = title.font; label.text = caption;
            label.fontSize = 18; label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
            var button = go.GetComponent<Button>(); button.onClick.AddListener(action); return button;
        }
    }
}
