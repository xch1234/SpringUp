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

        /// <summary>记录已生成 prefab 版本的标记文件。</summary>
        private const string MarkerPath = OutputFolder + "/.generated-version.txt";

        /// <summary>敌人子弹贴图；存在时优先于灰盒方块（由 EnemyArtSetup 导入）。</summary>
        private const string BulletSpritePath = "Assets/Art/Enemies/Bullet/enemy_bullet.png";

        /// <summary>
        /// prefab 生成器版本。**改动影响 prefab 序列化内容的东西就要加一**，
        /// 否则已有的 prefab 会一直留着旧值——这里踩过一次：
        /// 敌人尺寸从 <c>EnemyFactory.VisualSize</c> 烘进 prefab 的 Transform，
        /// 2026-10-13 把它从 0.8 改成 1.35 时，六个 prefab 不会自己更新，
        /// 结果"玩家变大了、敌人还是小的"。<c>projectileVisualSize</c> 同理，
        /// 它是 <c>[SerializeField]</c> 的默认值，只在**生成 prefab 那一刻**被写进去。
        ///
        /// 版本历史：
        /// 1 → 初版（六种原型，尺寸 0.8，弹体 0.25）
        /// 2 → 尺寸随相机视野放大：敌人 0.8 → 1.35，弹体 0.25 → 0.421875
        /// 3 → 新增第 7 个原型「木桩 Dummy」（不移动、不攻击、血量 99999）
        /// 4 → **修木桩朝玩家开枪**：只删 EnemyAttack 会被
        ///      `EnemyAttackFlash` 的 `[RequireComponent(typeof(EnemyAttack))]` 自动补回来，
        ///      改为两个一起删，并加"保存后核对 prefab 内容"的校验
        /// 5 → 敌人搬进 `TideBorne_Enemy` 层（角色要能穿过它们）。
        ///      **4 版漏了这一步**：加分层代码时 prefab 已经是 4 版生成的，
        ///      版本号没动 → 没有重建 → prefab 还留在 Default(0) 层，
        ///      于是"角色穿过敌人"不生效，仍然能推着敌人走
        /// 6 → **有美术就用美术**：`Assets/Art/Enemies/&lt;原型&gt;/` 下存在
        ///      `<原型>_00.png` 与 `<原型>.controller` 时，prefab 的精灵改用美术首帧、
        ///      颜色改白并挂 Animator；没有美术的原型继续走灰盒方块。
        ///      同时给开火型敌人补回 `EnemyAttackFlash`（此前漏加，重建会丢开火闪黄）。
        /// 7 → 复核「美术接入」真的生效（含 RangedChaser 1.5 倍尺寸与 Animator）。
        /// 8 → 其余五种敌人（普通 / 快速 / 远程 / 治疗 / 兵工厂）接入贴图动画。
        /// </summary>
        private const int PrefabVersion = 8;

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
            if (missing.Count > 0)
            {
                Debug.Log("[EnemyPrefabBuilder] 缺少 " + missing.Count + " 个敌人 prefab，自动补齐：" +
                          string.Join(", ", missing));
                BuildMissing(missing);
            }

            RebuildIfStale();
        }

        /// <summary>
        /// 生成器版本变了就**自动重建全部敌人 prefab**。
        ///
        /// **为什么从"只警告"改成"自动重建"**：曾经只打印一句警告，结果是
        /// 「玩家放大了、敌人还是小的」和「木桩朝玩家开枪」这两种半生效状态——
        /// 前者肉眼难判断漏了哪步，后者更是要对着 prefab 内容才查得出来。
        /// 版本号是**我们自己**在改序列化内容时主动加的，既然加了，就说明旧 prefab 一定不对，
        /// 那么"让用户记得点菜单"就是纯粹的多余风险。
        ///
        /// 代价：会覆盖对 prefab 的手工调整。可接受——这些 prefab 是**生成物**
        /// （版本标记文件里写明了），要长期手调应该另存成别的资产。
        ///
        /// 失败时只告警不抛出：`GenerateAll` 内部会删掉旧资产再重建，
        /// 万一中途出错，宁可留一句明确的提示，也不要把编辑器加载整个搞崩。
        /// </summary>
        private static void RebuildIfStale()
        {
            var marker = ReadMarkerVersion();
            if (marker == PrefabVersion)
            {
                VerifySpawnablePrefabsExist();
                VerifyBakedLayers();
                return;
            }

            Debug.Log("[EnemyPrefabBuilder] 敌人 prefab 的生成版本是 " + marker +
                      "，当前代码是 " + PrefabVersion + "，自动重建全部敌人 prefab。");

            try
            {
                var built = GenerateAll();
                Debug.Log("[EnemyPrefabBuilder] 自动重建完成，共 " + built.Count + " 个。");
                VerifyBakedLayers();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[EnemyPrefabBuilder] 自动重建失败（" + exception.Message + "）。" +
                                 "请手动跑菜单 Tools → 程序C → 生成全部敌人 prefab。" +
                                 "**旧的 prefab 可能已被删除**，别忽略这条。");
            }
        }

        /// <summary>
        /// 只有"会被波次刷出来"的原型缺了才算严重问题；木桩是测试道具，缺了不影响战斗。
        /// 由 <see cref="RebuildIfStale"/> 在版本已是最新时调用。
        /// </summary>
        private static void VerifySpawnablePrefabsExist()
        {
            foreach (var archetype in EnemyArchetypeConfig.SpawnableArchetypes())
            {
                var config = EnemyArchetypeConfig.Get(archetype);
                var path = OutputFolder + "/" + config.displayName + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogError("[EnemyPrefabBuilder] 可刷怪的原型缺少 prefab：" + path +
                                   "（刷怪器会退回运行时原型，行为可能与预期不同）");
                }
            }
        }

        private static int ReadMarkerVersion()
        {
            try
            {
                if (!System.IO.File.Exists(MarkerPath))
                    return 0;

                foreach (var line in System.IO.File.ReadAllLines(MarkerPath))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("version="))
                        continue;

                    int parsed;
                    if (int.TryParse(trimmed.Substring("version=".Length).Trim(), out parsed))
                        return parsed;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[EnemyPrefabBuilder] 读版本标记失败：" + exception.Message);
            }

            return 0;
        }

        private static void WriteMarker()
        {
            var lines = new[]
            {
                "# 本文件由 EnemyPrefabBuilder 生成，记录六个敌人 prefab 的生成版本。",
                "# 版本不符 = prefab 里可能还留着旧的尺寸/弹体大小，跑菜单重建。",
                "version=" + PrefabVersion,
                "generated=" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            System.IO.File.WriteAllText(MarkerPath, string.Join(System.Environment.NewLine, lines) + System.Environment.NewLine);
            AssetDatabase.ImportAsset(MarkerPath, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// 找出还没有 prefab 的原型。
        /// 用 <see cref="EnemyArchetypeConfig.AllArchetypes"/>（含木桩）——木桩虽然不参与刷怪，
        /// 但它也是个要能拖进场景的 prefab，所以同样要保证存在。
        /// </summary>
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
            WriteMarker();
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
            WriteMarker();
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

            var art = FindArt(config.displayName);
            var instance = EnemyFactory.CreateGreyboxEnemy(
                config.displayName,
                art != null ? art.firstFrame : sprite,
                art != null ? Color.white : config.color);

            // 视觉缩放乘在基础尺寸上。没有美术时保持原来的灰盒尺寸。
            var scale = GreyboxAssets.EnemyWorldSize * VisualScaleOf(config);
            instance.transform.localScale = new Vector3(scale, scale, 1f);

            if (art != null)
            {
                var animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = art.controller;
                animator.applyRootMotion = false;
            }
            else
            {
                RemoveComponent<Animator>(instance);
            }

            // 敌人一律放「敌人层」：与玩家层互不相碰（角色穿过敌人），
            // 但敌人彼此保留碰撞体积。碰撞矩阵见 BattleLayers。
            instance.layer = BattleLayers.Resolve(BattleLayers.Enemy);

            try
            {
                ConfigureInstance(instance, config);

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
                if (prefab == null)
                {
                    Debug.LogError("[EnemyPrefabBuilder] 保存失败：" + path);
                    return null;
                }

                // 必须在保存之后查：RequireComponent 的自动补挂发生在序列化阶段，
                // 只看内存里的实例会漏掉"删了又长回来"。
                VerifySavedPrefab(prefab, config, path);

                return path;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>美术目录根：每种原型一个子目录，目录名与 <see cref="EnemyDefinition.displayName"/> 相同。</summary>
        private const string ArtRoot = "Assets/Art/Enemies";

        /// <summary>一种原型的美术：首帧精灵 + 循环动画控制器。</summary>
        private sealed class EnemyArt
        {
            public Sprite firstFrame;
            public RuntimeAnimatorController controller;
        }

        /// <summary>
        /// 找某个原型的美术。目录里同时有 <c>&lt;原型&gt;.controller</c> 和至少一张精灵才算数，
        /// 缺一样就退回灰盒，避免生成一个"有控制器但没图"的半成品 prefab。
        /// </summary>
        private static EnemyArt FindArt(string displayName)
        {
            var folder = ArtRoot + "/" + displayName;
            if (!AssetDatabase.IsValidFolder(folder))
                return null;

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                folder + "/" + displayName + ".controller");
            if (controller == null)
                return null;

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            if (guids == null || guids.Length == 0)
                return null;

            var paths = new List<string>(guids.Length);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            paths.Sort(System.StringComparer.Ordinal);

            for (var i = 0; i < paths.Count; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
                if (sprite != null)
                    return new EnemyArt { firstFrame = sprite, controller = controller };
            }

            return null;
        }

        /// <summary>
        /// 每种原型的视觉缩放（乘在基础尺寸上）。
        /// 目前只有 RangedChaser 有美术、且策划要求先放大 1.5 倍观察；
        /// 等美术规模上来再搬进 <see cref="EnemyDefinition"/>，现在给八个 .asset 补字段不划算。
        /// </summary>
        private static float VisualScaleOf(EnemyDefinition config)
        {
            return config != null && config.id == "RangedChaser" ? 1.5f : 1f;
        }

        /// <summary>按配置给实例装配行为组件与数值。</summary>
        private static void ConfigureInstance(GameObject instance, EnemyDefinition config)
        {
            ApplyData(instance, config);
            if (!config.hasContactDamage && instance.GetComponent<EnemyContactDamage>() != null)
                Object.DestroyImmediate(instance.GetComponent<EnemyContactDamage>());

            if (config.hasContactDamage && instance.GetComponent<EnemyContactDamage>() == null)
                instance.AddComponent<EnemyContactDamage>();
            if (!config.hasRangedAttack) RemoveAttackIfUnused(instance, config);
            else
            {
                if (instance.GetComponent<EnemyAttack>() == null) instance.AddComponent<EnemyAttack>();
                ApplyRangedAttack(instance, config);

                // 开火闪黄的可读性提示。此前只存在于旧 prefab 里，重建会丢，这里补回来。
                if (instance.GetComponent<EnemyAttackFlash>() == null)
                    instance.AddComponent<EnemyAttackFlash>();
            }
            if (config.hasProducer) instance.AddComponent<EnemyProducer>();
            if (config.hasHealer) instance.AddComponent<EnemyHealer>();
        }

        private static void ApplyData(GameObject instance, EnemyDefinition config)
        {
            instance.GetComponent<EnemyData>().Configure(config);
        }

        /// <summary>射击敌人：接上弹体精灵，并把发射开关打开。</summary>
        private static void ApplyRangedAttack(GameObject instance, EnemyDefinition config)
        {
            var attack = instance.GetComponent<EnemyAttack>();
            if (attack == null)
                return;

            var serialized = new SerializedObject(attack);
            SetBool(serialized, "useProjectiles", true);
            SetFloat(serialized, "projectileVisualSize", GreyboxAssets.ProjectileWorldSize);

            var bulletSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BulletSpritePath);
            if (bulletSprite != null)
            {
                // 真美术自带颜色，不能再乘染色的 config.color。
                SetColor(serialized, "projectileColor", Color.white);
                SetObject(serialized, "projectileSprite", bulletSprite);
            }
            else
            {
                SetColor(serialized, "projectileColor", config.color);
                SetObject(serialized, "projectileSprite", EnsureSquareSprite());
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 不需要攻击能力时，把开火相关的组件全部移除。
        ///
        /// **踩过的坑（木桩朝玩家开枪）**：这里原来只销毁 <c>EnemyAttack</c>，
        /// 而 <see cref="EnemyAttackFlash"/> 上写着 <c>[RequireComponent(typeof(EnemyAttack))]</c>——
        /// Unity 会在保存 prefab 时**把这个依赖重新加回来**，于是销毁等于没做，
        /// 子弹照飞。这种"删了又自己长回来"的问题不会报任何错，只能靠对照 prefab 内容发现。
        ///
        /// 所以现在两个一起删，顺序上先删带依赖的那个。
        /// </summary>
        private static void RemoveAttackIfUnused(GameObject instance, EnemyDefinition config)
        {
            if (config.hasRangedAttack)
                return;

            RemoveComponent<EnemyAttackFlash>(instance);
            RemoveComponent<EnemyAttack>(instance);
        }

        /// <summary>把某个组件从实例上删掉（没有就跳过）。</summary>
        private static void RemoveComponent<T>(GameObject instance) where T : Component
        {
            var component = instance.GetComponent<T>();
            if (component != null)
                Object.DestroyImmediate(component, true);
        }

        /// <summary>
        /// 保存后核对 prefab 的真实内容：不该有 <c>EnemyAttack</c> 的敌人身上必须没有。
        ///
        /// **为什么要在保存之后查**：`RequireComponent` 的自动补挂发生在序列化/保存阶段，
        /// 只检查内存里的实例是查不出来的——上面那个坑正是这么漏过去的。
        /// </summary>
        private static void VerifySavedPrefab(GameObject prefab, EnemyDefinition config, string path)
        {
            if (prefab == null)
                return;

            if (!config.hasRangedAttack && prefab.GetComponent<EnemyAttack>() != null)
            {
                Debug.LogError("[EnemyPrefabBuilder] " + path + " 上不该有 EnemyAttack，但它存在——" +
                               "多半是某个组件的 [RequireComponent] 把它补回来了。" +
                               "这个敌人会朝玩家开枪。（木桩曾因此出错）");
            }

            if (!config.hasContactDamage && prefab.GetComponent<EnemyContactDamage>() != null)
            {
                Debug.LogError("[EnemyPrefabBuilder] " + path + " 上不该有 EnemyContactDamage，但它存在。");
            }

            // 有美术就必须挂上 Animator，否则 prefab 只会显示第 0 帧、不会动。
            if (FindArt(config.displayName) != null && prefab.GetComponent<Animator>() == null)
            {
                Debug.LogError("[EnemyPrefabBuilder] " + path + " 有美术却没有 Animator，" +
                               "场景里会一直停在第一帧。");
            }

            // 层也要核对。**加分层时漏掉这一步的后果很难反推**：
            // prefab 留在 Default(0) 层时，"角色穿过敌人"会失效（仍然被卡住 / 推得动），
            // 但场景里的 Player 层是对的，看起来一切正常。
            var expectedLayer = BattleLayers.Resolve(BattleLayers.Enemy);
            if (prefab.layer != expectedLayer)
            {
                Debug.LogError("[EnemyPrefabBuilder] " + path + " 的层是 " + prefab.layer +
                               "，应为 " + expectedLayer + "（" + BattleLayers.Enemy + "）。" +
                               "角色会推得动这个敌人。");
            }
        }

        /// <summary>
        /// 核对所有已存在的敌人 prefab 的层是否正确。
        ///
        /// 与 <see cref="VerifySavedPrefab"/>（只在刚生成后跑）不同，这个查的是**磁盘上的资产**，
        /// 所以能发现"prefab 是旧版本生成的、版本号却没跟着动"这种情况——
        /// 那正是"角色还能推动敌人"的成因。
        /// </summary>
        private static void VerifyBakedLayers()
        {
            var expectedLayer = BattleLayers.Resolve(BattleLayers.Enemy);

            foreach (var archetype in EnemyArchetypeConfig.AllArchetypes())
            {
                var config = EnemyArchetypeConfig.Get(archetype);
                var path = OutputFolder + "/" + config.displayName + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                if (prefab.layer != expectedLayer)
                {
                    Debug.LogError("[EnemyPrefabBuilder] " + path + " 的层是 " + prefab.layer +
                                   "，应为 " + expectedLayer + "（" + BattleLayers.Enemy + "）。" +
                                   "角色会推得动这个敌人、也不会穿过它。" +
                                   "把 PrefabVersion 加一强制重建即可。");
                }
            }
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
