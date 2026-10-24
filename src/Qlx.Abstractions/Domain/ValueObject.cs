/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：值对象基类，基于公共属性做相等性比较
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 值对象基类。值对象没有唯一标识（Identity），相等性由所有公共属性组合决定。
/// 继承类只需实现 <see cref="GetEqualityComponents"/> 枚举所有参与相等性比较的属性值。
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>枚举参与相等性比较的所有组件（属性值 / 字段值）。</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <inheritdoc />
    public bool Equals(ValueObject? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    /// <summary>== 操作符。</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right) => left?.Equals(right) ?? right is null;

    /// <summary>!= 操作符。</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
