/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:45:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 21:30:00 +08:00 中国标准时间 星期五
 * 描述：共享库只读读取器实现（ICacheService 统一缓存短缓存 + 故障降级）与 PlatformSharedOptions
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.MultiTenancy.PlatformShared;

/// <summary>
/// 共享库读取配置。对应配置节 <c>Qlx:PlatformShared</c>，启动期 ValidateOnStart 校验。
/// 连接串复用 <see cref="SharedDatabaseOptions"/>（Qlx:Tenancy:SharedDatabase），不重复配置。
/// </summary>
public sealed record PlatformSharedOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:PlatformShared";

    /// <summary>全局项收件箱合并缓存时长（秒），默认 30（规范 §信息中心：全局项只读优先读取短缓存）。</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 3600)]
    public int InboxCacheSeconds { get; init; } = 30;
}

/// <summary>
/// 全局项读取器实现。<see cref="ICacheService"/> 统一缓存短缓存（C-1-1 唯一入口，跨实例一致；
/// 全局项对全部租户同视图，无租户维度键）；共享库查询失败降级空集（全局项缺失不影响本地项读写）。
/// </summary>
/// <param name="dbContextFactory">共享库上下文工厂（每次拉取独立实例，避免长连接占用）。</param>
/// <param name="cacheService">统一缓存服务。</param>
/// <param name="options">共享库读取配置。</param>
/// <param name="obfuscator">全局 Id 混淆器（出口统一混淆，宿主 AddQlxIds 注册同盐实例）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class PlatformSharedReader(
    IDbContextFactory<PlatformSharedDbContext> dbContextFactory,
    ICacheService cacheService,
    IOptions<PlatformSharedOptions> options,
    IIdObfuscator obfuscator,
    ILogger<PlatformSharedReader> logger) : IPlatformSharedReader
{
    private static readonly string CacheKey = QlxCacheKey.BuildPortalKey("msg", "glb-messages:active");

    private readonly IDbContextFactory<PlatformSharedDbContext> _dbContextFactory = dbContextFactory;
    private readonly ICacheService _cacheService = cacheService;
    private readonly PlatformSharedOptions _options = options.Value;
    private readonly IIdObfuscator _obfuscator = obfuscator;
    private readonly ILogger<PlatformSharedReader> _logger = logger;

    /// <inheritdoc />
    public async Task<IReadOnlyList<GlobalMessageView>> GetActiveMessagesAsync(CancellationToken cancellationToken)
    {
        var cached = await _cacheService
            .GetAsync<IReadOnlyList<GlobalMessageView>>(CacheKey, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            await using var db = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var rows = await db.Messages
                .AsNoTracking()
                .Where(m => !m.Revoked
                    && (m.EffectiveFrom == null || m.EffectiveFrom <= now)
                    && (m.EffectiveTo == null || m.EffectiveTo >= now))
                .OrderByDescending(static m => m.PublishedAt)
                .Take(100)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var views = rows
                .Select(m => new GlobalMessageView(
                    _obfuscator.Encode(m.Id), m.MsgType, m.Subject, m.Content,
                    m.Priority, m.ForceAccept, m.PublishedBy, m.PublishedAt))
                .ToList();

            await _cacheService.SetAsync(
                CacheKey,
                (IReadOnlyList<GlobalMessageView>)views,
                cancellationToken,
                new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.InboxCacheSeconds) })
                .ConfigureAwait(false);
            return views;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 故障降级：共享库不可达时返回空集（租户本地项不受影响），下一缓存周期自动重试
            LogSharedReadFailed(ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "共享库全局消息读取失败，已降级返回空集（下一缓存周期重试）")]
    private partial void LogSharedReadFailed(Exception exception);
}
