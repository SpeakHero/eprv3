/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：智能枚举基类，兼具 int 值、显示名称与 TryParse，替代硬编码枚举
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 智能枚举基类。兼具 <see cref="int"/> 值（持久化存储）、
/// <see cref="DisplayName"/>（面向用户的中文显示）与 <see cref="Code"/>（可选英文编码），
/// 支持隐式转换、TryParse、枚举全部值。
/// 继承类应定义 <see langword="static"/> <see langword="readonly"/> 实例。
/// </summary>
/// <example>
/// <code>
/// public sealed class OrderStatus : SmartEnum&lt;OrderStatus&gt;
/// {
///     public static readonly OrderStatus Draft = new(0, "草稿", "draft");
///     public static readonly OrderStatus Approved = new(1, "已审批", "approved");
///     private OrderStatus(int value, string displayName, string code = "") : base(value, displayName, code) { }
/// }
/// </code>
/// </example>
/// <remarks>
/// 构造函数。
/// </remarks>
public abstract class SmartEnum<TSelf>(int value, string displayName, string code = "") : IEquatable<SmartEnum<TSelf>>
    where TSelf : SmartEnum<TSelf>
{
    private static readonly Lazy<List<TSelf>> s_allValues = new(() =>
        [.. typeof(TSelf).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => typeof(TSelf).IsAssignableFrom(f.FieldType))
            .Select(f => (TSelf?)f.GetValue(null))
            .Where(v => v is not null)
            .Select(v => v!)]);

    /// <summary>枚举值（int），持久化到数据库的实际值。</summary>
    public int Value { get; } = value;

    /// <summary>显示名称（中文，面向用户）。</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>英文编码（可选，面向程序/API 输出）。</summary>
    public string Code { get; } = code;

    /// <summary>获取全部静态实例。</summary>
    public static IReadOnlyList<TSelf> All => s_allValues.Value;

    /// <summary>按 int 值查找，未找到返回 null。</summary>
    public static TSelf? FromValue(int value)
    {
        return s_allValues.Value.FirstOrDefault(v => v.Value == value);
    }

    /// <summary>按 Code 查找，未找到返回 null。</summary>
    public static TSelf? FromCode(string code)
    {
        return string.IsNullOrWhiteSpace(code) ? null : s_allValues.Value.FirstOrDefault(v => string.Equals(v.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按 int 值查找，未找到抛异常。</summary>
    public static TSelf FromValueRequired(int value)
    {
        return FromValue(value) ?? throw new InvalidOperationException($"{typeof(TSelf).Name} 不存在值 {value}");
    }

    /// <summary>
    /// 尝试按 int 值解析。
    /// </summary>
    public static bool TryParse(int value, out TSelf? result)
    {
        result = FromValue(value);
        return result is not null;
    }

    /// <summary>隐式转换为 int。</summary>
    public static implicit operator int(SmartEnum<TSelf> smart)
    {
        return smart.Value;
    }

    /// <summary>== 操作符。</summary>
    public static bool operator ==(SmartEnum<TSelf>? left, SmartEnum<TSelf>? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        return left is not null && right is not null && left.Value == right.Value;
    }

    /// <summary>!= 操作符。</summary>
    public static bool operator !=(SmartEnum<TSelf>? left, SmartEnum<TSelf>? right)
    {
        return !(left == right);
    }

    /// <inheritdoc />
    public bool Equals(SmartEnum<TSelf>? other)
    {
        return other is not null && Value == other.Value;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as SmartEnum<TSelf>);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return DisplayName;
    }
}
