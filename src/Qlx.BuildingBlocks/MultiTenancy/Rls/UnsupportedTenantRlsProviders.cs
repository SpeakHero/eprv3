/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 描述：不支持/待实现数据库的 RLS 提供程序（默认 Null / MySQL / PostgreSQL / Oracle）
 */

using System.Data.Common;

namespace Qlx.BuildingBlocks.MultiTenancy.Rls;

/// <summary>
/// 不支持 RLS 的提供程序基类：能力标记为 <see cref="RlsCapability.Unsupported"/>，
/// 会话上下文与策略脚本均为空操作，隔离降级为 L1（EF 过滤 + 写入守卫）+ L3（巡检）。
/// </summary>
/// <param name="providerName">数据库提供程序名称。</param>
public abstract class UnsupportedTenantRlsProvider(string providerName) : ITenantRlsProvider
{
    /// <inheritdoc />
    public string ProviderName { get; } = providerName;

    /// <inheritdoc />
    public RlsCapability Capability => RlsCapability.Unsupported;

    /// <inheritdoc />
    public Task SetSessionTenantAsync(DbConnection connection, long tenantId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public IReadOnlyList<string> BuildInstallStatements(IReadOnlyCollection<TenantRlsTable> tables) => [];
}

/// <summary>默认实现：RLS 未启用或数据库无原生能力时使用。</summary>
public sealed class NullTenantRlsProvider() : UnsupportedTenantRlsProvider("None");

/// <summary>
/// MySQL：无原生行级安全，能力标记 <see cref="RlsCapability.Unsupported"/>，
/// 降级 L1 + L3，并在升级建议中优先推荐独立库。
/// </summary>
public sealed class MySqlTenantRlsProvider() : UnsupportedTenantRlsProvider("MySql");

/// <summary>
/// PostgreSQL 骨架：目标实现为 <c>set_config('qlx.tenant_id', ...)</c> + <c>CREATE POLICY ... USING</c>，当前未实现。
/// </summary>
public sealed class PostgreSqlTenantRlsProvider() : UnsupportedTenantRlsProvider("PostgreSql");

/// <summary>
/// Oracle 骨架：目标实现为 <c>DBMS_SESSION.SET_CONTEXT</c> + VPD 策略，当前未实现。
/// </summary>
public sealed class OracleTenantRlsProvider() : UnsupportedTenantRlsProvider("Oracle");
