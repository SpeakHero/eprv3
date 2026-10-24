/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 22:15:00 +08:00 中国标准时间 星期三
 * 描述：租户连接信息值对象，经 TenantDatabaseResolver 组装的已解密连接参数（含共享库标记）
 */

using Qlx.Abstractions.Constants;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户数据库连接信息值对象（不可变）。门户侧从 <c>por_tenant_databases</c> 解密后组装，
/// 由 <see cref="ITenantDatabaseResolver"/> 返回；运行时据此动态构建 DbContext 连接串。
/// </summary>
public sealed record TenantConnectionInfo(
    long TenantId,
    string DbHost,
    int DbPort,
    string DbName,
    string DbUser,
    string DbPassword,
    int ConnectTimeoutSeconds = 30,
    bool MultipleActiveResultSets = true,
    int MinPoolSize = 0,
    int MaxPoolSize = 50,
    bool IsShared = false)
{
    /// <summary>
    /// 组装 SQL Server 连接串构造器（<see cref="ToConnectionString"/> 与 master 库连接共用）。
    /// </summary>
    /// <param name="initialCatalog">覆盖目标库（如迁移期连 master）；null 则用 <see cref="DbName"/>。</param>
    public Microsoft.Data.SqlClient.SqlConnectionStringBuilder ToBuilder(string? initialCatalog = null)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();

        // LocalDB 语法为 "(localdb)\实例名"，不支持 TCP 端口；普通主机才用 host,port
        var host = DbHost.Trim();
        if (host.StartsWith(QlxDatabaseConstants.LocalDbHostMarker, StringComparison.OrdinalIgnoreCase))
        {
            builder.DataSource = host.Contains('\\')
                ? host
                : $"{host}\\{QlxDatabaseConstants.LocalDbDefaultInstance}";
        }
        else
        {
            builder.DataSource = DbPort > 0 ? $"{host},{DbPort}" : host;
        }

        builder.InitialCatalog = initialCatalog ?? DbName;

        // 凭据为空（如开发环境 LocalDB 受信任连接）时走 Windows 集成安全
        if (string.IsNullOrWhiteSpace(DbUser))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = DbUser;
            builder.Password = DbPassword;
        }

        builder.ConnectTimeout = ConnectTimeoutSeconds;
        builder.MultipleActiveResultSets = MultipleActiveResultSets;
        builder.MinPoolSize = MinPoolSize;
        builder.MaxPoolSize = MaxPoolSize;
        builder.Encrypt = false;    // 开发默认关；生产按需启
        builder.TrustServerCertificate = false;

        return builder;
    }

    /// <summary>
    /// 组装完整 SQL Server 连接串。
    /// </summary>
    public string ToConnectionString() => ToBuilder().ConnectionString;
}
