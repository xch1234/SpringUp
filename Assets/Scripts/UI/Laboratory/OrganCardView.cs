using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpringUp.Laboratory
{
    public sealed class OrganCardView : MonoBehaviour
    {
        public Button button;
        public Text caption;

        public void Bind(string text, Action selected)
        {
            caption.text = text;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => selected());
        }
    }
}
