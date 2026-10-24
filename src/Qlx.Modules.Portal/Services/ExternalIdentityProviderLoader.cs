/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：启动期外部身份提供方加载器：一次性装载启用配置到内存注册表；无配置 / 库未就绪均不阻断启动
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 外部身份提供方启动加载器（BackgroundService）。
/// <para>
/// 设计取舍（R-9）：外部 IdP 配置存放在每个租户库之外的门户库，启动期只做「读配置 → 装载注册表」，
/// 真实 IdP 联调与认证处理器注册延后；未配置任何提供方时注册表为空集，日志提示后正常启动，不抛异常。
/// </para>
/// </summary>
/// <param name="scopeFactory">作用域工厂（来源实现消费 Scoped 门户库上下文）。</param>
/// <param name="registry">运行期注册表（Singleton）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class ExternalIdentityProviderLoader(
    IServiceScopeFactory scopeFactory,
    ExternalIdentityProviderRegistry registry,
    ILogger<ExternalIdentityProviderLoader> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ExternalIdentityProviderRegistry _registry = registry;
    private readonly ILogger<ExternalIdentityProviderLoader> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IExternalIdentityProviderSource>();
            var providers = await source.GetEnabledAsync(stoppingToken).ConfigureAwait(false);
            var count = _registry.Replace(providers);
            LogLoaded(count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 门户库未就绪 / 表尚未迁移：保持空注册表，避免阻断启动（无配置即不注册）
            LogLoadFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "外部身份提供方装载完成：启用 {Count} 个")]
    private partial void LogLoaded(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "外部身份提供方装载失败（按未配置处理，不阻断启动）")]
    private partial void LogLoadFailed(Exception exception);
}
