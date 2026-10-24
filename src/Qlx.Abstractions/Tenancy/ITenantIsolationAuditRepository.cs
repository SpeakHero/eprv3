/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户隔离巡检结果落库契约，BuildingBlocks 周期巡检服务消费、门户模块实现
 */

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 租户隔离巡检结果仓储契约。抽象门户库中巡检记录的写入与活跃租户枚举，
/// 让 BuildingBlocks 层的周期巡检服务无需直接依赖 Portal 模块。
/// </summary>
public interface ITenantIsolationAuditRepository
{
    /// <summary>
    /// 获取全部活跃（Active）租户 ID 列表。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<long>> GetActiveTenantIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 保存一次巡检报告。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="isHealthy">是否健康。</param>
    /// <param name="hasCritical">是否存在严重发现。</param>
    /// <param name="findingCount">发现条数。</param>
    /// <param name="findingsJson">发现明细 JSON（QlxJsonDefaults 序列化）。</param>
    /// <param name="reportText">可读文本报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveReportAsync(
        long tenantId, bool isHealthy, bool hasCritical, int findingCount,
        string findingsJson, string reportText, CancellationToken cancellationToken);
}
