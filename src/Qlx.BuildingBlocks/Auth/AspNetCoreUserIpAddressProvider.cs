/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 修改时间：2026-10-26
 * 描述：基于 IHttpContextAccessor 的客户端真实 IP 提供者实现，统一处理代理改写后的连接地址与 IPv4/IPv6 归一化
 */

using Microsoft.AspNetCore.Http;

using Qlx.Abstractions.Domain;

using System.Net;

namespace Qlx.BuildingBlocks.Auth;

/// <summary>
/// ASP.NET Core 环境下的客户端 IP 提供者实现。
/// <para>
/// 地址来源固定为 <see cref="HttpContext.Connection"/> 的远端地址：反向代理场景由管线首段的
/// <c>ForwardedHeaders</c> 中间件（仅信任 KnownProxies / KnownNetworks）依据 <c>X-Forwarded-For</c>
/// 改写该地址，因此本类不直接解析请求头，避免绕过代理信任链导致 IP 可伪造。
/// </para>
/// </summary>
public sealed class AspNetCoreUserIpAddressProvider(IHttpContextAccessor httpContextAccessor) : IUserIpAddressProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <inheritdoc />
    public string? GetCurrentUserIp()
    {
        IPAddress? ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (ipAddress is null)
        {
            return null;
        }

        // 仅归一化 IPv4-mapped IPv6（::ffff:a.b.c.d）；真实 IPv6 保持原样，
        // 否则 MapToIPv4 会把其末 4 字节误当作 IPv4 地址输出，造成地址失真。
        if (ipAddress.IsIPv4MappedToIPv6)
        {
            ipAddress = ipAddress.MapToIPv4();
        }

        return ipAddress.ToString();
    }
}
