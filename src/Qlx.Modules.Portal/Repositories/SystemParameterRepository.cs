/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 描述：系统参数仓储 EF Core 实现：全部异步 API 显式透传 CancellationToken
 */

using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Domain.Configuration;

namespace Qlx.Modules.Portal.Repositories;

/// <summary>
/// 系统参数仓储 EF Core 实现。
/// <para>
/// 数据访问边界铁律：每一个 EF Core 异步调用（FirstOrDefaultAsync / LongCountAsync /
/// ToListAsync / ExecuteDeleteAsync / SaveChangesAsync）都必须显式传入取消令牌——
/// 用于客户端断开、请求超时时及时终止数据库 IO，释放数据库连接，避免无效慢查询占用 DB 资源。
/// 禁止硬编码 <c>CancellationToken.None</c>。
/// </para>
/// </summary>
public sealed class SystemParameterRepository(PortalDbContext db) : ISystemParameterRepository
{
    /// <inheritdoc />
    public Task<SystemParameter?> FindByKeyAsync(string paramKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);

        return db.SystemParameters
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParamKey == paramKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SystemParameter?> FindTrackedByKeyAsync(string paramKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);

        // 写路径：保持跟踪态（不加 AsNoTracking），Update 后经 SaveChanges 落库
        return db.SystemParameters
            .FirstOrDefaultAsync(p => p.ParamKey == paramKey, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<SystemParameter>> QueryAsync(
        string? keyword, PagedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = db.SystemParameters.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            source = source.Where(p => p.ParamKey.Contains(keyword) || (p.Description != null && p.Description.Contains(keyword)));
        }

        return await source
            .OrderBy(p => p.ParamKey)
            .ToPagedAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(SystemParameter parameter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        await db.SystemParameters.AddAsync(parameter, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpdateAsync(SystemParameter parameter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        // 实体处于跟踪态（写路径由 FindTrackedByKeyAsync 取出），此处仅提交
        return db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string paramKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);

        var affected = await db.SystemParameters
            .Where(p => p.ParamKey == paramKey)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return affected > 0;
    }
}
