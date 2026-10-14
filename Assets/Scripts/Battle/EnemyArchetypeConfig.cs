using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 六种灰盒敌人的数值与行为配置。
    ///
    /// **为什么集中在一处**：如果每个 prefab 各自在 Inspector 里填数值，
    /// 改平衡时要开六个 prefab 逐个找，而且"快速敌人是普通敌人的 1.3 倍移速"
    /// 这种关系会随时间漂移。集中成数据表后，倍数关系写在代码里、可视化且可验证。
    ///
    /// **数值口径**：以 <c>EnemyData</c> / <c>EnemyAttack</c> 的**代码默认值**为基准
    /// （移速 2.5、血量 20、攻击力 5、冷却 1.5 秒）。策划会后期统一调整，
    /// 届时改这张表即可，不用动 prefab。
    /// </summary>
    public static class EnemyArchetypeConfig
    {
        /// <summary>基准移速，米/秒。玩家开局 5，近战追不上。</summary>
        public const float BaseMoveSpeed = 2.5f;

        /// <summary>基准血量，点。拳头伤害 5，普通怪 4 下打死。</summary>
        public const float BaseHealth = 20f;

        /// <summary>基准攻击力。一次接触或一发子弹，不要两种叠在同一个数上。</summary>
        public const float BaseAttackPower = 5f;

        /// <summary>基准攻击冷却，秒。对应 EnemyData 的默认值。</summary>
        public const float BaseAttackCooldown = 1.5f;

        /// <summary>射击敌人的射程，米。弹体飞 射程 ÷ 弹速 秒，取 6 米 / 4 米每秒 ≈ 1.5 秒可读。</summary>
        public const float RangedAttackRange = 6f;

        /// <summary>射击敌人的弹速，米/秒。</summary>
        public const float RangedProjectileSpeed = 4f;

        /// <summary>医疗兵的治疗间隔、半径、回复比例（策划定：3 秒 / 5 米 / 20%）。</summary>
        public const float MedicHealInterval = 3f;

        /// <summary>医疗兵治疗半径，米。</summary>
        public const float MedicHealRadius = 5f;

        /// <summary>医疗兵每次回复最大生命的比例。</summary>
        public const float MedicHealPercent = 0.2f;

        /// <summary>兵工厂的产出间隔，秒。</summary>
        public const float ArsenalProduceInterval = 3f;

        /// <summary>兵工厂同时存在的产出物上限。</summary>
        public const int ArsenalMaxAliveProducts = 12;

        /// <summary>
        /// 木桩的血量。取一个"打得完才怪"的大数而不是 <c>float.MaxValue</c>：
        /// 血条 / 百分比显示之类的 UI 遇到无穷大会算出 NaN，用有限大数更安全，
        /// 而 99999 相对单次几十点的伤害已经等于无限。
        /// </summary>
        public const float DummyHealth = 99999f;


        /// <summary>一种敌人的完整配置。</summary>
        public struct Config
        {
            /// <summary>原型标识。</summary>
            public EnemyArchetype archetype;

            /// <summary>显示名，同时是 prefab 文件名。</summary>
            public string displayName;

            /// <summary>移速，米/秒。</summary>
            public float moveSpeed;

            /// <summary>血量，点。</summary>
            public float health;

            /// <summary>攻击力（接触伤害或弹体伤害）。</summary>
            public float attackPower;

            /// <summary>攻击 / 接触伤害冷却，秒。</summary>
            public float attackCooldown;

            /// <summary>移动模式。</summary>
            public EnemyMoveMode moveMode;

            /// <summary>是否造成接触伤害。</summary>
            public bool hasContactDamage;

            /// <summary>是否发射弹体。</summary>
            public bool hasRangedAttack;

            /// <summary>是否持续产兵。</summary>
            public bool hasProducer;

            /// <summary>是否治疗周围敌人。</summary>
            public bool hasHealer;

            /// <summary>保持距离模式的目标距离，米。0 表示用停靠距离。</summary>
            public float preferredDistance;

            /// <summary>视觉颜色（灰盒占位）。</summary>
            public Color color;
        }

        /// <summary>取某种敌人的配置。</summary>
        public static Config Get(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Fast:
                    return Fast();

                case EnemyArchetype.RangedKeeper:
                    return RangedKeeper();

                case EnemyArchetype.RangedChaser:
                    return RangedChaser();

                case EnemyArchetype.Arsenal:
                    return Arsenal();

                case EnemyArchetype.Medic:
                    return Medic();

                case EnemyArchetype.Dummy:
                    return Dummy();

                default:
                    return Normal();
            }
        }

        /// <summary>1 普通敌人：追击 + 接触伤害。</summary>
        public static Config Normal()
        {
            return new Config
            {
                archetype = EnemyArchetype.Normal,
                displayName = "Normal",
                moveSpeed = BaseMoveSpeed,
                health = BaseHealth,
                attackPower = BaseAttackPower,
                attackCooldown = BaseAttackCooldown,
                moveMode = EnemyMoveMode.Chase,
                hasContactDamage = true,
                color = new Color(0.85f, 0.30f, 0.35f, 1f)
            };
        }

        /// <summary>2 快速敌人：血 15（3 拳）、攻击 4、移速 3.25，只接触。</summary>
        public static Config Fast()
        {
            var config = Normal();
            config.archetype = EnemyArchetype.Fast;
            config.displayName = "Fast";
            config.moveSpeed = 3.25f;
            config.health = 15f;
            config.attackPower = 4f;
            config.color = new Color(0.95f, 0.55f, 0.20f, 1f);
            return config;
        }

        /// <summary>3 静止射击：血 15、单发 4、移速 2，只射击。</summary>
        public static Config RangedKeeper()
        {
            return new Config
            {
                archetype = EnemyArchetype.RangedKeeper,
                displayName = "RangedKeeper",
                moveSpeed = 2f,
                health = 15f,
                attackPower = 4f,
                attackCooldown = BaseAttackCooldown,
                moveMode = EnemyMoveMode.KeepDistance,
                hasContactDamage = false,
                hasRangedAttack = true,
                // 由场景构建器按屏幕尺寸覆写为「1/4 屏」；这里的值只是兜底。
                preferredDistance = RangedAttackRange,
                color = new Color(0.45f, 0.55f, 0.95f, 1f)
            };
        }

        /// <summary>4 移动射击：血 20，接触和子弹各 3。</summary>
        public static Config RangedChaser()
        {
            return new Config
            {
                archetype = EnemyArchetype.RangedChaser,
                displayName = "RangedChaser",
                moveSpeed = BaseMoveSpeed,
                health = 20f,
                attackPower = 3f,
                attackCooldown = BaseAttackCooldown,
                moveMode = EnemyMoveMode.Chase,
                hasContactDamage = true,
                hasRangedAttack = true,
                color = new Color(0.70f, 0.35f, 0.85f, 1f)
            };
        }

        /// <summary>5 兵工厂：血 30、不攻击，只产兵。</summary>
        public static Config Arsenal()
        {
            return new Config
            {
                archetype = EnemyArchetype.Arsenal,
                displayName = "Arsenal",
                moveSpeed = 0f,
                health = 30f,
                attackPower = 0f,
                attackCooldown = BaseAttackCooldown,
                moveMode = EnemyMoveMode.Stationary,
                hasContactDamage = false,
                hasProducer = true,
                color = new Color(0.55f, 0.50f, 0.25f, 1f)
            };
        }

        /// <summary>6 医疗兵：血 15、攻击 0，只治疗。</summary>
        public static Config Medic()
        {
            return new Config
            {
                archetype = EnemyArchetype.Medic,
                displayName = "Medic",
                moveSpeed = 2.75f,
                health = 15f,
                attackPower = 0f,
                attackCooldown = BaseAttackCooldown,
                // 用「保持距离」而不是「远离」：地图有边界，纯远离会一路走到墙角贴着。
                // 保持距离天然有稳定解——太远靠近、太近后退、容差内不动。
                moveMode = EnemyMoveMode.KeepDistance,
                hasContactDamage = false,
                hasHealer = true,
                color = new Color(0.35f, 0.85f, 0.50f, 1f)
            };
        }

        /// <summary>
        /// 7 木桩：**不动、不攻击**，只用来测伤害。
        ///
        /// 每个行为开关都是 false 是有意的，对应 <see cref="EnemyPrefabBuilder"/> 里的处理：
        /// - <c>hasContactDamage = false</c> → 不挂 <c>EnemyContactDamage</c>，碰到玩家不掉血
        /// - <c>hasRangedAttack = false</c> → <c>EnemyAttack</c> 被移除，不发射弹体
        /// - <c>hasProducer</c> / <c>hasHealer = false</c> → 不产兵、不治疗
        /// - <c>moveMode = Stationary</c> → <c>EnemyMotor</c> 不移动
        ///
        /// 于是它**只剩下被打**这一件事：仍然挂 <c>EnemyRuntime</c>，
        /// 所以 <c>TakeDamage</c>（器官伤害唯一的真实入口）照常生效、照常发受击/击杀信号。
        /// 这一点很重要——木桩要能验真实伤害路径，不能是个假的靶子。
        /// </summary>
        public static Config Dummy()
        {
            return new Config
            {
                archetype = EnemyArchetype.Dummy,
                displayName = "Dummy",
                moveSpeed = 0f,
                health = DummyHealth,
                attackPower = 0f,
                attackCooldown = BaseAttackCooldown,
                moveMode = EnemyMoveMode.Stationary,
                hasContactDamage = false,
                hasRangedAttack = false,
                hasProducer = false,
                hasHealer = false,
                // 木头色，且刻意与六种战斗单位区分开（它们都偏红/橙/蓝/紫/黄/绿）。
                color = new Color(0.62f, 0.52f, 0.40f, 1f)
            };
        }

        /// <summary>全部原型，按 <see cref="EnemyArchetype"/> 的声明顺序。</summary>
        public static EnemyArchetype[] AllArchetypes()
        {
            return new[]
            {
                EnemyArchetype.Normal,
                EnemyArchetype.Fast,
                EnemyArchetype.RangedKeeper,
                EnemyArchetype.RangedChaser,
                EnemyArchetype.Arsenal,
                EnemyArchetype.Medic,
                EnemyArchetype.Dummy
            };
        }

        /// <summary>
        /// 会被波次刷怪用到的原型。**木桩不在其中**——它是测试道具，不该混进战斗。
        /// 刷怪表将来做成多条时，这个数组就是"可刷"的白名单。
        /// </summary>
        public static EnemyArchetype[] SpawnableArchetypes()
        {
            return new[]
            {
                EnemyArchetype.Normal,
                EnemyArchetype.Fast,
                EnemyArchetype.RangedKeeper,
                EnemyArchetype.RangedChaser,
                EnemyArchetype.Arsenal,
                EnemyArchetype.Medic
            };
        }    }
}
