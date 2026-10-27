/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:30:00 +08:00 中国标准时间 星期一
 * 描述：入库作业服务实现——无单禁入 + 仓库/批次/库位校验（V-1）+ 单号后端生成（N-1）+
 *       完成动作同事务入队 Outbox（A-17）+ 事务后持久发布完成事件
 */

using System.Globalization;

using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Wms.Domain.Inbound;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 入库作业服务。完成动作在仓储事务回调内写库存命令 Outbox（与作业状态同事务，A-17），
/// 事务提交后才持久发布 <see cref="Qlx.Abstractions.Events.Wms.StockInCompletedEvent"/>。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（WL-RK）。</param>
/// <param name="orgQuery">跨模块组织只读查询（解析单号中的组织码）。</param>
/// <param name="itemQuery">跨模块商品只读查询（批次属性与参考成本，A-14）。</param>
/// <param name="queue">库存命令队列（同事务入队）。</param>
/// <param name="eventBus">事件总线（事务提交后持久发布）。</param>
/// <param name="userContext">当前用户上下文（经办人）。</param>
internal sealed class StockInService(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IOrgQueryService orgQuery,
    IItemQueryService itemQuery,
    IStockCommandQueue queue,
    IEventBus eventBus,
    ICurrentUserContext userContext) : IStockInService
{
    /// <summary>状态机非法流转错误码。</summary>
    internal const string ErrorInvalidState = "WMS_INVALID_STATE";

    private const int MaxSourceDocNoLength = 32;
    private const int MaxBatchNoLength = 64;
    private const int MaxRemarkLength = 256;

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string? CurrentUserId => userContext.UserId;

    // ============ 查询 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockInTaskRow>> ListAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct)
    {
        ValidateStatusFilter(status);
        ValidateSourceTypeFilter(sourceType);
        if (warehouseId is <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }

        IReadOnlyList<StockInTask> tasks = await repo
            .ListStockInTasksAsync(status, warehouseId, sourceType, ct).ConfigureAwait(false);
        return [.. tasks.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<StockInTaskDetailRow> GetAsync(long id, CancellationToken ct)
    {
        StockInTask task = await FindTaskOrThrowAsync(id, ct).ConfigureAwait(false);
        return MapDetail(task);
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<StockInTaskDetailRow> CreateAsync(
        CreateStockInTaskPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return await repo.ExecuteInTransactionAsync(async token =>
        {
            int sourceType = ValidateSourceType(payload.SourceType);
            string sourceDocNo = RequireSourceDocNo(payload.SourceDocNo);
            string? remark = Normalize(payload.Remark, MaxRemarkLength, "remark");
            Warehouse warehouse = await FindEnabledWarehouseAsync(payload.WarehouseId, token)
                .ConfigureAwait(false);

            IReadOnlyList<CreateStockInLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "入库明细不能为空");
            }

            IReadOnlyDictionary<long, StockItemQueryRow> items =
                await LoadItemsAsync([.. lines.Select(static line => line.ItemId)], token)
                    .ConfigureAwait(false);

            for (int index = 0; index < lines.Count; index++)
            {
                CreateStockInLinePayload line = lines[index];
                ValidateLinePayload(line, index, items);
                await ValidateLocationAsync(
                    line.LocationId, warehouse.Id, $"lines[{index}].locationId", token)
                    .ConfigureAwait(false);
            }

            string orgCode = await ResolveOrgCodeAsync(warehouse.OrgId, token).ConfigureAwait(false);
            string taskNo = await numberGenerator
                .NextAsync(WmsDocumentPrefixes.StockIn, orgCode, token).ConfigureAwait(false);

            StockInTask task = new(TenantId, taskNo, sourceType, sourceDocNo, warehouse.Id, remark);
            for (int index = 0; index < lines.Count; index++)
            {
                CreateStockInLinePayload line = lines[index];
                task.AddLine(new StockInTaskLine(
                    TenantId,
                    index + 1,
                    line.ItemId,
                    line.ExpectQty,
                    line.LocationId,
                    Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo"),
                    line.UnitCost));
            }

            await repo.AddStockInTaskAsync(task, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapDetail(task);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StockInTaskRow> StartAsync(long id, CancellationToken ct)
        => await repo.ExecuteInTransactionAsync(async token =>
        {
            StockInTask task = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);
            if (task.Status != StockInTaskStatus.Pending)
            {
                throw new ConflictException("仅待作业的入库单可开始作业", ErrorInvalidState);
            }

            task.Start();
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(task);
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<StockInTaskDetailRow> CompleteAsync(
        long id, CompleteStockInTaskPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        StockInTaskDetailRow detail = await repo.ExecuteInTransactionAsync(async token =>
        {
            StockInTask task = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);
            if (task.Status != StockInTaskStatus.InProgress)
            {
                throw new ConflictException("仅作业中的入库单可完成", ErrorInvalidState);
            }

            IReadOnlyList<CompleteStockInLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "完成入参不能为空");
            }

            Dictionary<long, StockInTaskLine> tracked = task.Lines.ToDictionary(static line => line.Id);
            IReadOnlyDictionary<long, StockItemQueryRow> items = await LoadItemsAsync(
                [.. task.Lines.Select(static line => line.ItemId)], token).ConfigureAwait(false);

            Dictionary<long, CompleteStockInLinePayload> inputs = [];
            for (int index = 0; index < lines.Count; index++)
            {
                CompleteStockInLinePayload line = lines[index];
                if (!tracked.TryGetValue(line.LineId, out StockInTaskLine? entity))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细不属于该入库单");
                }

                if (!inputs.TryAdd(line.LineId, line))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细行重复提交");
                }

                ValidateCompleteLine(line, entity, index, items);
            }

            foreach ((long lineId, CompleteStockInLinePayload input) in inputs)
            {
                StockInTaskLine entity = tracked[lineId];
                await ValidateLocationAsync(
                    input.LocationId, task.WarehouseId, $"lines[{lineId}].locationId", token)
                    .ConfigureAwait(false);
                entity.RecordCount(
                    input.CountQty,
                    input.ConfirmQty,
                    input.LocationId,
                    Normalize(input.BatchNo, MaxBatchNoLength, $"lines[{lineId}].batchNo"),
                    input.UnitCost);
            }

            DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
            List<PostStockTxnRequest> requests = new(task.Lines.Count);
            foreach (StockInTaskLine line in task.Lines.OrderBy(static line => line.LineNo))
            {
                if (line.ConfirmQty <= 0)
                {
                    continue;
                }

                StockItemQueryRow item = items[line.ItemId];
                decimal unitCost = line.UnitCost ?? item.RefCostPrice ?? 0m;
                requests.Add(new PostStockTxnRequest(
                    MapSourceType(task.SourceType),
                    task.TaskNo,
                    line.LineNo,
                    line.ItemId,
                    task.WarehouseId,
                    line.BatchNo,
                    StockDirection.In,
                    line.ConfirmQty,
                    unitCost,
                    CurrentUserId,
                    $"wms:rk:{task.TaskNo}:{line.LineNo}",
                    occurredAt));
            }

            if (requests.Count == 0)
            {
                throw new ValidationException("lines", "至少一行确认数量须大于 0");
            }

            task.Complete();
            _ = await queue.EnqueueAsync(requests, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapDetail(task);
        }, ct).ConfigureAwait(false);

        // 事务已提交：持久发布完成事件（EventId 由总线生成）
        await eventBus.PublishDurableAsync(
            new Qlx.Abstractions.Events.Wms.StockInCompletedEvent
            {
                TaskId = detail.Task.Id,
                TaskNo = detail.Task.TaskNo,
                WarehouseId = detail.Task.WarehouseId,
                SourceDocNo = detail.Task.SourceDocNo,
                TenantCode = tenantCtx.TenantCode ?? TenantId.ToString(CultureInfo.InvariantCulture),
            },
            ct).ConfigureAwait(false);

        return detail;
    }

    /// <inheritdoc />
    public async Task<StockInTaskRow> CancelAsync(long id, CancellationToken ct)
        => await repo.ExecuteInTransactionAsync(async token =>
        {
            StockInTask task = await FindTaskOrThrowAsync(id, token).ConfigureAwait(false);
            if (task.Status is not (StockInTaskStatus.Pending or StockInTaskStatus.InProgress))
            {
                throw new ConflictException("仅待作业或作业中的入库单可取消", ErrorInvalidState);
            }

            task.Cancel();
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(task);
        }, ct).ConfigureAwait(false);

    // ============ 校验与内部实现 ============

    private async Task<StockInTask> FindTaskOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindStockInTaskAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("入库作业", id.ToString(CultureInfo.InvariantCulture));

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

    private async Task<IReadOnlyDictionary<long, StockItemQueryRow>> LoadItemsAsync(
        IReadOnlyList<long> itemIds, CancellationToken ct)
    {
        List<long> distinct = [.. itemIds.Distinct()];
        if (distinct.Count == 0)
        {
            return new Dictionary<long, StockItemQueryRow>();
        }

        IReadOnlyList<StockItemQueryRow> items = await itemQuery
            .FindByIdsAsync(distinct, ct).ConfigureAwait(false);
        Dictionary<long, StockItemQueryRow> map = items.ToDictionary(static item => item.Id);
        if (map.Count != distinct.Count)
        {
            throw new ValidationException("lines", "存在不存在的商品");
        }

        return map;
    }

    private static void ValidateLinePayload(
        CreateStockInLinePayload line, int index, IReadOnlyDictionary<long, StockItemQueryRow> items)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.ItemId <= 0)
        {
            throw new ValidationException($"lines[{index}].itemId", "商品 Id 非法");
        }

        if (line.ExpectQty <= 0)
        {
            throw new ValidationException($"lines[{index}].expectQty", "预期数量必须大于 0");
        }

        if (line.UnitCost is < 0)
        {
            throw new ValidationException($"lines[{index}].unitCost", "入库单价不能为负数");
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

    private static void ValidateCompleteLine(
        CompleteStockInLinePayload line,
        StockInTaskLine entity,
        int index,
        IReadOnlyDictionary<long, StockItemQueryRow> items)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(entity);
        if (line.CountQty < 0)
        {
            throw new ValidationException($"lines[{index}].countQty", "清点数量不能为负数");
        }

        if (line.ConfirmQty < 0)
        {
            throw new ValidationException($"lines[{index}].confirmQty", "确认数量不能为负数");
        }

        if (line.ConfirmQty > line.CountQty)
        {
            throw new ValidationException($"lines[{index}].confirmQty", "确认数量不能超过清点数量");
        }

        if (line.UnitCost is < 0)
        {
            throw new ValidationException($"lines[{index}].unitCost", "入库单价不能为负数");
        }

        if (line.LocationId is <= 0)
        {
            throw new ValidationException($"lines[{index}].locationId", "库位 Id 非法");
        }

        string? batchNo = Normalize(line.BatchNo, MaxBatchNoLength, $"lines[{index}].batchNo");
        if (items[entity.ItemId].BatchEnabled && batchNo is null)
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

        WarehouseLocation location = await repo
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

        OrgQueryRow? org = await orgQuery.FindAsync(orgId.Value, ct).ConfigureAwait(false);
        return org?.Code ?? "SYS";
    }

    private static int ValidateSourceType(int sourceType)
        => sourceType is >= 1 and <= 4
            ? sourceType
            : throw new ValidationException("sourceType", "来源类型必须为 1/2/3/4");

    private static void ValidateSourceTypeFilter(int? sourceType)
    {
        if (sourceType is < 1 or > 4)
        {
            throw new ValidationException("sourceType", "来源类型必须为 1/2/3/4");
        }
    }

    private static void ValidateStatusFilter(int? status)
    {
        if (status is < 1 or > 4)
        {
            throw new ValidationException("status", "状态必须为 1/2/3/4");
        }
    }

    private static StockSourceType MapSourceType(int sourceType) => sourceType switch
    {
        1 => StockSourceType.PurchaseIn,
        2 => StockSourceType.SaleReturn,
        3 => StockSourceType.TransferIn,
        4 => StockSourceType.Gain,
        _ => throw new ValidationException("sourceType", "来源类型必须为 1/2/3/4"),
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

    private static StockInTaskRow Map(StockInTask entity)
        => new(entity.Id, entity.TaskNo, entity.SourceType, entity.SourceDocNo,
            entity.WarehouseId, (int)entity.Status, entity.Remark, entity.CreatedAt);

    private static StockInTaskLineRow Map(StockInTaskLine entity)
        => new(entity.Id, entity.LineNo, entity.ItemId, entity.ExpectQty, entity.CountQty,
            entity.ConfirmQty, entity.LocationId, entity.BatchNo, entity.UnitCost);

    private static StockInTaskDetailRow MapDetail(StockInTask entity)
        => new(Map(entity), [.. entity.Lines.OrderBy(static line => line.LineNo).Select(Map)]);
}
