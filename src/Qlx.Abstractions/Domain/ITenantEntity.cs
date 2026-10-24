/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：租户实体契约，租户级全局过滤自动注入 TenantId 字段
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 租户实体契约。所有需要多租户隔离的实体必须实现此接口（或继承带 TenantId 的基类），
/// 由租户级 DbContext 自动注入 TenantId 全局查询过滤。
/// </summary>
public interface ITenantEntity
{
    /// <summary>租户 ID（与当前请求租户匹配才能被查询到）。</summary>
    long TenantId { get; }
}

/// <summary>
/// 带租户隔离的审计实体基类。组合 <see cref="AuditableEntity"/> + <see cref="ITenantEntity"/>。
/// 大多数业务实体应直接继承此类。
/// </summary>
public abstract class TenantAuditableEntity : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public long TenantId { get; protected set; }
}

/// <summary>
/// 带租户隔离的全审计实体基类（含软删）。
/// </summary>
public abstract class TenantFullAuditableEntity : FullAuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public long TenantId { get; protected set; }
}
