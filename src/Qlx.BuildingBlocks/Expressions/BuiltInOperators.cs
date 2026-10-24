/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：引擎内置运算符全集（逻辑短路 / 相等 / 关系 / 算术 / IN 成员判断 / 一元），全部为无状态 record
 */

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>内置二元 / 一元运算符装配入口（仅供注册表默认装配使用）。</summary>
internal static class BuiltInOperators
{
    /// <summary>内置二元运算符清单。</summary>
    internal static IReadOnlyList<IConditionBinaryOperator> Binary { get; } =
    [
        new AndOperator(),
        new OrOperator(),
        new EqualOperator(),
        new NotEqualOperator(),
        new GreaterThanOperator(),
        new GreaterOrEqualOperator(),
        new LessThanOperator(),
        new LessOrEqualOperator(),
        new InOperator(),
        new NotInOperator(),
        new AddOperator(),
        new SubtractOperator(),
        new MultiplyOperator(),
        new DivideOperator(),
        new ModuloOperator()
    ];

    /// <summary>内置一元运算符清单。</summary>
    internal static IReadOnlyList<IConditionUnaryOperator> Unary { get; } =
    [
        new NotOperator(),
        new NegateOperator(),
        new UnaryPlusOperator()
    ];
}

/// <summary>逻辑与：左操作数为假时短路（右操作数不求值）。</summary>
internal sealed record AndOperator : IConditionBinaryOperator, IConditionShortCircuitOperator
{
    /// <inheritdoc />
    public string Symbol => "&&";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => ["AND"];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.LogicalAnd;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.IsTruthy(left, context) && ConditionValueOps.IsTruthy(right, context);

    /// <inheritdoc />
    public bool TryShortCircuit(object? leftValue, IConditionEvaluationContext context, out object? result)
    {
        if (!ConditionValueOps.IsTruthy(leftValue, context))
        {
            result = false;
            return true;
        }

        result = null;
        return false;
    }
}

/// <summary>逻辑或：左操作数为真时短路（右操作数不求值）。</summary>
internal sealed record OrOperator : IConditionBinaryOperator, IConditionShortCircuitOperator
{
    /// <inheritdoc />
    public string Symbol => "||";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => ["OR"];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.LogicalOr;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.IsTruthy(left, context) || ConditionValueOps.IsTruthy(right, context);

    /// <inheritdoc />
    public bool TryShortCircuit(object? leftValue, IConditionEvaluationContext context, out object? result)
    {
        if (ConditionValueOps.IsTruthy(leftValue, context))
        {
            result = true;
            return true;
        }

        result = null;
        return false;
    }
}

/// <summary>相等判断（== / =），null == null 为真。</summary>
internal sealed record EqualOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "==";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => ["="];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Equality;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.ValuesEqual(left, right, context);
}

/// <summary>不等判断（!= / &lt;&gt;）。</summary>
internal sealed record NotEqualOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "!=";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => ["<>"];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Equality;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => !ConditionValueOps.ValuesEqual(left, right, context);
}

/// <summary>大于（&gt;）；任一侧 null 或不可比较时结果为 false。</summary>
internal sealed record GreaterThanOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => ">";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Relational;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.TryCompare(left, right, context, 0, out var result) && result > 0;
}

/// <summary>大于等于（&gt;=）。</summary>
internal sealed record GreaterOrEqualOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => ">=";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Relational;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.TryCompare(left, right, context, 0, out var result) && result >= 0;
}

/// <summary>小于（&lt;）。</summary>
internal sealed record LessThanOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "<";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Relational;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.TryCompare(left, right, context, 0, out var result) && result < 0;
}

/// <summary>小于等于（&lt;=）。</summary>
internal sealed record LessOrEqualOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "<=";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Relational;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.TryCompare(left, right, context, 0, out var result) && result <= 0;
}

