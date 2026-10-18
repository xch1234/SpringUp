using System;
using TideBorne.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpringUp.Flow
{
    public sealed class MenuSettingsView : MonoBehaviour
    {
        public Button[] buttons;
        public Slider music, sfx;
        public Text musicValue, sfxValue, hint;
        public Button[] bindings;
        public RectTransform panel;
        public Vector2 designSize;
        public float screenHeightFraction = .9f;
        int listening = -1, startFrame;
        bool navigationWasEnabled, restoreNavigation;
        public bool IsListening => listening >= 0;
        public void Fit()
        {
            if (panel == null || designSize.x <= 0) return;
            var root = (RectTransform)transform;
            float scale = Mathf.Min(root.rect.width * .94f / designSize.x, root.rect.height * screenHeightFraction / designSize.y);
            panel.localScale = Vector3.one * Mathf.Min(1, scale);
        }
        void LateUpdate()
        {
            Fit();
            if (restoreNavigation)
            { if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = navigationWasEnabled; restoreNavigation = false; }
        }
        public void BindMenu(Action start, Action settings, Action quit)
        { buttons[0].onClick.AddListener(() => start()); buttons[1].onClick.AddListener(() => settings()); buttons[2].onClick.AddListener(() => quit()); }
        public void BindSettings(Action close)
        {
            buttons[0].onClick.AddListener(() => { CancelBinding(); GameSettings.RestoreDefaults(); });
            buttons[1].onClick.AddListener(() => close());
            music.onValueChanged.AddListener(value => GameSettings.SetVolumes(value, GameSettings.Sfx));
            sfx.onValueChanged.AddListener(value => { GameSettings.SetVolumes(GameSettings.Music, value); GameAudio.Instance?.PreviewSfx(); });
            for (int i = 0; i < bindings.Length; i++)
            {
                int index = i;
                bindings[i].onClick.AddListener(() => BeginBinding(index));
            }
            GameSettings.Changed += Refresh;
            Refresh();
        }
        void Refresh()
        {
            music.SetValueWithoutNotify(GameSettings.Music); sfx.SetValueWithoutNotify(GameSettings.Sfx);
            musicValue.text = Mathf.RoundToInt(GameSettings.Music * 100) + "%";
            sfxValue.text = Mathf.RoundToInt(GameSettings.Sfx * 100) + "%";
            for (int i = 0; i < bindings.Length; i++) bindings[i].GetComponentInChildren<Text>().text = GameSettings.Display((GameBinding)i);
        }
        public void BeginBinding(int index)
        {
            listening = index; startFrame = Time.frameCount;
            if (EventSystem.current != null)
            { navigationWasEnabled = EventSystem.current.sendNavigationEvents; EventSystem.current.sendNavigationEvents = false; }
            foreach (var button in GetComponentsInChildren<Button>()) button.interactable = false;
            music.interactable = sfx.interactable = false;
            EventSystem.current?.SetSelectedGameObject(null);
            bindings[index].GetComponentInChildren<Text>().text = "请按键…";
            hint.text = "按下新按键；Esc / 鼠标右键取消。";
        }
        public void CancelBinding(string message = "已取消改键。")
        {
            if (!IsListening) return;
            int previous = listening; listening = -1;
            foreach (var button in GetComponentsInChildren<Button>()) button.interactable = true;
            music.interactable = sfx.interactable = true;
            Refresh(); hint.text = message;
            restoreNavigation = true;
            EventSystem.current?.SetSelectedGameObject(bindings[previous].gameObject);
        }
        // Called by FlowApplication before global shortcuts, so a recorded key is consumed this frame.
        public void CaptureKey()
        {
            if (!IsListening || Time.frameCount <= startFrame) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true || Mouse.current?.rightButton.wasPressedThisFrame == true)
            { CancelBinding(); return; }
            if (Keyboard.current == null) return;
            foreach (var key in Keyboard.current.allKeys)
                if (key.wasPressedThisFrame)
                {
                    bool ok = GameSettings.TryBind((GameBinding)listening, key.keyCode, out var error);
                    CancelBinding(ok ? "按键已保存。" : error); return;
                }
        }
        void OnDestroy()
        {
            if ((IsListening || restoreNavigation) && EventSystem.current != null) EventSystem.current.sendNavigationEvents = navigationWasEnabled;
            GameSettings.Changed -= Refresh;
        }
    }
}
