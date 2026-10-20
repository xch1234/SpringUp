using System.Linq;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorTools
{
    public sealed class BattleTuningWindow : EditorWindow
    {
        private int tab;
        private Vector2 scroll;
        private OrganActuatorConfig[] organs;
        private EnemyWaveMixSettings settings;
        private EnemyDefinition previewEnemy;
        private int previewWave = 1;

        [MenuItem("SpringUp/战斗调参")]
        public static void Open() => GetWindow<BattleTuningWindow>("战斗调参");

        private void OnEnable() => Reload();
        private void Reload()
        {
            settings = AssetDatabase.LoadAssetAtPath<EnemyWaveMixSettings>("Assets/Resources/EnemyWaveMix.asset");
            organs = AssetDatabase.FindAssets("t:OrganActuatorConfig", new[] { "Assets/Data/Organs/Actuators" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<OrganActuatorConfig>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null).OrderBy(asset => asset.DisplayName).ToArray();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("停止 Play 后修改，重新开始一局生效。修改直接写入现有资产，支持撤销；点击保存持久化。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新配置")) Reload();
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("保存配置")) AssetDatabase.SaveAssets();
            }
            tab = GUILayout.Toolbar(tab, new[] { "器官索敌", "波次敌人属性" });
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (tab == 0) DrawOrgans();
                else DrawWaves();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawOrgans()
        {
            EditorGUILayout.HelpBox("索敌距离以玩家为中心，不改变爆炸半径、弹体寿命等。激光还需检查实际光束长度；无目标器官的效果范围由自身参数控制。", MessageType.Info);
            foreach (var organ in organs ?? new OrganActuatorConfig[0])
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField(organ.DisplayName, organ, typeof(OrganActuatorConfig), false);
                    var serialized = new SerializedObject(organ);
                    serialized.Update();
                    var range = serialized.FindProperty("targetingRange");
                    EditorGUILayout.PropertyField(range, new GUIContent("索敌距离（世界单位）"));
                    if (float.IsNaN(range.floatValue) || float.IsInfinity(range.floatValue)) range.floatValue = 6;
                    range.floatValue = Mathf.Max(.1f, range.floatValue);
                    serialized.ApplyModifiedProperties();
                }
            }
        }

        private void DrawWaves()
        {
            if (settings == null)
            {
                EditorGUILayout.HelpBox("缺少 Assets/Resources/EnemyWaveMix.asset。", MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox("血量、伤害、移速乘以倍率；攻击间隔除以攻频倍率。倍率 1 保持基础值，允许 0.01–100；非法值恢复为 1。治疗、产兵和首次攻击延迟不受攻频影响。", MessageType.Info);
            var serialized = new SerializedObject(settings);
            serialized.Update();
            var waves = serialized.FindProperty("waves");
            for (int i = 0; i < waves.arraySize; i++)
            {
                var wave = waves.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"第 {wave.FindPropertyRelative("wave").intValue} 波", EditorStyles.boldLabel);
                    Multiplier(wave, "healthMultiplier", "敌人血量倍率");
                    Multiplier(wave, "attackFrequencyMultiplier", "敌人攻击频率倍率");
                    Multiplier(wave, "damageMultiplier", "敌人伤害倍率");
                    Multiplier(wave, "moveSpeedMultiplier", "敌人移速倍率");
                }
            }
            serialized.ApplyModifiedProperties();
            if (GUILayout.Button("定位完整波次配置（权重、上限、刷怪节奏）")) Selection.activeObject = settings;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("最终数值预览", EditorStyles.boldLabel);
            previewWave = Mathf.Max(1, EditorGUILayout.IntField("波次", previewWave));
            previewEnemy = (EnemyDefinition)EditorGUILayout.ObjectField("敌人基础配置", previewEnemy, typeof(EnemyDefinition), false);
            if (previewEnemy == null) return;
            var row = settings.ForWave(previewWave);
            if (row != null && row.wave != previewWave)
                EditorGUILayout.HelpBox($"未配置此波，按运行规则沿用最后一行（第 {row.wave} 波）。", MessageType.Info);
            if (previewEnemy.archetype == EnemyArchetype.Dummy) row = null;
            float hp = EnemyWaveMix.ValidMultiplier(row?.healthMultiplier ?? 1);
            float frequency = EnemyWaveMix.ValidMultiplier(row?.attackFrequencyMultiplier ?? 1);
            float damage = EnemyWaveMix.ValidMultiplier(row?.damageMultiplier ?? 1);
            float speed = EnemyWaveMix.ValidMultiplier(row?.moveSpeedMultiplier ?? 1);
            EditorGUILayout.LabelField($"生命 {previewEnemy.maxHealth * hp:g}    移速 {previewEnemy.moveSpeed * speed:g}");
            EditorGUILayout.LabelField($"单次伤害 {previewEnemy.attackPower * damage:g}");
            if (previewEnemy.hasRangedAttack)
                EditorGUILayout.LabelField($"远程攻击间隔 {Mathf.Max(.01f, previewEnemy.attackCooldown / frequency):g} 秒");
            if (previewEnemy.hasContactDamage)
            {
                float interval = previewEnemy.damageIntervalOverride > .01f
                    ? previewEnemy.damageIntervalOverride : Mathf.Max(.1f, previewEnemy.attackCooldown);
                EditorGUILayout.LabelField($"接触伤害间隔 {Mathf.Max(.01f, interval / frequency):g} 秒");
            }
            if (!previewEnemy.hasRangedAttack && !previewEnemy.hasContactDamage)
                EditorGUILayout.LabelField("此敌人未开启攻击能力，伤害字段不会使其自动攻击。");
        }

        private static void Multiplier(SerializedProperty wave, string name, string label)
        {
            var property = wave.FindPropertyRelative(name);
            EditorGUILayout.PropertyField(property, new GUIContent(label));
            property.floatValue = EnemyWaveMix.ValidMultiplier(property.floatValue);
        }
    }
}
