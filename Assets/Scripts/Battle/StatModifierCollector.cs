using System.Collections.Generic;

namespace TideBorne.Battle
{
    /// <summary>
    /// 属性修改的收集器：每帧取值前由 <see cref="PlayerStatsBase"/> 清空，
    /// 各 <see cref="IStatModifierSource"/> 把自己的修改写进来，再统一参与计算。
    ///
    /// 之所以是「每次重算」而不是「增量累加」，见 <c>接口草案.md</c> §5.4 乙：
    /// 装 / 拆器官时重算天然精确，不会累积误差。
    /// </summary>
    public sealed class StatModifierCollector
    {
        /// <summary>加法区的增量，按字段索引存放。</summary>
        private readonly float[] _additive = new float[StatFieldCount];

        /// <summary>乘法区的倍率，按字段索引存放；无修改时为 1。</summary>
        private readonly float[] _multiplicative = new float[StatFieldCount];

        /// <summary><see cref="StatField"/> 的字段总数，新增枚举值时必须同步。</summary>
        public const int StatFieldCount = 14;

        public StatModifierCollector()
        {
            Clear();
        }

        /// <summary>清空到「无修改」状态：加法区归零、乘法区归一。</summary>
        public void Clear()
        {
            for (var i = 0; i < StatFieldCount; i++)
            {
                _additive[i] = 0f;
                _multiplicative[i] = 1f;
            }
        }

        /// <summary>累加一条加法区修改。</summary>
        public void Add(StatField field, float value)
        {
            _additive[(int)field] += value;
        }

        /// <summary>累乘一条乘法区修改。</summary>
        public void Multiply(StatField field, float factor)
        {
            _multiplicative[(int)field] *= factor;
        }

        /// <summary>按 §5.4 甲的两层结构求最终值：先加、后乘。</summary>
        public float Apply(StatField field, float baseValue)
        {
            var index = (int)field;
            return (baseValue + _additive[index]) * _multiplicative[index];
        }

        /// <summary>把另一组来源的修改合并进来，便于分层汇总。</summary>
        public void AddSource(IStatModifierSource source)
        {
            if (source == null)
                return;

            source.CollectAdditiveModifiers(this);
            source.CollectMultiplicativeModifiers(this);
        }

        /// <summary>把一批来源的修改合并进来，集合本身为空时直接返回。</summary>
        public void AddSources(IReadOnlyList<IStatModifierSource> sources)
        {
            if (sources == null)
                return;

            for (var i = 0; i < sources.Count; i++)
                AddSource(sources[i]);
        }
    }
}
