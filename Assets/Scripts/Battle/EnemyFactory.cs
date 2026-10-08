using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TideBorne.Battle
{
    /// <summary>
    /// 程序 C 的最小敌人工厂：造一个灰盒敌人。
    /// 灰盒场景构建器与刷怪器共用，避免两处各写一遍组件拼装。
    /// </summary>
    public static class EnemyFactory
    {
        /// <summary>灰盒敌人的默认血量，够玩家侧验证「会死」。</summary>
        public const float DefaultHealth = 20f;

        /// <summary>灰盒敌人的默认移速，比玩家 5 慢，保证能拉开距离。</summary>
        public const float DefaultMoveSpeed = 2.5f;

        /// <summary>灰盒敌人的默认攻击力。</summary>
        public const float DefaultAttackPower = 5f;

        /// <summary>灰盒敌人的视觉边长，世界单位。</summary>
        public const float VisualSize = 0.8f;

        /// <summary>灰盒敌人的攻击距离，米。默认 1.2 米太近，弹道一闪而过看不见。</summary>
        public const float GreyboxAttackRange = 6f;

        /// <summary>灰盒敌人的弹速，米/秒。默认 8 太快，配合 6 米射程约飞 1 秒。</summary>
        public const float GreyboxProjectileSpeed = 4f;

        /// <summary>灰盒敌人的攻击冷却，秒。</summary>
        public const float GreyboxAttackCooldown = 1.5f;

        /// <summary>灰盒敌人 prefab 的存放路径。美术接手时替换它即可，代码不用改。</summary>
        public const string GreyboxEnemyPrefabPath = "Assets/Prefabs/Enemy_Greybox.prefab";

        /// <summary>
        /// 把灰盒敌人落成 prefab 资产并返回它（已存在则直接读）。
        ///
        /// **为什么要落成资产**：内存里 <c>new</c> 出来的原型只能在本次会话用，
        /// 无法在 Inspector 里查看、无法被美术替换、也无法被别的场景引用。
        /// 落成 prefab 之后，刷怪器引用的是一份可编辑、可替换的真实资产。
        ///
        /// 仅在编辑器下可用（<c>PrefabUtility</c> 属 <c>UnityEditor</c>）。
        /// </summary>
        public static GameObject EnsureGreyboxEnemyPrefab(Sprite sprite, Color color)
        {
#if UNITY_EDITOR
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxEnemyPrefabPath);
            if (existing != null)
                return existing;

            var folder = System.IO.Path.GetDirectoryName(GreyboxEnemyPrefabPath);
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder);
                var leaf = System.IO.Path.GetFileName(folder);
                if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
                    AssetDatabase.CreateFolder(parent.Replace('\\', '/'), leaf);
            }

            var instance = CreateGreyboxEnemy("Enemy_Greybox", sprite, color);
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, GreyboxEnemyPrefabPath);
            Object.DestroyImmediate(instance);

            if (prefab == null)
            {
                Debug.LogError("[EnemyFactory] prefab 保存失败：" + GreyboxEnemyPrefabPath);
                return null;
            }

            Debug.Log("[EnemyFactory] 灰盒敌人 prefab 已生成：" + GreyboxEnemyPrefabPath);
            return prefab;
#else
            // 发行版里没有 UnityEditor：返回 null，调用方会退回运行时原型。
            return null;
#endif
        }

        /// <summary>
        /// 造一个灰盒敌人。调用方负责设位置与父物体。
        ///
        /// <paramref name="sprite"/> 为 null 时用 <see cref="GreyboxAssets.SquareSprite"/>，
        /// **不再自己造 1 PPU 的小图**——那个尺寸小到看不见，是"敌人能碰到但看不到"的成因。
        ///
        /// 尺寸处理与玩家一致：精灵本身是 1 世界单位的方块，用 transform 缩到
        /// <see cref="VisualSize"/>，碰撞体尺寸写 1（随 transform 一起缩成 VisualSize）。
        /// **不用 <c>SpriteRenderer.drawMode = Sliced</c>**：那要求精灵带九宫格边框，
        /// 普通方块图在 Sliced 下可能直接不渲染。
        /// </summary>
        /// <param name="name">物体名。</param>
        /// <param name="sprite">精灵；为 null 时用共用的灰盒方块。</param>
        /// <param name="color">颜色。</param>
        public static GameObject CreateGreyboxEnemy(string name, Sprite sprite, Color color)
        {
            var enemy = new GameObject(name);

            enemy.transform.localScale = new Vector3(VisualSize, VisualSize, 1f);

            var renderer = enemy.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : GreyboxAssets.SquareSprite;
            renderer.color = color;
            renderer.sortingOrder = 2;

            // 尺寸写 1：transform 已缩到 VisualSize，世界尺寸随之变成 VisualSize。
            var collider = enemy.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;

            var body = enemy.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            enemy.AddComponent<EnemyData>();
            enemy.AddComponent<EnemyRuntime>();
            enemy.AddComponent<EnemyMotor>();
            enemy.AddComponent<EnemyAttack>();
            // 灰盒表现：出手时闪一下，否则"看不到敌人开火、只看到自己掉血"。
            enemy.AddComponent<EnemyAttackFlash>();

            ApplyGreyboxDefaults(enemy);

            return enemy;
        }

        /// <summary>
        /// 灰盒敌人的射程与弹速。
        ///
        /// **为什么要单独调这些值**：<see cref="EnemyData"/> 的默认 `attackRange` 是 1.2 米、
        /// `projectileSpeed` 是 8 米/秒——那意味着弹体只飞 **0.1 秒**（约 6 帧）就命中，
        /// 实测表现就是"看不到弹道"。灰盒阶段先把射程拉到看得见、把弹速降下来，
        /// 让攻击过程可读；正式数值等策划给难度曲线再覆盖。
        ///
        /// 用 <see cref="SerializedObject"/> 写私有序列化字段（Unity 的正式入口，
        /// 不需要把字段改成 public）。**整个方法用条件编译隔离**：
        /// `SerializedObject` 属于 `UnityEditor`，直接引用会让游戏程序集依赖编辑器程序集，
        /// **构建会失败**。运行时只保留下面那句直接赋值。
        /// </summary>
        private static void ApplyGreyboxDefaults(GameObject enemy)
        {
            var data = enemy.GetComponent<EnemyData>();
            if (data == null)
                return;

#if UNITY_EDITOR
            var serialized = new SerializedObject(data);
            SetFloat(serialized, "attackRange", GreyboxAttackRange);
            SetFloat(serialized, "projectileSpeed", GreyboxProjectileSpeed);
            SetFloat(serialized, "attackCooldown", GreyboxAttackCooldown);
            serialized.ApplyModifiedPropertiesWithoutUndo();
#else
            // 发行版里没有 UnityEditor：直接写公开访问器（字段本身带默认值，够用）。
            data.ApplyGreyboxTuning(GreyboxAttackRange, GreyboxProjectileSpeed, GreyboxAttackCooldown);
#endif
        }

#if UNITY_EDITOR
        private static void SetFloat(SerializedObject serialized, string fieldName, float value)
        {
            var property = serialized.FindProperty(fieldName);
            if (property == null)
                return;

            property.floatValue = value;
        }
#endif
    }
}
