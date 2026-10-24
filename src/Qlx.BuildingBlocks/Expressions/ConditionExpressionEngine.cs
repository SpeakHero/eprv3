/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式引擎实现（不可变注册表快照 + 求值上下文组装 + 布尔收敛）
 */

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件表达式引擎实现。实例创建时由 <see cref="ConditionEngineOptions"/> 构建一次不可变注册表快照，
/// 此后完全只读、线程安全；业务扩展在组合期注册，运行期零修改、零锁，核心运算逻辑稳定不变。
/// </summary>
public sealed class ConditionExpressionEngine : IConditionExpressionEngine
{
    private readonly ConditionRegistry _registry;
    private readonly int _maxExpressionLength;
    private readonly int _maxDepth;
    private readonly bool _throwOnUnresolvedVariable;
    private readonly IServiceProvider? _services;

    /// <summary>
    /// 构造函数（组合期一次性构建注册表快照；推荐经 <see cref="ConditionExpressionEngineFactory"/> 或 DI 创建）。
    /// </summary>
    /// <param name="options">组合期配置。</param>
    public ConditionExpressionEngine(ConditionEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _registry = options.Build();
        _maxExpressionLength = options.MaxExpressionLength;
        _maxDepth = options.MaxDepth;
        _throwOnUnresolvedVariable = options.ThrowOnUnresolvedVariable;
        _services = options.Services;
    }

    /// <inheritdoc />
    public ConditionExpression Parse(string expressionText)
    {
        if (string.IsNullOrWhiteSpace(expressionText))
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Parse,
                "表达式不能为空白。",
                expressionSnippet: expressionText);
        }

        if (expressionText.Length > _maxExpressionLength)
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Parse,
                $"表达式长度 {expressionText.Length} 超出限制（{_maxExpressionLength} 字符）。",
                expressionSnippet: expressionText[..Math.Min(64, expressionText.Length)]);
        }

        var result = ConditionParser.Parse(expressionText, _registry, _maxDepth);
        return new ConditionExpression
        {
            SourceText = expressionText,
            Root = result.Root,
            Variables = result.Variables
        };
    }

    /// <inheritdoc />
    public bool Evaluate(ConditionExpression expression, IVariableResolver? variables = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var value = EvaluateCore(expression, variables);
        return value switch
        {
            bool flag => flag,
            null => false,
            _ => throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                $"条件求值要求布尔结果，实际为 {Describe(value)}。请使用 EvaluateValue 取值，或调整表达式使结果为布尔。")
        };
    }

    /// <inheritdoc />
    public bool Evaluate(string expressionText, IVariableResolver? variables = null)
        => Evaluate(Parse(expressionText), variables);

    /// <inheritdoc />
    public object? EvaluateValue(ConditionExpression expression, IVariableResolver? variables = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return EvaluateCore(expression, variables);
    }

    /// <inheritdoc />
    public object? EvaluateValue(string expressionText, IVariableResolver? variables = null)
        => EvaluateValue(Parse(expressionText), variables);

    private object? EvaluateCore(ConditionExpression expression, IVariableResolver? variables)
    {
        var context = new ConditionEvaluationContext(_registry, variables, _services, _throwOnUnresolvedVariable);
        return expression.Root.Evaluate(context);
    }

    private static string Describe(object value)
        => $"{value.GetType().Name}（{ConditionValueOps.Format(value)}）";
}
