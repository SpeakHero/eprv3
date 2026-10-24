/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 描述：ITenantContext 请求作用域实现，由 TenantContextMiddleware 在管线入口填充
 */

using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// <see cref="ITenantContext"/> 请求作用域实现。每个 HTTP 请求一个实例，
/// 由 <see cref="TenantContextMiddleware"/> 在管线入口调用 setter 填充。
/// </summary>
public sealed class RequestTenantContext : ITenantContext
{
    /// <inheritdoc />
    public string? TenantCode { get; private set; }

    /// <inheritdoc />
    public long? TenantId { get; private set; }

    /// <inheritdoc />
    public bool IsResolved => TenantId.HasValue && TenantId.Value > 0;

    /// <inheritdoc />
    public void SetTenantId(long tenantId)
    {
        TenantId = tenantId > 0 ? tenantId : null;
    }

    /// <inheritdoc />
    public void SetTenantCode(string? tenantCode)
    {
        TenantCode = string.IsNullOrWhiteSpace(tenantCode) ? null : tenantCode;
    }
}
