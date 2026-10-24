/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：当前租户上下文抽象，由宿主中间件解析后注入，业务代码禁止自行解析请求头
 */

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 当前租户上下文。由宿主租户解析中间件在请求进入时填充。
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// 当前租户编码；门户级请求（无租户）为 <see langword="null"/>。
    /// </summary>
    string? TenantCode { get; }

    /// <summary>
    /// 当前租户内部标识；门户级请求为 <see langword="null"/>。
    /// </summary>
    long? TenantId { get; }

    /// <summary>
    /// 当前请求是否已解析到租户。
    /// </summary>
    bool IsResolved { get; }

    /// <summary>
    /// 请求管线内部设置当前租户（由多租户中间件调用；业务代码禁止）。
    /// </summary>
    void SetTenantId(long tenantId);

    /// <summary>
    /// 请求管线内部设置当前租户编码（由多租户中间件调用；业务代码禁止）。
    /// </summary>
    void SetTenantCode(string? tenantCode);
}
