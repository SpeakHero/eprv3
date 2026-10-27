/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏服务实现——手工建单（金额按当前均价估算）+ 提交审批挂起 +
 *       审批终态幂等应用（通过同事务入队 Gain 5 / Loss 9，驳回回草稿）
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 盘盈盘亏服务。手工建单只落草稿，金额按建单时点「当前均价 → 参考成本 → 0」三级回退估算；
/// 审批通过后由 <see cref="ApplyAsync"/> 在同一事务内写 Outbox（每行恰好 1 条命令，幂等键
/// <c>wms:py:{AdjustNo}:{LineNo}</c>），重复应用直接短路。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（WL-PYK）。</param>
/// <param name="orgQuery">跨模块组织只读查询（解析单号中的组织码）。</param>
/// <param name="itemQuery">跨模块商品只读查询（批次属性与参考成本）。</param>
/// <param name="txnCommand">库存写入命令契约（读取当前均价，A-13）。</param>
/// <param name="queue">库存命令队列（同事务入队，A-17）。</param>
/// <param name="approvalService">审批中心契约（发起盘盈盘亏审批）。</param>
/// <param name="userContext">当前用户上下文（发起人 / 经办人）。</param>
internal sealed class InventoryAdjustService(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IOrgQueryService orgQuery,
    IItemQueryService itemQuery,
    IStockTxnCommandService txnCommand,
    IStockCommandQueue queue,
    IApprovalService approvalService,
    ICurrentUserContext userContext) : IInventoryAdjustService, IInventoryAdjustApprovalApplier
{
    /// <summary>状态机非法流转错误码。</summary>
    internal const string ErrorInvalidState = "WMS_INVALID_STATE";

    /// <summary>盘盈盘亏审批业务类型。</summary>
    internal const string ApproveBizType = "wms.inventory.adjust";

    /// <summary>盘盈盘亏审批流程定义码。</summary>
    internal const string ApproveFlowCode = "flow.wms.inventory.adjust";

    private const int MaxReasonLength = 256;
    private const int MaxRemarkLength = 256;
    private const int MaxBatchNoLength = 64;

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => userContext.UserId ?? "system";

    // ============ 读路径 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryAdjustRow>> ListAsync(
        int? type, int? status, long? warehouseId, CancellationToken ct)
    {
        if (type is < 1 or > 2)
        {
            throw new ValidationException("type", "类型必须为 1 盘盈 / 2 盘亏");
        }

        if (status is < 1 or > 3)
        {
            throw new ValidationException("status", "状态必须为 1..3");
        }

        if (warehouseId is <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }

        IReadOnlyList<InventoryAdjustDoc> docs = await repo
            .ListInventoryAdjustDocsAsync(type, status, warehouseId, ct).ConfigureAwait(false);
        return [.. docs.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<InventoryAdjustDetailRow> GetAsync(long id, CancellationToken ct)
    {
        InventoryAdjustDoc doc = await FindDocOrThrowAsync(id, ct).ConfigureAwait(false);
        return MapDetail(doc);
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<InventoryAdjustDetailRow> CreateAsync(
        CreateInventoryAdjustPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return await repo.ExecuteInTransactionAsync(async token =>
        {
            if (payload.Type is not (1 or 2))
            {
                throw new ValidationException("type", "类型必须为 1 盘盈 / 2 盘亏");
            }

            Warehouse warehouse = await FindEnabledWarehouseAsync(payload.WarehouseId, token)
                .ConfigureAwait(false);
            string reason = RequireReason(payload.Reason);
            string? remark = Normalize(payload.Remark, MaxRemarkLength, "remark");

            IReadOnlyList<CreateInventoryAdjustLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "盘盈盘亏明细不能为空");
            }

            Dictionary<long, StockItemQueryRow> items = await LoadItemsAsync(
                [.. lines.Select(static line => line.ItemId)], token).ConfigureAwait(false);

            string orgCode = await ResolveOrgCodeAsync(warehouse.OrgId, token).ConfigureAwait(false);
            string adjustNo = await numberGenerator
                .NextAsync(WmsDocumentPrefixes.InventoryAdjust, orgCode, token).ConfigureAwait(false);

            InventoryAdjustDoc doc = new(
                TenantId, adjustNo, takeId: null, payload.Type, warehouse.Id, reason, remark);

            for (int index = 0; index < lines.Count; index++)
            {
                CreateInventoryAdjustLinePayload line = lines[index];
                ValidateLine(line, index, items);
                decimal unitCost = await ResolveUnitCostAsync(
                    line.ItemId, warehouse.Id, items[line.ItemId], token).ConfigureAwait(false);
                doc.AddLine(new InventoryAdjustLine(
                    TenantId,
                    index + 1,
                    line.ItemId,
                    Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo"),
                    line.Qty,
                    unitCost,
                    Normalize(line.Remark, MaxRemarkLength, $"lines[{index}].remark")));
            }

            await repo.AddInventoryAdjustDocAsync(doc, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapDetail(doc);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InventoryAdjustRow> SubmitAsync(long id, CancellationToken ct)
        => await repo.ExecuteInTransactionAsync(async token =>
        {
            InventoryAdjustDoc doc = await FindDocOrThrowAsync(id, token).ConfigureAwait(false);
            if (doc.Status != AdjustDocStatus.Draft)
            {
                throw new ConflictException("仅草稿状态的盘盈盘亏单可提交审批", ErrorInvalidState);
            }

            doc.Submit();
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);

            WmsApprovalRequest request = new(
                TenantId,
                ApproveBizType,
                doc.Id,
                JsonSerializer.Serialize(doc.Id, QlxJsonDefaults.PreserveNull));
            await repo.AddApprovalRequestAsync(request, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);

            string instanceId = await approvalService.StartApprovalAsync(
                new StartApprovalRequest(
                    ApprovalDefinitionCode: ApproveFlowCode,
                    BizType: ApproveBizType,
                    BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                    BizNo: doc.AdjustNo,
                    InitiatorUserId: ChangedBy,
                    Title: $"盘盈盘亏审批：{doc.AdjustNo}",
                    Description: null,
                    BizData: new Dictionary<string, object?>
                    {
                        ["inventoryAdjustDocId"] = doc.Id,
                        ["warehouseId"] = doc.WarehouseId,
                        ["type"] = doc.Type,
                        ["totalAmount"] = doc.TotalAmount,
                    }),
                token).ConfigureAwait(false);

            request.BindInstance(instanceId);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(doc);
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> ApplyAsync(long docId, bool approved, CancellationToken ct)
    {
        if (docId <= 0)
        {
            throw new ValidationException("id", "盘盈盘亏单 Id 非法");
        }

        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        return await repo.ExecuteInTransactionAsync(async token =>
        {
            InventoryAdjustDoc doc = await repo
                .FindInventoryAdjustDocAsync(docId, token).ConfigureAwait(false)
                ?? throw new NotFoundException("盘盈盘亏单", docId.ToString(CultureInfo.InvariantCulture));

            // 幂等：仅「待审批」可应用，重复回放或草稿态一律短路
            if (doc.Status != AdjustDocStatus.PendingApproval)
            {
                return false;
            }

            if (!approved)
            {
                doc.Reject();
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            }

            List<PostStockTxnRequest> requests = await BuildRequestsAsync(doc, occurredAt, token)
                .ConfigureAwait(false);
            _ = await queue.EnqueueAsync(requests, token).ConfigureAwait(false);
            doc.Approve();
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    // ============ 内部实现 ============

    private async Task<List<PostStockTxnRequest>> BuildRequestsAsync(
        InventoryAdjustDoc doc, DateTimeOffset occurredAt, CancellationToken token)
    {
        bool isGain = doc.Type == (int)AdjustType.Gain;
        Dictionary<long, StockItemQueryRow> items = await LoadItemsAsync(
            [.. doc.Lines.Select(static line => line.ItemId)], token).ConfigureAwait(false);

        List<PostStockTxnRequest> requests = new(doc.Lines.Count);
        foreach (InventoryAdjustLine line in doc.Lines.OrderBy(static line => line.LineNo))
        {
            decimal unitCost = isGain
                ? await ResolveUnitCostAsync(line.ItemId, doc.WarehouseId, items[line.ItemId], token)
                    .ConfigureAwait(false)
                : 0m;

            requests.Add(new PostStockTxnRequest(
                isGain ? StockSourceType.Gain : StockSourceType.Loss,
                doc.AdjustNo,
                line.LineNo,
                line.ItemId,
                doc.WarehouseId,
                line.BatchNo,
                isGain ? StockDirection.In : StockDirection.Out,
                line.Qty,
                unitCost,
                userContext.UserId,
                $"wms:py:{doc.AdjustNo}:{line.LineNo}",
                occurredAt));
        }

        return requests;
    }

    private static void ValidateLine(
        CreateInventoryAdjustLinePayload line,
        int index,
        Dictionary<long, StockItemQueryRow> items)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.ItemId <= 0)
        {
            throw new ValidationException($"lines[{index}].itemId", "商品 Id 非法");
        }

        if (line.Qty <= 0m)
        {
            throw new ValidationException($"lines[{index}].qty", "调整数量必须大于 0");
        }

        StockItemQueryRow item = items[line.ItemId];
        if (item.Status != 1)
        {
            throw new ValidationException($"lines[{index}].itemId", "商品已停用");
        }

        if (item.BatchEnabled && string.IsNullOrWhiteSpace(line.BatchNo))
        {
            throw new ValidationException($"lines[{index}].batchNo", "启用批次的商品必须填写批次号");
        }
    }

    private async Task<Dictionary<long, StockItemQueryRow>> LoadItemsAsync(
        IReadOnlyList<long> itemIds, CancellationToken ct)
    {
        List<long> distinct = [.. itemIds.Distinct()];
        if (distinct.Count == 0)
        {
            return [];
        }

        IReadOnlyList<StockItemQueryRow> rows = await itemQuery
            .FindByIdsAsync(distinct, ct).ConfigureAwait(false);
        Dictionary<long, StockItemQueryRow> map = rows.ToDictionary(static item => item.Id);
        if (map.Count != distinct.Count)
        {
            throw new ValidationException("lines", "存在不存在的商品");
        }

        return map;
    }

    /// <summary>成本估算三级回退：当前均价 → 参考成本价 → 0（A-14 同类口径）。</summary>
    private async Task<decimal> ResolveUnitCostAsync(
        long itemId, long warehouseId, StockItemQueryRow item, CancellationToken ct)
    {
        StockBalanceRow? balance = await txnCommand
            .FindBalanceAsync(itemId, warehouseId, ct).ConfigureAwait(false);
        if (balance?.AvgCost is { } avgCost && avgCost > 0m)
        {
            return avgCost;
        }

        return item.RefCostPrice is { } refCost && refCost > 0m ? refCost : 0m;
    }

    private async Task<InventoryAdjustDoc> FindDocOrThrowAsync(long id, CancellationToken ct)
    {
        if (id <= 0)
        {
            throw new ValidationException("id", "盘盈盘亏单 Id 非法");
        }

        return await repo.FindInventoryAdjustDocAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("盘盈盘亏单", id.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<Warehouse> FindEnabledWarehouseAsync(long warehouseId, CancellationToken ct)
    {
        if (warehouseId <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }

        Warehouse warehouse = await repo.FindWarehouseAsync(warehouseId, ct).ConfigureAwait(false)
            ?? throw new ValidationException("warehouseId", "仓库不存在");
        if (warehouse.Status != WarehouseStatus.Enabled)
        {
            throw new ValidationException("warehouseId", "仓库已停用");
        }

        return warehouse;
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

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("reason", "盘盈盘亏原因不能为空");
        }

        string trimmed = reason.Trim();
        return trimmed.Length <= MaxReasonLength
            ? trimmed
            : throw new ValidationException("reason", $"长度不能超过 {MaxReasonLength} 个字符");
    }

    private static string? Normalize(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ValidationException(fieldName, $"长度不能超过 {maxLength} 个字符");
    }

    private static InventoryAdjustRow Map(InventoryAdjustDoc doc)
        => new(doc.Id, doc.AdjustNo, doc.TakeId, doc.Type, doc.WarehouseId, doc.Reason,
            doc.TotalAmount, doc.Remark, (int)doc.Status, doc.CreatedAt);

    private static InventoryAdjustLineRow Map(InventoryAdjustLine line)
        => new(line.Id, line.LineNo, line.ItemId, line.BatchNo, line.Qty, line.UnitCost,
            line.Remark);

    private static InventoryAdjustDetailRow MapDetail(InventoryAdjustDoc doc)
        => new(Map(doc),
            [.. doc.Lines.OrderBy(static line => line.LineNo).Select(Map)]);
}
