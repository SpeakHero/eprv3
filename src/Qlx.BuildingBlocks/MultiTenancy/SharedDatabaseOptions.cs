/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 描述：租户共享库连接配置（配置节 Qlx:Tenancy:SharedDatabase），路由共享租户时使用
 */

using System.ComponentModel.DataAnnotations;
using Qlx.Abstractions.Constants;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户共享库连接配置。共享租户不读取 <c>por_tenant_databases</c> 中的凭据，
/// 统一使用本配置组装连接串；独立租户仍按路由行解密凭据。
/// </summary>
public sealed record SharedDatabaseOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Tenancy:SharedDatabase";

    /// <summary>共享库主机（LocalDB 可填 <c>(localdb)</c>）。</summary>
    [Required(AllowEmptyStrings = false)]
    public string DbHost { get; init; } = string.Empty;

    /// <summary>共享库端口（SQL Server 默认 1433）。</summary>
    [Range(1, 65535)]
    public int DbPort { get; init; } = QlxDatabaseConstants.SqlServerDefaultPort;

    /// <summary>共享库名称。</summary>
    [Required(AllowEmptyStrings = false)]
    public string DbName { get; init; } = string.Empty;

    /// <summary>共享库用户名；为空时使用 Windows 集成认证。</summary>
    public string DbUser { get; init; } = string.Empty;

    /// <summary>共享库密码；仅 <see cref="DbUser"/> 非空时生效。</summary>
    public string DbPassword { get; init; } = string.Empty;

    /// <summary>连接超时（秒）。</summary>
    [Range(1, 300)]
    public int ConnectTimeoutSeconds { get; init; } = 30;

    /// <summary>连接池最小大小。</summary>
    [Range(0, 500)]
    public int MinPoolSize { get; init; }

    /// <summary>连接池最大大小（共享库承载多个租户，默认高于独立库）。</summary>
    [Range(1, 1000)]
    public int MaxPoolSize { get; init; } = 200;

    /// <summary>启用多个活动结果集（MARS）。</summary>
    public bool MultipleActiveResultSets { get; init; } = true;

    /// <summary>
    /// 组装指定租户的共享库连接信息。共享库的库名/主机来自配置，凭据可空（集成认证）。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <returns>共享库连接信息。</returns>
    public TenantConnectionInfo ToConnectionInfo(long tenantId) => new(
        TenantId: tenantId,
        DbHost: DbHost,
        DbPort: DbPort,
        DbName: DbName,
        DbUser: DbUser,
        DbPassword: DbPassword,
        ConnectTimeoutSeconds: ConnectTimeoutSeconds,
        MultipleActiveResultSets: MultipleActiveResultSets,
        MinPoolSize: MinPoolSize,
        MaxPoolSize: MaxPoolSize,
        IsShared: true);
}