/// <summary>成员包含判断（IN）：左值与右集合任一元素相等即为真。</summary>
internal sealed record InOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "IN";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Equality;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => Enumerate(right).Any(item => ConditionValueOps.ValuesEqual(left, item, context));

    internal static IEnumerable<object?> Enumerate(object? right)
    {
        if (right is null)
        {
            yield break;
        }

        if (right is string)
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "IN 运算右操作数必须为集合（列表字面量或集合变量），不能为字符串。",
                expressionSnippet: "IN");
        }

        if (right is IEnumerable<object?> typed)
        {
            foreach (var item in typed)
            {
                yield return item;
            }

            yield break;
        }

        if (right is System.Collections.IEnumerable untyped)
        {
            foreach (var item in untyped)
            {
                yield return item;
            }

            yield break;
        }

        throw new ConditionExpressionException(
            ConditionExpressionErrorKind.Evaluate,
            $"IN 运算右操作数必须为集合，实际为 {right.GetType().Name}。",
            expressionSnippet: "IN");
    }
}

/// <summary>成员排除判断（NOT IN）。</summary>
internal sealed record NotInOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "NOT IN";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Equality;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => !Enumerate(right).Any(item => ConditionValueOps.ValuesEqual(left, item, context));

    private static IEnumerable<object?> Enumerate(object? right) => InOperator.Enumerate(right);
}

/// <summary>加法：数值相加（decimal 优先），任一侧为字符串时拼接。</summary>
internal sealed record AddOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "+";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Additive;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.Add(left, right);
}

/// <summary>减法：数值相减；日期族相减返回 <see cref="TimeSpan"/>。</summary>
internal sealed record SubtractOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "-";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Additive;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.Subtract(left, right);
}

/// <summary>乘法。</summary>
internal sealed record MultiplyOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "*";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Multiplicative;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.Multiply(left, right);
}

/// <summary>除法：整型参与时提升为 decimal（5 / 2 = 2.5），除零抛求值异常。</summary>
internal sealed record DivideOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "/";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Multiplicative;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.Divide(left, right);
}

/// <summary>取模。</summary>
internal sealed record ModuloOperator : IConditionBinaryOperator
{
    /// <inheritdoc />
    public string Symbol => "%";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public ConditionPrecedence Precedence => ConditionPrecedence.Multiplicative;

    /// <inheritdoc />
    public bool IsRightAssociative => false;

    /// <inheritdoc />
    public object? Apply(object? left, object? right, IConditionEvaluationContext context)
        => ConditionValueOps.Modulo(left, right);
}

/// <summary>逻辑非（! / NOT）：null 取非为 true。</summary>
internal sealed record NotOperator : IConditionUnaryOperator
{
    /// <inheritdoc />
    public string Symbol => "!";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => ["NOT"];

    /// <inheritdoc />
    public object? Apply(object? operand, IConditionEvaluationContext context)
        => !ConditionValueOps.IsTruthy(operand, context);
}

/// <summary>数值取负（一元 -）。</summary>
internal sealed record NegateOperator : IConditionUnaryOperator
{
    /// <inheritdoc />
    public string Symbol => "-";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public object? Apply(object? operand, IConditionEvaluationContext context)
        => operand switch
        {
            null => throw ConditionValueOps.IncompatibleUnaryException("-"),
            decimal d => -d,
            int i => -i,
            long l => -l,
            short s => -s,
            sbyte sb => -sb,
            double dbl => -dbl,
            float f => -f,
            _ when ConditionValueOps.IsNumeric(operand) => -Convert.ToDecimal(operand, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw ConditionValueOps.IncompatibleUnaryException("-")
        };
}

/// <summary>一元正号（+）：数值原样返回。</summary>
internal sealed record UnaryPlusOperator : IConditionUnaryOperator
{
    /// <inheritdoc />
    public string Symbol => "+";

    /// <inheritdoc />
    public IEnumerable<string> Aliases => [];

    /// <inheritdoc />
    public object? Apply(object? operand, IConditionEvaluationContext context)
        => ConditionValueOps.IsNumeric(operand)
            ? operand
            : throw ConditionValueOps.IncompatibleUnaryException("+");
}
