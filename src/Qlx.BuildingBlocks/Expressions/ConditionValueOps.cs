/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件值运算核心（真值判断 / 类型感知比较 / 算术运算 / 数值提升），仅供引擎与内置运算符使用
 */

using System.Collections.Frozen;
using System.Globalization;

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件值运算核心。定义引擎统一的空值语义、类型提升与比较规则：
/// <list type="bullet">
/// <item>逻辑上下文：null 视为 false，非布尔值抛求值异常；</item>
/// <item>相等判断：null == null 为真，null 与非 null 比较为假；</item>
/// <item>关系比较：任一侧为 null 不可比较（结果为 false）；</item>
/// <item>数值提升：整型 / decimal 参与 decimal 运算，含 float / double 时提升为 double；</item>
/// <item>日期族（DateTime / DateTimeOffset / DateOnly / TimeOnly）跨形态比较统一归一为 DateTime；</item>
/// <item>跨类别比较先经值强制转换器链（自定义在前、内置兜底），失败则不可比较。</item>
/// </list>
/// </summary>
internal static class ConditionValueOps
{
    /// <summary>数值类型全集（含 decimal 与浮点）。</summary>
    private static readonly FrozenSet<Type> s_numericTypes = new[]
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal)
    }.ToFrozenSet();

    /// <summary>
    /// 逻辑真值判断：bool 原样返回；null 视为 false；其他类型抛求值异常（条件场景禁止隐式数值转布尔）。
    /// </summary>
    internal static bool IsTruthy(object? value, IConditionEvaluationContext context) => value switch
    {
        bool b => b,
        null => false,
        _ => throw new ConditionExpressionException(
            ConditionExpressionErrorKind.Evaluate,
            $"逻辑运算要求布尔值，实际为 {Describe(value)}。",
            expressionSnippet: Describe(value))
    };

    /// <summary>
    /// 相等判断（== / != / IN）：null 语义按引擎约定；其余经类型感知比较。
    /// </summary>
    internal static bool ValuesEqual(object? left, object? right, IConditionEvaluationContext context)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return TryCompare(left, right, context, recursionDepth: 0, out var result) ? result == 0 : left.Equals(right);
    }

    /// <summary>
    /// 尝试类型感知比较。返回 false 表示不可比较（关系比较按 false 处理）。
    /// </summary>
    internal static bool TryCompare(
        object? left,
        object? right,
        IConditionEvaluationContext context,
        int recursionDepth,
        out int result)
    {
        result = 0;
        if (left is null || right is null)
        {
            return false;
        }

        var leftKind = GetKind(left);
        var rightKind = GetKind(right);

        if (leftKind == rightKind)
        {
            return CompareSameKind(left, right, leftKind, out result);
        }

        if (left is Enum || right is Enum)
        {
            return CompareEnum(left, right, out result);
        }

        // 跨类别：先走值强制转换器链（自定义在前、内置兜底），限制递归深度防循环转换
        if (recursionDepth < 2)
        {
            foreach (var coercer in context.ValueCoercers)
            {
                if (coercer.TryConvert(left, rightKind, out var convertedLeft)
                    && TryCompare(convertedLeft, right, context, recursionDepth + 1, out result))
                {
                    return true;
                }

                if (coercer.TryConvert(right, leftKind, out var convertedRight)
                    && TryCompare(left, convertedRight, context, recursionDepth + 1, out result))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>判断值是否为数值类型。</summary>
    internal static bool IsNumeric(object? value) => value is not null && s_numericTypes.Contains(value.GetType());

    /// <summary>把值格式化为不变文化字符串（字符串拼接与可读文本共用）。</summary>
    internal static string Format(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>数值加法（含字符串拼接路由）。</summary>
    internal static object Add(object? left, object? right)
    {
        if (left is string || right is string)
        {
            return Format(left) + Format(right);
        }

        return ApplyNumeric(left, right, static (a, b) => a + b, static (a, b) => a + b, nameof(Add));
    }

    /// <summary>数值减法；日期族相减返回 <see cref="TimeSpan"/>。</summary>
    internal static object Subtract(object? left, object? right)
    {
        if (left is null || right is null)
        {
            throw IncompatibleArithmeticException(nameof(Subtract), left, right);
        }

        if (IsDateKind(left) && IsDateKind(right))
        {
            return NormalizeDate(left) - NormalizeDate(right);
        }

        return ApplyNumeric(left, right, static (a, b) => a - b, static (a, b) => a - b, nameof(Subtract));
    }

    /// <summary>数值乘法。</summary>
    internal static object Multiply(object? left, object? right)
        => ApplyNumeric(left, right, static (a, b) => a * b, static (a, b) => a * b, nameof(Multiply));

    /// <summary>数值除法（除零抛求值异常；整型参与时提升为 decimal，避免 5/2=2 的反直觉截断）。</summary>
    internal static object Divide(object? left, object? right)
    {
        if (IsZero(right))
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "除法运算除数不能为零。",
                expressionSnippet: "/");
        }

        return ApplyNumeric(left, right, static (a, b) => a / b, static (a, b) => a / b, nameof(Divide));
    }

    /// <summary>数值取模（除零抛求值异常）。</summary>
    internal static object Modulo(object? left, object? right)
    {
        if (IsZero(right))
        {
            throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Evaluate,
                "取模运算除数不能为零。",
                expressionSnippet: "%");
        }

        return ApplyNumeric(left, right, static (a, b) => a % b, static (a, b) => a % b, nameof(Modulo));
    }

    private static bool IsZero(object? value) => value switch
    {
        decimal d => d == 0m,
        double d => d == 0d,
        float f => f == 0f,
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(value, CultureInfo.InvariantCulture) == 0L,
        _ => false
    };

    private static object ApplyNumeric(
        object? left,
        object? right,
        Func<decimal, decimal, decimal> decimalOp,
        Func<double, double, double> doubleOp,
        string operatorName)
    {
        if (left is null || right is null || !IsNumeric(left) || !IsNumeric(right))
        {
            throw IncompatibleArithmeticException(operatorName, left, right);
        }

        try
        {
            return decimalOp(
                Convert.ToDecimal(left, CultureInfo.InvariantCulture),
                Convert.ToDecimal(right, CultureInfo.InvariantCulture));
        }
        catch (OverflowException ex)
        {
            // decimal 溢出（超大 double 或超范围整型）时回退 double 运算，保证极端数值场景可用
            var doubleResult = doubleOp(
                Convert.ToDouble(left, CultureInfo.InvariantCulture),
                Convert.ToDouble(right, CultureInfo.InvariantCulture));
            if (double.IsInfinity(doubleResult))
            {
                throw new ConditionExpressionException(
                    ConditionExpressionErrorKind.Evaluate,
                    $"数值运算溢出：{Format(left)} 与 {Format(right)} 的 {operatorName} 运算结果超出可表示范围。",
                    innerException: ex);
            }

            return doubleResult;
        }
    }

    private static bool CompareSameKind(object left, object right, ConditionValueKind kind, out int result)
    {
        result = 0;
        switch (kind)
        {
            case ConditionValueKind.Numeric:
                if (left is float or double || right is float or double)
                {
                    var leftDouble = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    var rightDouble = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    result = leftDouble.CompareTo(rightDouble);
                }
                else
                {
                    try
                    {
                        var leftDecimal = Convert.ToDecimal(left, CultureInfo.InvariantCulture);
                        var rightDecimal = Convert.ToDecimal(right, CultureInfo.InvariantCulture);
                        result = leftDecimal.CompareTo(rightDecimal);
                    }
                    catch (OverflowException)
                    {
                        result = Convert.ToDouble(left, CultureInfo.InvariantCulture)
                            .CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
                    }
                }

                return true;

            case ConditionValueKind.DateTime:
                result = NormalizeDate(left).CompareTo(NormalizeDate(right));
                return true;

            case ConditionValueKind.Text:
                var leftString = left is char c ? c.ToString() : (string)left;
                var rightString = right is char c2 ? c2.ToString() : (string)right;
                result = string.CompareOrdinal(leftString, rightString);
                return true;

            case ConditionValueKind.Other:
            case ConditionValueKind.UniqueIdentifier:
                if (left.GetType() == right.GetType() && left is IComparable comparable)
                {
                    result = comparable.CompareTo(right);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool CompareEnum(object left, object right, out int result)
    {
        result = 0;
        if (left is Enum leftEnum)
        {
            if (right is Enum)
            {
                // 不同枚举类型之间不可比较
                return false;
            }

            if (right is string rightText)
            {
                result = string.CompareOrdinal(leftEnum.ToString(), rightText);
                return true;
            }

            if (IsNumeric(right))
            {
                result = Convert.ToInt64(leftEnum, CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToInt64(right, CultureInfo.InvariantCulture));
                return true;
            }

            return false;
        }

        return CompareEnum(right, left, out result);
    }

    /// <summary>日期族归一：统一转为 <see cref="DateTime"/> 后比较（DateTimeOffset 取 UtcDateTime，DateOnly 取当日零点）。</summary>
    private static DateTime NormalizeDate(object value) => value switch
    {
        DateTime dt => dt,
        DateTimeOffset dto => dto.UtcDateTime,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        TimeOnly t => DateTime.Today.Add(t.ToTimeSpan()),
        _ => throw new ConditionExpressionException(
            ConditionExpressionErrorKind.Evaluate,
            $"日期比较收到非日期值：{Describe(value)}。")
    };

    private static bool IsDateKind(object value) => value is DateTime or DateTimeOffset or DateOnly or TimeOnly;

    /// <summary>值类别判定（决定跨类别比较的转换目标）。</summary>
    internal static ConditionValueKind GetKind(object? value) => value switch
    {
        null => ConditionValueKind.Other,
        bool => ConditionValueKind.Boolean,
        _ when s_numericTypes.Contains(value.GetType()) => ConditionValueKind.Numeric,
        string or char => ConditionValueKind.Text,
        DateTime or DateTimeOffset or DateOnly or TimeOnly => ConditionValueKind.DateTime,
        Guid => ConditionValueKind.UniqueIdentifier,
        _ => ConditionValueKind.Other
    };

    private static ConditionExpressionException IncompatibleArithmeticException(string operatorName, object? left, object? right)
        => new(
            ConditionExpressionErrorKind.Evaluate,
            $"算术运算 {operatorName} 要求两侧均为数值（或字符串拼接），实际为 {Describe(left)} 与 {Describe(right)}。",
            expressionSnippet: operatorName);

    /// <summary>构造一元运算符类型不兼容异常（内置一元运算符共用）。</summary>
    internal static ConditionExpressionException IncompatibleUnaryException(string operatorSymbol)
        => new(
            ConditionExpressionErrorKind.Evaluate,
            $"一元运算符 {operatorSymbol} 要求操作数为数值。",
            expressionSnippet: operatorSymbol);

    private static string Describe(object? value) => value switch
    {
        null => "null",
        _ => $"{value.GetType().Name}（{Format(value)}）"
    };
}
