/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:00:00 +08:00 中国标准时间 星期一
 * 描述：跨模块商品只读查询服务（IItemQueryService 实现）——扁平原始字段，不做展示映射与权限裁剪
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Repositories;

namespace Qlx.Modules.Stk.Services;

/// <summary>
/// 跨模块商品只读查询服务。仅依赖当前租户上下文与 Stk 租户仓储；
/// 返回业务校验所需的原始字段（批次属性/参考成本），不做展示拼装（B-2）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Stk 租户仓储。</param>
internal sealed class ItemQueryService(
    ITenantContext tenantCtx,
    IStkTenantRepository repo) : IItemQueryService
{
    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<StockItemQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        _ = TenantId;
        Item? item = await repo.FindItemAsync(id, cancellationToken).ConfigureAwait(false);
        return item is null ? null : Map(item);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockItemQueryRow>> FindByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken)
    {
        _ = TenantId;
        if (ids.Count == 0)
        {
            return [];
        }

        IReadOnlyList<Item> items = await repo
            .ListItemsByIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        return [.. items.Select(Map)];
    }

    private static StockItemQueryRow Map(Item item)
        => new(item.Id, item.BatchEnabled, item.ExpiryEnabled, item.RefCostPrice, (int)item.Status,
            item.CategoryId);
}
