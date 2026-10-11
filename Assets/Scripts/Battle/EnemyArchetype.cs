namespace TideBorne.Battle
{
    /// <summary>
    /// 六种灰盒敌人的原型标识。
    /// 用于按名取 prefab（兵工厂要生成普通敌人、未来掉落/关卡配置也要按名引用），
    /// 比字符串硬编码可靠。
    /// </summary>
    public enum EnemyArchetype
    {
        /// <summary>1 普通敌人：追击 + 接触伤害。</summary>
        Normal = 0,

        /// <summary>2 快速敌人：移速 1.3 倍、血量 0.7 倍 + 接触伤害。</summary>
        Fast = 1,

        /// <summary>3 静止射击敌人：保持约 1/4 屏距离，朝玩家射击，不接触伤害。</summary>
        RangedKeeper = 2,

        /// <summary>4 移动射击敌人：追击并接触伤害，同时朝玩家射击。</summary>
        RangedChaser = 3,

        /// <summary>5 兵工厂：原地不动，持续产生普通敌人。</summary>
        Arsenal = 4,

        /// <summary>6 医疗兵：远离玩家，间歇治疗周围敌人。</summary>
        Medic = 5
    }
}
