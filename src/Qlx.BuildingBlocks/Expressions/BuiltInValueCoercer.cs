/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：内置值强制转换器（字符串→数值/日期/布尔/Guid、char→字符串），自定义转换器优先于此链执行
 */

using System.Globalization;

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 内置值强制转换器：覆盖 ERP 条件场景最常见的跨类别比较
/// （字符串字段与数字阈值、字符串日期与日期变量、字典编码与 Guid 等）。
/// </summary>
internal sealed class BuiltInValueCoercer : IConditionValueCoercer
{
    /// <summary>共享实例（无状态线程安全）。</summary>
    internal static BuiltInValueCoercer Shared { get; } = new();

    /// <inheritdoc />
    public bool TryConvert(object? value, ConditionValueKind target, out object? converted)
    {
        converted = null;
        if (value is null)
        {
            return false;
        }

        return target switch
        {
            ConditionValueKind.Numeric => TryConvertNumeric(value, out converted),
            ConditionValueKind.DateTime => TryConvertDateTime(value, out converted),
            ConditionValueKind.UniqueIdentifier => Guid.TryParse(value as string, out var guid) && Set(guid, out converted),
            ConditionValueKind.Boolean => bool.TryParse(value as string, out var flag) && Set(flag, out converted),
            ConditionValueKind.Text => value is char ch && Set(ch.ToString(), out converted),
            _ => false
        };
    }

    private static bool Set<T>(T value, out object? converted)
    {
        converted = value;
        return true;
    }

    private static bool TryConvertNumeric(object value, out object? converted)
    {
        if (value is not string text)
        {
            converted = null;
            return false;
        }

        // 优先 decimal（金额场景精确），带小数/指数或超范围时回退 double
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            converted = d;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
        {
            converted = dbl;
            return true;
        }

        converted = null;
        return false;
    }

    private static bool TryConvertDateTime(object value, out object? converted)
    {
        if (value is not string text)
        {
            converted = null;
            return false;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            converted = dt;
            return true;
        }

        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var dateOnly))
        {
            converted = dateOnly;
            return true;
        }

        converted = null;
        return false;
    }
}
