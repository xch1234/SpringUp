using UnityEngine;
using UnityEngine.UI;

namespace Tideflesh.Systems
{
    public sealed class ChargeAttackHud : MonoBehaviour
    {
        private PlayerChargeAttack attack;
        private RectTransform fill;
        private Image fillImage;
        private Text chargeText, statusText, bindingText;
        private GameObject panel;

        public static ChargeAttackHud Create(Transform parent, Font font, PlayerChargeAttack attack)
        {
            var root = new GameObject("Charge attack HUD", typeof(RectTransform), typeof(ChargeAttackHud));
            root.transform.SetParent(parent, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
            var hud = root.GetComponent<ChargeAttackHud>();
            hud.attack = attack;
            hud.panel = Box(root.transform, "Charge panel", new Vector2(0, 1), new Vector2(16, -344), new Vector2(480, 112));
            hud.chargeText = Label(hud.panel.transform, font, new Vector2(12, -6), new Vector2(456, 26), 18);
            var track = Box(hud.panel.transform, "Charge track", new Vector2(0, 1), new Vector2(12, -38), new Vector2(456, 18));
            track.GetComponent<Image>().color = new Color(.2f, .24f, .27f);
            var fill = Box(track.transform, "Charge fill", new Vector2(0, 0), Vector2.zero, Vector2.zero);
            hud.fill = (RectTransform)fill.transform;
            hud.fill.anchorMax = Vector2.one;
            hud.fill.offsetMin = hud.fill.offsetMax = Vector2.zero;
            hud.fillImage = fill.GetComponent<Image>();
            hud.statusText = Label(hud.panel.transform, font, new Vector2(12, -64), new Vector2(456, 42), 16);
            var buttonGo = Box(root.transform, "Change attack key", Vector2.one, new Vector2(-16, -6), new Vector2(260, 32));
            buttonGo.GetComponent<Image>().raycastTarget = true;
            buttonGo.GetComponent<Image>().color = new Color(.18f, .3f, .32f);
            var button = buttonGo.AddComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(attack.ToggleRebind);
            hud.bindingText = Label(buttonGo.transform, font, Vector2.zero, new Vector2(260, 32), 16);
            hud.bindingText.alignment = TextAnchor.MiddleCenter;
            return hud;
        }

        public void Refresh(bool combat)
        {
            panel.SetActive(combat);
            bindingText.text = attack.IsRebinding
                ? (attack.Feedback.Length > 0 ? "按键已占用 · 请换一个" : "键盘/鼠标左右键 · Esc 取消")
                : "攻击键 [" + attack.KeyLabel + "] · 点击改键";
            chargeText.text = $"充能 {attack.Charge:0} / {attack.Capacity:0}    本轮需要 {attack.RequiredCharge:0}";
            fill.anchorMax = new Vector2(attack.Capacity > 0 ? Mathf.Clamp01(attack.Charge / attack.Capacity) : 0, 1);
            fillImage.color = attack.Ready ? new Color(.3f, .85f, .65f) : new Color(.3f, .58f, .82f);
            statusText.text = attack.Feedback.Length > 0 ? attack.Feedback : attack.IsRebinding ? "等待新按键；Esc 取消"
                : !attack.HasAttackOrgans ? "请在休息期装备攻击器官"
                : attack.IsAttacking ? "正在按槽位顺序攻击…"
                : attack.Ready ? "按 " + attack.KeyLabel + " 攻击，长按连续发动" : "充能恢复中…";
        }

        private static GameObject Box(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(.08f, .1f, .12f, .94f);
            go.GetComponent<Image>().raycastTarget = false;
            return go;
        }

        private static Text Label(Transform parent, Font font, Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            return text;
        }
    }
}
