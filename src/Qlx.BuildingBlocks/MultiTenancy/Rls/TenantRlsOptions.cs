/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 00:10:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 00:10:00 +08:00 中国标准时间 星期四
 * 描述：租户 RLS 强类型配置（默认关闭）与提供程序工厂
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.MultiTenancy.Rls;

/// <summary>
/// 租户 RLS 配置（<c>Qlx:Tenancy:Rls</c>）。默认关闭；
/// 启用时按 <see cref="Provider"/> 选择实现，数据库不支持 RLS 时降级 L1+L3。
/// </summary>
public sealed record TenantRlsOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Tenancy:Rls";

    /// <summary>是否启用 RLS（数据库层安装策略 + 会话上下文）。默认 <see langword="false"/>。</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// 数据库提供程序：<c>SqlServer</c> / <c>MySql</c> / <c>PostgreSql</c> / <c>Oracle</c>。
    /// 仅 <see cref="Enabled"/> 为 <see langword="true"/> 时生效。
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Provider { get; init; } = "SqlServer";
}

/// <summary>RLS 提供程序工厂。</summary>
public static class TenantRlsProviderFactory
{
    /// <summary>按配置创建提供程序；未启用或未知名称返回 <see cref="NullTenantRlsProvider"/>。</summary>
    public static ITenantRlsProvider Create(TenantRlsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return new NullTenantRlsProvider();
        }

        return options.Provider.Trim().ToLowerInvariant() switch
        {
            "sqlserver" => new SqlServerTenantRlsProvider(),
            "mysql" => new MySqlTenantRlsProvider(),
            "postgresql" or "postgres" => new PostgreSqlTenantRlsProvider(),
            "oracle" => new OracleTenantRlsProvider(),
            _ => new NullTenantRlsProvider(),
        };
    }
}
