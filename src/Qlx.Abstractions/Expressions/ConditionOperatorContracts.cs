/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：二元 / 一元运算符扩展契约
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 二元运算符契约。业务方实现本接口并经引擎工厂注册，
/// 即可在不修改引擎核心代码的前提下扩展自定义二元运算符。
/// </summary>
/// <remarks>
/// 实现要求：实例必须线程安全且无状态（引擎单例全局复用）；运算逻辑必须为纯函数，
/// 禁止在 <see cref="Apply"/> 中修改引擎状态。
/// </remarks>
public interface IConditionBinaryOperator
{
    /// <summary>
    /// 运算符主符号（如 <c>"&gt;"</c>、<c>"AND"</c>）。符号运算区分大小写，纯字母符号按不区分大小写匹配。
    /// </summary>
    string Symbol { get; }

    /// <summary>别名集合（如 <c>=</c> 与 <c>==</c> 等价）。允许为空集合，禁止包含与 <see cref="Symbol"/> 相同的项。</summary>
    IEnumerable<string> Aliases { get; }

    /// <summary>优先级分组。自定义运算符从 <see cref="ConditionPrecedence"/> 既有分组中选择。</summary>
    ConditionPrecedence Precedence { get; }

    /// <summary>是否右结合（如幂运算）；默认左结合。</summary>
    bool IsRightAssociative { get; }

    /// <summary>
    /// 执行运算。
    /// </summary>
    /// <param name="left">左操作数值。</param>
    /// <param name="right">右操作数值。</param>
    /// <param name="context">求值上下文（可访问变量与 DI 服务）。</param>
    /// <returns>运算结果值。</returns>
    object? Apply(object? left, object? right, IConditionEvaluationContext context);
}

/// <summary>
/// 一元（前缀）运算符契约。一元运算符绑定优先级恒高于全部二元运算符。
/// 业务方实现本接口并经引擎工厂注册。
/// </summary>
public interface IConditionUnaryOperator
{
    /// <summary>运算符主符号（如 <c>"!"</c>、<c>"NOT"</c>）。</summary>
    string Symbol { get; }

    /// <summary>别名集合。允许为空集合。</summary>
    IEnumerable<string> Aliases { get; }

    /// <summary>
    /// 执行运算。
    /// </summary>
    /// <param name="operand">操作数值。</param>
    /// <param name="context">求值上下文。</param>
    /// <returns>运算结果值。</returns>
    object? Apply(object? operand, IConditionEvaluationContext context);
}

/// <summary>
/// 短路运算符内部契约：实现本接口的二元运算符在左操作数已能确定结果时可跳过右操作数求值。
/// </summary>
public interface IConditionShortCircuitOperator
{
    /// <summary>
    /// 尝试仅凭左操作数值短路求值。
    /// </summary>
    /// <param name="leftValue">左操作数值。</param>
    /// <param name="context">求值上下文。</param>
    /// <param name="result">短路时直接返回的结果值。</param>
    /// <returns>true 表示可以短路（右操作数不会被求值）；false 表示继续求值右操作数。</returns>
    bool TryShortCircuit(object? leftValue, IConditionEvaluationContext context, out object? result);
}

/// <summary>
/// 二元运算符优先级分组。自定义运算符必须从既有分组中选择，
/// 引擎核心优先级体系保持稳定，新增分组属于破坏性变更。
/// </summary>
public enum ConditionPrecedence
{
    /// <summary>逻辑或（|| / OR），最低优先级。</summary>
    LogicalOr = 1,

    /// <summary>逻辑与（&amp;&amp; / AND）。</summary>
    LogicalAnd = 2,

    /// <summary>相等性判断（== / != / = / &lt;&gt; / IN / NOT IN）。</summary>
    Equality = 3,

    /// <summary>关系比较（&gt; / &gt;= / &lt; / &lt;=）。</summary>
    Relational = 4,

    /// <summary>加减（+ / -）。</summary>
    Additive = 5,

    /// <summary>乘除取模（* / %），二元运算中最高优先级。</summary>
    Multiplicative = 6
}
