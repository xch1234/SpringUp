using System.Collections.Generic;
using SpringUp.Laboratory;
using SpringUp.Organs;
using UnityEngine;
using UnityEngine.UI;

namespace Tideflesh.Systems
{
    // 战斗 HUD 只读；装配仍由休息阶段的实验室和 RunController 负责。
    public sealed class RunEquipmentHud : MonoBehaviour
    {
        private readonly List<(LaboratoryPart part, int index, Text text, Image background)> slots = new();
        private RunLaboratoryEquipment equipment;
        private OrganCombatReport report;
        private Text reportText;
        private int reportRevision = -1;
        public void BindReport(OrganCombatReport value) { report = value; reportRevision = -1; }
        private void Update()
        {
            if (report == null) return;
            foreach (var slot in slots)
                slot.background.color = (int)slot.part < 3 && report.IsFlashing((BodyPart)(int)slot.part, slot.index)
                    ? new Color(1f, .72f, .2f) : equipment?.GetSlot(slot.part, slot.index) == null ? EmptyColor : EquippedColor;
            if (reportText.gameObject.activeInHierarchy && reportRevision != report.Revision)
            { reportRevision = report.Revision; reportText.text = report.Describe(); }
        }
        private static readonly Color EmptyColor = new(.14f, .18f, .21f, .94f);
        private static readonly Color EquippedColor = new(.16f, .36f, .32f, .98f);

        public static RunEquipmentHud Create(Transform parent, Font font)
        {
            var root = new GameObject("Equipment HUD", typeof(RectTransform), typeof(Image), typeof(RunEquipmentHud));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(16, -60);
            rect.sizeDelta = new Vector2(480, 272);
            var background = root.GetComponent<Image>();
            background.color = new Color(.08f, .1f, .12f, .94f);
            background.raycastTarget = false;
            var hud = root.GetComponent<RunEquipmentHud>();
            Label(root.transform, "Title", font, "当前装备", 12, 8, 456, 26, 20);
            var hint = Label(root.transform, "Hint", font, "器官自动攻击 · 仅休息期间可修改装备", 12, 36, 456, 24, 16);
            hint.color = new Color(.72f, .79f, .8f);
            string[] parts = { "head", "hand", "leg", "torso" };
            for (int p = 0; p < parts.Length; p++)
            {
                float y = 66 + p * 50;
                Label(root.transform, parts[p] + " label", font, LaboratoryPresenter.PartNames[p], 12, y, 48, 44, 16);
                for (int s = 0; s < Inventory.SlotCounts[parts[p]]; s++)
                {
                    var cell = new GameObject(parts[p] + " slot " + (s + 1), typeof(RectTransform), typeof(Image));
                    cell.transform.SetParent(root.transform, false);
                    Place((RectTransform)cell.transform, 64 + s * 67, y, 63, 44);
                    var image = cell.GetComponent<Image>();
                    image.color = EmptyColor;
                    image.raycastTarget = false;
                    var text = Label(cell.transform, "Caption", font, (s + 1) + "\n空", 2, 0, 59, 44, 14);
                    text.alignment = TextAnchor.MiddleCenter;
                    hud.slots.Add(((LaboratoryPart)p, s, text, image));
                }
            }
            var toggle = new GameObject("Combat report toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            toggle.transform.SetParent(root.transform, false);
            Place((RectTransform)toggle.transform, 290, 8, 178, 26);
            toggle.GetComponent<Image>().color = EquippedColor;
            Label(toggle.transform, "Label", font, "伤害 / 触发记录", 4, 0, 170, 26, 16);
            var panel = new GameObject("Combat report", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            Place((RectTransform)panel.transform, 0, 284, 480, 210);
            panel.GetComponent<Image>().color = new Color(.08f, .1f, .12f, .96f);
            panel.GetComponent<Image>().raycastTarget = false;
            hud.reportText = Label(panel.transform, "Report", font, "等待器官发动", 12, 8, 456, 194, 16);
            hud.reportText.alignment = TextAnchor.UpperLeft;
            panel.SetActive(false);
            var button = toggle.GetComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => panel.SetActive(!panel.activeSelf));
            return hud;
        }

        public void Bind(RunLaboratoryEquipment source)
        {
            if (equipment != null) equipment.Changed -= Refresh;
            equipment = source;
            if (equipment != null) equipment.Changed += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            foreach (var slot in slots)
            {
                var item = equipment?.GetSlot(slot.part, slot.index);
                slot.text.text = (slot.index + 1) + "\n" + (item?.Name ?? "空");
                slot.text.color = item == null ? new Color(.58f, .65f, .68f) : Color.white;
                slot.background.color = item == null ? EmptyColor : EquippedColor;
            }
        }

        private static Text Label(Transform parent, string name, Font font, string caption,
            float x, float y, float width, float height, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, width, height);
            var text = go.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.text = caption;
            text.color = Color.white; text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private void OnDestroy()
        {
            if (equipment != null) equipment.Changed -= Refresh;
        }
    }
}
