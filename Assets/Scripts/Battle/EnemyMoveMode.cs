namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人朝玩家移动的方式。
    /// 拆成枚举而不是多个脚本：移动逻辑相同、只有"目标距离"不同，
    /// 写成一个组件的多种模式比复制四份代码更不容易漂移。
    /// </summary>
    public enum EnemyMoveMode
    {
        /// <summary>追击玩家，直到进入攻击距离。</summary>
        Chase = 0,

        /// <summary>保持一段距离：太近就后退，太远就靠近。用于"静止"射击敌人。</summary>
        KeepDistance = 1,

        /// <summary>远离玩家。用于医疗兵。</summary>
        Flee = 2,

        /// <summary>完全不动。用于兵工厂。</summary>
        Stationary = 3
    }
}
