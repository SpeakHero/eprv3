/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：实体基类，定义雪花 ID（long）为唯一标识，模块实体统一继承以保证主键类型一致
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 实体基类。所有业务实体必须继承，主键为雪花 ID（long），
/// 由雪花 ID 生成器在实体创建时注入。
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    /// <summary>
    /// 实体唯一标识（雪花 ID，启动期由 <c>SaveChangesInterceptor</c> 自动赋值，
    /// 同 assembly 内部可通过 <see cref="AssignId"/> 显式分配）。
    /// </summary>
    public long Id { get; protected set; }

    /// <summary>
    /// 手动分配实体 Id（供同 assembly 的种子服务 / 测试使用）。
    /// Id 必须 > 0，且只能在当前 Id=0 时调用。
    /// </summary>
    internal void AssignId(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        if (Id != 0)
        {
            throw new InvalidOperationException($"实体 {GetType().Name} 已有 Id={Id}，禁止重新分配为 {id}");
        }
        Id = id;
    }

    /// <inheritdoc />
    public bool Equals(Entity? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id > 0 && Id == other.Id && GetType() == other.GetType();
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Entity);

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>== 操作符。</summary>
    public static bool operator ==(Entity? left, Entity? right) => left?.Equals(right) ?? right is null;

    /// <summary>!= 操作符。</summary>
    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
