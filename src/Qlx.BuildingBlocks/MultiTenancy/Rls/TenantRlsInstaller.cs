/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 00:10:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 00:10:00 +08:00 中国标准时间 星期四
 * 描述：租户 RLS 安装器：按 EF 模型元数据推导目标表并在连接上安装策略
 */

using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Qlx.BuildingBlocks.MultiTenancy.Rls;

/// <summary>
/// 租户 RLS 安装器。由迁移执行器在模块迁移完成后调用：
/// 根据 EF 模型推导策略目标表（<see cref="TenantRlsPolicyCatalog.Build"/>），
/// 在当前连接上幂等执行提供程序生成的安装脚本。
/// </summary>
public sealed partial class TenantRlsInstaller(ITenantRlsProvider provider, ILogger<TenantRlsInstaller> logger)
{
    /// <summary>是否启用（提供程序支持 RLS）。</summary>
    public bool IsEnabled => provider.Capability == RlsCapability.Supported;

    /// <summary>
    /// 为指定模型在连接上安装 RLS 策略。模型中不含租户表或提供程序不支持时为空操作。
    /// </summary>
    /// <param name="connection">目标数据库连接（须已打开）。</param>
    /// <param name="model">模块 EF 模型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已安装策略的表集合（未启用或无租户表时为空）。</returns>
    public async Task<IReadOnlyList<TenantRlsTable>> InstallAsync(
        DbConnection connection, IModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(model);

        if (!IsEnabled)
        {
            LogSkipped(provider.ProviderName);
            return [];
        }

        var tables = TenantRlsPolicyCatalog.Build(model);
        if (tables.Count == 0)
        {
            return [];
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var statement in provider.BuildInstallStatements(tables))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        LogInstalled(provider.ProviderName, tables.Count);
        return tables;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "RLS 未启用（提供程序 {ProviderName} 不支持），跳过策略安装")]
    private partial void LogSkipped(string providerName);

    [LoggerMessage(Level = LogLevel.Information, Message = "RLS 策略安装完成（{ProviderName}，{TableCount} 张表）")]
    private partial void LogInstalled(string providerName, int tableCount);
}
