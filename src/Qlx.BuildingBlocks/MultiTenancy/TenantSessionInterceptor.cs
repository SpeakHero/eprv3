/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 描述：租户会话拦截器：连接打开时把当前租户写入数据库会话上下文（RLS 前提）
 */

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Qlx.BuildingBlocks.MultiTenancy.Rls;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户会话拦截器（三层隔离的 L2 入口）。连接打开时按 <see cref="TenantDbContext.CurrentTenantId"/>
/// 写入数据库会话上下文；连接池复用连接时会重写，避免残留上一个租户的上下文。
/// 仅在 <see cref="ITenantRlsProvider.Capability"/> 为 <see cref="RlsCapability.Supported"/> 时生效。
/// </summary>
public sealed class TenantSessionInterceptor(
    ITenantRlsProvider provider) : DbConnectionInterceptor
{
    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (ResolveTenantId(eventData) is not { } tenantId)
        {
            return;
        }

        // DbConnectionInterceptor 同步回调无环境 CancellationToken 可用，按规范【豁免点 1】显式传 None
        provider.SetSessionTenantAsync(connection, tenantId, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken)
    {
        return ResolveTenantId(eventData) is { } tenantId
            ? provider.SetSessionTenantAsync(connection, tenantId, cancellationToken)
            : Task.CompletedTask;
    }

    private long? ResolveTenantId(ConnectionEndEventData eventData)
    {
        if (provider.Capability != RlsCapability.Supported)
        {
            return null;
        }

        return eventData.Context is TenantDbContext { CurrentTenantId: > 0 } context
            ? context.CurrentTenantId
            : null;
    }
}
