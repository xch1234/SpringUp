using System.Collections.Generic;
using TideBorne.Battle;
using UnityEditor;
using UnityEngine;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 按 <see cref="EnemyArchetypeConfig"/> 生成六种敌人 prefab 到
    /// <c>Assets/Resources/Enemies/</c>。
    ///
    /// **为什么用代码生成而不是手工在 Inspector 里配**：
    /// 数值关系（快速敌人 = 1.3 倍移速 / 0.7 倍血量）写在配置表里可以核对，
    /// 手工填六个 prefab 会随时间漂移；而且新增第 7 种敌人只是往配置表加一条。
    ///
    /// **为什么放 Resources/Enemies**：运行时按名加载不需要任何初始化顺序，
    /// 也省掉一个静态注册表（静态字段跨不过域重载，本项目已因初始化顺序踩过两次）。
    /// </summary>
    public static class EnemyPrefabBuilder
    {
        /// <summary>敌人 prefab 的输出目录（Resources 下，与 EnemyPrefabLibrary 对应）。</summary>
        public const string OutputFolder = "Assets/Resources/Enemies";

        private static bool s_AutoBuildAttempted;

        /// <summary>
        /// 编辑器加载后检查敌人 prefab 是否齐全，缺了就补齐。
        ///
        /// **为什么要有这个**：生成 prefab 是"需要人记得点菜单"的动作，实测漏过一次
        /// （用户以为跑了，实际 `Assets/Resources/Enemies` 目录都没建）。
        /// 与灰盒场景同一套思路：缺什么自动补什么，让"打开工程就能用"成立。
        ///
        /// 只补缺失的，不覆盖已有的——手工调过的 prefab 不会被冲掉。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ScheduleAutoBuild()
        {
            EditorApplication.delayCall += TryAutoBuildMissing;
        }

        private static void TryAutoBuildMissing()
        {
            if (s_AutoBuildAttempted)
                return;

            s_AutoBuildAttempted = true;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            var missing = FindMissingArchetypes();
            if (missing.Count == 0)
                return;

            Debug.Log("[EnemyPrefabBuilder] 缺少 " + missing.Count + " 个敌人 prefab，自动补齐：" +
                      string.Join(", ", missing));
            BuildMissing(missing);
        }

        /// <summary>找出还没有 prefab 的原型。</summary>
        private static List<EnemyArchetype> FindMissingArchetypes()
        {
            var missing = new List<EnemyArchetype>();

            foreach (var archetype in EnemyArchetypeConfig.AllArchetypes())
            {
                var config = EnemyArchetypeConfig.Get(archetype);
                var path = OutputFolder + "/" + config.displayName + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    missing.Add(archetype);
            }

            return missing;
        }

        /// <summary>只生成缺失的原型，不动已有的。</summary>
        private static void BuildMissing(List<EnemyArchetype> archetypes)
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "Enemies");

            var sprite = EnsureSquareSprite();
            var built = 0;

            foreach (var archetype in archetypes)
            {
                if (!string.IsNullOrEmpty(BuildOne(archetype, sprite, overwrite: false)))
                    built++;
            }

            if (built == 0)
                return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EnemyPrefabLibrary.Clear();
            Debug.Log("[EnemyPrefabBuilder] 已补齐 " + built + " 个敌人 prefab。");
        }

        [MenuItem("Tools/程序C/生成全部敌人 prefab", false, 20)]
        public static void BuildAllEnemyPrefabs()
        {
            var prefabs = GenerateAll();
            if (prefabs == null || prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("生成失败", "没有生成任何敌人 prefab，请查看 Console。", "好");
                return;
            }

            var names = new List<string>();
            foreach (var pair in prefabs)
                names.Add(pair.Key + " → " + pair.Value);

            Debug.Log("[EnemyPrefabBuilder] 已生成 " + prefabs.Count + " 个敌人 prefab：\n" +
                      string.Join("\n", names));
            EditorUtility.DisplayDialog("已生成敌人 prefab",
                "共 " + prefabs.Count + " 个，位于 " + OutputFolder + "。\n\n" + string.Join("\n", names), "好");
        }

        /// <summary>
        /// 生成全部敌人 prefab。返回「原型 → 资产路径」的映射；
        /// 失败时返回已成功的部分。供场景构建器在重建场景时顺带调用。
        /// </summary>
        public static Dictionary<EnemyArchetype, string> GenerateAll()
        {
            var results = new Dictionary<EnemyArchetype, string>();

            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "Enemies");

            var sprite = EnsureSquareSprite();

            foreach (var archetype in EnemyArchetypeConfig.AllArchetypes())
            {
                var path = BuildOne(archetype, sprite, overwrite: true);
                if (!string.IsNullOrEmpty(path))
                    results[archetype] = path;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EnemyPrefabLibrary.Clear();
            return results;
        }

        /// <summary>
        /// 生成单个敌人 prefab，返回资产路径；失败返回 null。
        /// <paramref name="overwrite"/> 为 false 时，资产已存在就直接返回它，不重建——
        /// 这样"自动补齐缺失"不会冲掉手工调过的 prefab。
        /// </summary>
        public static string BuildOne(EnemyArchetype archetype, Sprite sprite, bool overwrite)
        {
            var config = EnemyArchetypeConfig.Get(archetype);
            var path = OutputFolder + "/" + config.displayName + ".prefab";

            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                return path;

            // 覆盖式重建：先删旧资产，保证改动一定生效（幂等）。
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var instance = EnemyFactory.CreateGreyboxEnemy(config.displayName, sprite, config.color);

            try
            {
                ConfigureInstance(instance, config);

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
                if (prefab == null)
                {
                    Debug.LogError("[EnemyPrefabBuilder] 保存失败：" + path);
                    return null;
                }

                return path;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>按配置给实例装配行为组件与数值。</summary>
        private static void ConfigureInstance(GameObject instance, EnemyArchetypeConfig.Config config)
        {
            ApplyData(instance, config);

            // 移动模式：EnemyMotor 已经在 EnemyFactory 里挂上，这里改模式与参数。
            var motor = instance.GetComponent<EnemyMotor>();
            if (motor != null)
            {
                var serialized = new SerializedObject(motor);
                SetEnum(serialized, "moveMode", (int)config.moveMode);
                if (config.preferredDistance > 0.01f)
                    SetFloat(serialized, "preferredDistance", config.preferredDistance);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // 接触伤害：按需挂。
            if (config.hasContactDamage)
                instance.AddComponent<EnemyContactDamage>();
            else
                RemoveAttackIfUnused(instance, config);

            // 远程攻击：EnemyAttack 由 EnemyFactory 默认挂上；不需要就移除，
            // 避免"静止射击敌人"这类不该射的敌人悄悄射出子弹。
            var attack = instance.GetComponent<EnemyAttack>();
            if (attack != null && !config.hasRangedAttack)
                Object.DestroyImmediate(attack, true);

            if (config.hasRangedAttack)
                ApplyRangedAttack(instance, config);

            // 产兵：兵工厂专用能力，通用组件。
            if (config.hasProducer)
            {
                var producer = instance.AddComponent<EnemyProducer>();
                var serialized = new SerializedObject(producer);
                SetEnum(serialized, "productArchetype", (int)EnemyArchetype.Normal);
                SetFloat(serialized, "produceInterval", EnemyArchetypeConfig.ArsenalProduceInterval);
                SetInt(serialized, "maxAliveProducts", EnemyArchetypeConfig.ArsenalMaxAliveProducts);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // 治疗：医疗兵专用能力。
            if (config.hasHealer)
            {
                var healer = instance.AddComponent<EnemyHealer>();
                var serialized = new SerializedObject(healer);
                SetFloat(serialized, "healInterval", EnemyArchetypeConfig.MedicHealInterval);
                SetFloat(serialized, "healRadius", EnemyArchetypeConfig.MedicHealRadius);
                SetFloat(serialized, "healPercent", EnemyArchetypeConfig.MedicHealPercent);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>把数值写进 EnemyData。</summary>
        private static void ApplyData(GameObject instance, EnemyArchetypeConfig.Config config)
        {
            var data = instance.GetComponent<EnemyData>();
            if (data == null)
            {
                Debug.LogError("[EnemyPrefabBuilder] 实例上没有 EnemyData。");
                return;
            }

            var serialized = new SerializedObject(data);
            SetString(serialized, "id", config.displayName);
            SetFloat(serialized, "moveSpeed", config.moveSpeed);
            SetFloat(serialized, "maxHealth", config.health);
            SetFloat(serialized, "attackPower", config.attackPower);
            SetFloat(serialized, "attackCooldown", config.attackCooldown);
            SetFloat(serialized, "attackRange", EnemyArchetypeConfig.RangedAttackRange);
            SetFloat(serialized, "projectileSpeed", EnemyArchetypeConfig.RangedProjectileSpeed);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>射击敌人：接上弹体精灵，并把发射开关打开。</summary>
        private static void ApplyRangedAttack(GameObject instance, EnemyArchetypeConfig.Config config)
        {
            var attack = instance.GetComponent<EnemyAttack>();
            if (attack == null)
                return;

            var serialized = new SerializedObject(attack);
            SetBool(serialized, "useProjectiles", true);
            SetFloat(serialized, "projectileVisualSize", 0.25f);
            SetColor(serialized, "projectileColor", config.color);
            SetObject(serialized, "projectileSprite", EnsureSquareSprite());
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>不需要接触伤害时，把 EnemyAttack 一并移除——它现在的语义是远程射击。</summary>
        private static void RemoveAttackIfUnused(GameObject instance, EnemyArchetypeConfig.Config config)
        {
            if (config.hasRangedAttack)
                return;

            var attack = instance.GetComponent<EnemyAttack>();
            if (attack != null)
                Object.DestroyImmediate(attack, true);
        }

        /// <summary>取共用的灰盒方块精灵资产；没有就生成。</summary>
        private static Sprite EnsureSquareSprite()
        {
            const string path = "Assets/Art/Textures/white_16.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
                return existing;

            return GreyboxAssets.SquareSprite;
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        // ---- SerializedObject 辅助：私有字段也能写，用 Unity 的正式入口 ----

        private static void SetFloat(SerializedObject serialized, string field, float value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
                property.floatValue = value;
        }

        private static void SetInt(SerializedObject serialized, string field, int value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
                property.intValue = value;
        }

        private static void SetBool(SerializedObject serialized, string field, bool value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
                property.boolValue = value;
        }

        private static void SetString(SerializedObject serialized, string field, string value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
                property.stringValue = value;
        }

        private static void SetEnum(SerializedObject serialized, string field, int value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
                property.enumValueIndex = value;
        }

        private static void SetColor(SerializedObject serialized, string field, Color value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
                property.colorValue = value;
        }

        private static void SetObject(SerializedObject serialized, string field, Object value)
        {
            var property = serialized.FindProperty(field);
            if (property != null)
                property.objectReferenceValue = value;
        }
    }
}
