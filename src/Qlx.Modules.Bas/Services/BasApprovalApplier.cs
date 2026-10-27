/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 14:00:00 +08:00 中国标准时间 星期日
 * 描述：审批完成事件应用器——按 ApprovalInstanceId 幂等解析挂起请求，通过后事务内应用载荷
 */

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Events.Bas;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.EventBus;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;

using System.Text.Json;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 审批完成事件应用器。仅处理 Bas 域已登记 BizType（组织调整 / 岗位超编 / 分类停用 /
/// 供应商准入 / 供应商银行变更 / 客户准入 / 客户信用额度调整）；
/// 以 <see cref="BasApprovalRequest.ApprovalInstanceId"/> 为幂等键（同一实例二次事件直接返回），
/// 通过与驳回/撤回分路径落状态。
/// </summary>
internal interface IBasApprovalApplier
{
    /// <summary>应用一条审批完成事件。</summary>
    Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct);
}

/// <summary>
/// 审批完成事件应用器实现。批准时在同一数据库事务内应用载荷并登记 AppliedAt，
/// 事务提交后持久发布集成事件（<see cref="SupplierApprovedEvent"/> / <see cref="CreditLineAdjustedEvent"/>）；
/// 任一步失败整体回滚且异常上抛（Outbox 重试），保证请求仍为待审批可重放。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="orgService">组织主数据服务。</param>
/// <param name="positionService">岗位主数据服务。</param>
/// <param name="categoryService">商品分类主数据服务。</param>
/// <param name="supplierService">供应商主数据服务。</param>
/// <param name="customerService">客户主数据服务。</param>
/// <param name="eventBus">事件总线（事务提交后持久发布集成事件）。</param>
/// <param name="cache">统一缓存服务（应用后按前缀失效）。</param>
internal sealed class BasApprovalApplier(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IOrgMasterDataService orgService,
    IPositionMasterDataService positionService,
    IItemCategoryService categoryService,
    ISupplierService supplierService,
    ICustomerService customerService,
    IEventBus eventBus,
    ICacheService cache) : IBasApprovalApplier
{
    private static readonly HashSet<string> s_handledBizTypes =
        new(StringComparer.Ordinal)
        {
            OrgMasterDataService.OrgBizType,
            PositionMasterDataService.OverstaffBizType,
            ItemCategoryService.CategoryDisableBizType,
            SupplierService.AdmissionBizType,
            SupplierService.BankChangeBizType,
            CustomerService.AdmissionBizType,
            CustomerService.CreditAdjustBizType,
        };

    /// <summary>判断事件业务类型是否属于 Bas 域（消费者入口过滤，避免无关事件创建作用域）。</summary>
    /// <param name="bizType">业务类型编码。</param>
    /// <returns>属于 Bas 域返回 <see langword="true"/>。</returns>
    internal static bool IsHandledBizType(string bizType) => s_handledBizTypes.Contains(bizType);

    /// <inheritdoc />
    public async Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (!IsHandledBizType(completed.BizType))
        {
            return;
        }

        BasApprovalRequest? request = await repo
            .FindApprovalRequestByInstanceAsync(completed.ApprovalInstanceId, ct).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        // 幂等：非待审批状态（已通过/已驳回/已撤回）直接返回，重复事件不重复生效
        if (!request.IsPending)
        {
            return;
        }

        if (completed.FinalStatus == ApprovalStatus.Approved)
        {
            await ApplyApprovedAsync(request, ct).ConfigureAwait(false);
            await InvalidateCacheAsync(ct).ConfigureAwait(false);
            return;
        }

        if (completed.FinalStatus == ApprovalStatus.Rejected)
        {
            request.MarkRejected();
        }
        else if (completed.FinalStatus == ApprovalStatus.Withdrawn)
        {
            request.MarkWithdrawn();
        }
        else
        {
            return;
        }

        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task ApplyApprovedAsync(BasApprovalRequest request, CancellationToken ct)
    {
        object? applied = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                object? result = await ApplyPayloadAsync(request, token).ConfigureAwait(false);
                request.MarkApplied(DateTimeOffset.UtcNow);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return result;
            },
            ct).ConfigureAwait(false);

        await PublishAppliedEventAsync(applied, ct).ConfigureAwait(false);
    }

    /// <summary>事务提交后持久发布集成事件（Outbox，at-least-once；消费方按 EventId 幂等）。</summary>
    private async Task PublishAppliedEventAsync(object? applied, CancellationToken ct)
    {
        string tenantCode = tenantCtx.TenantCode
            ?? (tenantCtx.TenantId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

        switch (applied)
        {
            case SupplierAdmissionApplied supplier:
                await eventBus.PublishDurableAsync(
                    new SupplierApprovedEvent
                    {
                        SupplierId = supplier.SupplierId,
                        SupplierCode = supplier.SupplierCode,
                        Grade = supplier.Grade,
                        TenantCode = tenantCode,
                    },
                    ct).ConfigureAwait(false);
                break;

            case CustomerCreditApplied customer:
                await eventBus.PublishDurableAsync(
                    new CreditLineAdjustedEvent
                    {
                        CustomerId = customer.CustomerId,
                        CustomerCode = customer.CustomerCode,
                        OldLimit = customer.OldLimit,
                        NewLimit = customer.NewLimit,
                        TenantCode = tenantCode,
                    },
                    ct).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private async Task<object?> ApplyPayloadAsync(BasApprovalRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            throw new InvalidOperationException($"审批请求载荷为空：requestId={request.Id}。");
        }

        if (string.Equals(request.BizType, OrgMasterDataService.OrgBizType, StringComparison.Ordinal))
        {
            SubmitOrgAdjustPayload payload = JsonSerializer.Deserialize<SubmitOrgAdjustPayload>(
                request.PayloadJson, QlxJsonDefaults.PreserveNull)
                ?? throw new InvalidOperationException($"组织调整载荷反序列化为空：requestId={request.Id}。");
            await orgService.ApplyAsync(payload, ct).ConfigureAwait(false);
            return null;
        }

        if (string.Equals(request.BizType, PositionMasterDataService.OverstaffBizType, StringComparison.Ordinal))
        {
            OverstaffPayload payload = JsonSerializer.Deserialize<OverstaffPayload>(
                request.PayloadJson, QlxJsonDefaults.PreserveNull)
                ?? throw new InvalidOperationException($"超编载荷反序列化为空：requestId={request.Id}。");
            await positionService
                .ApplyHeadcountAsync(payload.PositionId, payload.Headcount, ct).ConfigureAwait(false);
            return null;
        }

        if (string.Equals(request.BizType, ItemCategoryService.CategoryDisableBizType, StringComparison.Ordinal))
        {
            CategoryDisablePayload payload = JsonSerializer.Deserialize<CategoryDisablePayload>(
                request.PayloadJson, QlxJsonDefaults.PreserveNull)
                ?? throw new InvalidOperationException($"分类停用载荷反序列化为空：requestId={request.Id}。");
            await categoryService.ApplyDisableAsync(payload.CategoryId, ct).ConfigureAwait(false);
            return null;
        }

        if (string.Equals(request.BizType, SupplierService.AdmissionBizType, StringComparison.Ordinal))
        {
            SupplierAdmissionPayload payload = JsonSerializer.Deserialize<SupplierAdmissionPayload>(
                request.PayloadJson, QlxJsonDefaults.PreserveNull)
                ?? throw new InvalidOperationException($"供应商准入载荷反序列化为空：requestId={request.Id}。");
            return await supplierService.ApplyAdmissionAsync(payload, ct).ConfigureAwait(false);
        }

        if (string.Equals(request.BizType, SupplierService.BankChangeBizType, StringComparison.Ordinal))
        {
            SupplierBankChangePayload payload = JsonSerializer.Deserialize<SupplierBankChangePayload>(
                request.PayloadJson, QlxJsonDefaults.PreserveNull)
                ?? throw new InvalidOperationException($"供应商银行变更载荷反序列化为空：requestId={request.Id}。");
            await supplierService.ApplyBankChangeAsync(payload, ct).ConfigureAwait(false);
            return null;
        }

        if (string.Equals(request.BizType, CustomerService.AdmissionBizType, StringComparison.Ordinal))
        {
            CustomerAdmissionPayload payload = JsonSerializer.Deserialize<CustomerAdmissionPayload>(
                request.PayloadJson, QlxJsonDefaults.PreserveNull)
                ?? throw new InvalidOperationException($"客户准入载荷反序列化为空：requestId={request.Id}。");
            await customerService.ApplyAdmissionAsync(payload, ct).ConfigureAwait(false);
            return null;
        }

        if (string.Equals(request.BizType, CustomerService.CreditAdjustBizType, StringComparison.Ordinal))
        {
            CustomerCreditAdjustPayload payload = JsonSerializer.Deserialize<CustomerCreditAdjustPayload>(
                request.PayloadJson, QlxJsonDefaults.PreserveNull)
                ?? throw new InvalidOperationException($"客户额度调整载荷反序列化为空：requestId={request.Id}。");
            return await customerService.ApplyCreditAdjustAsync(payload, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"未支持的审批业务类型：{request.BizType}。");
    }

    private Task<long> InvalidateCacheAsync(CancellationToken ct)
    {
        long? tenantId = tenantCtx.TenantId;
        if (tenantId is null or <= 0)
        {
            throw new ForbiddenException("未识别到租户上下文");
        }

        return cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{tenantId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:",
            ct);
    }
}
