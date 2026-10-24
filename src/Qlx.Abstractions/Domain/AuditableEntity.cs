/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：审计实体基类，自动注入 CreatedAt/UpdatedAt/CreatedBy/UpdatedBy 阴影属性
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 审计实体基类。在 <see cref="Entity"/> 基础上增加创建/更新时间与人员。
/// 时间由 EF Core 拦截器自动填充，CreatedBy/UpdatedBy 从当前用户上下文读取。
/// </summary>
public abstract class AuditableEntity : Entity
{
    /// <summary>创建时间（UTC），由拦截器自动填充。</summary>
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <summary>最近更新时间（UTC），由拦截器自动填充。</summary>
    public DateTimeOffset UpdatedAt { get; protected set; }

    /// <summary>创建人标识（用户 ID 或账号），由拦截器从当前用户上下文填充。</summary>
    public string? CreatedBy { get; protected set; }

    /// <summary>最近更新人标识，由拦截器从当前用户上下文填充。</summary>
    public string? UpdatedBy { get; protected set; }
}
