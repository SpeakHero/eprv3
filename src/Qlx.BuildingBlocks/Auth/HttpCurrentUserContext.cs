/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 08:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26 09:00:00 +08:00 中国标准时间 星期一
 * 描述：基于 IHttpContextAccessor 的当前用户上下文实现，供审计拦截器读取操作人；客户端 IP 委托 IUserIpAddressProvider
 */

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;

using System.Security.Claims;

namespace Qlx.BuildingBlocks.Auth;

/// <summary>
/// 基于 <see cref="IHttpContextAccessor"/> 的当前用户上下文（单例注册，按请求实时读取 claims）。
/// <para>
/// 设计为单例以便审计拦截器（单例）安全消费：属性访问时经 <see cref="IHttpContextAccessor"/>
/// 定位当前请求，后台任务 / 启动期等非请求上下文自动降级为匿名（各属性返回 null / false）。
/// claim 名与 OpenIddict 签发保持原始名（未做入站映射）：sub / name / login_account。
/// </para>
/// <para>
/// 客户端 IP 不在本类内解析，统一委托 <see cref="IUserIpAddressProvider"/>，
/// 保证代理转发头处理与 IPv4/IPv6 归一化只有一份实现（DRY）。
/// </para>
/// </summary>
public sealed class HttpCurrentUserContext(
    IHttpContextAccessor accessor,
    IUserIpAddressProvider userIpAddressProvider) : ICurrentUserContext
{
    /// <summary>登录账号 claim 名（与全局 Claim 常量约定一致）。</summary>
    public const string LoginAccountClaim = QlxClaimConstants.LoginAccount;

    /// <inheritdoc />
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
        ? userId
        : accessor.HttpContext?.User.FindFirstValue(QlxClaimConstants.Subject);

    /// <inheritdoc />
    public string? UserName => accessor.HttpContext?.User.FindFirstValue(LoginAccountClaim);

    /// <inheritdoc />
    public string? UserDisplayName => accessor.HttpContext?.User.FindFirstValue(QlxClaimConstants.Name);

    /// <inheritdoc />
    public string? CurrentUserIp => userIpAddressProvider.GetCurrentUserIp();
}

/// <summary>
/// 当前用户上下文注册扩展。
/// </summary>
public static class CurrentUserContextServiceCollectionExtensions
{
    /// <summary>
    /// 注册 HTTP 请求感知的 <see cref="ICurrentUserContext"/>（单例 + IHttpContextAccessor 实时读取）
    /// 与客户端 IP 提供者 <see cref="IUserIpAddressProvider"/>。
    /// 须在 AddQlxAudit 之后调用，后注册者覆盖 NullCurrentUserContext 默认实现。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxHttpCurrentUserContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddHttpContextAccessor();
        _ = services.AddSingleton<IUserIpAddressProvider, AspNetCoreUserIpAddressProvider>();
        _ = services.AddSingleton<ICurrentUserContext, HttpCurrentUserContext>();

        return services;
    }
}
