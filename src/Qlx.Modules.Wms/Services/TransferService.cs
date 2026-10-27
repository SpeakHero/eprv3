/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：调拨与在途服务实现——跨公司流程码选择 + 新建即挂起审批 +
 *       发货成本快照（A-13）与同事务 Outbox/在途行（A-17）+ 收货入账并关闭在途 + 事务后持久发布事件
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Events.Wms;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 调拨与在途服务。调拨单新建即进入待审批（跨公司/内部两套流程定义码）；
/// 发货按调出仓均价快照成本并在同事务写 Outbox 与在途行；收货按在途快照入账并关闭在途行。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（WL-DB）。</param>
/// <param name="orgQuery">跨模块组织只读查询（解析单号中的组织码）。</param>
/// <param name="itemQuery">跨模块商品只读查询（批次属性）。</param>
/// <param name="txnCommand">库存写入命令契约（发货成本快照，A-13）。</param>
/// <param name="queue">库存命令队列（同事务入队）。</param>
/// <param name="approvalService">审批中心契约（发起调拨审批）。</param>
/// <param name="parameters">系统参数提供方（在途天数）。</param>
/// <param name="eventBus">事件总线（事务提交后持久发布）。</param>
/// <param name="userContext">当前用户上下文（经办人）。</param>
internal sealed class TransferService(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IOrgQueryService orgQuery,
    IItemQueryService itemQuery,
    IStockTxnCommandService txnCommand,
    IStockCommandQueue queue,
    IApprovalService approvalService,
    ISystemParameterProvider parameters,
    IEventBus eventBus,
    ICurrentUserContext userContext) : ITransferService
{
    /// <summary>状态机非法流转错误码。</summary>
    internal const string ErrorInvalidState = "WMS_INVALID_STATE";

    /// <summary>调出仓无台账错误码（409）。</summary>
    internal const string ErrorNoStock = "WMS_NO_STOCK";

    /// <summary>调拨审批业务类型。</summary>
    internal const string TransferBizType = "wms.transfer";

    /// <summary>同公司调拨审批流程定义码。</summary>
    internal const string InternalFlowCode = "flow.wms.transfer.internal";

    /// <summary>跨公司调拨审批流程定义码。</summary>
    internal const string CrossFlowCode = "flow.wms.transfer.cross";

    /// <summary>预计在途天数参数键。</summary>
    internal const string InTransitDaysParamKey = "wms.transfer.in_transit.days";

    /// <summary>预计在途天数默认值。</summary>
    internal const int DefaultInTransitDays = 7;

    private const int MaxBatchNoLength = 64;
    private const int MaxRemarkLength = 256;
    private const int MaxDiffNoteLength = 256;

    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IWmsTenantRepository _repo = repo;
    private readonly IDocumentNumberGenerator _numberGenerator = numberGenerator;
    private readonly IOrgQueryService _orgQuery = orgQuery;
    private readonly IItemQueryService _itemQuery = itemQuery;
    private readonly IStockTxnCommandService _txnCommand = txnCommand;
    private readonly IStockCommandQueue _queue = queue;
    private readonly IApprovalService _approvalService = approvalService;
    private readonly ISystemParameterProvider _parameters = parameters;
    private readonly IEventBus _eventBus = eventBus;
    private readonly ICurrentUserContext _userContext = userContext;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string? CurrentUserId => _userContext.UserId;

    private string ChangedBy => _userContext.UserId ?? "system";

    // ============ 读路径 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTransferRow>> ListAsync(
        int? status, long? warehouseId, CancellationToken ct)
    {
        ValidateStatusFilter(status);
        ValidateWarehouseFilter(warehouseId);

        IReadOnlyList<StockTransfer> transfers = await _repo
            .ListStockTransfersAsync(status, warehouseId, ct).ConfigureAwait(false);
        return [.. transfers.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<StockTransferDetailRow> GetAsync(long id, CancellationToken ct)
    {
        StockTransfer transfer = await FindOrThrowAsync(id, ct).ConfigureAwait(false);
        return MapDetail(transfer);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InTransitStockRow>> ListInTransitAsync(
        int? status, long? warehouseId, CancellationToken ct)
    {
        if (status is < 1 or > 2)
        {
            throw new ValidationException("status", "在途状态必须为 1..2");
        }

        ValidateWarehouseFilter(warehouseId);

        IReadOnlyList<InTransitStock> rows = await _repo
            .ListInTransitStocksAsync(status, warehouseId, ct).ConfigureAwait(false);
        return [.. rows.Select(MapInTransit)];
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<StockTransferDetailRow> CreateAsync(
        CreateStockTransferPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        StockTransfer transfer = await _repo.ExecuteInTransactionAsync(async token =>
        {
            if (payload.FromWarehouseId == payload.ToWarehouseId)
            {
                throw new ValidationException("toWarehouseId", "调出仓与调入仓不能相同");
            }

            Warehouse fromWarehouse = await FindEnabledWarehouseAsync(
                payload.FromWarehouseId, "fromWarehouseId", token).ConfigureAwait(false);
            Warehouse toWarehouse = await FindEnabledWarehouseAsync(
                payload.ToWarehouseId, "toWarehouseId", token).ConfigureAwait(false);

            string? remark = Normalize(payload.Remark, MaxRemarkLength, "remark");
            int inTransitDays = await ResolveInTransitDaysAsync(payload.InTransitDays, token)
                .ConfigureAwait(false);

            IReadOnlyList<CreateStockTransferLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "调拨明细不能为空");
            }

            IReadOnlyDictionary<long, StockItemQueryRow> items =
                await LoadItemsAsync([.. lines.Select(static line => line.ItemId)], token)
                    .ConfigureAwait(false);

            for (int index = 0; index < lines.Count; index++)
            {
                ValidateLinePayload(lines[index], index, items);
            }

            string orgCode = await ResolveOrgCodeAsync(fromWarehouse.OrgId, token).ConfigureAwait(false);
            string transferNo = await _numberGenerator
                .NextAsync(WmsDocumentPrefixes.Transfer, orgCode, token).ConfigureAwait(false);

            StockTransfer entity = new(
                TenantId,
                transferNo,
                fromWarehouse.Id,
                toWarehouse.Id,
                fromWarehouse.OrgId,
                toWarehouse.OrgId,
                inTransitDays,
                remark);
            for (int index = 0; index < lines.Count; index++)
            {
                CreateStockTransferLinePayload line = lines[index];
                entity.AddLine(new StockTransferLine(
                    TenantId,
                    index + 1,
                    line.ItemId,
                    line.Qty,
                    Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo")));
            }

            await _repo.AddStockTransferAsync(entity, token).ConfigureAwait(false);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);

            string flowCode = IsCrossCompany(fromWarehouse.OrgId, toWarehouse.OrgId)
                ? CrossFlowCode
                : InternalFlowCode;

            WmsApprovalRequest request = new(
                TenantId, TransferBizType, entity.Id,
                JsonSerializer.Serialize(entity.Id, QlxJsonDefaults.PreserveNull));
            await _repo.AddApprovalRequestAsync(request, token).ConfigureAwait(false);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);

            string instanceId = await _approvalService.StartApprovalAsync(
                new StartApprovalRequest(
                    ApprovalDefinitionCode: flowCode,
                    BizType: TransferBizType,
                    BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                    BizNo: entity.TransferNo,
                    InitiatorUserId: ChangedBy,
                    Title: $"物资调拨审批：{entity.TransferNo}",
                    Description: null,
                    BizData: new Dictionary<string, object?>
                    {
                        ["transferId"] = entity.Id,
                        ["fromWarehouseId"] = entity.FromWarehouseId,
                        ["toWarehouseId"] = entity.ToWarehouseId,
                    }),
                token).ConfigureAwait(false);

            request.BindInstance(instanceId);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return entity;
        }, ct).ConfigureAwait(false);

        return MapDetail(transfer);
    }

    /// <inheritdoc />
    public async Task<StockTransferDetailRow> ShipAsync(
        long id, ShipStockTransferPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        DateTimeOffset shippedAt = DateTimeOffset.UtcNow;

        StockTransfer transfer = await _repo.ExecuteInTransactionAsync(async token =>
        {
            StockTransfer entity = await FindOrThrowAsync(id, token).ConfigureAwait(false);
            if (entity.Status != TransferStatus.Effective)
            {
                throw new ConflictException("仅已生效的调拨单可发货", ErrorInvalidState);
            }

            IReadOnlyList<ShipStockTransferLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "发货入参不能为空");
            }

            Dictionary<long, StockTransferLine> tracked =
                entity.Lines.ToDictionary(static line => line.Id);
            Dictionary<long, ShipStockTransferLinePayload> inputs = [];
            for (int index = 0; index < lines.Count; index++)
            {
                ShipStockTransferLinePayload line = lines[index];
                if (!tracked.TryGetValue(line.LineId, out StockTransferLine? taskLine))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细不属于该调拨单");
                }

                if (!inputs.TryAdd(line.LineId, line))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细行重复提交");
                }

                if (line.ShipQty < 0)
                {
                    throw new ValidationException($"lines[{index}].shipQty", "发货数量不能为负数");
                }

                if (line.ShipQty > taskLine.Qty)
                {
                    throw new ValidationException($"lines[{index}].shipQty", "发货数量不能超过调拨数量");
                }
            }

            List<PostStockTxnRequest> requests = [];
            List<InTransitStock> inTransitRows = [];
            foreach (StockTransferLine taskLine in entity.Lines.OrderBy(static line => line.LineNo))
            {
                if (!inputs.TryGetValue(taskLine.Id, out ShipStockTransferLinePayload? input)
                    || input.ShipQty <= 0)
                {
                    continue;
                }

                StockBalanceRow? balance = await _txnCommand
                    .FindBalanceAsync(taskLine.ItemId, entity.FromWarehouseId, token)
                    .ConfigureAwait(false);
                if (balance is null)
                {
                    throw new ConflictException(
                        $"调出仓无商品 {taskLine.ItemId} 台账，无法发货", ErrorNoStock);
                }

                decimal unitCost = Math.Round(
                    balance.AvgCost ?? 0m, 4, MidpointRounding.AwayFromZero);
                taskLine.RecordShip(input.ShipQty, unitCost);
                requests.Add(new PostStockTxnRequest(
                    StockSourceType.TransferOut,
                    entity.TransferNo,
                    taskLine.LineNo,
                    taskLine.ItemId,
                    entity.FromWarehouseId,
                    taskLine.BatchNo,
                    StockDirection.Out,
                    input.ShipQty,
                    0m,
                    CurrentUserId,
                    $"wms:db-out:{entity.TransferNo}:{taskLine.LineNo}",
                    shippedAt));
                inTransitRows.Add(new InTransitStock(
                    TenantId,
                    entity.Id,
                    taskLine.Id,
                    entity.TransferNo,
                    taskLine.ItemId,
                    entity.FromWarehouseId,
                    entity.ToWarehouseId,
                    taskLine.BatchNo,
                    input.ShipQty,
                    unitCost,
                    shippedAt,
                    shippedAt.AddDays(entity.InTransitDays)));
            }

            if (requests.Count == 0)
            {
                throw new ValidationException("lines", "至少一行发货数量须大于 0");
            }

            entity.Ship();
            _ = await _queue.EnqueueAsync(requests, token).ConfigureAwait(false);
            foreach (InTransitStock row in inTransitRows)
            {
                await _repo.AddInTransitStockAsync(row, token).ConfigureAwait(false);
            }

            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return entity;
        }, ct).ConfigureAwait(false);

        await _eventBus.PublishDurableAsync(
            new TransferShippedEvent
            {
                TransferId = transfer.Id,
                TransferNo = transfer.TransferNo,
                FromWarehouseId = transfer.FromWarehouseId,
                ToWarehouseId = transfer.ToWarehouseId,
            },
            ct).ConfigureAwait(false);

        return MapDetail(transfer);
    }

    /// <inheritdoc />
    public async Task<StockTransferDetailRow> ReceiveAsync(
        long id, ReceiveStockTransferPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        DateTimeOffset receivedAt = DateTimeOffset.UtcNow;

        StockTransfer transfer = await _repo.ExecuteInTransactionAsync(async token =>
        {
            StockTransfer entity = await FindOrThrowAsync(id, token).ConfigureAwait(false);
            if (entity.Status != TransferStatus.Shipped)
            {
                throw new ConflictException("仅调出已发货的调拨单可收货", ErrorInvalidState);
            }

            IReadOnlyList<ReceiveStockTransferLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "收货入参不能为空");
            }

            Dictionary<long, StockTransferLine> tracked =
                entity.Lines.ToDictionary(static line => line.Id);
            Dictionary<long, ReceiveStockTransferLinePayload> inputs = [];
            for (int index = 0; index < lines.Count; index++)
            {
                ReceiveStockTransferLinePayload line = lines[index];
                if (!tracked.TryGetValue(line.LineId, out StockTransferLine? taskLine))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细不属于该调拨单");
                }

                if (!inputs.TryAdd(line.LineId, line))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细行重复提交");
                }

                if (taskLine.ShipQty <= 0)
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细行尚未发货，不能收货");
                }

                if (line.ReceiveQty < 0)
                {
                    throw new ValidationException($"lines[{index}].receiveQty", "收货数量不能为负数");
                }

                if (line.ReceiveQty > taskLine.ShipQty)
                {
                    throw new ValidationException($"lines[{index}].receiveQty", "收货数量不能超过发货数量");
                }

                string? diffNote = Normalize(
                    line.DiffNote, MaxDiffNoteLength, $"lines[{index}].diffNote");
                if (taskLine.ShipQty - line.ReceiveQty > 0 && diffNote is null)
                {
                    throw new ValidationException(
                        $"lines[{index}].diffNote", "收货数量少于发货数量时必须填写差异说明");
                }
            }

            Dictionary<long, InTransitStock> transitRows = [];
            int positiveQtyLines = 0;
            foreach (StockTransferLine taskLine in entity.Lines.OrderBy(static line => line.LineNo))
            {
                if (taskLine.ShipQty <= 0)
                {
                    continue;
                }

                if (!inputs.TryGetValue(taskLine.Id, out ReceiveStockTransferLinePayload? input))
                {
                    throw new ValidationException("lines", "收货须覆盖全部已发货明细");
                }

                InTransitStock row = await _repo.FindInTransitStockAsync(taskLine.Id, token)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(
                        "在途库存", taskLine.Id.ToString(CultureInfo.InvariantCulture));
                if (row.Status != InTransitStatus.InTransit)
                {
                    throw new ConflictException("在途行已关闭，不能重复收货", ErrorInvalidState);
                }

                if (input.ReceiveQty > 0)
                {
                    positiveQtyLines++;
                }

                transitRows[taskLine.Id] = row;
            }

            if (positiveQtyLines == 0)
            {
                throw new ValidationException("lines", "至少一行收货数量须大于 0");
            }

            List<PostStockTxnRequest> requests = new(transitRows.Count);
            foreach (StockTransferLine taskLine in entity.Lines.OrderBy(static line => line.LineNo))
            {
                if (!inputs.TryGetValue(taskLine.Id, out ReceiveStockTransferLinePayload? input))
                {
                    continue;
                }

                InTransitStock row = transitRows[taskLine.Id];
                string? diffNote = Normalize(
                    input.DiffNote, MaxDiffNoteLength, $"lines.{taskLine.LineNo}.diffNote");
                row.Close();
                taskLine.RecordReceive(input.ReceiveQty, diffNote);
                if (input.ReceiveQty <= 0)
                {
                    continue;
                }

                requests.Add(new PostStockTxnRequest(
                    StockSourceType.TransferIn,
                    entity.TransferNo,
                    taskLine.LineNo,
                    taskLine.ItemId,
                    entity.ToWarehouseId,
                    taskLine.BatchNo,
                    StockDirection.In,
                    input.ReceiveQty,
                    row.UnitCost,
                    CurrentUserId,
                    $"wms:db-in:{entity.TransferNo}:{taskLine.LineNo}",
                    receivedAt));
            }

            entity.Receive();
            _ = await _queue.EnqueueAsync(requests, token).ConfigureAwait(false);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return entity;
        }, ct).ConfigureAwait(false);

        await _eventBus.PublishDurableAsync(
            new TransferReceivedEvent
            {
                TransferId = transfer.Id,
                TransferNo = transfer.TransferNo,
                FromWarehouseId = transfer.FromWarehouseId,
                ToWarehouseId = transfer.ToWarehouseId,
            },
            ct).ConfigureAwait(false);

        return MapDetail(transfer);
    }

    /// <inheritdoc />
    public async Task<StockTransferRow> VoidAsync(long id, CancellationToken ct)
        => await _repo.ExecuteInTransactionAsync(async token =>
        {
            StockTransfer transfer = await FindOrThrowAsync(id, token).ConfigureAwait(false);
            if (transfer.Status is not (TransferStatus.PendingApproval or TransferStatus.Effective))
            {
                throw new ConflictException("仅待审批或已生效的调拨单可作废", ErrorInvalidState);
            }

            transfer.Void();
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(transfer);
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ApplyApprovalAsync(long id, bool approved, CancellationToken ct)
    {
        _ = await _repo.ExecuteInTransactionAsync(async token =>
        {
            StockTransfer transfer = await FindOrThrowAsync(id, token).ConfigureAwait(false);

            // 幂等：非待审批状态不再改动（重复事件 / 已发货 / 已作废）
            if (transfer.Status != TransferStatus.PendingApproval)
            {
                return true;
            }

            if (approved)
            {
                transfer.Activate();
            }
            else
            {
                transfer.Void();
            }

            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    // ============ 内部实现 ============

    private async Task<StockTransfer> FindOrThrowAsync(long id, CancellationToken ct)
        => await _repo.FindStockTransferAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("调拨单", id.ToString(CultureInfo.InvariantCulture));

    private async Task<Warehouse> FindEnabledWarehouseAsync(
        long warehouseId, string fieldName, CancellationToken ct)
    {
        if (warehouseId <= 0)
        {
            throw new ValidationException(fieldName, "仓库 Id 非法");
        }

        Warehouse warehouse = await _repo.FindWarehouseAsync(warehouseId, ct).ConfigureAwait(false)
            ?? throw new ValidationException(fieldName, "仓库不存在");
        if (warehouse.Status != WarehouseStatus.Enabled)
        {
            throw new ValidationException(fieldName, "仓库已停用");
        }

        return warehouse;
    }

    private async Task<IReadOnlyDictionary<long, StockItemQueryRow>> LoadItemsAsync(
        IReadOnlyList<long> itemIds, CancellationToken ct)
    {
        List<long> distinct = [.. itemIds.Distinct()];
        IReadOnlyList<StockItemQueryRow> items = await _itemQuery
            .FindByIdsAsync(distinct, ct).ConfigureAwait(false);
        Dictionary<long, StockItemQueryRow> map = items.ToDictionary(static item => item.Id);
        if (map.Count != distinct.Count)
        {
            throw new ValidationException("lines", "存在不存在的商品");
        }

        return map;
    }

    private static void ValidateLinePayload(
        CreateStockTransferLinePayload line,
        int index,
        IReadOnlyDictionary<long, StockItemQueryRow> items)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.ItemId <= 0)
        {
            throw new ValidationException($"lines[{index}].itemId", "商品 Id 非法");
        }

        if (line.Qty <= 0)
        {
            throw new ValidationException($"lines[{index}].qty", "调拨数量必须大于 0");
        }

        StockItemQueryRow item = items[line.ItemId];
        if (item.Status != 1)
        {
            throw new ValidationException($"lines[{index}].itemId", "商品已停用");
        }

        string? batchNo = Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo");
        if (item.BatchEnabled && batchNo is null)
        {
            throw new ValidationException($"lines[{index}].batchNo", "启用批次的商品必须填写批次号");
        }
    }

    private async Task<int> ResolveInTransitDaysAsync(int? requested, CancellationToken ct)
    {
        if (requested is not null)
        {
            return ValidateInTransitDays(requested.Value);
        }

        string raw = await _parameters
            .GetStringAsync(
                InTransitDaysParamKey,
                DefaultInTransitDays.ToString(CultureInfo.InvariantCulture),
                ct)
            .ConfigureAwait(false);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days))
        {
            return DefaultInTransitDays;
        }

        return days is >= 1 and <= 365 ? days : DefaultInTransitDays;
    }

    private static int ValidateInTransitDays(int days)
        => days is >= 1 and <= 365
            ? days
            : throw new ValidationException("inTransitDays", "预计在途天数必须为 1..365");

    private static bool IsCrossCompany(long? fromOrgId, long? toOrgId)
        => fromOrgId is null || toOrgId is null || fromOrgId != toOrgId;

    private async Task<string> ResolveOrgCodeAsync(long? orgId, CancellationToken ct)
    {
        if (orgId is null or <= 0)
        {
            return "SYS";
        }

        OrgQueryRow? org = await _orgQuery.FindAsync(orgId.Value, ct).ConfigureAwait(false);
        return org?.Code ?? "SYS";
    }

    private static void ValidateStatusFilter(int? status)
    {
        if (status is < 1 or > 5)
        {
            throw new ValidationException("status", "状态必须为 1..5");
        }
    }

    private static void ValidateWarehouseFilter(long? warehouseId)
    {
        if (warehouseId is <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }
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

    private static StockTransferRow Map(StockTransfer entity)
        => new(entity.Id, entity.TransferNo, entity.FromWarehouseId, entity.ToWarehouseId,
            entity.FromOrgId, entity.ToOrgId, entity.InTransitDays, (int)entity.Status,
            entity.Remark, entity.CreatedAt);

    private static StockTransferLineRow Map(StockTransferLine entity)
        => new(entity.Id, entity.LineNo, entity.ItemId, entity.Qty, entity.ShipQty,
            entity.ReceiveQty, entity.BatchNo, entity.UnitCost, entity.DiffNote);

    private static StockTransferDetailRow MapDetail(StockTransfer entity)
        => new(Map(entity),
            [.. entity.Lines.OrderBy(static line => line.LineNo).Select(Map)]);

    private static InTransitStockRow MapInTransit(InTransitStock entity)
        => new(entity.Id, entity.TransferId, entity.TransferNo, entity.TransferLineId,
            entity.ItemId, entity.FromWarehouseId, entity.ToWarehouseId, entity.BatchNo,
            entity.Qty, entity.UnitCost, entity.ShippedAt, entity.ExpectArrive, (int)entity.Status);
}
