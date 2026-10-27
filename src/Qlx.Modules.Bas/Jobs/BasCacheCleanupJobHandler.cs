/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas 缓存清理任务（bas.cache.cleanup）——按当前租户前缀清理模块缓存，
 *       经 ICacheService.RemoveByPrefixAsync 内部 SCAN 分批删除（禁止 KEYS），执行结果落 job_executions
 */

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.Modules.Bas.Jobs;

/// <summary>
/// Bas 缓存清理任务处理器（C-6：定期清理无效缓存）。仅清理当前租户 <c>tenant:{tenantId}:bas:</c> 前缀，
/// 幂等且可重入；无租户上下文时返回失败结果（不误清其它租户或门户级缓存）。
/// </summary>
/// <param name="tenantContext">当前租户上下文。</param>
/// <param name="cacheService">统一缓存服务（唯一入口，C-1）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class BasCacheCleanupJobHandler(
    ITenantContext tenantContext,
    ICacheService cacheService,
    ILogger<BasCacheCleanupJobHandler> logger) : IJobHandler
{
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ILogger<BasCacheCleanupJobHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => BasJobCodes.CacheCleanup;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        long? tenantId = _tenantContext.TenantId;
        if (tenantId is null || tenantId <= 0)
        {
            LogTenantMissing(context.ExecutionId);
            return new CompletedResult(
                new { Removed = 0L, TenantId = tenantId },
                null, null, stopwatch.ElapsedMilliseconds, IsSuccess: false,
                "无租户上下文（ITenantContext.TenantId 为空），跳过 Bas 缓存清理。");
        }

        // 键规范 C-1-2：tenant:{tenantId}:{modulePrefix}:；前缀必须含冒号边界
        var prefix = $"{QlxCacheKey.TenantKeyPrefix}:{tenantId.Value}:{Qlx.Abstractions.Constants.QlxModuleCodes.Bas}:";
        long removed = await _cacheService
            .RemoveByPrefixAsync(prefix, cancellationToken)
            .ConfigureAwait(false);

        LogCleanupCompleted(tenantId.Value, prefix, removed);
        return new CompletedResult(
            new { TenantId = tenantId.Value, Prefix = prefix, Removed = removed },
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bas 缓存清理任务跳过：无租户上下文（executionId={ExecutionId}）")]
    private partial void LogTenantMissing(Guid executionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bas 缓存清理完成：租户 {TenantId}，前缀 {Prefix}，移除 {Removed} 条")]
    private partial void LogCleanupCompleted(long tenantId, string prefix, long removed);
}
