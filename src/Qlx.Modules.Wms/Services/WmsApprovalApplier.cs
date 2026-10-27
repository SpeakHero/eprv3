/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：Wms 审批完成事件应用器——按 ApprovalInstanceId 幂等解析挂起请求，
 *       按 BizType 分发（大额出库 / 调拨 / 盘盈盘亏 / 仓库停用）落地业务变更
 */

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Exceptions;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>Wms 审批完成事件应用器（事件消费者调用）。</summary>
internal interface IWmsApprovalApplier
{
    /// <summary>应用一条审批完成事件（同一实例幂等）。</summary>
    /// <param name="completed">审批完成终态事件。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct);
}

/// <summary>
/// Wms 审批应用器实现。以 <see cref="WmsApprovalRequest.ApprovalInstanceId"/> 为幂等键，
/// 仅处理「待审批」请求；通过与驳回分路径落地，业务侧状态机自身保证重复回放安全。
/// </summary>
/// <param name="repo">Wms 租户仓储（请求幂等键与状态回写）。</param>
/// <param name="stockOutService">大额出库应用。</param>
/// <param name="transferService">调拨应用。</param>
/// <param name="inventoryAdjustApplier">盘盈盘亏应用。</param>
/// <param name="warehouseService">仓库停用应用。</param>
internal sealed class WmsApprovalApplier(
    IWmsTenantRepository repo,
    IStockOutService stockOutService,
    ITransferService transferService,
    IInventoryAdjustApprovalApplier inventoryAdjustApplier,
    IWarehouseService warehouseService) : IWmsApprovalApplier
{
    /// <summary>仓库停用审批业务类型。</summary>
    internal const string WarehouseDisableBizType = "wms.warehouse.disable";

    /// <summary>是否为本模块负责的业务类型。</summary>
    /// <param name="bizType">审批业务类型。</param>
    /// <returns>true 表示需要本模块应用。</returns>
    public static bool IsHandledBizType(string? bizType)
        => bizType is StockOutService.OutboundLargeBizType
            or TransferService.TransferBizType
            or InventoryAdjustService.ApproveBizType
            or WarehouseDisableBizType;

    /// <inheritdoc />
    public async Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(completed);

        if (!IsHandledBizType(completed.BizType))
        {
            return;
        }

        WmsApprovalRequest? request = await repo
            .FindApprovalRequestByInstanceAsync(completed.ApprovalInstanceId, ct).ConfigureAwait(false);

        // 幂等：同一实例重复事件 / 非待审批请求直接返回
        if (request is null || !request.IsPending)
        {
            return;
        }

        bool approved = completed.FinalStatus == ApprovalStatus.Approved;
        await ApplyBranchAsync(completed.BizType, request.BizId, approved, ct).ConfigureAwait(false);

        if (approved)
        {
            request.MarkApplied(completed.CompletedAt);
        }
        else if (completed.FinalStatus == ApprovalStatus.Withdrawn)
        {
            request.MarkWithdrawn();
        }
        else
        {
            request.MarkRejected();
        }

        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>按业务类型分发到对应服务（各服务状态机保证幂等）。</summary>
    private async Task ApplyBranchAsync(
        string bizType, long bizId, bool approved, CancellationToken ct)
    {
        switch (bizType)
        {
            case StockOutService.OutboundLargeBizType:
                await stockOutService.ApplyApprovalAsync(bizId, approved, ct).ConfigureAwait(false);
                return;

            case TransferService.TransferBizType:
                await transferService.ApplyApprovalAsync(bizId, approved, ct).ConfigureAwait(false);
                return;

            case InventoryAdjustService.ApproveBizType:
                _ = await inventoryAdjustApplier.ApplyAsync(bizId, approved, ct).ConfigureAwait(false);
                return;

            case WarehouseDisableBizType:
                await warehouseService.ApplyDisableApprovalAsync(bizId, approved, ct).ConfigureAwait(false);
                return;

            default:
                throw new ConflictException(
                    $"未登记的审批业务类型：{bizType}", "WMS_APPROVAL_BIZ_TYPE_UNKNOWN");
        }
    }
}
