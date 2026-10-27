/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业服务实现——FEFO 建议（业务规则后端求值）+ 复核一致性强校验（409 WMS_CHECK_MISMATCH）+
 *       大额出库审批挂起（A-13 均价取台账）+ 普通完成同事务入队 Outbox（A-17）+ 事务后持久发布完成事件
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
using Qlx.Modules.Wms.Domain.Outbound;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 出库作业服务。FEFO 拣货建议由后端按批次效期升序求值（业务规则，B-2 不适用）；
/// 大额出库（拣货金额超阈值）只挂起审批、不写 Outbox，审批通过后由 Applier 调用完成逻辑（Task 21）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（WL-CK）。</param>
/// <param name="orgQuery">跨模块组织只读查询（解析单号中的组织码）。</param>
/// <param name="itemQuery">跨模块商品只读查询（批次属性）。</param>
/// <param name="stockQuery">跨模块库存只读查询（FEFO 批次行）。</param>
/// <param name="txnCommand">库存写入命令契约（大额审批均价取台账，A-13）。</param>
/// <param name="queue">库存命令队列（同事务入队）。</param>
/// <param name="approvalService">审批中心契约（发起大额出库审批）。</param>
/// <param name="parameters">系统参数提供方（大额阈值）。</param>
/// <param name="eventBus">事件总线（事务提交后持久发布）。</param>
/// <param name="userContext">当前用户上下文（拣货人/复核人）。</param>
internal sealed class StockOutService(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IOrgQueryService orgQuery,
    IItemQueryService itemQuery,
    IStockQueryService stockQuery,
    IStockTxnCommandService txnCommand,
    IStockCommandQueue queue,
    IApprovalService approvalService,
    ISystemParameterProvider parameters,
    IEventBus eventBus,
    ICurrentUserContext userContext) : IStockOutService
{
    /// <summary>状态机非法流转错误码。</summary>
    internal const string ErrorInvalidState = "WMS_INVALID_STATE";

    /// <summary>复核数量与拣货数量不一致错误码（409）。</summary>
    internal const string ErrorCheckMismatch = "WMS_CHECK_MISMATCH";

    /// <summary>大额出库已挂起审批错误码（重复提交）。</summary>
    internal const string ErrorApprovalPending = "WMS_APPROVAL_PENDING";

    /// <summary>大额出库审批业务类型。</summary>
    internal const string OutboundLargeBizType = "wms.outbound.large";

    /// <summary>大额出库审批流程定义码。</summary>
    internal const string OutboundLargeFlowCode = "flow.wms.outbound.large";

    /// <summary>大额出库金额阈值参数键。</summary>
    internal const string LargeThresholdParamKey = "wms.outbound.large.threshold";

    /// <summary>大额出库金额阈值默认值（元）。</summary>
    internal const string DefaultLargeThresholdText = "50000";

    private const int MaxSourceDocNoLength = 32;
    private const int MaxBatchNoLength = 64;
    private const int MaxRemarkLength = 256;
    private const int MaxUserIdLength = 64;
    private const int MaxHandoverLength = 64;

    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IWmsTenantRepository _repo = repo;
    private readonly IDocumentNumberGenerator _numberGenerator = numberGenerator;
    private readonly IOrgQueryService _orgQuery = orgQuery;
    private readonly IItemQueryService _itemQuery = itemQuery;
    private readonly IStockQueryService _stockQuery = stockQuery;
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
    public async Task<IReadOnlyList<StockOutTaskRow>> ListAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct)
    {
        ValidateStatusFilter(status);
        ValidateSourceTypeFilter(sourceType);
        if (warehouseId is <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }

        IReadOnlyList<StockOutTask> tasks = await _repo
            .ListStockOutTasksAsync(status, warehouseId, sourceType, ct).ConfigureAwait(false);
        return [.. tasks.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<StockOutTaskDetailRow> GetAsync(long id, CancellationToken ct)
    {
        StockOutTask task = await FindTaskOrThrowAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<StockOutFefoSuggestionRow> suggestions =
            await BuildFefoSuggestionsAsync(task, ct).ConfigureAwait(false);
        return MapDetail(task, suggestions);
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<StockOutTaskDetailRow> CreateAsync(
        CreateStockOutTaskPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        StockOutTask task = await _repo.ExecuteInTransactionAsync(async token =>
        {
            int sourceType = ValidateSourceType(payload.SourceType);
            string sourceDocNo = RequireSourceDocNo(payload.SourceDocNo);
            string? remark = Normalize(payload.Remark, MaxRemarkLength, "remark");
            Warehouse warehouse = await FindEnabledWarehouseAsync(payload.WarehouseId, token)
                .ConfigureAwait(false);

            IReadOnlyList<CreateStockOutLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "出库明细不能为空");
            }

            IReadOnlyDictionary<long, StockItemQueryRow> items =
                await LoadItemsAsync([.. lines.Select(static line => line.ItemId)], token)
                    .ConfigureAwait(false);

            for (int index = 0; index < lines.Count; index++)
            {
                CreateStockOutLinePayload line = lines[index];
                ValidateLinePayload(line, index, items);
                await ValidateLocationAsync(
                    line.LocationId, warehouse.Id, $"lines[{index}].locationId", token)
                    .ConfigureAwait(false);
            }

            string orgCode = await ResolveOrgCodeAsync(warehouse.OrgId, token).ConfigureAwait(false);
            string taskNo = await _numberGenerator
                .NextAsync(WmsDocumentPrefixes.StockOut, orgCode, token).ConfigureAwait(false);

            StockOutTask entity =
                new(TenantId, taskNo, sourceType, sourceDocNo, warehouse.Id, remark);
            for (int index = 0; index < lines.Count; index++)
            {
                CreateStockOutLinePayload line = lines[index];
                entity.AddLine(new StockOutTaskLine(
                    TenantId,
                    index + 1,
                    line.ItemId,
                    line.ApplyQty,
                    line.LocationId,
                    Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo")));
            }

            await _repo.AddStockOutTaskAsync(entity, token).ConfigureAwait(false);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return entity;
        }, ct).ConfigureAwait(false);

        return await GetAsync(task.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StockOutTaskDetailRow> PickAsync(
        long id, PickStockOutTaskPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        StockOutTask task = await _repo.ExecuteInTransactionAsync(async token =>
        {
            StockOutTask entity = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);
            if (entity.Status != StockOutTaskStatus.Pending)
            {
                throw new ConflictException("仅待作业的出库单可拣货", ErrorInvalidState);
            }

            IReadOnlyList<PickStockOutLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "拣货入参不能为空");
            }

            Dictionary<long, StockOutTaskLine> tracked =
                entity.Lines.ToDictionary(static line => line.Id);
            IReadOnlyDictionary<long, StockItemQueryRow> items = await LoadItemsAsync(
                [.. entity.Lines.Select(static line => line.ItemId)], token).ConfigureAwait(false);

            Dictionary<long, PickStockOutLinePayload> inputs = [];
            for (int index = 0; index < lines.Count; index++)
            {
                PickStockOutLinePayload line = lines[index];
                if (!tracked.TryGetValue(line.LineId, out StockOutTaskLine? taskLine))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细不属于该出库单");
                }

                if (!inputs.TryAdd(line.LineId, line))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细行重复提交");
                }

                ValidatePickLine(line, taskLine, index, items);
                await ValidateLocationAsync(
                    line.LocationId, entity.WarehouseId, $"lines[{index}].locationId", token)
                    .ConfigureAwait(false);
            }

            foreach ((long lineId, PickStockOutLinePayload input) in inputs)
            {
                tracked[lineId].RecordPick(
                    input.PickQty,
                    input.LocationId,
                    Normalize(input.BatchNo, MaxBatchNoLength, $"lines[{lineId}].batchNo"));
            }

            entity.StartPicking(CurrentUserId);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return entity;
        }, ct).ConfigureAwait(false);

        IReadOnlyList<StockOutFefoSuggestionRow> suggestions =
            await BuildFefoSuggestionsAsync(task, ct).ConfigureAwait(false);
        return MapDetail(task, suggestions);
    }

    /// <inheritdoc />
    public async Task<StockOutTaskDetailRow> CheckAsync(
        long id, CheckStockOutTaskPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        StockOutTask task = await _repo.ExecuteInTransactionAsync(async token =>
        {
            StockOutTask entity = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);
            if (entity.Status != StockOutTaskStatus.Picking)
            {
                throw new ConflictException("仅拣货中的出库单可复核", ErrorInvalidState);
            }

            string handoverTo = RequireHandover(payload.HandoverTo);
            IReadOnlyList<CheckStockOutLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0 || lines.Count != entity.Lines.Count)
            {
                throw new ValidationException("lines", "复核须覆盖该单全部明细");
            }

            Dictionary<long, StockOutTaskLine> tracked =
                entity.Lines.ToDictionary(static line => line.Id);
            HashSet<long> seen = [];
            for (int index = 0; index < lines.Count; index++)
            {
                CheckStockOutLinePayload line = lines[index];
                if (!tracked.TryGetValue(line.LineId, out StockOutTaskLine? taskLine))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细不属于该出库单");
                }

                if (!seen.Add(line.LineId))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细行重复提交");
                }

                if (line.CheckQty < 0)
                {
                    throw new ValidationException($"lines[{index}].checkQty", "复核数量不能为负数");
                }

                if (line.CheckQty != taskLine.PickQty)
                {
                    throw new ConflictException("复核数量与拣货数量不一致", ErrorCheckMismatch);
                }
            }

            foreach (StockOutTaskLine taskLine in tracked.Values)
            {
                taskLine.RecordCheck(taskLine.PickQty);
            }

            entity.Check(CurrentUserId, handoverTo);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return entity;
        }, ct).ConfigureAwait(false);

        IReadOnlyList<StockOutFefoSuggestionRow> suggestions =
            await BuildFefoSuggestionsAsync(task, ct).ConfigureAwait(false);
        return MapDetail(task, suggestions);
    }

    /// <inheritdoc />
    public async Task<StockOutCompleteResultRow> CompleteAsync(long id, CancellationToken ct)
    {
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        (StockOutTaskRow Row, decimal TotalAmount, bool ApprovalRequired, bool Completed) result =
            await _repo.ExecuteInTransactionAsync(async token =>
            {
                StockOutTask task = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);
                if (task.Status != StockOutTaskStatus.PendingCheck)
                {
                    throw new ConflictException("仅待复核的出库单可完成", ErrorInvalidState);
                }

                decimal totalAmount = await CalculateTotalAmountAsync(task, token).ConfigureAwait(false);
                task.RecordTotalAmount(totalAmount);

                decimal threshold = await ReadLargeThresholdAsync(token).ConfigureAwait(false);
                if (totalAmount > threshold)
                {
                    if (await _repo.FindPendingApprovalRequestAsync(
                            OutboundLargeBizType, task.Id, token).ConfigureAwait(false) is not null)
                    {
                        throw new ConflictException("该出库单已提交大额审批，请等待审批结果", ErrorApprovalPending);
                    }

                    WmsApprovalRequest request = new(
                        TenantId, OutboundLargeBizType, task.Id,
                        JsonSerializer.Serialize(task.Id, QlxJsonDefaults.PreserveNull));
                    await _repo.AddApprovalRequestAsync(request, token).ConfigureAwait(false);
                    _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);

                    string instanceId = await _approvalService.StartApprovalAsync(
                        new StartApprovalRequest(
                            ApprovalDefinitionCode: OutboundLargeFlowCode,
                            BizType: OutboundLargeBizType,
                            BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                            BizNo: task.TaskNo,
                            InitiatorUserId: ChangedBy,
                            Title: $"出库作业大额审批：{task.TaskNo}",
                            Description: null,
                            BizData: new Dictionary<string, object?>
                            {
                                ["stockOutTaskId"] = task.Id,
                                ["warehouseId"] = task.WarehouseId,
                                ["totalAmount"] = totalAmount,
                            }),
                        token).ConfigureAwait(false);

                    request.BindInstance(instanceId);
                    _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                    return (Map(task), totalAmount, true, false);
                }

                List<PostStockTxnRequest> requests = BuildRequests(task, occurredAt);
                if (requests.Count == 0)
                {
                    throw new ValidationException("lines", "至少一行拣货数量须大于 0");
                }

                task.Complete();
                _ = await _queue.EnqueueAsync(requests, token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(task), totalAmount, false, true);
            }, ct).ConfigureAwait(false);

        // 事务已提交：持久发布完成事件（审批挂起不发布）
        if (result.Completed)
        {
            await _eventBus.PublishDurableAsync(
                new StockOutCompletedEvent
                {
                    TaskId = result.Row.Id,
                    TaskNo = result.Row.TaskNo,
                    WarehouseId = result.Row.WarehouseId,
                    SourceDocNo = result.Row.SourceDocNo,
                    TenantCode = _tenantCtx.TenantCode
                        ?? TenantId.ToString(CultureInfo.InvariantCulture),
                },
                ct).ConfigureAwait(false);
        }

        return new StockOutCompleteResultRow(result.Row, result.TotalAmount, result.ApprovalRequired);
    }

    /// <inheritdoc />
    public async Task ApplyApprovalAsync(long id, bool approved, CancellationToken ct)
    {
        (StockOutTaskRow Row, bool Completed) result = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                StockOutTask task = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);

                // 幂等：仅「待复核」可应用，重复回放 / 已取消一律短路
                if (task.Status != StockOutTaskStatus.PendingCheck)
                {
                    return (Map(task), false);
                }

                // 驳回：保持待复核，用户可修改后重新提交
                if (!approved)
                {
                    return (Map(task), false);
                }

                List<PostStockTxnRequest> requests = BuildRequests(task, DateTimeOffset.UtcNow);
                if (requests.Count == 0)
                {
                    throw new ValidationException("lines", "至少一行拣货数量须大于 0");
                }

                task.Complete();
                _ = await _queue.EnqueueAsync(requests, token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(task), true);
            }, ct).ConfigureAwait(false);

        if (result.Completed)
        {
            await _eventBus.PublishDurableAsync(
                new StockOutCompletedEvent
                {
                    TaskId = result.Row.Id,
                    TaskNo = result.Row.TaskNo,
                    WarehouseId = result.Row.WarehouseId,
                    SourceDocNo = result.Row.SourceDocNo,
                    TenantCode = _tenantCtx.TenantCode
                        ?? TenantId.ToString(CultureInfo.InvariantCulture),
                },
                ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<StockOutTaskRow> CancelAsync(long id, CancellationToken ct)
        => await _repo.ExecuteInTransactionAsync(async token =>
        {
            StockOutTask task = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);
            if (task.Status is not (StockOutTaskStatus.Pending
                or StockOutTaskStatus.Picking
                or StockOutTaskStatus.PendingCheck))
            {
                throw new ConflictException("仅待作业、拣货中或待复核的出库单可取消", ErrorInvalidState);
            }

            task.Cancel();
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(task);
        }, ct).ConfigureAwait(false);

    // ============ FEFO 与金额估算 ============

    private async Task<IReadOnlyList<StockOutFefoSuggestionRow>> BuildFefoSuggestionsAsync(
        StockOutTask task, CancellationToken ct)
    {
        Dictionary<long, IReadOnlyList<StockBatchRow>> batchesByItem = [];
        List<StockOutFefoSuggestionRow> rows = [];
        foreach (StockOutTaskLine line in task.Lines.OrderBy(static line => line.LineNo))
        {
            if (!batchesByItem.TryGetValue(line.ItemId, out IReadOnlyList<StockBatchRow>? batches))
            {
                batches = await _stockQuery
                    .ListBatchesAsync(line.ItemId, task.WarehouseId, ct).ConfigureAwait(false);
                batchesByItem[line.ItemId] = batches;
            }

            foreach (StockBatchRow batch in batches)
            {
                rows.Add(new StockOutFefoSuggestionRow(
                    line.LineNo, line.ItemId, batch.BatchNo, batch.ExpiryDate, batch.Qty));
            }
        }

        return rows;
    }

    private async Task<decimal> CalculateTotalAmountAsync(StockOutTask task, CancellationToken ct)
    {
        decimal total = 0m;
        foreach (StockOutTaskLine line in task.Lines)
        {
            if (line.PickQty <= 0)
            {
                continue;
            }

            StockBalanceRow? balance = await _txnCommand
                .FindBalanceAsync(line.ItemId, task.WarehouseId, ct).ConfigureAwait(false);
            decimal avgCost = balance?.AvgCost ?? 0m;
            total += line.PickQty * avgCost;
        }

        return Math.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    private static List<PostStockTxnRequest> BuildRequests(
        StockOutTask task, DateTimeOffset occurredAt)
    {
        List<PostStockTxnRequest> requests = new(task.Lines.Count);
        foreach (StockOutTaskLine line in task.Lines.OrderBy(static line => line.LineNo))
        {
            if (line.PickQty <= 0)
            {
                continue;
            }

            requests.Add(new PostStockTxnRequest(
                MapSourceType(task.SourceType),
                task.TaskNo,
                line.LineNo,
                line.ItemId,
                task.WarehouseId,
                line.BatchNo,
                StockDirection.Out,
                line.PickQty,
                0m,
                null,
                $"wms:ck:{task.TaskNo}:{line.LineNo}",
                occurredAt));
        }

        return requests;
    }

    private async Task<decimal> ReadLargeThresholdAsync(CancellationToken ct)
    {
        string raw = await _parameters
            .GetStringAsync(LargeThresholdParamKey, DefaultLargeThresholdText, ct)
            .ConfigureAwait(false);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : 50000m;
    }

    // ============ 校验与内部实现 ============

    private async Task<StockOutTask> FindTaskOrThrowAsync(long id, CancellationToken ct)
        => await _repo.FindStockOutTaskAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("出库作业", id.ToString(CultureInfo.InvariantCulture));

    private async Task<Warehouse> FindEnabledWarehouseAsync(long warehouseId, CancellationToken ct)
    {
        if (warehouseId <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }

        Warehouse warehouse = await _repo.FindWarehouseAsync(warehouseId, ct).ConfigureAwait(false)
            ?? throw new ValidationException("warehouseId", "仓库不存在");
        if (warehouse.Status != WarehouseStatus.Enabled)
        {
            throw new ValidationException("warehouseId", "仓库已停用");
        }

        return warehouse;
    }

    private async Task<IReadOnlyDictionary<long, StockItemQueryRow>> LoadItemsAsync(
        IReadOnlyList<long> itemIds, CancellationToken ct)
    {
        List<long> distinct = [.. itemIds.Distinct()];
        if (distinct.Count == 0)
        {
            return new Dictionary<long, StockItemQueryRow>();
        }

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
        CreateStockOutLinePayload line,
        int index,
        IReadOnlyDictionary<long, StockItemQueryRow> items)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.ItemId <= 0)
        {
            throw new ValidationException($"lines[{index}].itemId", "商品 Id 非法");
        }

        if (line.ApplyQty <= 0)
        {
            throw new ValidationException($"lines[{index}].applyQty", "申请数量必须大于 0");
        }

        _ = Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo");
        if (line.LocationId is <= 0)
        {
            throw new ValidationException($"lines[{index}].locationId", "库位 Id 非法");
        }

        if (items[line.ItemId].Status != 1)
        {
            throw new ValidationException($"lines[{index}].itemId", "商品已停用");
        }
    }

    private static void ValidatePickLine(
        PickStockOutLinePayload line,
        StockOutTaskLine taskLine,
        int index,
        IReadOnlyDictionary<long, StockItemQueryRow> items)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(taskLine);
        if (line.PickQty < 0)
        {
            throw new ValidationException($"lines[{index}].pickQty", "实拣数量不能为负数");
        }

        if (line.PickQty > taskLine.ApplyQty)
        {
            throw new ValidationException($"lines[{index}].pickQty", "实拣数量不能超过申请数量");
        }

        if (line.LocationId is <= 0)
        {
            throw new ValidationException($"lines[{index}].locationId", "库位 Id 非法");
        }

        string? batchNo = Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo");
        if (line.PickQty > 0 && items[taskLine.ItemId].BatchEnabled && batchNo is null)
        {
            throw new ValidationException($"lines[{index}].batchNo", "启用批次的商品必须填写批次号");
        }
    }

    private async Task ValidateLocationAsync(
        long? locationId, long warehouseId, string fieldName, CancellationToken ct)
    {
        if (locationId is null)
        {
            return;
        }

        if (locationId <= 0)
        {
            throw new ValidationException(fieldName, "库位 Id 非法");
        }

        WarehouseLocation location = await _repo
            .FindLocationAsync(locationId.Value, ct).ConfigureAwait(false)
            ?? throw new ValidationException(fieldName, "库位不存在");
        if (location.WarehouseId != warehouseId)
        {
            throw new ValidationException(fieldName, "库位不属于该仓库");
        }

        if (location.Status != WarehouseStatus.Enabled)
        {
            throw new ValidationException(fieldName, "库位已停用");
        }
    }

    private async Task<string> ResolveOrgCodeAsync(long? orgId, CancellationToken ct)
    {
        if (orgId is null or <= 0)
        {
            return "SYS";
        }

        OrgQueryRow? org = await _orgQuery.FindAsync(orgId.Value, ct).ConfigureAwait(false);
        return org?.Code ?? "SYS";
    }

    private static int ValidateSourceType(int sourceType)
        => sourceType is >= 1 and <= 5
            ? sourceType
            : throw new ValidationException("sourceType", "来源类型必须为 1..5");

    private static void ValidateSourceTypeFilter(int? sourceType)
    {
        if (sourceType is < 1 or > 5)
        {
            throw new ValidationException("sourceType", "来源类型必须为 1..5");
        }
    }

    private static void ValidateStatusFilter(int? status)
    {
        if (status is < 1 or > 5)
        {
            throw new ValidationException("status", "状态必须为 1..5");
        }
    }

    private static StockSourceType MapSourceType(int sourceType) => sourceType switch
    {
        1 => StockSourceType.SaleOut,
        2 => StockSourceType.PurchaseReturn,
        3 => StockSourceType.TransferOut,
        4 => StockSourceType.Loss,
        5 => StockSourceType.Use,
        _ => throw new ValidationException("sourceType", "来源类型必须为 1..5"),
    };

    private static string RequireSourceDocNo(string? sourceDocNo)
    {
        if (string.IsNullOrWhiteSpace(sourceDocNo))
        {
            throw new ValidationException("sourceDocNo", "来源单据号不能为空");
        }

        string trimmed = sourceDocNo.Trim();
        return trimmed.Length <= MaxSourceDocNoLength
            ? trimmed
            : throw new ValidationException("sourceDocNo", "来源单据号不能超过 32 个字符");
    }

    private static string RequireHandover(string? handoverTo)
    {
        if (string.IsNullOrWhiteSpace(handoverTo))
        {
            throw new ValidationException("handoverTo", "交接人不能为空");
        }

        string trimmed = handoverTo.Trim();
        return trimmed.Length <= MaxHandoverLength
            ? trimmed
            : throw new ValidationException("handoverTo", "交接人不能超过 64 个字符");
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

    private static StockOutTaskRow Map(StockOutTask entity)
        => new(entity.Id, entity.TaskNo, entity.SourceType, entity.SourceDocNo,
            entity.WarehouseId, (int)entity.Status, entity.PickerId, entity.CheckerId,
            entity.HandoverTo, entity.TotalAmount, entity.Remark, entity.CreatedAt);

    private static StockOutTaskLineRow Map(StockOutTaskLine entity)
        => new(entity.Id, entity.LineNo, entity.ItemId, entity.ApplyQty, entity.PickQty,
            entity.CheckQty, entity.LocationId, entity.BatchNo);

    private static StockOutTaskDetailRow MapDetail(
        StockOutTask entity, IReadOnlyList<StockOutFefoSuggestionRow> suggestions)
        => new(Map(entity),
            [.. entity.Lines.OrderBy(static line => line.LineNo).Select(Map)],
            suggestions);
}
