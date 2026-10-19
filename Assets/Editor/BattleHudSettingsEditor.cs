using SpringUp.Flow;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    [CustomEditor(typeof(BattleHudSettings))]
    public sealed class BattleHudSettingsEditor : Editor
    {
        public const string AssetPath = "Assets/Resources/BattleHudSettings.asset";
        static readonly string[] Anchors = { "屏幕左上角", "屏幕右上角", "屏幕左下角", "屏幕右下角", "屏幕中心", "装备栏左下沿" };
        static readonly string[] Alignments = { "左上", "居中靠上", "右上", "居左", "居中", "居右", "左下", "居中靠下", "右下" };
        readonly bool[] expanded = { true, true, true, true, false };
        bool equipmentExpanded = true;

        [MenuItem("Tools/SpringUp/战斗界面/编辑 HUD 布局与文字")]
        public static void SelectSettings()
        {
            Selection.activeObject = EnsureAsset(); EditorGUIUtility.PingObject(Selection.activeObject);
        }

        public static BattleHudSettings EnsureAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<BattleHudSettings>(AssetPath);
            if (settings == null)
            {
                settings = CreateInstance<BattleHudSettings>(); AssetDatabase.CreateAsset(settings, AssetPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("战斗 HUD · 布局与文字", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("战斗与休整区共用此配置。从 Bootstrap 运行进入场景后，在此修改即可实时预览，暂停时也生效。资源参数会保存；停止运行后仍保留。位置单位采用 Canvas 参考分辨率（1440×900），随窗口缩放。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hideWithWindows"), new GUIContent("打开背包 / 菜单时隐藏"));
            Section(0, "wave", "波次（默认右上角）");
            Section(1, "time", "剩余时间（默认右上角）");
            Section(2, "health", "红色血条（默认右上角）");
            if (expanded[2])
            {
                var bar = serializedObject.FindProperty("healthBar");
                using (new EditorGUI.IndentLevelScope())
                {
                    Field(bar, "showValue", "显示血量文字");
                    Field(bar, "fillColor", "血条填充颜色");
                    Field(bar, "emptyColor", "已损失血量区域颜色");
                    Field(bar, "padding", "血条内边距");
                }
            }
            EquipmentSection();
            Section(3, "marrow", "骨髓（默认装备栏下方）");
            Section(4, "help", "操作提示（默认左下角）");
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("文字占位符：\n{wave} 当前波次　{maxWave} 总波数\n{time} 剩余时间（分:秒）　{seconds} 剩余总秒数\n{health} 当前血量　{maxHealth} 最大血量\n{marrow} 骨髓数量\n{interaction} 商店 / 实验室互动提示（随改键更新，战斗为空）\n{inventoryKey} 当前背包按键与 Tab\n休整时 {time} 显示“休整中”，{seconds} 为 0\n可自由改标签或换行；不认识的占位符会原样显示。", MessageType.Info);
            if (GUILayout.Button("恢复默认 HUD 布局与文字"))
            {
                Undo.RecordObject(target, "Restore battle HUD defaults");
                var defaults = CreateInstance<BattleHudSettings>();
                EditorUtility.CopySerialized(defaults, target); DestroyImmediate(defaults);
                target.name = "BattleHudSettings"; EditorUtility.SetDirty(target);
            }
        }

        void EquipmentSection()
        {
            EditorGUILayout.Space();
            equipmentExpanded = EditorGUILayout.Foldout(equipmentExpanded, "装备栏（战斗与休整通用）", true);
            if (!equipmentExpanded) return;
            var p = serializedObject.FindProperty("equipment");
            using (new EditorGUI.IndentLevelScope())
            {
                Field(p, "visible", "显示装备栏");
                Field(p, "backgroundSprite", "装备栏图片（留空使用原图）");
                Field(p, "backgroundColor", "底板颜色（A 控制底图透明度）");
                EditorGUILayout.HelpBox("底板颜色与图片颜色相乘，白色保留原色。A 只影响底图透明度，器官图标不受影响；整体不透明度仍控制整个装备栏。", MessageType.Info);
                EditorGUILayout.HelpBox("将导入为 Sprite (2D and UI) 的图片拖入上方即可替换，运行中立即生效。清空恢复原图。只替换底图，器官槽位位置不变；建议新图沿用原图比例与槽位布局。", MessageType.Info);
                var anchor = p.FindPropertyRelative("anchor");
                anchor.enumValueIndex = EditorGUILayout.Popup("定位锚点", Mathf.Clamp(anchor.enumValueIndex, 0, 4),
                    new[] { "屏幕左上角", "屏幕右上角", "屏幕左下角", "屏幕右下角", "屏幕中心" });
                Field(p, "offset", "位置偏移（X 向右 / Y 向上）");
                Field(p, "size", "装备栏尺寸（宽 / 高）");
                Field(p, "scale", "整体缩放");
                Field(p, "opacity", "不透明度（0 隐藏 / 1 不透明）");
                EditorGUILayout.HelpBox("边框、器官图标和槽位一起缩放。保持原比例请调整体缩放；单独改宽高会拉伸。骨髓仍跟随装备栏下沿。", MessageType.Info);
            }
        }

        void Section(int index, string name, string title)
        {
            EditorGUILayout.Space();
            expanded[index] = EditorGUILayout.Foldout(expanded[index], title, true);
            if (!expanded[index]) return;
            var p = serializedObject.FindProperty(name);
            using (new EditorGUI.IndentLevelScope())
            {
                Field(p, "visible", "显示此项");
                var anchor = p.FindPropertyRelative("anchor");
                anchor.enumValueIndex = EditorGUILayout.Popup("定位锚点", anchor.enumValueIndex, Anchors);
                Field(p, "offset", "位置偏移（X 向右 / Y 向上）");
                Field(p, "size", name == "health" ? "血条尺寸（宽 / 高）" : "文字框尺寸（宽 / 高）");
                Field(p, "scale", "整体缩放");
                Field(p, "fontSize", "字体大小");
                Field(p, "opacity", "不透明度（0 隐藏 / 1 不透明）");
                Field(p, "textColor", "文字颜色");
                Field(p, "backgroundColor", "底板颜色（A 控制底板透明度）");
                var alignment = p.FindPropertyRelative("alignment");
                alignment.enumValueIndex = EditorGUILayout.Popup("文字对齐", alignment.enumValueIndex, Alignments);
                Field(p, "bold", "加粗"); Field(p, "outline", "文字描边");
                Field(p, "text", "显示文字 / 模板");
            }
        }

        static void Field(SerializedProperty parent, string name, string label)
            => EditorGUILayout.PropertyField(parent.FindPropertyRelative(name), new GUIContent(label), true);
    }
}
