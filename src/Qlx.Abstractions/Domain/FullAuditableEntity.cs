/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：软删实体基类，自动注入 IsDeleted/DeletedAt，软删操作拦截为 Update
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 全审计实体基类。在 <see cref="AuditableEntity"/> 基础上增加软删标记。
/// 软删实体的删除操作不会执行 DELETE SQL，由拦截器改写为 UPDATE IsDeleted=true。
/// 查询时自动注入 <c>IsDeleted = false</c> 全局过滤。
/// </summary>
public abstract class FullAuditableEntity : AuditableEntity
{
    /// <summary>软删标记，默认 false。</summary>
    public bool IsDeleted { get; protected set; }

    /// <summary>软删时间（UTC），未删除时为 null。</summary>
    public DateTimeOffset? DeletedAt { get; protected set; }

    /// <summary>软删人标识。</summary>
    public string? DeletedBy { get; protected set; }
}
