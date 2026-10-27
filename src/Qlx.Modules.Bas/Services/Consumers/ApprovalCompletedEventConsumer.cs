/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 14:05:00 +08:00 中国标准时间 星期日
 * 描述：审批完成事件消费者——按事件 TenantCode 回填租户上下文后派发 Bas 审批应用器
 */

using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Tenancy;

namespace Qlx.Modules.Bas.Services.Consumers;

/// <summary>
/// 审批完成事件消费者。<see cref="Qlx.BuildingBlocks.EventBus.Outbox.OutboxDispatcher"/> 在无租户上下文的
/// Worker 作用域内派发，故本类仅依赖作用域工厂，先回填租户再解析应用器（DbContext 构造时读取 tenantId）。
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

        // 仅处理 Bas 域已登记业务类型；无关事件不创建作用域
        if (!BasApprovalApplier.IsHandledBizType(eventData.BizType))
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

        IBasApprovalApplier applier = scope.ServiceProvider.GetRequiredService<IBasApprovalApplier>();
        await applier.ApplyAsync(eventData, cancellationToken).ConfigureAwait(false);
    }
}
