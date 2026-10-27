/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 修改时间：2026-10-26
 * 描述：客户端真实 IP 提供者接口，统一封装当前请求的地址解析（代理转发 + IPv4/IPv6 归一化）
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 当前请求的客户端 IP 地址提供者。
/// <para>
/// 地址解析（反向代理转发头信任链、IPv4 / IPv6 归一化）由实现侧统一负责，
/// 消费方（<see cref="ICurrentUserContext"/>、审计链路）只读取最终字符串，禁止各自重复解析。
/// </para>
/// </summary>
public interface IUserIpAddressProvider
{
    /// <summary>获取当前请求的客户端真实 IP；无请求上下文或地址不可用时返回 null。</summary>
    string? GetCurrentUserIp();
}
