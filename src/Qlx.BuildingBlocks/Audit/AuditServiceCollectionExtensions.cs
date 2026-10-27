/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26
 * 描述：审计服务集合扩展：注册 AuditDbContext + EfCoreAuditService + 拦截器
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Domain;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 审计服务注册扩展。
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// 注册审计 DbContext（固定连接审计库，跨租户集中存储）、
    /// 生产级 EfCoreAuditService、AuditSaveChangesInterceptor、
    /// ICurrentUserContext 默认实现。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根，绑定 <see cref="AuditOptions.SectionName"/> 节。</param>
    public static IServiceCollection AddQlxAudit(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // 强类型 AuditOptions + 启动校验（§7.6 C2 铁律：禁止裸读 IConfiguration 字符串键）
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.AddOptions<AuditOptions>()
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                "Audit:ConnectionString 不能为空")
            .ValidateOnStart();

        // 审计 DbContext：B4 后并入 qlx_portal 门户库，表前缀 plog_，迁移历史表独立
        services.AddDbContextFactory<AuditDbContext>((provider, options) =>
        {
            var connStr = provider.GetRequiredService<IOptions<AuditOptions>>().Value.ConnectionString;
            options.UseSqlServer(connStr, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_audit"));
        });

        // 生产级审计服务（写数据库）
        services.TryAddSingleton<IAuditService, EfCoreAuditService>();

        // 当前用户上下文默认实现（未认证场景）
        services.TryAddSingleton<ICurrentUserContext, NullCurrentUserContext>();

        // 审计 SaveChanges 拦截器（注册为 IInterceptor 以便 TenantDbContext 自动注入）
        services.TryAddSingleton<AuditSaveChangesInterceptor>();
        services.AddSingleton<IInterceptor>(sp => sp.GetRequiredService<AuditSaveChangesInterceptor>());

        return services;
    }

    private sealed class NullCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => false;
        public string? UserId => null;
        public string? UserName => null;
        public string? UserDisplayName => null;
        public string? CurrentUserIp => null;
    }
}
