/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 描述：条件表达式引擎公开接口（解析 / 条件求值 / 取值求值），从 BuildingBlocks 下沉至 Abstractions
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件表达式引擎公开接口。引擎实例不可变、线程安全，可作为单例全局复用；
/// 上层模块（审批中心、预警规则、业务模板等）仅依赖本接口，不感知具体实现。
/// </summary>
public interface IConditionExpressionEngine
{
    /// <summary>
    /// 解析表达式为编译制品（推荐：解析一次、多次求值）。
    /// </summary>
    /// <param name="expressionText">表达式源文本，如 <c>total_amount &gt; 50000 &amp;&amp; status == 'Draft'</c>。</param>
    /// <returns>已编译表达式制品。</returns>
    /// <exception cref="ConditionExpressionException">语法错误、超出长度 / 深度限制时抛出。</exception>
    ConditionExpression Parse(string expressionText);

    /// <summary>
    /// 条件求值：结果必须是布尔（null 视为 false，其他类型抛求值异常）。
    /// </summary>
    /// <param name="expression">已编译表达式。</param>
    /// <param name="variables">变量解析器（可选）。</param>
    /// <returns>条件结果。</returns>
    /// <exception cref="ConditionExpressionException">变量未解析（严格模式）、类型不兼容、扩展执行异常时抛出。</exception>
    bool Evaluate(ConditionExpression expression, IVariableResolver? variables = null);

    /// <summary>
    /// 条件求值（便捷重载：内部即时解析，适合一次性场景；高频场景请先 <see cref="Parse"/> 再求值）。
    /// </summary>
    /// <param name="expressionText">表达式源文本。</param>
    /// <param name="variables">变量解析器（可选）。</param>
    /// <returns>条件结果。</returns>
    /// <exception cref="ConditionExpressionException">语法错误或求值失败时抛出。</exception>
    bool Evaluate(string expressionText, IVariableResolver? variables = null);

    /// <summary>
    /// 取值求值：返回表达式结果原始值（用于业务模板取值场景，如 <c>if(amount &gt; 1000, 'A', 'B')</c>）。
    /// </summary>
    /// <param name="expression">已编译表达式。</param>
    /// <param name="variables">变量解析器（可选）。</param>
    /// <returns>表达式结果值。</returns>
    /// <exception cref="ConditionExpressionException">求值失败时抛出。</exception>
    object? EvaluateValue(ConditionExpression expression, IVariableResolver? variables = null);

    /// <summary>
    /// 取值求值（便捷重载：内部即时解析）。
    /// </summary>
    /// <param name="expressionText">表达式源文本。</param>
    /// <param name="variables">变量解析器（可选）。</param>
    /// <returns>表达式结果值。</returns>
    /// <exception cref="ConditionExpressionException">语法错误或求值失败时抛出。</exception>
    object? EvaluateValue(string expressionText, IVariableResolver? variables = null);
}
