/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21 03:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 描述：生产版租户数据库路由解析器：从门户库查询 + 共享/独立双分支组装 + 缓存
 */

using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 生产版租户数据库路由解析器。
/// Host 启动期由 TenantConnectionPreloadService 触发 PreloadAsync，
/// 从门户库读取租户路由行：共享租户连接统一来自 <see cref="SharedDatabaseOptions"/>（不读路由行凭据），
/// 独立租户经 Data Protection 解密凭据后组装为 <see cref="TenantConnectionInfo"/> 缓存到内存。
/// 运行时 Resolve 零 DB 查询。
/// </summary>
public sealed partial class PortalTenantDatabaseResolver : ITenantDatabaseResolver
{
    private readonly IDbContextFactory<PortalDbContext> _portalFactory;
    private readonly SharedDatabaseOptions _sharedDatabase;
    private readonly IDataProtector _protector;
    private readonly ILogger<PortalTenantDatabaseResolver> _logger;

    // ConcurrentDictionary 可被 Interlocked.Exchange 原子替换（非 readonly）
    private ConcurrentDictionary<long, TenantConnectionInfo> _cache;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="portalFactory">门户库上下文工厂。</param>
    /// <param name="dataProtection">数据保护提供程序（解密独立库凭据）。</param>
    /// <param name="sharedDatabase">租户共享库连接配置（共享租户统一使用）。</param>
    /// <param name="logger">日志。</param>
    public PortalTenantDatabaseResolver(
        IDbContextFactory<PortalDbContext> portalFactory,
        IDataProtectionProvider dataProtection,
        IOptions<SharedDatabaseOptions> sharedDatabase,
        ILogger<PortalTenantDatabaseResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(portalFactory);
        ArgumentNullException.ThrowIfNull(dataProtection);
        ArgumentNullException.ThrowIfNull(sharedDatabase);

        _portalFactory = portalFactory;
        _protector = dataProtection.CreateProtector(QlxDataProtectionPurposes.TenantDatabaseCredentials);
        _sharedDatabase = sharedDatabase.Value;
        _logger = logger ?? NullLogger<PortalTenantDatabaseResolver>.Instance;
        _cache = new ConcurrentDictionary<long, TenantConnectionInfo>();
    }

    /// <inheritdoc />
    public TenantConnectionInfo Resolve(long tenantId)
    {
        if (_cache.TryGetValue(tenantId, out var info))
        {
            return info;
        }

        throw new InvalidOperationException(
            $"租户 {tenantId} 连接信息未缓存，可能原因：租户未激活、数据库未初始化、或缓存已失效。" +
            $"请确认门户库 por_tenants + por_tenant_databases 中存在该租户的有效记录（共享/独立均可）。");
    }

    /// <inheritdoc />
    public async Task PreloadAsync(CancellationToken cancellationToken)
    {
        List<Tenant>? tenants = null;
        try
        {
            await using var db = await _portalFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            // 表达式树里不能用元组字面量，先查实体再在内存组装。
            // 共享租户：路由行存在即可纳入（新建即 Active，不依赖建库）。
            // 独立租户：待预配（Provisioning）但已登记连接信息的租户必须纳入——迁移执行器靠 Resolve
            // 拿连接串来开通数据库（开通后才转 Active），排除会造成"先有库才 Active、
            // 要 Active 才进缓存"的死锁。
            tenants = await db.Tenants
                .AsNoTracking()
                .Include(t => t.Database)
                .Where(t => t.Database != null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPreloadFailed(ex);
            return;
        }

        var builder = new ConcurrentDictionary<long, TenantConnectionInfo>();

        foreach (var tenant in tenants!)
        {
            var database = tenant.Database!;
            var isShared = database.DatabaseMode == TenantDatabaseMode.Shared;

            // 独立库仍需 Active/Provisioning 才纳入缓存（沿用原行为）；共享库路由行存在即可
            if (!isShared && tenant.Status is not (TenantStatus.Active or TenantStatus.Provisioning))
            {
                continue;
            }

            try
            {
                var connInfo = isShared
                    ? _sharedDatabase.ToConnectionInfo(tenant.Id)
                    : DecryptAndBuild(tenant.Id, database);
                builder[tenant.Id] = connInfo;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantDecryptFailed(tenant.TenantCode, tenant.Id, ex);
            }
        }

        // 原子替换缓存（避免 Resolve 读取中间态）
        Interlocked.Exchange(ref _cache, builder);
        LogPreloadCompleted(builder.Count);
    }

    /// <inheritdoc />
    public void Invalidate(long tenantId) => _cache.TryRemove(tenantId, out _);

    private TenantConnectionInfo DecryptAndBuild(long tenantId, TenantDatabase dbInfo)
    {
        // 生产环境 EncryptedDbUser / EncryptedDbPassword 是 Data Protection 密文
        // 开发环境可能明文占位，Unprotect 失败则直接用原值
        string dbUser;
        string dbPassword;
        try
        {
            dbUser = _protector.Unprotect(dbInfo.EncryptedDbUser);
        }
        catch
        {
            dbUser = dbInfo.EncryptedDbUser;
        }

        try
        {
            dbPassword = _protector.Unprotect(dbInfo.EncryptedDbPassword);
        }
        catch
        {
            dbPassword = dbInfo.EncryptedDbPassword;
        }

        return new TenantConnectionInfo(
            TenantId: tenantId,
            DbHost: dbInfo.DbHost,
            DbPort: dbInfo.DbPort,
            DbName: dbInfo.DbName,
            DbUser: dbUser,
            DbPassword: dbPassword,
            ConnectTimeoutSeconds: dbInfo.ConnectTimeoutSeconds,
            MultipleActiveResultSets: dbInfo.MultipleActiveResultSets,
            MinPoolSize: dbInfo.MinPoolSize,
            MaxPoolSize: dbInfo.MaxPoolSize);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "已预热 {Count} 个租户的数据库路由（含待预配）")]
    private partial void LogPreloadCompleted(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "门户库查询租户连接信息失败（可能首次运行无租户），缓存保持空")]
    private partial void LogPreloadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户 {TenantCode}（Id={TenantId}）连接信息解密失败，跳过")]
    private partial void LogTenantDecryptFailed(string tenantCode, long tenantId, Exception ex);
}
