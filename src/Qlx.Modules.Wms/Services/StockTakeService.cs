/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 修改时间：2026-10-26
 * 描述：库存盘点服务实现——按范围快照台账（批次商品逐批次成行，冻结仍按商品×仓库累计）+ 冻结（A-15）+
 *       盲盘字段裁剪（B-3）+ 初盘有差异自动生复盘 + 复盘完成解冻并生成盘盈亏草稿
 */

using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Wms.Domain.StockTake;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 库存盘点服务。生成盘点单时快照台账账面量并对每一行调用
/// <see cref="IStockTxnCommandService.AdjustLockedQtyAsync"/> 冻结（A-15，Wms 不写 stk_ 表）；
/// 解冻只对 <c>Frozen=true</c> 的行反向调整，保证重复完成/取消不会重复解冻。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（WL-PDJH / WL-PD）。</param>
/// <param name="orgQuery">跨模块组织只读查询（解析单号中的组织码）。</param>
/// <param name="itemQuery">跨模块商品只读查询（品类范围过滤）。</param>
/// <param name="stockQuery">跨模块库存只读查询（台账快照）。</param>
/// <param name="txnCommand">库存写入命令契约（冻结/解冻，A-15）。</param>
/// <param name="draftCreator">盘盈亏草稿生成接缝（Task 19 实现）。</param>
internal sealed class StockTakeService(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IOrgQueryService orgQuery,
    IItemQueryService itemQuery,
    IStockQueryService stockQuery,
    IStockTxnCommandService txnCommand,
    IStockTakeAdjustDraftCreator draftCreator) : IStockTakeService
{
    /// <summary>状态机非法流转错误码。</summary>
    internal const string ErrorInvalidState = "WMS_INVALID_STATE";

    private const int MaxRemarkLength = 256;
    private const int MaxDiffReasonLength = 256;

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    // ============ 计划 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTakePlanRow>> ListPlansAsync(
        int? scopeType, long? warehouseId, CancellationToken ct)
    {
        if (scopeType is < 1 or > 3)
        {
            throw new ValidationException("scopeType", "范围类型必须为 1/2/3");
        }

        ValidateWarehouseFilter(warehouseId);

        IReadOnlyList<StockTakePlan> plans = await repo
            .ListStockTakePlansAsync(scopeType, warehouseId, ct).ConfigureAwait(false);
        return [.. plans.Select(MapPlan)];
    }

    /// <inheritdoc />
    public async Task<StockTakePlanRow> CreatePlanAsync(
        CreateStockTakePlanPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return await repo.ExecuteInTransactionAsync(async token =>
        {
            StockTakePlan plan = await CreatePlanCoreAsync(payload, token).ConfigureAwait(false);
            await repo.AddStockTakePlanAsync(plan, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapPlan(plan);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StockTakeDetailRow> GenerateAsync(long planId, CancellationToken ct)
    {
        if (planId <= 0)
        {
            throw new ValidationException("id", "盘点计划 Id 非法");
        }

        return await repo.ExecuteInTransactionAsync(async token =>
        {
            StockTakePlan plan = await repo.FindStockTakePlanAsync(planId, token).ConfigureAwait(false)
                ?? throw new NotFoundException("盘点计划", planId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (plan.IsGenerated)
            {
                throw new ConflictException("盘点计划已生成盘点单，不能重复生成", ErrorInvalidState);
            }

            Warehouse warehouse = await FindEnabledWarehouseAsync(plan.WarehouseId, token)
                .ConfigureAwait(false);
            IReadOnlyList<StockBalanceRow> balances = await ListScopedBalancesAsync(plan, token)
                .ConfigureAwait(false);
            if (balances.Count == 0)
            {
                throw new ValidationException("plan", "盘点范围内无可盘点库存");
            }

            string orgCode = await ResolveOrgCodeAsync(warehouse.OrgId, token).ConfigureAwait(false);
            string takeNo = await numberGenerator
                .NextAsync(WmsDocumentPrefixes.StockTake, orgCode, token).ConfigureAwait(false);

            StockTake take = new(
                TenantId, takeNo, plan.Id, plan.WarehouseId, round: 1, plan.IsBlind,
                sourceTakeId: null, plan.Remark);
            int lineNo = 0;
            foreach (StockBalanceRow balance in balances)
            {
                // 批次商品逐批次成行：批次号随差异带入复盘与盘盈亏草稿，下游库存命令才不会因缺批次被拒
                IReadOnlyList<StockBatchRow> batches = await stockQuery
                    .ListBatchesAsync(balance.ItemId, plan.WarehouseId, token).ConfigureAwait(false);
                List<StockBatchRow> counted = [.. batches
                    .Where(static batch => batch.Qty > 0m)
                    .OrderBy(static batch => batch.BatchNo, StringComparer.Ordinal)];
                if (counted.Count == 0)
                {
                    lineNo++;
                    take.AddLine(new StockTakeLine(
                        TenantId, lineNo, balance.ItemId, batchNo: null, balance.QtyOnhand ?? 0m));
                    continue;
                }

                foreach (StockBatchRow batch in counted)
                {
                    lineNo++;
                    take.AddLine(new StockTakeLine(
                        TenantId, lineNo, balance.ItemId, batch.BatchNo, batch.Qty));
                }
            }

            // 先落库再冻结：单号唯一冲突等本地失败先于任何跨模块冻结暴露，避免回滚后遗留已提交的冻结量
            await repo.AddStockTakeAsync(take, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);

            await FreezeAsync(take, token).ConfigureAwait(false);

            plan.MarkGenerated(take.Id);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapDetail(take);
        }, ct).ConfigureAwait(false);
    }

    // ============ 盘点单 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTakeRow>> ListAsync(
        int? status, long? warehouseId, CancellationToken ct)
    {
        if (status is < 1 or > 6)
        {
            throw new ValidationException("status", "状态必须为 1..6");
        }

        ValidateWarehouseFilter(warehouseId);

        IReadOnlyList<StockTake> takes = await repo
            .ListStockTakesAsync(status, warehouseId, ct).ConfigureAwait(false);
        return [.. takes.Select(MapTake)];
    }

    /// <inheritdoc />
    public async Task<StockTakeDetailRow> GetAsync(long id, CancellationToken ct)
    {
        StockTake take = await FindTakeOrThrowAsync(id, ct).ConfigureAwait(false);
        return MapDetail(take);
    }

    /// <inheritdoc />
    public async Task<StockTakeRow> StartAsync(long id, CancellationToken ct)
        => await repo.ExecuteInTransactionAsync(async token =>
        {
            StockTake take = await FindTakeOrThrowAsync(id, token).ConfigureAwait(false);
            if (take.Status != StockTakeStatus.Pending)
            {
                throw new ConflictException("仅待盘点的盘点单可开始盘点", ErrorInvalidState);
            }

            take.Start();
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapTake(take);
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<StockTakeDetailRow> SaveCountsAsync(
        long id, SaveStockTakeCountsPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return await repo.ExecuteInTransactionAsync(async token =>
        {
            StockTake take = await FindTakeOrThrowAsync(id, token).ConfigureAwait(false);
            if (take.Status is not (StockTakeStatus.InitialCounting or StockTakeStatus.Rechecking))
            {
                throw new ConflictException("仅初盘中或复盘中的盘点单可录入实盘", ErrorInvalidState);
            }

            IReadOnlyList<SaveStockTakeCountLinePayload> lines = payload.Lines ?? [];
            if (lines.Count == 0)
            {
                throw new ValidationException("lines", "实盘入参不能为空");
            }

            Dictionary<long, StockTakeLine> tracked = take.Lines.ToDictionary(static line => line.Id);
            HashSet<long> seen = [];
            for (int index = 0; index < lines.Count; index++)
            {
                SaveStockTakeCountLinePayload input = lines[index];
                if (!tracked.TryGetValue(input.LineId, out StockTakeLine? entity))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细不属于该盘点单");
                }

                if (!seen.Add(input.LineId))
                {
                    throw new ValidationException($"lines[{index}].lineId", "明细行重复提交");
                }

                if (input.ActualQty < 0)
                {
                    throw new ValidationException($"lines[{index}].actualQty", "实盘数量不能为负数");
                }

                decimal diffQty = input.ActualQty - entity.BookQty;
                string? diffReason = Normalize(
                    input.DiffReason, MaxDiffReasonLength, $"lines[{index}].diffReason");
                if (diffQty != 0m && diffReason is null)
                {
                    throw new ValidationException($"lines[{index}].diffReason", "存在差异时必须填写差异原因");
                }

                entity.RecordCount(input.ActualQty, diffQty == 0m ? null : diffReason);
            }

            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapDetail(take);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StockTakeFinishResultRow> FinishAsync(long id, CancellationToken ct)
        => await repo.ExecuteInTransactionAsync(async token =>
        {
            StockTake take = await FindTakeOrThrowAsync(id, token).ConfigureAwait(false);
            if (take.Status is not (StockTakeStatus.InitialCounting or StockTakeStatus.Rechecking))
            {
                throw new ConflictException("仅初盘中或复盘中的盘点单可完成", ErrorInvalidState);
            }

            EnsureAllCounted(take);

            if (take.Round == 1)
            {
                return await FinishInitialAsync(take, token).ConfigureAwait(false);
            }

            return await FinishRecheckAsync(take, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<StockTakeRow> CancelAsync(long id, CancellationToken ct)
        => await repo.ExecuteInTransactionAsync(async token =>
        {
            StockTake take = await FindTakeOrThrowAsync(id, token).ConfigureAwait(false);
            if (take.Status is not (StockTakeStatus.InitialCounting or StockTakeStatus.Rechecking))
            {
                throw new ConflictException("仅初盘中或复盘中的盘点单可取消", ErrorInvalidState);
            }

            StockTake frozenSource = take;
            if (take.Round == 2)
            {
                frozenSource = await FindTakeOrThrowAsync(
                    take.SourceTakeId!.Value, token).ConfigureAwait(false);
            }

            await UnfreezeAsync(frozenSource, token).ConfigureAwait(false);
            take.Cancel();
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return MapTake(take);
        }, ct).ConfigureAwait(false);

    // ============ 内部实现 ============

    private async Task<StockTakeFinishResultRow> FinishInitialAsync(
        StockTake take, CancellationToken token)
    {
        List<StockTakeLine> diffs = [.. take.Lines.Where(static line => line.HasDiff)
            .OrderBy(static line => line.LineNo)];
        take.FinishInitial();

        if (diffs.Count == 0)
        {
            await UnfreezeAsync(take, token).ConfigureAwait(false);
            take.CompleteWithoutRecheck();
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return new StockTakeFinishResultRow(MapDetail(take), RecheckTakeId: null);
        }

        Warehouse warehouse = await FindEnabledWarehouseAsync(take.WarehouseId, token)
            .ConfigureAwait(false);
        string orgCode = await ResolveOrgCodeAsync(warehouse.OrgId, token).ConfigureAwait(false);
        string recheckNo = await numberGenerator
            .NextAsync(WmsDocumentPrefixes.StockTake, orgCode, token).ConfigureAwait(false);

        StockTake recheck = new(
            TenantId, recheckNo, take.PlanId, take.WarehouseId, round: 2,
            take.IsBlind, sourceTakeId: take.Id, remark: null);
        for (int index = 0; index < diffs.Count; index++)
        {
            StockTakeLine source = diffs[index];
            recheck.AddLine(new StockTakeLine(
                TenantId, index + 1, source.ItemId, source.BatchNo, source.BookQty));
        }

        await repo.AddStockTakeAsync(recheck, token).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
        return new StockTakeFinishResultRow(MapDetail(take), recheck.Id);
    }

    private async Task<StockTakeFinishResultRow> FinishRecheckAsync(
        StockTake take, CancellationToken token)
    {
        StockTake initial = await FindTakeOrThrowAsync(
            take.SourceTakeId!.Value, token).ConfigureAwait(false);

        List<StockTakeAdjustDraftLine> draftLines = [.. take.Lines
            .Where(static line => line.HasDiff)
            .OrderBy(static line => line.LineNo)
            .Select(static line => new StockTakeAdjustDraftLine(
                line.ItemId,
                line.BatchNo,
                line.BookQty,
                line.ActualQty ?? line.BookQty,
                line.DiffQty ?? 0m))];

        take.FinishRecheck();
        if (draftLines.Count > 0)
        {
            _ = await draftCreator.CreateAsync(
                new StockTakeAdjustDraftContext(take.Id, take.TakeNo, take.WarehouseId, draftLines),
                token).ConfigureAwait(false);
            // 先落库盘盈亏草稿：单号唯一冲突等本地失败在解冻前暴露，避免事务回滚后遗留已提交的解冻
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
        }

        await UnfreezeAsync(initial, token).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
        return new StockTakeFinishResultRow(MapDetail(take), RecheckTakeId: null);
    }

    private async Task FreezeAsync(StockTake take, CancellationToken token)
    {
        foreach (StockTakeLine line in take.Lines.OrderBy(static line => line.LineNo))
        {
            if (line.Frozen || line.BookQty <= 0m)
            {
                continue;
            }

            await txnCommand.AdjustLockedQtyAsync(
                line.ItemId, take.WarehouseId, line.BookQty, $"盘点冻结：{take.TakeNo}", token)
                .ConfigureAwait(false);
            line.MarkFrozen();
        }
    }

    private async Task UnfreezeAsync(StockTake take, CancellationToken token)
    {
        foreach (StockTakeLine line in take.Lines.OrderBy(static line => line.LineNo))
        {
            if (!line.Frozen)
            {
                continue;
            }

            await txnCommand.AdjustLockedQtyAsync(
                line.ItemId, take.WarehouseId, -line.BookQty, $"盘点解冻：{take.TakeNo}", token)
                .ConfigureAwait(false);
            line.MarkUnfrozen();
        }
    }

    private async Task<StockTakePlan> CreatePlanCoreAsync(
        CreateStockTakePlanPayload payload, CancellationToken token)
    {
        if (payload.PlanDate == default)
        {
            throw new ValidationException("planDate", "盘点日期不能为空");
        }

        if (payload.ScopeType is < 1 or > 3)
        {
            throw new ValidationException("scopeType", "范围类型必须为 1/2/3");
        }

        if (payload.WarehouseId <= 0)
        {
            throw new ValidationException("warehouseId", "仓库 Id 非法");
        }

        Warehouse warehouse = await FindEnabledWarehouseAsync(payload.WarehouseId, token)
            .ConfigureAwait(false);

        long? categoryId = null;
        long? locationId = null;
        switch (payload.ScopeType)
        {
            case 2:
                if (payload.CategoryId is null or <= 0)
                {
                    throw new ValidationException("categoryId", "品类范围必须指定品类");
                }

                categoryId = payload.CategoryId;
                break;
            case 3:
                if (payload.LocationId is null or <= 0)
                {
                    throw new ValidationException("locationId", "库位范围必须指定库位");
                }

                WarehouseLocation location = await repo
                    .FindLocationAsync(payload.LocationId.Value, token).ConfigureAwait(false)
                    ?? throw new ValidationException("locationId", "库位不存在");
                if (location.WarehouseId != warehouse.Id)
                {
                    throw new ValidationException("locationId", "库位不属于该仓库");
                }

                if (location.Status != WarehouseStatus.Enabled)
                {
                    throw new ValidationException("locationId", "库位已停用");
                }

                locationId = payload.LocationId;
                break;
            default:
                break;
        }

        string? remark = Normalize(payload.Remark, MaxRemarkLength, "remark");
        string orgCode = await ResolveOrgCodeAsync(warehouse.OrgId, token).ConfigureAwait(false);
        string planNo = await numberGenerator
            .NextAsync(WmsDocumentPrefixes.StockTakePlan, orgCode, token).ConfigureAwait(false);

        return new StockTakePlan(
            TenantId, planNo, payload.PlanDate, payload.ScopeType, warehouse.Id,
            categoryId, locationId, payload.IsBlind, remark);
    }

    private async Task<IReadOnlyList<StockBalanceRow>> ListScopedBalancesAsync(
        StockTakePlan plan, CancellationToken token)
    {
        IReadOnlyList<StockBalanceRow> balances = await stockQuery
            .ListBalancesByWarehouseAsync(plan.WarehouseId, token).ConfigureAwait(false);
        List<StockBalanceRow> positive = [.. balances
            .Where(static balance => (balance.QtyOnhand ?? 0m) > 0m)
            .OrderBy(static balance => balance.ItemId)];

        if (plan.ScopeType != (int)StockTakeScopeType.Category || plan.CategoryId is null)
        {
            return positive;
        }

        long categoryId = plan.CategoryId.Value;
        Dictionary<long, StockItemQueryRow> items = (await itemQuery
            .FindByIdsAsync([.. positive.Select(static balance => balance.ItemId)], token)
            .ConfigureAwait(false))
            .ToDictionary(static item => item.Id);
        return [.. positive.Where(balance =>
            items.TryGetValue(balance.ItemId, out StockItemQueryRow? item)
            && item.CategoryId == categoryId)];
    }

    private async Task<StockTake> FindTakeOrThrowAsync(long id, CancellationToken ct)
    {
        if (id <= 0)
        {
            throw new ValidationException("id", "盘点单 Id 非法");
        }

        return await repo.FindStockTakeAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("盘点单", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<Warehouse> FindEnabledWarehouseAsync(long warehouseId, CancellationToken ct)
    {
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

    private static void EnsureAllCounted(StockTake take)
    {
        if (take.Lines.Any(static line => line.ActualQty is null))
        {
            throw new ValidationException("lines", "全部明细须录入实盘数量后才能完成");
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

    private static StockTakePlanRow MapPlan(StockTakePlan plan)
        => new(plan.Id, plan.PlanNo, plan.PlanDate, plan.ScopeType, plan.WarehouseId,
            plan.CategoryId, plan.LocationId, plan.IsBlind, plan.Remark, plan.TakeId,
            plan.CreatedAt);

    private static StockTakeRow MapTake(StockTake take)
        => new(take.Id, take.TakeNo, take.PlanId, take.WarehouseId, take.Round, take.IsBlind,
            take.SourceTakeId, (int)take.Status, take.Remark, take.CreatedAt);

    private static StockTakeDetailRow MapDetail(StockTake take)
        => new(MapTake(take),
            [.. take.Lines.OrderBy(static line => line.LineNo)
                .Select(line => MapLine(line, take.IsBlind))]);

    private static StockTakeLineRow MapLine(StockTakeLine line, bool isBlind)
        => new(line.Id, line.LineNo, line.ItemId, line.BatchNo,
            isBlind ? null : line.BookQty,
            line.ActualQty,
            isBlind ? null : line.DiffQty,
            line.DiffReason,
            line.Frozen);
}
