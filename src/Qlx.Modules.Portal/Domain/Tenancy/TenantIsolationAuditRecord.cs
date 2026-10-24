/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户隔离巡检记录实体，周期巡检报告落库留存
 */

using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 租户隔离巡检记录（表前缀 por_）。周期巡检服务每轮对活跃租户产出一行：
/// 健康结论 + 严重标记 + 发现明细 JSON + 可读文本报告。
/// 继承 <see cref="Qlx.Abstractions.Domain.AuditableEntity"/>：雪花 Id + 审计字段自动填充。
/// </summary>
[Table("por_isolation_audit_records")]
[Index(nameof(TenantId))]
[Index(nameof(CreatedAt))]
public sealed class TenantIsolationAuditRecord : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>巡检租户 ID。</summary>
    public long TenantId { get; private set; }

    /// <summary>是否健康（无 Critical / Warning 发现）。</summary>
    public bool IsHealthy { get; private set; }

    /// <summary>是否存在严重发现。</summary>
    public bool HasCritical { get; private set; }

    /// <summary>发现条数。</summary>
    public int FindingCount { get; private set; }

    /// <summary>发现明细 JSON。</summary>
    public string FindingsJson { get; private set; } = string.Empty;

    /// <summary>可读文本报告。</summary>
    public string ReportText { get; private set; } = string.Empty;

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public TenantIsolationAuditRecord() { }

    /// <summary>
    /// 保存一次巡检报告。
    /// </summary>
    /// <param name="tenantId">巡检租户 ID。</param>
    /// <param name="isHealthy">是否健康。</param>
    /// <param name="hasCritical">是否存在严重发现。</param>
    /// <param name="findingCount">发现条数。</param>
    /// <param name="findingsJson">发现明细 JSON。</param>
    /// <param name="reportText">可读文本报告。</param>
    public TenantIsolationAuditRecord(
        long tenantId, bool isHealthy, bool hasCritical, int findingCount, string findingsJson, string reportText)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(findingsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportText);

        TenantId = tenantId;
        IsHealthy = isHealthy;
        HasCritical = hasCritical;
        FindingCount = findingCount;
        FindingsJson = findingsJson;
        ReportText = reportText;
    }
}
