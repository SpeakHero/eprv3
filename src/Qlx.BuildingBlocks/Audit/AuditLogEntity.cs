/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：实体变更审计表实体（plog_audit）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 实体变更审计表实体。
/// </summary>
[Table("plog_audit")]
[Index(nameof(TenantId), nameof(EntityType), nameof(EntityId))]
[Index(nameof(TenantId), nameof(ChangedAt))]
public sealed class AuditLogEntity
{
    /// <summary>审计事件雪花 ID。</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("event_id")]
    public long EventId { get; set; }

    /// <summary>实体类型名（含命名空间）。</summary>
    [Column("entity_type")]
    [MaxLength(256)]
    public required string EntityType { get; set; }

    /// <summary>实体主键。</summary>
    [Column("entity_id")]
    public long EntityId { get; set; }

    /// <summary>操作类型：INSERT / UPDATE / DELETE / SOFT_DELETE。</summary>
    [Column("operation")]
    [MaxLength(32)]
    public required string Operation { get; set; }

    /// <summary>变更前值 JSON（UPDATE / DELETE 时）。</summary>
    [Column("old_values", TypeName = "nvarchar(max)")]
    public string? OldValues { get; set; }

    /// <summary>变更后值 JSON（INSERT / UPDATE 时）。</summary>
    [Column("new_values", TypeName = "nvarchar(max)")]
    public string? NewValues { get; set; }

    /// <summary>操作人（用户名或账号）。</summary>
    [Column("changed_by")]
    [MaxLength(128)]
    public required string ChangedBy { get; set; }

    /// <summary>变更时间（UTC）。</summary>
    [Column("changed_at")]
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>所属租户 ID。</summary>
    [Column("tenant_id")]
    public long TenantId { get; set; }
}
