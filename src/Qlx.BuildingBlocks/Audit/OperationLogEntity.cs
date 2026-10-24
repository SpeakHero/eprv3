/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：业务级操作日志表实体（plog_operation）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 业务级操作日志表实体。
/// </summary>
[Table("plog_operation")]
[Index(nameof(TenantId), nameof(ModuleCode), nameof(OperatedAt))]
[Index(nameof(OperatedBy), nameof(OperatedAt))]
public sealed class OperationLogEntity
{
    /// <summary>操作事件雪花 ID。</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("event_id")]
    public long EventId { get; set; }

    /// <summary>业务模块编码。</summary>
    [Column("module_code")]
    [MaxLength(64)]
    public required string ModuleCode { get; set; }

    /// <summary>操作类型（APPROVE / REJECT / CREATE 等）。</summary>
    [Column("operation")]
    [MaxLength(64)]
    public required string Operation { get; set; }

    /// <summary>操作描述（面向用户可读）。</summary>
    [Column("description")]
    [MaxLength(1024)]
    public required string Description { get; set; }

    /// <summary>操作人。</summary>
    [Column("operated_by")]
    [MaxLength(128)]
    public required string OperatedBy { get; set; }

    /// <summary>操作时间（UTC）。</summary>
    [Column("operated_at")]
    public DateTimeOffset OperatedAt { get; set; }

    /// <summary>所属租户 ID。</summary>
    [Column("tenant_id")]
    public long TenantId { get; set; }

    /// <summary>目标实体类型（可选）。</summary>
    [Column("target_type")]
    [MaxLength(256)]
    public string? TargetType { get; set; }

    /// <summary>目标实体主键（可选）。</summary>
    [Column("target_id")]
    public long? TargetId { get; set; }

    /// <summary>结构化扩展信息（JSON，如登录 IP、客户端信息，可选）。</summary>
    [Column("metadata")]
    [MaxLength(2048)]
    public string? Metadata { get; set; }
}
