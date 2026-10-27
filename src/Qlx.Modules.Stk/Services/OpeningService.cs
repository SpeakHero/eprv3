/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：期初建账服务实现——草稿 CRUD + 期初单号后端生成（SP-QC，N-1）+ 提交过账审批 + 每仓库唯一已过账 +
 *       审批通过后经唯一台账入口写 SourceType=Opening 流水（A-5）+ 应用幂等 + 审计 +
 *       事务提交成功后经 Outbox 持久发布 OpeningPostedEvent（幂等重放不重复发布）
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Events.Stk;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Repositories;

namespace Qlx.Modules.Stk.Services;

/// <summary>
/// 期初建账服务。期初单号经 <see cref="IDocumentNumberGenerator"/>（<c>SP-QC</c>）后端生成，前端不传码（N-1）；
/// 过账只能经 <see cref="IStockTxnCommandService"/> 写流水（A-5），台账不缓存（A-9）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Stk 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（期初单号）。</param>
/// <param name="txnCommand">库存写入命令契约（唯一台账入口）。</param>
/// <param name="approvalService">审批中心契约（发起过账审批）。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
/// <param name="eventBus">事件总线（过账事务提交后 Outbox 持久发布期初过账事件）。</param>
internal sealed class OpeningService(
    ITenantContext tenantCtx,
    IStkTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IStockTxnCommandService txnCommand,
    IApprovalService approvalService,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext,
    IEventBus eventBus) : IOpeningService
{
    /// <summary>期初过账审批业务类型（事件消费者按 <see cref="OpeningBizTypePrefix"/> 路由）。</summary>
    internal const string OpeningBizType = "stk.opening.post";

    /// <summary>Stk 期初审批业务类型统一前缀。</summary>
    internal const string OpeningBizTypePrefix = "stk.opening.";

    /// <summary>期初过账审批流程定义码。</summary>
    internal const string OpeningFlowCode = "flow.stk.opening.post";

    /// <summary>期初单号前缀（spec A-12 偏差登记号段）。</summary>
    internal const string OpeningDocPrefix = "SP-QC";

    /// <summary>单据号组织码（期初为系统级动作，固定 SYS）。</summary>
    internal const string OrgCode = "SYS";

    /// <summary>同仓库已存在已过账期初错误码。</summary>
    internal const string ErrorAlreadyPosted = "STK_OPENING_ALREADY_POSTED";

    /// <summary>台账批量写入失败兜底错误码。</summary>
    internal const string ErrorLedgerFailed = "STK_OPENING_LEDGER_FAILED";

    private const int BatchNoMaxLength = 32;
    private const int RemarkMaxLength = 256;

    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IStkTenantRepository _repo = repo;
    private readonly IDocumentNumberGenerator _numberGenerator = numberGenerator;
    private readonly IStockTxnCommandService _txnCommand = txnCommand;
    private readonly IApprovalService _approvalService = approvalService;
    private readonly IAuditService _audit = audit;
    private readonly ISnowflakeIdGenerator _snowflake = snowflake;
    private readonly ICurrentUserContext _userContext = userContext;
    private readonly IEventBus _eventBus = eventBus;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => _userContext.UserId ?? "system";

    // ============ 读路径（台账/期初不缓存，A-9）============

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpeningBatchRow>> ListAsync(
        int? status, long? warehouseId, CancellationToken ct)
    {
        _ = TenantId;
        OpeningBatchStatus? filter = status is null ? null : (OpeningBatchStatus)status.Value;
        IReadOnlyList<OpeningBatch> batches = await _repo
            .ListOpeningBatchesAsync(filter, warehouseId, ct).ConfigureAwait(false);
        return [.. batches.Select(static batch => Map(batch))];
    }

    /// <inheritdoc />
    public async Task<OpeningBatchDetailRow> GetAsync(long id, CancellationToken ct)
    {
        _ = TenantId;
        return await LoadDetailAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpeningWarehouseSummaryRow>> GetWarehouseSummaryAsync(CancellationToken ct)
    {
        _ = TenantId;
        IReadOnlyList<OpeningBatch> posted = await _repo
            .ListPostedOpeningBatchesAsync(ct).ConfigureAwait(false);
        if (posted.Count == 0)
        {
            return [];
        }

        Dictionary<long, long> warehouseByBatchId =
            posted.ToDictionary(static batch => batch.Id, static batch => batch.WarehouseId);
        IReadOnlyList<long> batchIds = [.. posted.Select(static batch => batch.Id)];
        IReadOnlyList<OpeningItem> items = await _repo
            .ListOpeningItemsByBatchIdsAsync(batchIds, ct).ConfigureAwait(false);

        // SQLite 不支持 decimal 聚合下推：明细取出后内存汇总（扁平原始值，B-2）
        return
        [
            .. items
                .Where(item => warehouseByBatchId.ContainsKey(item.OpeningBatchId))
                .GroupBy(item => warehouseByBatchId[item.OpeningBatchId])
                .Select(group => new OpeningWarehouseSummaryRow(
                    group.Key,
                    StockMath.Round(group.Sum(static item => item.Qty), StockMath.QtyDigits),
                    StockMath.Round(group.Sum(static item => item.Amount), StockMath.AmountDigits)))
                .OrderBy(static row => row.WarehouseId),
        ];
    }

    // ============ 草稿写路径（事务 → 审计）============

    /// <inheritdoc />
    public async Task<OpeningBatchDetailRow> CreateDraftAsync(
        CreateOpeningPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        OpeningBatch batch = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                if (payload.WarehouseId <= 0)
                {
                    throw new ValidationException("warehouseId", "仓库不能为空");
                }

                string? remark = NormalizeRemark(payload.Remark);
                IReadOnlyList<OpeningItemPayload> lines =
                    await ValidateLinesAsync(payload.Lines, token).ConfigureAwait(false);

                string batchNo = await _numberGenerator
                    .NextAsync(OpeningDocPrefix, OrgCode, token).ConfigureAwait(false);
                OpeningBatch entity = new(TenantId, batchNo, payload.WarehouseId, remark);
                await _repo.AddOpeningBatchAsync(entity, token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);

                await _repo.AddOpeningItemsAsync(BuildItems(entity.Id, lines), token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return entity;
            },
            ct).ConfigureAwait(false);

        OpeningBatchRow row = Map(batch);
        await CaptureAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        return await LoadDetailAsync(batch.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OpeningBatchDetailRow> UpdateDraftAsync(
        long id, UpdateOpeningPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (OpeningBatchRow Row, string OldJson) result = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                OpeningBatch batch = await FindTrackedOrThrowAsync(id, token).ConfigureAwait(false);
                batch.EnsureDraft();
                if (payload.WarehouseId <= 0)
                {
                    throw new ValidationException("warehouseId", "仓库不能为空");
                }

                string? remark = NormalizeRemark(payload.Remark);
                IReadOnlyList<OpeningItemPayload> lines =
                    await ValidateLinesAsync(payload.Lines, token).ConfigureAwait(false);

                string oldJson = Serialize(Map(batch));
                batch.UpdateDraft(payload.WarehouseId, remark);
                await _repo.ReplaceOpeningItemsAsync(
                    batch.Id, BuildItems(batch.Id, lines), token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(batch), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        return await LoadDetailAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteDraftAsync(long id, CancellationToken ct)
    {
        (OpeningBatchRow Row, string OldJson) result = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                OpeningBatch batch = await FindTrackedOrThrowAsync(id, token).ConfigureAwait(false);
                batch.EnsureDraft();
                string oldJson = Serialize(Map(batch));

                await _repo.ReplaceOpeningItemsAsync(batch.Id, [], token).ConfigureAwait(false);
                await _repo.RemoveOpeningBatchAsync(batch, token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(batch), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("DELETE", result.Row, result.OldJson, ct).ConfigureAwait(false);
    }

    // ============ 提交过账审批 ============

    /// <inheritdoc />
    public async Task<StkApprovalRequestRow> SubmitAsync(long id, CancellationToken ct)
    {
        OpeningBatch batch = await FindTrackedOrThrowAsync(id, ct).ConfigureAwait(false);
        batch.EnsureDraft();

        // 过账前置（spec §6 / 一次性锁定）：同仓库已存在已过账期初即拒绝
        if (await _repo.HasPostedOpeningAsync(batch.WarehouseId, ct).ConfigureAwait(false))
        {
            throw new ConflictException("该仓库已存在已过账期初", ErrorAlreadyPosted);
        }

        IReadOnlyList<OpeningItem> items = await _repo.ListOpeningItemsAsync(id, ct).ConfigureAwait(false);
        if (items.Count == 0)
        {
            throw new ValidationException("lines", "期初明细不能为空");
        }

        StkApprovalRequest request = new(TenantId, OpeningBizType, Serialize(batch.Id));
        await _repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await _approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: OpeningFlowCode,
                BizType: OpeningBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: batch.BatchNo,
                InitiatorUserId: ChangedBy,
                Title: $"期初过账申请：{batch.BatchNo}",
                Description: null,
                BizData: new Dictionary<string, object?>
                {
                    ["openingBatchId"] = batch.Id,
                    ["warehouseId"] = batch.WarehouseId,
                }),
            ct).ConfigureAwait(false);

        batch.Submit();
        request.BindInstance(instanceId);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    // ============ 审批完成事件应用（幂等）============

    /// <inheritdoc />
    public async Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (string.IsNullOrWhiteSpace(completed.BizType)
            || !completed.BizType.StartsWith(OpeningBizTypePrefix, StringComparison.Ordinal))
        {
            return;
        }

        StkApprovalRequest? request = await _repo
            .FindApprovalRequestByInstanceAsync(completed.ApprovalInstanceId, ct).ConfigureAwait(false);
        if (request is null || !request.IsPending)
        {
            // 幂等：同一 ApprovalInstanceId 重复事件 / 非待审批请求直接返回
            return;
        }

        if (completed.FinalStatus != ApprovalStatus.Approved)
        {
            request.MarkRejected();
            long rejectedBatchId = DeserializePayload(request.PayloadJson, request.Id);
            OpeningBatch? rejectedBatch = await _repo
                .FindOpeningBatchTrackedAsync(rejectedBatchId, ct).ConfigureAwait(false);
            if (rejectedBatch is not null && rejectedBatch.Status == OpeningBatchStatus.Pending)
            {
                rejectedBatch.Reject();
            }

            _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        string approvedBy = string.IsNullOrWhiteSpace(completed.InitiatorUserId)
            ? ChangedBy
            : completed.InitiatorUserId;
        DateTimeOffset postedAt = ResolveCompletedAt(completed);

        (OpeningBatchRow Row, string? OldJson, bool PostedNow, decimal TotalAmount) applied =
            await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                long batchId = DeserializePayload(request.PayloadJson, request.Id);
                OpeningBatch batch = await _repo.FindOpeningBatchTrackedAsync(batchId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"期初批次不存在：batchId={batchId}。");

                if (batch.Status == OpeningBatchStatus.Posted)
                {
                    // 幂等兜底：批次已过账则不再重复写流水，也不再重复发布事件
                    return (Map(batch), (string?)null, false, 0m);
                }

                if (await _repo.HasPostedOpeningAsync(batch.WarehouseId, token).ConfigureAwait(false))
                {
                    throw new ConflictException("该仓库已存在已过账期初", ErrorAlreadyPosted);
                }

                IReadOnlyList<OpeningItem> items = await _repo
                    .ListOpeningItemsAsync(batch.Id, token).ConfigureAwait(false);
                if (items.Count == 0)
                {
                    throw new ValidationException("lines", "期初明细不能为空");
                }

                // SQLite 不支持 decimal 聚合下推：明细取出后内存汇总（事件载荷用扁平原始金额）
                decimal totalAmount = StockMath.Round(
                    items.Sum(static item => item.Amount), StockMath.AmountDigits);

                string? oldJson = Serialize(Map(batch));
                batch.MarkPosted(postedAt);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);

                await PostLedgerAsync(batch, items, approvedBy, postedAt, token).ConfigureAwait(false);

                // PostBatchAsync 内部会清空跟踪器（并发重试），必须重新查询请求后再标记已应用
                StkApprovalRequest? tracked = await _repo
                    .FindApprovalRequestByInstanceAsync(completed.ApprovalInstanceId, token)
                    .ConfigureAwait(false);
                tracked?.MarkApplied(postedAt);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(batch), oldJson, true, totalAmount);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", applied.Row, applied.OldJson, ct).ConfigureAwait(false);

        if (applied.PostedNow)
        {
            // 事务已提交：Outbox 持久发布期初过账完成事件（EventId 由总线生成）
            await _eventBus.PublishDurableAsync(
                new OpeningPostedEvent
                {
                    OpeningBatchId = applied.Row.Id,
                    BatchNo = applied.Row.BatchNo,
                    WarehouseId = applied.Row.WarehouseId,
                    TotalAmount = applied.TotalAmount,
                    TenantCode = _tenantCtx.TenantCode ?? TenantId.ToString(CultureInfo.InvariantCulture),
                },
                ct).ConfigureAwait(false);
        }
    }

    // ============ 内部实现 ============

    private async Task PostLedgerAsync(
        OpeningBatch batch,
        IReadOnlyList<OpeningItem> items,
        string approvedBy,
        DateTimeOffset postedAt,
        CancellationToken ct)
    {
        List<PostStockTxnRequest> requests = new(items.Count);
        for (int index = 0; index < items.Count; index++)
        {
            OpeningItem item = items[index];
            int lineNo = index + 1;
            requests.Add(new PostStockTxnRequest(
                StockSourceType.Opening,
                batch.BatchNo,
                lineNo,
                item.ItemId,
                batch.WarehouseId,
                item.BatchNo,
                StockDirection.In,
                item.Qty,
                item.UnitCost,
                approvedBy,
                $"opening:{batch.BatchNo}:{lineNo.ToString(CultureInfo.InvariantCulture)}",
                postedAt));
        }

        IReadOnlyList<StockPostResult> results = await _txnCommand
            .PostBatchAsync(requests, ct).ConfigureAwait(false);
        StockPostResult? failed = results.FirstOrDefault(static result => !result.Success);
        if (failed is not null)
        {
            throw new ConflictException(
                $"期初过账失败：{failed.ErrorCode}", failed.ErrorCode ?? ErrorLedgerFailed);
        }
    }

    private async Task<OpeningBatchDetailRow> LoadDetailAsync(long id, CancellationToken ct)
    {
        OpeningBatch batch = await _repo.FindOpeningBatchAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("期初批次", id.ToString(CultureInfo.InvariantCulture));
        IReadOnlyList<OpeningItem> items = await _repo
            .ListOpeningItemsAsync(id, ct).ConfigureAwait(false);
        return new OpeningBatchDetailRow(Map(batch), [.. items.Select(static item => Map(item))]);
    }

    private async Task<OpeningBatch> FindTrackedOrThrowAsync(long id, CancellationToken ct)
    {
        _ = TenantId;
        return await _repo.FindOpeningBatchTrackedAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("期初批次", id.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<IReadOnlyList<OpeningItemPayload>> ValidateLinesAsync(
        IReadOnlyList<OpeningItemPayload>? lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ValidationException("lines", "期初明细不能为空");
        }

        List<OpeningItemPayload> normalized = new(lines.Count);
        HashSet<(long ItemId, string? BatchNo)> keys = [];
        foreach (OpeningItemPayload line in lines)
        {
            if (line.ItemId <= 0)
            {
                throw new ValidationException("lines", "商品不能为空");
            }

            if (line.Qty <= 0m)
            {
                throw new ValidationException("lines", "期初数量必须大于 0");
            }

            if (line.UnitCost < 0m)
            {
                throw new ValidationException("lines", "期初单价不能为负");
            }

            string? batchNo = NormalizeBatchNo(line.BatchNo);
            if (!keys.Add((line.ItemId, batchNo)))
            {
                throw new ValidationException("lines", "同一商品与批次不能重复");
            }

            normalized.Add(line with { BatchNo = batchNo });
        }

        IReadOnlyList<long> itemIds = [.. normalized.Select(static line => line.ItemId).Distinct()];
        IReadOnlyList<Item> items = await _repo.FindItemsAsync(itemIds, ct).ConfigureAwait(false);
        HashSet<long> found = [.. items.Select(static item => item.Id)];

        foreach (OpeningItemPayload line in normalized)
        {
            if (!found.Contains(line.ItemId))
            {
                throw new ValidationException("lines", "商品不存在");
            }
        }

        Dictionary<long, Item> batchEnabled = items
            .Where(static item => item.BatchEnabled)
            .ToDictionary(static item => item.Id);
        foreach (OpeningItemPayload line in normalized)
        {
            if (batchEnabled.ContainsKey(line.ItemId) && line.BatchNo is null)
            {
                throw new ValidationException("lines", "批次商品必须填写批次号");
            }
        }

        return normalized;
    }

    private List<OpeningItem> BuildItems(long openingBatchId, IReadOnlyList<OpeningItemPayload> lines)
        => [.. lines.Select(line => new OpeningItem(
            TenantId, openingBatchId, line.ItemId, line.BatchNo, line.Qty, line.UnitCost))];

    private static long DeserializePayload(string? payloadJson, long requestId)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new InvalidOperationException($"审批请求载荷为空：requestId={requestId}。");
        }

        long batchId = JsonSerializer.Deserialize<long>(payloadJson, QlxJsonDefaults.PreserveNull);
        if (batchId <= 0)
        {
            throw new InvalidOperationException($"审批请求载荷非法：requestId={requestId}。");
        }

        return batchId;
    }

    private static string? NormalizeBatchNo(string? batchNo)
    {
        if (string.IsNullOrWhiteSpace(batchNo))
        {
            return null;
        }

        string trimmed = batchNo.Trim();
        if (trimmed.Length > BatchNoMaxLength)
        {
            throw new ValidationException("lines", $"批次号长度不能超过 {BatchNoMaxLength}");
        }

        return trimmed;
    }

    private static string? NormalizeRemark(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            return null;
        }

        string trimmed = remark.Trim();
        if (trimmed.Length > RemarkMaxLength)
        {
            throw new ValidationException("remark", $"备注长度不能超过 {RemarkMaxLength}");
        }

        return trimmed;
    }

    private static DateTimeOffset ResolveCompletedAt(ApprovalCompletedEvent completed)
        => completed.CompletedAt == default ? DateTimeOffset.UtcNow : completed.CompletedAt;

    private Task CaptureAuditAsync(string operation, OpeningBatchRow row, string? oldJson, CancellationToken ct)
        => _audit.CaptureChangeAsync(
            new AuditLogEntry(
                _snowflake.NextId(),
                nameof(OpeningBatch),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, QlxJsonDefaults.PreserveNull);

    private static OpeningBatchRow Map(OpeningBatch entity)
        => new(
            entity.Id,
            entity.BatchNo,
            entity.WarehouseId,
            (int)entity.Status,
            entity.PostedAt,
            entity.Remark);

    private static OpeningItemRow Map(OpeningItem entity)
        => new(
            entity.Id,
            entity.ItemId,
            entity.BatchNo,
            entity.Qty,
            entity.UnitCost,
            entity.Amount);

    private static StkApprovalRequestRow Map(StkApprovalRequest request)
        => new(
            request.Id,
            request.ApprovalInstanceId,
            request.BizType,
            request.BizId,
            (int)request.Status);
}
