/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：引擎内置函数全集（区间 / 字符串 / 数值 / 空值 / 流程控制 / 日期），全部为无状态 record
 */

using System.Globalization;

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>内置函数装配入口（仅供注册表默认装配使用）。</summary>
internal static class BuiltInFunctions
{
    /// <summary>内置函数清单。</summary>
    internal static IReadOnlyList<IConditionFunction> All { get; } =
    [
        new BetweenFunction(),
        new ContainsFunction(),
        new StartsWithFunction(),
        new EndsWithFunction(),
        new IsNullFunction(),
        new IsNotNullFunction(),
        new LengthFunction(),
        new AbsFunction(),
        new MinFunction(),
        new MaxFunction(),
        new RoundFunction(),
        new FloorFunction(),
        new CeilingFunction(),
        new UpperFunction(),
        new LowerFunction(),
        new TrimFunction(),
        new SubstringFunction(),
        new IfFunction(),
        new CoalesceFunction(),
        new NowFunction(),
        new TodayFunction()
    ];
}

/// <summary>区间判断：between(value, low, high)，双端包含。</summary>
internal sealed record BetweenFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Between";

    /// <inheritdoc />
    public int MinArgumentCount => 3;

    /// <inheritdoc />
    public int MaxArgumentCount => 3;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
    {
        var (value, low, high) = (arguments[0], arguments[1], arguments[2]);
        if (value is null)
        {
            return false;
        }

        var aboveLow = ConditionValueOps.ValuesEqual(value, low, context)
            || (ConditionValueOps.TryCompare(value, low, context, 0, out var lowCmp) && lowCmp > 0);
        if (!aboveLow)
        {
            return false;
        }

        return ConditionValueOps.ValuesEqual(value, high, context)
            || (ConditionValueOps.TryCompare(value, high, context, 0, out var highCmp) && highCmp < 0);
    }
}

/// <summary>字符串包含：contains(text, sub)。</summary>
internal sealed record ContainsFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Contains";

    /// <inheritdoc />
    public int MinArgumentCount => 2;

    /// <inheritdoc />
    public int MaxArgumentCount => 2;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] is string text
            && arguments[1] is string sub
            && text.Contains(sub, StringComparison.Ordinal);
}

/// <summary>前缀匹配：startsWith(text, prefix)。</summary>
internal sealed record StartsWithFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "StartsWith";

    /// <inheritdoc />
    public int MinArgumentCount => 2;

    /// <inheritdoc />
    public int MaxArgumentCount => 2;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] is string text
            && arguments[1] is string prefix
            && text.StartsWith(prefix, StringComparison.Ordinal);
}

/// <summary>后缀匹配：endsWith(text, suffix)。</summary>
internal sealed record EndsWithFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "EndsWith";

    /// <inheritdoc />
    public int MinArgumentCount => 2;

    /// <inheritdoc />
    public int MaxArgumentCount => 2;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] is string text
            && arguments[1] is string suffix
            && text.EndsWith(suffix, StringComparison.Ordinal);
}

/// <summary>空值判断：isNull(x)。</summary>
internal sealed record IsNullFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "IsNull";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] is null;
}

/// <summary>非空判断：isNotNull(x)。</summary>
internal sealed record IsNotNullFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "IsNotNull";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] is not null;
}

/// <summary>长度：length(x)，字符串取字符数，集合取元素数。</summary>
internal sealed record LengthFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Length";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] switch
        {
            string s => s.Length,
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object?>().Count(),
            _ => throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "Length 函数要求参数为字符串或集合。",
                expressionSnippet: Name)
        };
}

/// <summary>绝对值：abs(x)。</summary>
internal sealed record AbsFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Abs";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] switch
        {
            decimal d => Math.Abs(d),
            int i => Math.Abs(i),
            long l => Math.Abs(l),
            double dbl => Math.Abs(dbl),
            float f => Math.Abs(f),
            _ => throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "Abs 函数要求参数为数值。",
                expressionSnippet: Name)
        };
}

/// <summary>最小值：min(a, b, ...)，经类型感知比较。</summary>
internal sealed record MinFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Min";

    /// <inheritdoc />
    public int MinArgumentCount => 2;

    /// <inheritdoc />
    public int MaxArgumentCount => int.MaxValue;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => FunctionHelpers.Extremum(arguments, context, takeSmaller: true);
}

/// <summary>最大值：max(a, b, ...)，经类型感知比较。</summary>
internal sealed record MaxFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Max";

    /// <inheritdoc />
    public int MinArgumentCount => 2;

    /// <inheritdoc />
    public int MaxArgumentCount => int.MaxValue;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => FunctionHelpers.Extremum(arguments, context, takeSmaller: false);
}

