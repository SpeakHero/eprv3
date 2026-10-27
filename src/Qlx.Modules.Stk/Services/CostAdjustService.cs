/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：成本调整服务实现——草稿 CRUD + 单号后端生成（SP-CB，N-1）+ 旧均价/差额后端取值（V-1）+
 *       阈值选审批定义码 + 审批通过后红蓝字成对经唯一台账入口写 CostAdjust 流水（A-5）+ 幂等 + 审计 +
 *       事务提交成功后经 Outbox 持久发布 CostAdjustedEvent（幂等重放不重复发布）
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Events.Stk;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Modules;
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
/// 成本调整服务。旧均价与差额一律由后端从台账取值（V-1），前端不传；审批定义码按差额绝对值与
/// 参数 <c>stk.cost.adjust.threshold</c> 选择（小额 <c>flow.stk.cost.adjust</c> / 大额 <c>flow.stk.cost.adjust.large</c>）。
/// 台账写入只经 <see cref="IStockTxnCommandService"/>（A-5），台账不缓存（A-9）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Stk 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（成本调整单号）。</param>
/// <param name="txnCommand">库存写入命令契约（唯一台账入口）。</param>
/// <param name="approvalService">审批中心契约（发起成本调整审批）。</param>
/// <param name="parameters">系统参数提供方（调整额阈值）。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
/// <param name="eventBus">事件总线（生效事务提交后 Outbox 持久发布成本调整事件）。</param>
internal sealed class CostAdjustService(
    ITenantContext tenantCtx,
    IStkTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IStockTxnCommandService txnCommand,
    IApprovalService approvalService,
    ISystemParameterProvider parameters,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext,
    IEventBus eventBus) : ICostAdjustService
{
    /// <summary>成本调整审批业务类型（大小额共用，定义码区分审批链）。</summary>
    internal const string CostAdjustBizType = "stk.cost.adjust";

    /// <summary>成本调整审批业务类型前缀（事件消费者路由依据）。</summary>
    internal const string CostAdjustBizTypePrefix = "stk.cost.adjust";

    /// <summary>小额成本调整审批流程定义码。</summary>
    internal const string NormalFlowCode = "flow.stk.cost.adjust";

    /// <summary>大额成本调整审批流程定义码。</summary>
    internal const string LargeFlowCode = "flow.stk.cost.adjust.large";

    /// <summary>成本调整单号前缀。</summary>
    internal const string AdjNoPrefix = "SP-CB";

    /// <summary>单据号组织码（成本调整为系统级动作，固定 SYS）。</summary>
    internal const string OrgCode = "SYS";

    /// <summary>成本调整额阈值参数键。</summary>
    internal const string ThresholdParamKey = "stk.cost.adjust.threshold";

    /// <summary>成本调整额阈值默认值（参数表未初始化时兜底）。</summary>
    internal const string DefaultThresholdText = "10000";

    /// <summary>存在冻结库存错误码。</summary>
    internal const string ErrorLocked = "STK_COST_ADJUST_LOCKED";

    /// <summary>台账行不存在错误码。</summary>
    internal const string ErrorNoBalance = "STK_COST_ADJUST_NO_BALANCE";

    /// <summary>台账批量写入失败兜底错误码。</summary>
    internal const string ErrorLedgerFailed = "STK_COST_ADJUST_LEDGER_FAILED";

    /// <summary>来源单据行号步长（非批次行：行序 × 10 + 1/2）。</summary>
    private const int LineNoStride = 10;

    /// <summary>批次行行号步长（行序 × 1000 + 批次序 × 10 + 1/2）。</summary>
    private const int BatchLineNoStride = 1000;

    private const int RemarkMaxLength = 256;

    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IStkTenantRepository _repo = repo;
    private readonly IDocumentNumberGenerator _numberGenerator = numberGenerator;
    private readonly IStockTxnCommandService _txnCommand = txnCommand;
    private readonly IApprovalService _approvalService = approvalService;
    private readonly ISystemParameterProvider _parameters = parameters;
    private readonly IAuditService _audit = audit;
    private readonly ISnowflakeIdGenerator _snowflake = snowflake;
    private readonly ICurrentUserContext _userContext = userContext;
    private readonly IEventBus _eventBus = eventBus;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => _userContext.UserId ?? "system";

    // ============ 读路径（台账/成本调整不缓存，A-9）============

    /// <inheritdoc />
    public async Task<IReadOnlyList<CostAdjustRow>> ListAsync(int? status, CancellationToken ct)
    {
        _ = TenantId;
        CostAdjustStatus? filter = status is null ? null : (CostAdjustStatus)status.Value;
        IReadOnlyList<CostAdjust> rows = await _repo
            .ListCostAdjustsAsync(filter, ct).ConfigureAwait(false);
        return [.. rows.Select(static adjust => Map(adjust))];
    }

    /// <inheritdoc />
    public async Task<CostAdjustDetailRow> GetAsync(long id, CancellationToken ct)
    {
        _ = TenantId;
        return await LoadDetailAsync(id, ct).ConfigureAwait(false);
    }

    // ============ 草稿写路径（事务 → 审计）============

    /// <inheritdoc />
    public async Task<CostAdjustDetailRow> CreateDraftAsync(
        CreateCostAdjustPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateReasonCode(payload.ReasonCode);

        CostAdjust adjust = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                (IReadOnlyList<PendingLine> lines, decimal total) = await ComputeDraftAsync(
                    payload.Lines, payload.WarehouseId, token).ConfigureAwait(false);
                string adjNo = await _numberGenerator
                    .NextAsync(AdjNoPrefix, OrgCode, token).ConfigureAwait(false);

                CostAdjust entity = new(
                    TenantId, adjNo, payload.ReasonCode, payload.WarehouseId, total,
                    NormalizeRemark(payload.Remark));
                await _repo.AddCostAdjustAsync(entity, token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);

                await _repo.AddCostAdjustLinesAsync(BuildLines(entity.Id, lines), token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return entity;
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("INSERT", Map(adjust), oldJson: null, ct).ConfigureAwait(false);
        return await LoadDetailAsync(adjust.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CostAdjustDetailRow> UpdateDraftAsync(
        long id, UpdateCostAdjustPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateReasonCode(payload.ReasonCode);

        (CostAdjustRow Row, string OldJson) result = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                CostAdjust adjust = await FindTrackedOrThrowAsync(id, token).ConfigureAwait(false);
                adjust.EnsureDraft();

                (IReadOnlyList<PendingLine> lines, decimal total) = await ComputeDraftAsync(
                    payload.Lines, payload.WarehouseId, token).ConfigureAwait(false);
                string oldJson = Serialize(Map(adjust));

                adjust.UpdateDraft(payload.ReasonCode, payload.WarehouseId, total, NormalizeRemark(payload.Remark));
                await _repo.ReplaceCostAdjustLinesAsync(
                    adjust.Id, BuildLines(adjust.Id, lines), token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(adjust), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        return await LoadDetailAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteDraftAsync(long id, CancellationToken ct)
    {
        (CostAdjustRow Row, string OldJson) result = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                CostAdjust adjust = await FindTrackedOrThrowAsync(id, token).ConfigureAwait(false);
                adjust.EnsureDraft();
                string oldJson = Serialize(Map(adjust));

                await _repo.ReplaceCostAdjustLinesAsync(adjust.Id, [], token).ConfigureAwait(false);
                await _repo.RemoveCostAdjustAsync(adjust, token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(adjust), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("DELETE", result.Row, result.OldJson, ct).ConfigureAwait(false);
    }

    // ============ 提交审批（阈值选定义码）============

    /// <inheritdoc />
    public async Task<StkApprovalRequestRow> SubmitAsync(long id, CancellationToken ct)
    {
        CostAdjust adjust = await FindTrackedOrThrowAsync(id, ct).ConfigureAwait(false);
        adjust.EnsureDraft();

        IReadOnlyList<CostAdjustLine> lines = await _repo
            .ListCostAdjustLinesAsync(id, ct).ConfigureAwait(false);
        if (lines.Count == 0)
        {
            throw new ValidationException("lines", "成本调整明细不能为空");
        }

        await EnsureNoLockedAsync(lines, ct).ConfigureAwait(false);

        decimal threshold = await ReadThresholdAsync(ct).ConfigureAwait(false);
        string flowCode = Math.Abs(adjust.TotalDiffAmount) > threshold ? LargeFlowCode : NormalFlowCode;

        StkApprovalRequest request = new(TenantId, CostAdjustBizType, Serialize(adjust.Id));
        await _repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await _approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: flowCode,
                BizType: CostAdjustBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: adjust.AdjNo,
                InitiatorUserId: ChangedBy,
                Title: $"成本调整申请：{adjust.AdjNo}",
                Description: null,
                BizData: new Dictionary<string, object?>
                {
                    ["costAdjustId"] = adjust.Id,
                    ["totalDiffAmount"] = adjust.TotalDiffAmount,
                }),
            ct).ConfigureAwait(false);

        adjust.Submit();
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
            || !completed.BizType.StartsWith(CostAdjustBizTypePrefix, StringComparison.Ordinal))
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
            long rejectedId = DeserializePayload(request.PayloadJson, request.Id);
            CostAdjust? rejected = await _repo
                .FindCostAdjustTrackedAsync(rejectedId, ct).ConfigureAwait(false);
            if (rejected is not null && rejected.Status == CostAdjustStatus.Pending)
            {
                rejected.MarkRejected();
            }

            _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        string approvedBy = string.IsNullOrWhiteSpace(completed.InitiatorUserId)
            ? ChangedBy
            : completed.InitiatorUserId;
        DateTimeOffset appliedAt = ResolveCompletedAt(completed);

        (CostAdjustRow Row, string? OldJson, bool AppliedNow) applied = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                long adjustId = DeserializePayload(request.PayloadJson, request.Id);
                CostAdjust adjust = await _repo.FindCostAdjustTrackedAsync(adjustId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"成本调整单不存在：costAdjustId={adjustId}。");

                if (adjust.Status == CostAdjustStatus.Approved)
                {
                    // 幂等兜底：单据已通过则不再重复写流水，也不再重复发布事件
                    return (Map(adjust), (string?)null, false);
                }

                IReadOnlyList<CostAdjustLine> lines = await _repo
                    .ListCostAdjustLinesAsync(adjust.Id, token).ConfigureAwait(false);
                if (lines.Count == 0)
                {
                    throw new ValidationException("lines", "成本调整明细不能为空");
                }

                await EnsureNoLockedAsync(lines, token).ConfigureAwait(false);

                string? oldJson = Serialize(Map(adjust));
                adjust.MarkApproved(appliedAt);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);

                IReadOnlyDictionary<(long ItemId, long WarehouseId), decimal> qtyBefore =
                    await PostLedgerAsync(adjust, lines, approvedBy, appliedAt, token).ConfigureAwait(false);
                await VerifyTargetAvgAsync(lines, qtyBefore, token).ConfigureAwait(false);

                // PostBatchAsync 内部会清空跟踪器（并发重试），必须重新查询请求后再标记已应用
                StkApprovalRequest? tracked = await _repo
                    .FindApprovalRequestByInstanceAsync(completed.ApprovalInstanceId, token)
                    .ConfigureAwait(false);
                tracked?.MarkApplied(appliedAt);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(adjust), oldJson, true);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", applied.Row, applied.OldJson, ct).ConfigureAwait(false);

        if (applied.AppliedNow)
        {
            // 事务已提交：Outbox 持久发布成本调整生效事件（EventId 由总线生成）
            await _eventBus.PublishDurableAsync(
                new CostAdjustedEvent
                {
                    CostAdjustId = applied.Row.Id,
                    AdjNo = applied.Row.AdjNo,
                    TotalDiffAmount = applied.Row.TotalDiffAmount,
                    TenantCode = _tenantCtx.TenantCode ?? TenantId.ToString(CultureInfo.InvariantCulture),
                },
                ct).ConfigureAwait(false);
        }
    }

    // ============ 内部实现：草稿计算 ============

    private async Task<(IReadOnlyList<PendingLine> Lines, decimal Total)> ComputeDraftAsync(
        IReadOnlyList<CostAdjustLinePayload>? payloadLines, long? headerWarehouseId, CancellationToken ct)
    {
        if (payloadLines is null || payloadLines.Count == 0)
        {
            throw new ValidationException("lines", "成本调整明细不能为空");
        }

        List<PendingLine> pending = new(payloadLines.Count);
        HashSet<(long ItemId, long WarehouseId)> keys = [];
        foreach (CostAdjustLinePayload line in payloadLines)
        {
            if (line.ItemId <= 0)
            {
                throw new ValidationException("lines", "商品不能为空");
            }

            if (line.NewAvgCost < 0m)
            {
                throw new ValidationException("lines", "新均价不能为负");
            }

            if (headerWarehouseId is > 0 && line.WarehouseId is > 0 && line.WarehouseId != headerWarehouseId.Value)
            {
                throw new ValidationException("lines", "明细仓库与表头仓库不一致");
            }

            long warehouseId = line.WarehouseId > 0 ? line.WarehouseId : headerWarehouseId ?? 0;
            if (warehouseId <= 0)
            {
                throw new ValidationException("lines", "仓库不能为空");
            }

            if (!keys.Add((line.ItemId, warehouseId)))
            {
                throw new ValidationException("lines", "同一商品与仓库不能重复");
            }

            pending.Add(new PendingLine(line.ItemId, warehouseId, line.NewAvgCost));
        }

        IReadOnlyList<long> itemIds = [.. pending.Select(static line => line.ItemId).Distinct()];
        IReadOnlyList<long> warehouseIds = [.. pending.Select(static line => line.WarehouseId).Distinct()];
        IReadOnlyList<StockBalance> balances = await _repo
            .FindStockBalancesByKeysAsync(itemIds, warehouseIds, ct).ConfigureAwait(false);
        Dictionary<(long ItemId, long WarehouseId), StockBalance> balanceMap =
            balances.ToDictionary(static balance => (balance.ItemId, balance.WarehouseId));

        List<PendingLine> resolved = new(pending.Count);
        decimal total = 0m;
        foreach (PendingLine line in pending)
        {
            if (!balanceMap.TryGetValue((line.ItemId, line.WarehouseId), out StockBalance? balance))
            {
                throw new ValidationException("lines", "台账不存在，无法调整成本");
            }

            decimal diff = StockMath.Round(
                (line.NewAvgCost - balance.AvgCost) * balance.QtyOnhand, StockMath.AmountDigits);
            resolved.Add(line with
            {
                OldAvgCost = balance.AvgCost,
                Qty = balance.QtyOnhand,
                DiffAmount = diff,
            });
            total += diff;
        }

        return (resolved, StockMath.Round(total, StockMath.AmountDigits));
    }

    private List<CostAdjustLine> BuildLines(long costAdjustId, IReadOnlyList<PendingLine> lines)
        => [.. lines.Select(line => new CostAdjustLine(
            TenantId,
            costAdjustId,
            line.ItemId,
            line.WarehouseId,
            line.OldAvgCost,
            line.NewAvgCost,
            line.Qty))];

    // ============ 内部实现：台账写入 ============

    private async Task<IReadOnlyDictionary<(long ItemId, long WarehouseId), decimal>> PostLedgerAsync(
        CostAdjust adjust,
        IReadOnlyList<CostAdjustLine> lines,
        string approvedBy,
        DateTimeOffset appliedAt,
        CancellationToken ct)
    {
        IReadOnlyList<long> itemIds = [.. lines.Select(static line => line.ItemId).Distinct()];
        IReadOnlyList<long> warehouseIds = [.. lines.Select(static line => line.WarehouseId).Distinct()];
        IReadOnlyList<Item> items = await _repo.FindItemsAsync(itemIds, ct).ConfigureAwait(false);
        Dictionary<long, Item> itemMap = items.ToDictionary(static item => item.Id);
        IReadOnlyList<StockBalance> balances = await _repo
            .FindStockBalancesByKeysAsync(itemIds, warehouseIds, ct).ConfigureAwait(false);
        Dictionary<(long ItemId, long WarehouseId), StockBalance> balanceMap =
            balances.ToDictionary(static balance => (balance.ItemId, balance.WarehouseId));
        IReadOnlyList<StockBatch> batches = await _repo
            .ListStockBatchesByKeysAsync(itemIds, warehouseIds, ct).ConfigureAwait(false);

        Dictionary<(long ItemId, long WarehouseId), decimal> qtyBefore = new();
        List<PostStockTxnRequest> requests = [];
        for (int index = 0; index < lines.Count; index++)
        {
            CostAdjustLine line = lines[index];
            if (!itemMap.TryGetValue(line.ItemId, out Item? item))
            {
                throw new InvalidOperationException($"商品不存在：itemId={line.ItemId}。");
            }

            if (!balanceMap.TryGetValue((line.ItemId, line.WarehouseId), out StockBalance? balance))
            {
                throw new ConflictException("台账不存在，无法调整成本", ErrorNoBalance);
            }

            qtyBefore[(line.ItemId, line.WarehouseId)] = balance.QtyOnhand;
            string lineKey = index.ToString(CultureInfo.InvariantCulture);

            if (item.BatchEnabled)
            {
                List<StockBatch> lineBatches =
                [
                    .. batches
                        .Where(batch => batch.ItemId == line.ItemId
                            && batch.WarehouseId == line.WarehouseId
                            && batch.Qty > 0m)
                        .OrderBy(static batch => batch.BatchNo, StringComparer.Ordinal),
                ];

                List<(string BatchNo, decimal Qty, int LineNo)> plan = new(lineBatches.Count);
                for (int batchIndex = 0; batchIndex < lineBatches.Count; batchIndex++)
                {
                    StockBatch batch = lineBatches[batchIndex];
                    plan.Add((
                        batch.BatchNo,
                        batch.Qty,
                        (index * BatchLineNoStride)
                            + (batchIndex * LineNoStride)
                            + 1));
                }

                // 先全部红字出清批次，再全部蓝字按新均价入账，保证末次出清后金额归零、均价精确等于目标
                foreach ((string batchNo, decimal qty, int lineNo) in plan)
                {
                    requests.Add(OutRequest(
                        adjust.AdjNo, line, batchNo, qty, lineNo,
                        $"{lineKey}:out:{batchNo}", approvedBy, appliedAt));
                }

                foreach ((string batchNo, decimal qty, int lineNo) in plan)
                {
                    requests.Add(InRequest(
                        adjust.AdjNo, line, batchNo, qty, lineNo + 1,
                        $"{lineKey}:in:{batchNo}", approvedBy, appliedAt));
                }
            }
            else
            {
                if (balance.QtyOnhand <= 0m)
                {
                    continue;
                }

                int outLineNo = (index * LineNoStride) + 1;
                requests.Add(OutRequest(
                    adjust.AdjNo, line, batchNo: null, balance.QtyOnhand, outLineNo,
                    $"{lineKey}:out", approvedBy, appliedAt));
                requests.Add(InRequest(
                    adjust.AdjNo, line, batchNo: null, balance.QtyOnhand, outLineNo + 1,
                    $"{lineKey}:in", approvedBy, appliedAt));
            }
        }

        if (requests.Count > 0)
        {
            IReadOnlyList<StockPostResult> results = await _txnCommand
                .PostBatchAsync(requests, ct).ConfigureAwait(false);
            StockPostResult? failed = results.FirstOrDefault(static result => !result.Success);
            if (failed is not null)
            {
                throw new ConflictException(
                    $"成本调整过账失败：{failed.ErrorCode}", failed.ErrorCode ?? ErrorLedgerFailed);
            }
        }

        return qtyBefore;
    }

    private static PostStockTxnRequest OutRequest(
        string adjNo,
        CostAdjustLine line,
        string? batchNo,
        decimal qty,
        int lineNo,
        string keySuffix,
        string operatorId,
        DateTimeOffset appliedAt)
        => new(
            StockSourceType.CostAdjust,
            adjNo,
            lineNo,
            line.ItemId,
            line.WarehouseId,
            batchNo,
            StockDirection.Out,
            qty,
            0m,
            operatorId,
            $"costadj:{adjNo}:{keySuffix}",
            appliedAt);

    private static PostStockTxnRequest InRequest(
        string adjNo,
        CostAdjustLine line,
        string? batchNo,
        decimal qty,
        int lineNo,
        string keySuffix,
        string operatorId,
        DateTimeOffset appliedAt)
        => new(
            StockSourceType.CostAdjust,
            adjNo,
            lineNo,
            line.ItemId,
            line.WarehouseId,
            batchNo,
            StockDirection.In,
            qty,
            line.NewAvgCost,
            operatorId,
            $"costadj:{adjNo}:{keySuffix}",
            appliedAt);

    private async Task VerifyTargetAvgAsync(
        IReadOnlyList<CostAdjustLine> lines,
        IReadOnlyDictionary<(long ItemId, long WarehouseId), decimal> qtyBefore,
        CancellationToken ct)
    {
        IReadOnlyList<long> itemIds = [.. lines.Select(static line => line.ItemId).Distinct()];
        IReadOnlyList<long> warehouseIds = [.. lines.Select(static line => line.WarehouseId).Distinct()];
        IReadOnlyList<StockBalance> balances = await _repo
            .FindStockBalancesByKeysAsync(itemIds, warehouseIds, ct).ConfigureAwait(false);
        Dictionary<(long ItemId, long WarehouseId), StockBalance> balanceMap =
            balances.ToDictionary(static balance => (balance.ItemId, balance.WarehouseId));

        foreach (CostAdjustLine line in lines)
        {
            if (!qtyBefore.TryGetValue((line.ItemId, line.WarehouseId), out decimal qty) || qty <= 0m)
            {
                continue;
            }

            if (!balanceMap.TryGetValue((line.ItemId, line.WarehouseId), out StockBalance? balance))
            {
                throw new InvalidOperationException(
                    $"成本调整后台账行不存在：itemId={line.ItemId}，warehouseId={line.WarehouseId}。");
            }

            if (balance.AvgCost != line.NewAvgCost)
            {
                throw new InvalidOperationException(
                    $"成本调整后移动加权平均价与目标不一致：itemId={line.ItemId}，"
                    + $"warehouseId={line.WarehouseId}，expected={line.NewAvgCost}，actual={balance.AvgCost}。");
            }
        }
    }

    // ============ 内部实现：校验与映射 ============

    private async Task EnsureNoLockedAsync(IReadOnlyList<CostAdjustLine> lines, CancellationToken ct)
    {
        IReadOnlyList<long> itemIds = [.. lines.Select(static line => line.ItemId).Distinct()];
        IReadOnlyList<long> warehouseIds = [.. lines.Select(static line => line.WarehouseId).Distinct()];
        IReadOnlyList<StockBalance> balances = await _repo
            .FindStockBalancesByKeysAsync(itemIds, warehouseIds, ct).ConfigureAwait(false);
        Dictionary<(long ItemId, long WarehouseId), StockBalance> balanceMap =
            balances.ToDictionary(static balance => (balance.ItemId, balance.WarehouseId));

        foreach (CostAdjustLine line in lines)
        {
            if (!balanceMap.TryGetValue((line.ItemId, line.WarehouseId), out StockBalance? balance))
            {
                throw new ConflictException("台账不存在，无法调整成本", ErrorNoBalance);
            }

            if (balance.QtyLocked > 0m)
            {
                throw new ConflictException("存在冻结库存，无法调整成本", ErrorLocked);
            }
        }
    }

    private async Task<decimal> ReadThresholdAsync(CancellationToken ct)
    {
        string raw = await _parameters
            .GetStringAsync(ThresholdParamKey, DefaultThresholdText, ct).ConfigureAwait(false);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal threshold)
            ? threshold
            : 10000m;
    }

    private async Task<CostAdjustDetailRow> LoadDetailAsync(long id, CancellationToken ct)
    {
        CostAdjust adjust = await _repo.FindCostAdjustAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("成本调整单", id.ToString(CultureInfo.InvariantCulture));
        IReadOnlyList<CostAdjustLine> lines = await _repo
            .ListCostAdjustLinesAsync(id, ct).ConfigureAwait(false);
        return new CostAdjustDetailRow(Map(adjust), [.. lines.Select(static line => Map(line))]);
    }

    private async Task<CostAdjust> FindTrackedOrThrowAsync(long id, CancellationToken ct)
    {
        _ = TenantId;
        return await _repo.FindCostAdjustTrackedAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("成本调整单", id.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidateReasonCode(int reasonCode)
    {
        if (reasonCode is < 1 or > 3)
        {
            throw new ValidationException("reasonCode", "调整原因必须为 1/2/3");
        }
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

    private static long DeserializePayload(string? payloadJson, long requestId)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new InvalidOperationException($"审批请求载荷为空：requestId={requestId}。");
        }

        long adjustId = JsonSerializer.Deserialize<long>(payloadJson, QlxJsonDefaults.PreserveNull);
        if (adjustId <= 0)
        {
            throw new InvalidOperationException($"审批请求载荷非法：requestId={requestId}。");
        }

        return adjustId;
    }

    private static DateTimeOffset ResolveCompletedAt(ApprovalCompletedEvent completed)
        => completed.CompletedAt == default ? DateTimeOffset.UtcNow : completed.CompletedAt;

    private Task CaptureAuditAsync(string operation, CostAdjustRow row, string? oldJson, CancellationToken ct)
        => _audit.CaptureChangeAsync(
            new AuditLogEntry(
                _snowflake.NextId(),
                nameof(CostAdjust),
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

    private static CostAdjustRow Map(CostAdjust entity)
        => new(
            entity.Id,
            entity.AdjNo,
            entity.ReasonCode,
            entity.WarehouseId,
            entity.TotalDiffAmount,
            (int)entity.Status,
            entity.AppliedAt,
            entity.Remark);

    private static CostAdjustLineRow Map(CostAdjustLine entity)
        => new(
            entity.Id,
            entity.ItemId,
            entity.WarehouseId,
            entity.OldAvgCost,
            entity.NewAvgCost,
            entity.DiffAmount);

    private static StkApprovalRequestRow Map(StkApprovalRequest request)
        => new(
            request.Id,
            request.ApprovalInstanceId,
            request.BizType,
            request.BizId,
            (int)request.Status);

    /// <summary>草稿计算中间行（旧均价 / 数量 / 差额由台账补齐）。</summary>
    /// <param name="ItemId">商品 Id。</param>
    /// <param name="WarehouseId">仓库 Id。</param>
    /// <param name="NewAvgCost">目标移动加权平均单价。</param>
    private sealed record PendingLine(long ItemId, long WarehouseId, decimal NewAvgCost)
    {
        /// <summary>旧移动加权平均单价（台账当前值）。</summary>
        public decimal OldAvgCost { get; init; }

        /// <summary>台账数量（用于计算差额）。</summary>
        public decimal Qty { get; init; }

        /// <summary>差额金额（后端计算）。</summary>
        public decimal DiffAmount { get; init; }
    }
}
