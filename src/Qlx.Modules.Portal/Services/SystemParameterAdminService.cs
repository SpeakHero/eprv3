/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 描述：系统参数管理服务实现：Upsert/删除 + 缓存失效 + 审计字段维护
 */

using System.Text.RegularExpressions;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;
using SystemParameterEntity = Qlx.Modules.Portal.Domain.Configuration.SystemParameter;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 系统参数管理服务实现。
/// <para>
/// 取消令牌铁律：本层不创建 <see cref="CancellationTokenSource"/>、不吞
/// <see cref="OperationCanceledException"/>，仅把上游（Controller → <c>HttpContext.RequestAborted</c>）
/// 传入的令牌原样透传给仓储——用于客户端断开、请求超时时及时终止数据库 IO，
/// 释放数据库连接，避免无效慢查询占用 DB 资源。
/// </para>
/// </summary>
public sealed partial class SystemParameterAdminService(
    ISystemParameterRepository repository,
    ICacheService cache,
    ICurrentUserContext currentUser) : ISystemParameterAdminService
{
    /// <summary>参数键格式：点分小写段（如 auth.max_password_fail_attempts），至少两段（源生成预编译，200ms 匹配超时）。</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex ParamKeyPattern();

    /// <inheritdoc />
    public Task<PagedResult<SystemParameterItem>> QueryAsync(
        SystemParameterQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return QueryCoreAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SystemParameterItem> GetAsync(string paramKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);

        var parameter = await repository.FindByKeyAsync(paramKey, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.SystemParameter, paramKey);
        return ToItem(parameter);
    }

    /// <inheritdoc />
    public async Task<SystemParameterItem> UpsertAsync(
        string paramKey, SaveSystemParameterRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);
        ArgumentNullException.ThrowIfNull(request);

        ValidateParamKey(paramKey);

        var updatedBy = currentUser.UserName;
        var existing = await repository.FindTrackedByKeyAsync(paramKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Update(request.ParamValue, request.Description, updatedBy);
            await repository.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var created = new SystemParameterEntity(paramKey, request.ParamValue, request.Description);
            created.MarkCreated(updatedBy);
            await repository.AddAsync(created, cancellationToken).ConfigureAwait(false);
        }

        // 写后失效读缓存（Cache-Aside 键与 PortalSystemParameterService 共用常量）
        await cache.RemoveAsync(SystemParameterKeys.CacheKeyPrefix + paramKey, cancellationToken).ConfigureAwait(false);

        var item = await repository.FindByKeyAsync(paramKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"参数 {paramKey} 写入后读取失败");
        return ToItem(item);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string paramKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);

        var deleted = await repository.DeleteAsync(paramKey, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            throw new NotFoundException(PortalConstants.ResourceNames.SystemParameter, paramKey);
        }

        await cache.RemoveAsync(SystemParameterKeys.CacheKeyPrefix + paramKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>校验参数键格式（点分小写），不合法抛 ValidationException（字段级错误）。</summary>
    private static void ValidateParamKey(string paramKey)
    {
        if (!ParamKeyPattern().IsMatch(paramKey))
        {
            throw new ValidationException("paramKey", "参数键必须为点分小写格式，如 auth.max_password_fail_attempts");
        }
    }

    private async Task<PagedResult<SystemParameterItem>> QueryCoreAsync(
        SystemParameterQueryRequest request, CancellationToken cancellationToken)
    {
        var page = await repository.QueryAsync(request.Keyword, request, cancellationToken).ConfigureAwait(false);
        return new PagedResult<SystemParameterItem>(
            page.Items.Select(ToItem).ToList(), page.TotalCount, page.Page, page.PageSize);
    }

    private static SystemParameterItem ToItem(SystemParameterEntity parameter) => new()
    {
        ParamKey = parameter.ParamKey,
        ParamValue = parameter.ParamValue,
        Description = parameter.Description,
        UpdatedAt = parameter.UpdatedAt,
        UpdatedBy = parameter.UpdatedBy,
    };
}
