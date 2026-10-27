/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 修改时间：2026-10-26 20:00:00 +08:00 中国标准时间 星期一
 * 描述：审批完成事件消费者——按事件 TenantCode 回填租户上下文后派发 Stk 审批应用
 *       （商品 stk.item.* / 期初 stk.opening.* / 成本调整 stk.cost.adjust）
 */

using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Tenancy;

namespace Qlx.Modules.Stk.Services.Consumers;

/// <summary>
/// Stk 审批完成事件消费者。<see cref="Qlx.BuildingBlocks.EventBus.Outbox.OutboxDispatcher"/> 在无租户上下文的
/// Worker 作用域内派发，故本类仅依赖作用域工厂，先回填租户再解析商品服务（DbContext 构造时读取 tenantId）。
/// </summary>
/// <param name="scopeFactory">作用域工厂。</param>
/// <remarks>
/// 必须为 <c>public</c>：<c>OutboxDispatcher</c> 以 <c>dynamic</c> 跨程序集调用 <c>ConsumeAsync</c>，
/// 非公开类型会被运行时绑定器降级为 <c>object</c> 导致派发失败（架构测试守护）。
/// </remarks>
public sealed class ApprovalCompletedEventConsumer(IServiceScopeFactory scopeFactory)
    : IIntegrationEventConsumer<ApprovalCompletedEvent>
{
    /// <inheritdoc />
    public async Task ConsumeAsync(ApprovalCompletedEvent eventData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // 仅处理 Stk 域业务类型；无关事件不创建作用域
        if (string.IsNullOrWhiteSpace(eventData.BizType))
        {
            return;
        }

        bool isItem = eventData.BizType.StartsWith(
            ItemService.ItemBizTypePrefix, StringComparison.Ordinal);
        bool isOpening = eventData.BizType.StartsWith(
            OpeningService.OpeningBizTypePrefix, StringComparison.Ordinal);
        bool isCostAdjust = eventData.BizType.StartsWith(
            CostAdjustService.CostAdjustBizTypePrefix, StringComparison.Ordinal);
        if (!isItem && !isOpening && !isCostAdjust)
        {
            return;
        }

        if (!long.TryParse(
                eventData.TenantCode, NumberStyles.None, CultureInfo.InvariantCulture, out long tenantId)
            || tenantId <= 0)
        {
            throw new InvalidOperationException(
                $"审批完成事件缺少可解析的租户标识：eventId={eventData.EventId}。");
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ITenantContext tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenantId(tenantId);
        tenantContext.SetTenantCode(eventData.TenantCode);

        if (isItem)
        {
            IItemService itemService = scope.ServiceProvider.GetRequiredService<IItemService>();
            await itemService.ApplyAsync(eventData, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (isOpening)
        {
            IOpeningService openingService = scope.ServiceProvider.GetRequiredService<IOpeningService>();
            await openingService.ApplyAsync(eventData, cancellationToken).ConfigureAwait(false);
            return;
        }

        ICostAdjustService costAdjustService =
            scope.ServiceProvider.GetRequiredService<ICostAdjustService>();
        await costAdjustService.ApplyAsync(eventData, cancellationToken).ConfigureAwait(false);
    }
}
