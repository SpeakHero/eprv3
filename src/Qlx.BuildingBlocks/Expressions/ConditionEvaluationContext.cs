/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:20:00 +08:00 中国标准时间 星期三
 * 描述：单次表达式求值的上下文（实现 IConditionEvaluationContext 接口）
 */

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件表达式求值上下文。每次 <c>Evaluate</c> 调用由引擎创建一次，
/// 承载变量解析器、可选 DI 服务与注册表引用；实现 <see cref="IConditionEvaluationContext"/> 接口，
/// 供 Abstractions 层的 <see cref="ConditionNode"/> 判别联合在求值时访问运算符 / 函数注册表。
/// </summary>
public sealed class ConditionEvaluationContext : IConditionEvaluationContext
{
    private readonly ConditionRegistry _registry;
    private readonly bool _throwOnUnresolvedVariable;

    internal ConditionEvaluationContext(
        ConditionRegistry registry,
        IVariableResolver? variables,
        IServiceProvider? services,
        bool throwOnUnresolvedVariable)
    {
        _registry = registry;
        Variables = variables;
        Services = services;
        _throwOnUnresolvedVariable = throwOnUnresolvedVariable;
    }

    /// <inheritdoc />
    public IVariableResolver? Variables { get; }

    /// <inheritdoc />
    public IServiceProvider? Services { get; }

    /// <inheritdoc />
    public IReadOnlyList<IConditionValueCoercer> ValueCoercers => _registry.ValueCoercers;

    /// <inheritdoc />
    public object? ResolveVariable(string name)
    {
        if (Variables is not null && Variables.TryResolve(name, out var value))
        {
            return value;
        }

        if (_throwOnUnresolvedVariable)
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                $"变量未解析：{name}。请检查变量名或传入的变量解析器。",
                expressionSnippet: name);
        }

        return null;
    }

    /// <inheritdoc />
    public IConditionBinaryOperator RequireBinaryOperator(string symbol)
        => _registry.TryGetBinaryOperator(symbol)
           ?? throw new ConditionExpressionException(
               ConditionExpressionErrorKind.Evaluate,
               $"二元运算符未注册：{symbol}",
               expressionSnippet: symbol);

    /// <inheritdoc />
    public IConditionUnaryOperator RequireUnaryOperator(string symbol)
        => _registry.TryGetUnaryOperator(symbol)
           ?? throw new ConditionExpressionException(
               ConditionExpressionErrorKind.Evaluate,
               $"一元运算符未注册：{symbol}",
               expressionSnippet: symbol);

    /// <inheritdoc />
    public IConditionFunction RequireFunction(string name)
        => _registry.TryGetFunction(name)
           ?? throw new ConditionExpressionException(
               ConditionExpressionErrorKind.Evaluate,
               $"函数未注册：{name}",
               expressionSnippet: name);

    /// <inheritdoc />
    public bool IsTruthy(object? value) => ConditionValueOps.IsTruthy(value, this);
}
