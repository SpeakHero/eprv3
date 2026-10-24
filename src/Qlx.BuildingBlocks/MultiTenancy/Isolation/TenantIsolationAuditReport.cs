/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 01:20:00 +08:00 中国标准时间 星期四
 * 描述：租户隔离巡检报告与严重级别定义
 */

namespace Qlx.BuildingBlocks.MultiTenancy.Isolation;

using System.Globalization;

/// <summary>巡检发现严重级别。</summary>
public enum TenantIsolationSeverity
{
    /// <summary>信息提示（如 RLS 未启用，仅执行模型检查）。</summary>
    Info = 0,

    /// <summary>警告（如存在孤立策略、数据库连接失败等）。</summary>
    Warning = 1,

    /// <summary>严重（如缺少 TenantId、唯一索引缺租户列、策略漏装、跨租户数据可见）。</summary>
    Critical = 2,
}

/// <summary>单条巡检发现。</summary>
/// <param name="Category">分类：Model / Policy / Sample / Connection。</param>
/// <param name="Target">目标：表名、实体名或策略名。</param>
/// <param name="Message">可读描述。</param>
/// <param name="Severity">严重级别。</param>
public sealed record TenantIsolationFinding(
    string Category,
    string Target,
    string Message,
    TenantIsolationSeverity Severity);

/// <summary>
/// 租户隔离巡检报告。
/// </summary>
/// <param name="GeneratedAtUtc">生成时间（UTC）。</param>
/// <param name="TenantId">巡检租户 ID。</param>
/// <param name="DatabaseName">巡检目标数据库名。</param>
/// <param name="RlsProviderName">当前 RLS 提供程序。</param>
/// <param name="Findings">全部巡检发现（按严重程度倒序排列）。</param>
public sealed record TenantIsolationAuditReport(
    DateTimeOffset GeneratedAtUtc,
    long TenantId,
    string DatabaseName,
    string RlsProviderName,
    IReadOnlyList<TenantIsolationFinding> Findings)
{
    /// <summary>是否健康（无 Critical / Warning）。</summary>
    public bool IsHealthy => Findings.All(static f => f.Severity == TenantIsolationSeverity.Info);

    /// <summary>是否存在严重问题。</summary>
    public bool HasCritical => Findings.Any(static f => f.Severity == TenantIsolationSeverity.Critical);

    /// <summary>转换为可读文本报告。</summary>
    public string ToReadableText()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"租户隔离巡检报告（租户 {TenantId}）");
        builder.AppendLine(CultureInfo.InvariantCulture, $"数据库：{DatabaseName}  RLS 提供程序：{RlsProviderName}  UTC 时间：{GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();

        if (Findings.Count == 0)
        {
            builder.AppendLine("未发现任何问题。");
        }
        else
        {
            foreach (var finding in Findings)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"[{finding.Severity}] {finding.Category} | {finding.Target}: {finding.Message}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"结论：{(IsHealthy ? "健康" : HasCritical ? "存在严重风险" : "存在警告")}");
        return builder.ToString();
    }
}
