/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:50:00 +08:00 中国标准时间 星期五
 * 描述：共享库读取装配扩展（AddQlxPlatformShared）：共享库解析器 / 上下文工厂 /
 *       只读读取器 / 迁移计划（glb_messages，独立历史表 __EFMigrationsHistory_shared）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.MultiTenancy.PlatformShared;

/// <summary>共享库只读装配扩展。</summary>
public static class PlatformSharedServiceCollectionExtensions
{
    /// <summary>
    /// 注册共享库读取组件：<see cref="PlatformSharedOptions"/>（Qlx:PlatformShared 节）+
    /// <see cref="PlatformSharedDbContextFactory"/>（共享库固定连接）+ <see cref="PlatformSharedDbContext"/> +
    /// <see cref="IPlatformSharedReader"/>（短缓存 + 故障降级）+ glb_messages 迁移计划。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddQlxPlatformShared(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PlatformSharedOptions>()
            .BindConfiguration(PlatformSharedOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 共享库固定连接工厂（连接来自 SharedDatabaseOptions，禁止走租户路由兜底）
        services.TryAddSingleton<IDbContextFactory<PlatformSharedDbContext>, PlatformSharedDbContextFactory>();
        services.TryAddScoped<PlatformSharedDbContext>(static sp =>
            sp.GetRequiredService<IDbContextFactory<PlatformSharedDbContext>>().CreateDbContext());

        // 只读读取器（ICacheService 统一缓存短缓存；宿主经 AddQlxCaching 注册唯一缓存入口）
        services.TryAddSingleton<IPlatformSharedReader, PlatformSharedReader>();

        // glb_messages 迁移计划（共享库独立历史表 __EFMigrationsHistory_shared）
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITenantMigrationPlan, PlatformSharedMigrationPlan>());

        return services;
    }
}

/// <summary>
/// 共享库上下文工厂。连接串固定自 <see cref="SharedDatabaseOptions"/>（QLX 四库红线：glb_* 只进共享库）；
/// 构造的上下文已显式配置连接（<c>IsConfigured=true</c>），<see cref="TenantDbContext"/> 不再走租户路由。
/// </summary>
/// <param name="sharedDatabase">共享库连接配置。</param>
public sealed class PlatformSharedDbContextFactory(IOptions<SharedDatabaseOptions> sharedDatabase)
    : IDbContextFactory<PlatformSharedDbContext>
{
    private readonly IOptions<SharedDatabaseOptions> _sharedDatabase = sharedDatabase;

    /// <inheritdoc />
    public PlatformSharedDbContext CreateDbContext()
    {
        var resolver = new SharedDatabaseResolver(_sharedDatabase);
        var builder = new DbContextOptionsBuilder<PlatformSharedDbContext>();
        _ = builder.UseSqlServer(_sharedDatabase.Value.ToConnectionInfo(0).ToConnectionString());
        return new PlatformSharedDbContext(resolver, tenantId: 0, builder.Options);
    }

    /// <inheritdoc />
    public Task<PlatformSharedDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(CreateDbContext());
    }
}

/// <summary>共享库全局消息迁移计划（glb_messages；无依赖，共享库最先建表之一）。</summary>
internal sealed class PlatformSharedMigrationPlan : ITenantMigrationPlan
{
    /// <summary>计划编码（共享库历史表后缀 <c>__EFMigrationsHistory_shared</c>）。</summary>
    public string ModuleCode => "shared";

    /// <inheritdoc />
    public Type DbContextType => typeof(PlatformSharedDbContext);

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];
}
