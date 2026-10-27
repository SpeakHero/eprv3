/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点完成 → 盘盈亏草稿生成（按差异正负拆分盘盈/盘亏单，金额按当前均价估算，不自行提交）
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>盘点差异行（原始字段，不做展示拼装）。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="BookQty">账面数量。</param>
/// <param name="ActualQty">实盘数量。</param>
/// <param name="DiffQty">差异数量（实盘 − 账面，正数盘盈 / 负数盘亏）。</param>
internal sealed record StockTakeAdjustDraftLine(
    long ItemId,
    string? BatchNo,
    decimal BookQty,
    decimal ActualQty,
    decimal DiffQty);

/// <summary>盘盈亏草稿生成上下文。</summary>
/// <param name="TakeId">盘点单 Id。</param>
/// <param name="TakeNo">盘点单号。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="Lines">差异行（差异非零）。</param>
internal sealed record StockTakeAdjustDraftContext(
    long TakeId,
    string TakeNo,
    long WarehouseId,
    IReadOnlyList<StockTakeAdjustDraftLine> Lines);

/// <summary>
/// 盘盈亏草稿生成器（盘点完成接入点）。Task 19 实现真实落库，Task 18 先注册空实现。
/// </summary>
internal interface IStockTakeAdjustDraftCreator
{
    /// <summary>按盘点差异生成盘盈/盘亏草稿单。</summary>
    /// <param name="context">生成上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>生成的草稿单数量。</returns>
    Task<int> CreateAsync(StockTakeAdjustDraftContext context, CancellationToken ct);
}

/// <summary>
/// 盘盈亏草稿生成实现：差异为正的行归入一张盘盈单（type=1），为负的行归入一张盘亏单（type=2）；
/// 单号经 <see cref="IDocumentNumberGenerator"/> 生成（WL-PYK），金额按建单时点均价估算。
/// <para>只追加跟踪态实体，不提交事务——由 <see cref="StockTakeService"/> 在盘点完成事务内统一保存。</para>
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（WL-PYK）。</param>
/// <param name="orgQuery">跨模块组织只读查询（解析单号中的组织码）。</param>
/// <param name="itemQuery">跨模块商品只读查询（参考成本回退）。</param>
/// <param name="txnCommand">库存写入命令契约（读取当前均价，A-13）。</param>
internal sealed class StockTakeAdjustDraftCreator(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IOrgQueryService orgQuery,
    IItemQueryService itemQuery,
    IStockTxnCommandService txnCommand) : IStockTakeAdjustDraftCreator
{
    private const int MaxReasonLength = 256;

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<int> CreateAsync(StockTakeAdjustDraftContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<StockTakeAdjustDraftLine> gains = [.. context.Lines
            .Where(static line => line.DiffQty > 0m)
            .OrderBy(static line => line.ItemId)];
        List<StockTakeAdjustDraftLine> losses = [.. context.Lines
            .Where(static line => line.DiffQty < 0m)
            .OrderBy(static line => line.ItemId)];
        if (gains.Count == 0 && losses.Count == 0)
        {
            return 0;
        }

        Warehouse warehouse = await repo.FindWarehouseAsync(context.WarehouseId, ct)
            .ConfigureAwait(false)
            ?? throw new ValidationException("warehouseId", "仓库不存在");
        string orgCode = await ResolveOrgCodeAsync(warehouse.OrgId, ct).ConfigureAwait(false);

        int created = 0;
        if (gains.Count > 0)
        {
            created += await CreateDocAsync(
                context, AdjustType.Gain, gains, orgCode, ct).ConfigureAwait(false);
        }

        if (losses.Count > 0)
        {
            created += await CreateDocAsync(
                context, AdjustType.Loss, losses, orgCode, ct).ConfigureAwait(false);
        }

        return created;
    }

    private async Task<int> CreateDocAsync(
        StockTakeAdjustDraftContext context,
        AdjustType type,
        List<StockTakeAdjustDraftLine> lines,
        string orgCode,
        CancellationToken ct)
    {
        string adjustNo = await numberGenerator
            .NextAsync(WmsDocumentPrefixes.InventoryAdjust, orgCode, ct).ConfigureAwait(false);
        string reason = $"盘点差异自动生成：{context.TakeNo}";
        if (reason.Length > MaxReasonLength)
        {
            reason = reason[..MaxReasonLength];
        }

        InventoryAdjustDoc doc = new(
            TenantId, adjustNo, context.TakeId, (int)type, context.WarehouseId, reason, remark: null);

        IReadOnlyList<StockItemQueryRow> rows = await itemQuery
            .FindByIdsAsync([.. lines.Select(static line => line.ItemId).Distinct()], ct)
            .ConfigureAwait(false);
        Dictionary<long, StockItemQueryRow> items = rows.ToDictionary(static item => item.Id);

        for (int index = 0; index < lines.Count; index++)
        {
            StockTakeAdjustDraftLine line = lines[index];
            decimal unitCost = await ResolveUnitCostAsync(
                line.ItemId, context.WarehouseId, items, ct).ConfigureAwait(false);
            doc.AddLine(new InventoryAdjustLine(
                TenantId,
                index + 1,
                line.ItemId,
                line.BatchNo,
                Math.Abs(line.DiffQty),
                unitCost,
                remark: null));
        }

        await repo.AddInventoryAdjustDocAsync(doc, ct).ConfigureAwait(false);
        return 1;
    }

    private async Task<decimal> ResolveUnitCostAsync(
        long itemId,
        long warehouseId,
        Dictionary<long, StockItemQueryRow> items,
        CancellationToken ct)
    {
        StockBalanceRow? balance = await txnCommand
            .FindBalanceAsync(itemId, warehouseId, ct).ConfigureAwait(false);
        if (balance?.AvgCost is { } avgCost && avgCost > 0m)
        {
            return avgCost;
        }

        return items.TryGetValue(itemId, out StockItemQueryRow? item)
            && item.RefCostPrice is { } refCost
            && refCost > 0m
                ? refCost
                : 0m;
    }

    private async Task<string> ResolveOrgCodeAsync(long? orgId, CancellationToken ct)
    {
        if (orgId is null or <= 0)
        {
            return "SYS";
        }

        OrgQueryRow? org = await orgQuery.FindAsync(orgId.Value, ct).ConfigureAwait(false);
        return org?.Code ?? "SYS";
    }
}
