/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存台账服务（唯一写入口）——移动加权平均、负库存拦截、批次同事务、幂等去重、
 *       Version 并发重试 3 次（A-5/A-8/spec §6）；台账不缓存（A-9）；
 *       事务提交成功后经 Outbox 持久发布 StockChangedEvent（幂等命中不重复发布）
 */

using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Events.Stk;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Repositories;

namespace Qlx.Modules.Stk.Services;

/// <summary>
/// 库存台账服务：全平台唯一的库存写入通道（A-5）。任何 Controller/Wms 作业不得直接改台账，
/// 一律经 <see cref="IStockTxnCommandService"/> 提交流水，由本服务在同一事务内完成
/// 「台账 + 批次 + 流水」三写入（spec §6）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Stk 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（流水号）。</param>
/// <param name="userContext">当前用户上下文（经办人兜底）。</param>
/// <param name="eventBus">事件总线（事务提交后 Outbox 持久发布库存变动事件）。</param>
internal sealed class StockLedgerService(
    ITenantContext tenantCtx,
    IStkTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserContext userContext,
    IEventBus eventBus) : IStockTxnCommandService
{
    /// <summary>库存流水号前缀（D-09 未定义独立号段，沿用 SP- 域）。</summary>
    internal const string TxnNoPrefix = "SP-LS";

    /// <summary>流水号组织码（台账为系统级动作，固定 SYS）。</summary>
    internal const string OrgCode = "SYS";

    /// <summary>负库存拦截错误码。</summary>
    internal const string ErrorInsufficient = "STK_INSUFFICIENT";

    /// <summary>批次商品缺少批次号错误码。</summary>
    internal const string ErrorBatchRequired = "STK_BATCH_REQUIRED";

    /// <summary>批次不存在错误码。</summary>
    internal const string ErrorBatchNotFound = "STK_BATCH_NOT_FOUND";

    /// <summary>幂等命中错误码（幂等命中按成功返回，仅登记用途）。</summary>
    internal const string ErrorDuplicate = "STK_DUPLICATE";

    /// <summary>数量非法错误码。</summary>
    internal const string ErrorInvalidQty = "STK_INVALID_QTY";

    /// <summary>并发冲突错误码。</summary>
    internal const string ErrorConcurrency = "STK_CONCURRENCY_CONFLICT";

    /// <summary>冻结量不得为负错误码。</summary>
    internal const string ErrorLockedNegative = "STK_LOCKED_NEGATIVE";

    /// <summary>事务内最大尝试次数（并发冲突整单重试，A-8）。</summary>
    private const int MaxAttempts = 3;

    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IStkTenantRepository _repo = repo;
    private readonly IDocumentNumberGenerator _numberGenerator = numberGenerator;
    private readonly ICurrentUserContext _userContext = userContext;
    private readonly IEventBus _eventBus = eventBus;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    // ============ 单笔提交 ============

    /// <inheritdoc />
    public async Task<StockPostResult> PostAsync(PostStockTxnRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        _ = TenantId;

        if (request.Qty <= 0m)
        {
            return Failed(ErrorInvalidQty);
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // 重试前清空跟踪态，避免上一轮部分变更残留（并发冲突整单重算）
            _repo.ResetChangeTracker();
            try
            {
                PostOutcome outcome = await _repo.ExecuteInTransactionAsync(
                    token => ApplySingleAsync(request, token), ct).ConfigureAwait(false);

                // 事务已提交：仅本次实际写入的流水才持久发布（幂等命中不重复发布）
                if (outcome.Written is not null)
                {
                    await PublishStockChangedAsync(outcome.Written, ct).ConfigureAwait(false);
                }

                return outcome.Result;
            }
            catch (DbUpdateConcurrencyException)
            {
                _repo.ResetChangeTracker();
                if (attempt == MaxAttempts)
                {
                    throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
                }
            }
            catch (DbUpdateException)
            {
                // 幂等唯一索引竞态：另一并发同幂等键已写入，重新查询并返回既有结果
                _repo.ResetChangeTracker();
                StockTxn? duplicate = await FindExistingTxnAsync(request, ct).ConfigureAwait(false);
                if (duplicate is not null)
                {
                    return MapResult(duplicate);
                }

                if (attempt == MaxAttempts)
                {
                    throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
                }
            }
        }

        throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
    }

    // ============ 批量提交（单事务，任一行失败整批回滚）============

    /// <inheritdoc />
    /// <remarks>
    /// 语义约定：整批共用一个事务，任一行业务失败（如负库存/批次不存在）即整批回滚、零落库；
    /// 返回列表与请求顺序一致，失败行携带各自错误码，其余行统一为
    /// <c>Success=false</c> 且 <c>ErrorCode=null</c>（其写入已随事务回滚，不得再视为成功）。
    /// 参数级错误（空单号/幂等键/时间等）仍按 <see cref="ValidationException"/> 抛出。
    /// </remarks>
    public async Task<IReadOnlyList<StockPostResult>> PostBatchAsync(
        IReadOnlyList<PostStockTxnRequest> requests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);
        _ = TenantId;

        if (requests.Count == 0)
        {
            return [];
        }

        foreach (PostStockTxnRequest request in requests)
        {
            ValidateRequest(request);
        }

        IReadOnlyList<PostOutcome>? abortedOutcomes = null;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            _repo.ResetChangeTracker();
            abortedOutcomes = null;
            try
            {
                IReadOnlyList<PostOutcome> outcomes = await _repo.ExecuteInTransactionAsync(
                    async token =>
                    {
                        IReadOnlyList<PostOutcome> results =
                            await ApplyBatchAsync(requests, token).ConfigureAwait(false);
                        if (results.Any(static outcome => !outcome.Result.Success))
                        {
                            // 已成功行的写入必须随事务回滚：以受控异常触发回滚，外层按失败语义返回
                            abortedOutcomes = results;
                            throw new InvalidOperationException("批量库存变动存在失败行，整批回滚");
                        }

                        return results;
                    },
                    ct).ConfigureAwait(false);

                // 事务已提交：按请求逐条持久发布本次实际写入的流水（幂等命中不重复发布）
                foreach (PostOutcome outcome in outcomes)
                {
                    if (outcome.Written is not null)
                    {
                        await PublishStockChangedAsync(outcome.Written, ct).ConfigureAwait(false);
                    }
                }

                return [.. outcomes.Select(static outcome => outcome.Result)];
            }
            catch (InvalidOperationException) when (abortedOutcomes is not null)
            {
                _repo.ResetChangeTracker();
                return NormalizeBatchResults([.. abortedOutcomes!.Select(static outcome => outcome.Result)]);
            }
            catch (DbUpdateConcurrencyException)
            {
                _repo.ResetChangeTracker();
                if (attempt == MaxAttempts)
                {
                    throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
                }
            }
            catch (DbUpdateException)
            {
                _repo.ResetChangeTracker();
                if (attempt == MaxAttempts)
                {
                    throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
                }
            }
        }

        throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
    }

    // ============ 冻结量调整 ============

    /// <inheritdoc />
    /// <remarks>
    /// 冻结/解冻不产生库存流水（数量与金额不变），仅递增台账 <c>Version</c>；
    /// 冻结量不得为负。调整原因 <paramref name="reason"/> 为审计语义入参，本服务只校验非空。
    /// </remarks>
    public async Task AdjustLockedQtyAsync(
        long itemId, long warehouseId, decimal deltaQty, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (itemId <= 0)
        {
            throw new ValidationException("itemId", "商品不能为空");
        }

        if (warehouseId <= 0)
        {
            throw new ValidationException("warehouseId", "仓库不能为空");
        }

        _ = TenantId;

        if (deltaQty == 0m)
        {
            return;
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            _repo.ResetChangeTracker();
            try
            {
                _ = await _repo.ExecuteInTransactionAsync(
                    async token =>
                    {
                        StockBalance? balance = await _repo
                            .FindStockBalanceTrackedAsync(itemId, warehouseId, token).ConfigureAwait(false);

                        if (balance is null)
                        {
                            if (deltaQty < 0m)
                            {
                                throw new ConflictException("冻结量不能为负", ErrorLockedNegative);
                            }

                            balance = new StockBalance(TenantId, itemId, warehouseId);
                            await _repo.AddStockBalanceAsync(balance, token).ConfigureAwait(false);
                        }

                        if (balance.QtyLocked + deltaQty < 0m)
                        {
                            throw new ConflictException("冻结量不能为负", ErrorLockedNegative);
                        }

                        balance.ApplyLockedDelta(deltaQty);
                        _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                        return true;
                    },
                    ct).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                _repo.ResetChangeTracker();
                if (attempt == MaxAttempts)
                {
                    throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
                }
            }
        }

        throw new ConflictException("库存并发冲突，请重试", ErrorConcurrency);
    }

    // ============ 台账只读（不做成本权限裁剪，裁剪属查询服务）============

    /// <inheritdoc />
    public async Task<StockBalanceRow?> FindBalanceAsync(long itemId, long warehouseId, CancellationToken ct)
    {
        _ = TenantId;
        StockBalance? balance = await _repo
            .FindStockBalanceAsync(itemId, warehouseId, ct).ConfigureAwait(false);
        return balance is null
            ? null
            : new StockBalanceRow(
                balance.ItemId,
                balance.WarehouseId,
                balance.QtyOnhand,
                balance.AvgCost,
                balance.Amount,
                balance.QtyLocked,
                balance.LastTxnAt);
    }

    // ============ 内部实现：单笔算法 ============

    private async Task<IReadOnlyList<PostOutcome>> ApplyBatchAsync(
        IReadOnlyList<PostStockTxnRequest> requests, CancellationToken ct)
    {
        List<PostOutcome> results = new(requests.Count);
        foreach (PostStockTxnRequest request in requests)
        {
            results.Add(await ApplySingleAsync(request, ct).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<PostOutcome> ApplySingleAsync(PostStockTxnRequest request, CancellationToken ct)
    {
        if (request.Qty <= 0m)
        {
            return FailedOutcome(ErrorInvalidQty);
        }

        Item item = await _repo.FindItemAsync(request.ItemId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(
                "商品", request.ItemId.ToString(CultureInfo.InvariantCulture));

        string? batchNo = NormalizeBatchNo(request.BatchNo);
        if (item.BatchEnabled && batchNo is null)
        {
            return FailedOutcome(ErrorBatchRequired);
        }

        // 幂等预检：同一幂等键重复投递（Outbox 重投/用户重试）直接返回既有流水结果，不重复写
        StockTxn? existing = await FindExistingTxnAsync(request, batchNo, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return new PostOutcome(MapResult(existing), Written: null);
        }

        StockBalance? balance = await _repo
            .FindStockBalanceTrackedAsync(request.ItemId, request.WarehouseId, ct).ConfigureAwait(false);

        return request.Direction == StockDirection.In
            ? await ApplyInAsync(request, item, balance, batchNo, ct).ConfigureAwait(false)
            : await ApplyOutAsync(request, item, balance, batchNo, ct).ConfigureAwait(false);
    }

    private async Task<PostOutcome> ApplyInAsync(
        PostStockTxnRequest request, Item item, StockBalance? balance, string? batchNo, CancellationToken ct)
    {
        decimal amount = StockMath.Round(request.Qty * request.UnitCost, StockMath.AmountDigits);

        StockBalance target = balance ?? new StockBalance(TenantId, request.ItemId, request.WarehouseId);
        if (balance is null)
        {
            await _repo.AddStockBalanceAsync(target, ct).ConfigureAwait(false);
        }

        target.ApplyIn(request.Qty, request.UnitCost, request.OccurredAt);

        if (item.BatchEnabled && batchNo is not null)
        {
            StockBatch? batch = await _repo
                .FindStockBatchTrackedAsync(request.ItemId, request.WarehouseId, batchNo, ct).ConfigureAwait(false);
            if (batch is null)
            {
                batch = new StockBatch(TenantId, request.ItemId, request.WarehouseId, batchNo);
                batch.ApplyIn(request.Qty, request.UnitCost);
                await _repo.AddStockBatchAsync(batch, ct).ConfigureAwait(false);
            }
            else
            {
                batch.ApplyIn(request.Qty, request.UnitCost);
            }
        }

        StockTxn txn = await CreateTxnAsync(
            request, batchNo, request.UnitCost, amount, target, ct).ConfigureAwait(false);
        return new PostOutcome(
            new StockPostResult(true, txn.Id, txn.TxnNo, target.QtyOnhand, target.Amount, null, 0m),
            txn);
    }

    private async Task<PostOutcome> ApplyOutAsync(
        PostStockTxnRequest request, Item item, StockBalance? balance, string? batchNo, CancellationToken ct)
    {
        // 出库时台账行不存在 → 可用量为 0（零写入）
        if (balance is null)
        {
            return FailedOutcome(ErrorInsufficient, request.Qty);
        }

        decimal available = balance.QtyOnhand - balance.QtyLocked;
        if (request.Qty > available)
        {
            return FailedOutcome(ErrorInsufficient, request.Qty - available);
        }

        StockBatch? batch = null;
        if (item.BatchEnabled && batchNo is not null)
        {
            batch = await _repo
                .FindStockBatchTrackedAsync(request.ItemId, request.WarehouseId, batchNo, ct).ConfigureAwait(false);
            if (batch is null)
            {
                return FailedOutcome(ErrorBatchNotFound);
            }

            if (batch.Qty < request.Qty)
            {
                return FailedOutcome(ErrorInsufficient, request.Qty - batch.Qty);
            }
        }

        // 出库单价取当时移动加权平均价（请求单价忽略），金额按 2 位舍入
        decimal unitCost = balance.ApplyOut(request.Qty, request.OccurredAt);
        batch?.ApplyOut(request.Qty);
        decimal amount = StockMath.Round(request.Qty * unitCost, StockMath.AmountDigits);

        StockTxn txn = await CreateTxnAsync(request, batchNo, unitCost, amount, balance, ct).ConfigureAwait(false);
        return new PostOutcome(
            new StockPostResult(true, txn.Id, txn.TxnNo, balance.QtyOnhand, balance.Amount, null, 0m),
            txn);
    }

    private async Task<StockTxn> CreateTxnAsync(
        PostStockTxnRequest request, string? batchNo, decimal unitCost, decimal amount,
        StockBalance balance, CancellationToken ct)
    {
        string txnNo = await _numberGenerator
            .NextAsync(TxnNoPrefix, OrgCode, ct).ConfigureAwait(false);

        StockTxn txn = new(
            TenantId,
            txnNo,
            (int)request.SourceType,
            request.SourceDocNo,
            request.SourceLineNo,
            request.ItemId,
            request.WarehouseId,
            batchNo,
            (int)request.Direction,
            request.Qty,
            unitCost,
            amount,
            balance.QtyOnhand,
            balance.Amount,
            request.OperatorId ?? _userContext.UserId,
            request.OccurredAt);

        await _repo.AddStockTxnAsync(txn, ct).ConfigureAwait(false);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return txn;
    }

    // ============ 内部实现：校验与映射 ============

    private static void ValidateRequest(PostStockTxnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ItemId <= 0)
        {
            throw new ValidationException("itemId", "商品不能为空");
        }

        if (request.WarehouseId <= 0)
        {
            throw new ValidationException("warehouseId", "仓库不能为空");
        }

        if (string.IsNullOrWhiteSpace(request.SourceDocNo))
        {
            throw new ValidationException("sourceDocNo", "来源单据号不能为空");
        }

        if (request.SourceLineNo <= 0)
        {
            throw new ValidationException("sourceLineNo", "来源单据行号必须大于 0");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ValidationException("idempotencyKey", "幂等键不能为空");
        }

        if (request.OccurredAt == default)
        {
            throw new ValidationException("occurredAt", "业务发生时间不能为空");
        }

        if (request.UnitCost < 0m)
        {
            throw new ValidationException("unitCost", "单价不能为负");
        }
    }

    private Task<StockTxn?> FindExistingTxnAsync(PostStockTxnRequest request, CancellationToken ct)
        => FindExistingTxnAsync(request, NormalizeBatchNo(request.BatchNo), ct);

    private Task<StockTxn?> FindExistingTxnAsync(
        PostStockTxnRequest request, string? batchNo, CancellationToken ct)
        => _repo.FindStockTxnByIdempotencyAsync(
            request.SourceType,
            request.SourceDocNo,
            request.SourceLineNo,
            request.ItemId,
            request.WarehouseId,
            batchNo,
            request.Direction,
            ct);

    private static string? NormalizeBatchNo(string? batchNo)
        => string.IsNullOrWhiteSpace(batchNo) ? null : batchNo.Trim();

    private static StockPostResult Failed(string? errorCode, decimal shortageQty = 0m)
        => new(false, null, null, 0m, 0m, errorCode, shortageQty);

    private static PostOutcome FailedOutcome(string? errorCode, decimal shortageQty = 0m)
        => new(Failed(errorCode, shortageQty), Written: null);

    private static StockPostResult MapResult(StockTxn txn)
        => new(true, txn.Id, txn.TxnNo, txn.BalanceQtyAfter, txn.BalanceAmountAfter, null, 0m);

    /// <summary>
    /// 事务提交成功后经 Outbox 持久发布库存变动事件；<c>EventId</c> 由事件总线生成，本服务不设置。
    /// </summary>
    private Task PublishStockChangedAsync(StockTxn txn, CancellationToken ct)
        => _eventBus.PublishDurableAsync(
            new StockChangedEvent
            {
                ItemId = txn.ItemId,
                WarehouseId = txn.WarehouseId,
                BatchNo = txn.BatchNo,
                Direction = txn.Direction,
                Qty = txn.Qty,
                BalanceQtyAfter = txn.BalanceQtyAfter,
                TenantCode = _tenantCtx.TenantCode ?? TenantId.ToString(CultureInfo.InvariantCulture),
            },
            ct);

    /// <summary>
    /// 整批回滚后的结果归一：失败行保留自身错误码与缺口数量，
    /// 其余行（原已写入但随事务回滚）统一标记为失败且无错误码。
    /// </summary>
    private static IReadOnlyList<StockPostResult> NormalizeBatchResults(
        IReadOnlyList<StockPostResult> results)
        => [.. results.Select(static result => result.Success
            ? Failed(errorCode: null)
            : Failed(result.ErrorCode, result.ShortageQty))];

    /// <summary>单笔提交结果与本次新写入流水（幂等命中时 <see cref="Written"/> 为 <see langword="null"/>）。</summary>
    /// <param name="Result">对外提交结果。</param>
    /// <param name="Written">本次新写入的流水（用于事务提交后发布事件）。</param>
    private sealed record PostOutcome(StockPostResult Result, StockTxn? Written);
}