/// <summary>四舍五入：round(x) 或 round(x, digits)。</summary>
internal sealed record RoundFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Round";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 2;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
    {
        var digits = arguments.Count == 2
            ? Convert.ToInt32(arguments[1] ?? 0, CultureInfo.InvariantCulture)
            : 0;
        return arguments[0] switch
        {
            decimal d => Math.Round(d, digits),
            double dbl => Math.Round(dbl, digits),
            _ => throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "Round 函数要求第一个参数为数值。",
                expressionSnippet: Name)
        };
    }
}

/// <summary>向下取整：floor(x)。</summary>
internal sealed record FloorFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Floor";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] switch
        {
            decimal d => Math.Floor(d),
            double dbl => Math.Floor(dbl),
            _ => throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "Floor 函数要求参数为数值。",
                expressionSnippet: Name)
        };
}

/// <summary>向上取整：ceiling(x)。</summary>
internal sealed record CeilingFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Ceiling";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments[0] switch
        {
            decimal d => Math.Ceiling(d),
            double dbl => Math.Ceiling(dbl),
            _ => throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "Ceiling 函数要求参数为数值。",
                expressionSnippet: Name)
        };
}

/// <summary>转大写：upper(s)。</summary>
internal sealed record UpperFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Upper";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => FunctionHelpers.RequireString(arguments[0], Name).ToUpper(CultureInfo.InvariantCulture);
}

/// <summary>转小写：lower(s)。</summary>
internal sealed record LowerFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Lower";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => FunctionHelpers.RequireString(arguments[0], Name).ToLower(CultureInfo.InvariantCulture);
}

/// <summary>去首尾空白：trim(s)。</summary>
internal sealed record TrimFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Trim";

    /// <inheritdoc />
    public int MinArgumentCount => 1;

    /// <inheritdoc />
    public int MaxArgumentCount => 1;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => FunctionHelpers.RequireString(arguments[0], Name).Trim();
}

/// <summary>子串：substring(s, start) 或 substring(s, start, length)，越界抛求值异常。</summary>
internal sealed record SubstringFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Substring";

    /// <inheritdoc />
    public int MinArgumentCount => 2;

    /// <inheritdoc />
    public int MaxArgumentCount => 3;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
    {
        var text = FunctionHelpers.RequireString(arguments[0], Name);
        var start = Convert.ToInt32(arguments[1] ?? 0, CultureInfo.InvariantCulture);
        try
        {
            return arguments.Count == 3
                ? text.Substring(start, Convert.ToInt32(arguments[2] ?? 0, CultureInfo.InvariantCulture))
                : text.Substring(start);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                $"Substring 参数越界：start={start}，字符串长度={text.Length}。",
                expressionSnippet: Name,
                innerException: ex);
        }
    }
}

/// <summary>流程控制：if(condition, trueValue, falseValue)。</summary>
internal sealed record IfFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "If";

    /// <inheritdoc />
    public int MinArgumentCount => 3;

    /// <inheritdoc />
    public int MaxArgumentCount => 3;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => ConditionValueOps.IsTruthy(arguments[0], context) ? arguments[1] : arguments[2];
}

/// <summary>空值合并：coalesce(a, b, ...)，返回第一个非 null 值。</summary>
internal sealed record CoalesceFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Coalesce";

    /// <inheritdoc />
    public int MinArgumentCount => 2;

    /// <inheritdoc />
    public int MaxArgumentCount => int.MaxValue;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => arguments.FirstOrDefault(item => item is not null);
}

/// <summary>当前时间：now()，服务器本地时间。</summary>
internal sealed record NowFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Now";

    /// <inheritdoc />
    public int MinArgumentCount => 0;

    /// <inheritdoc />
    public int MaxArgumentCount => 0;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => DateTime.Now;
}

/// <summary>今天零点：today()，服务器本地时区。</summary>
internal sealed record TodayFunction : IConditionFunction
{
    /// <inheritdoc />
    public string Name => "Today";

    /// <inheritdoc />
    public int MinArgumentCount => 0;

    /// <inheritdoc />
    public int MaxArgumentCount => 0;

    /// <inheritdoc />
    public object? Invoke(IReadOnlyList<object?> arguments, IConditionEvaluationContext context)
        => DateTime.Today;
}

file static class FunctionHelpers
{
    public static string RequireString(object? value, string functionName)
        => value is string s
            ? s
            : throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                $"{functionName} 函数要求参数为字符串，实际为 {value?.GetType().Name ?? "null"}。",
                expressionSnippet: functionName);

    public static object? Extremum(IReadOnlyList<object?> arguments, IConditionEvaluationContext context, bool takeSmaller)
    {
        object? best = null;
        foreach (var item in arguments)
        {
            if (item is null)
            {
                continue;
            }

            if (best is null)
            {
                best = item;
                continue;
            }

            if (ConditionValueOps.TryCompare(item, best, context, 0, out var cmp)
                && (takeSmaller ? cmp < 0 : cmp > 0))
            {
                best = item;
            }
        }

        return best;
    }
}
