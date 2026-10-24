/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:15:00 +08:00 中国标准时间 星期三
 * 描述：条件求值上下文契约（含变量解析 / DI 服务 / 运算符注册表 / 函数注册表）
 */

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件表达式求值上下文契约。每次求值由引擎组装一次，
/// 承载变量解析器、可选 DI 服务与运算符 / 函数注册表引用。
/// </summary>
public interface IConditionEvaluationContext
{
    /// <summary>变量解析器（业务方传入；null 表示不解析任何变量）。</summary>
    IVariableResolver? Variables { get; }

    /// <summary>服务提供程序（自定义函数 / 自定义运算符访问宿主 DI 服务的入口；可能为 null）。</summary>
    IServiceProvider? Services { get; }

    /// <summary>值强制转换器列表（内置 + 自定义，按优先级排列）。</summary>
    IReadOnlyList<IConditionValueCoercer> ValueCoercers { get; }

    /// <summary>
    /// 解析变量。未注册解析器或变量不存在时，按引擎开关决定抛出求值异常或回退 null。
    /// </summary>
    /// <param name="name">变量名（不含 <c>${}</c> 包装）。</param>
    /// <returns>变量值。</returns>
    /// <exception cref="ConditionExpressionException">变量无法解析且引擎开启严格模式时抛出。</exception>
    object? ResolveVariable(string name);

    /// <summary>
    /// 要求获取已注册的二元运算符（未注册抛求值异常）。
    /// </summary>
    /// <param name="symbol">运算符主符号或别名。</param>
    /// <returns>运算符实例。</returns>
    /// <exception cref="ConditionExpressionException">运算符未注册时抛出。</exception>
    IConditionBinaryOperator RequireBinaryOperator(string symbol);

    /// <summary>
    /// 要求获取已注册的一元运算符（未注册抛求值异常）。
    /// </summary>
    /// <param name="symbol">运算符主符号或别名。</param>
    /// <returns>运算符实例。</returns>
    /// <exception cref="ConditionExpressionException">运算符未注册时抛出。</exception>
    IConditionUnaryOperator RequireUnaryOperator(string symbol);

    /// <summary>
    /// 要求获取已注册的函数（未注册抛求值异常）。
    /// </summary>
    /// <param name="name">函数名（不区分大小写）。</param>
    /// <returns>函数实例。</returns>
    /// <exception cref="ConditionExpressionException">函数未注册时抛出。</exception>
    IConditionFunction RequireFunction(string name);

    /// <summary>
    /// 条件求值真值判断（用于三元节点、短路运算符等场景）。
    /// </summary>
    /// <param name="value">待判断值。</param>
    /// <returns>真值。</returns>
    bool IsTruthy(object? value);
}
