/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 10:00:00 +08:00 中国标准时间 星期六
 * 描述：租户预配进度 SSE 推送器（尽力而为）：预配后台服务经本组件下发进度事件，
 *       SSE 未注册或推送失败均不影响预配主流程（仅记 Warning）
 */

using System.Globalization;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Sse;
using Qlx.BuildingBlocks.Sse;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>租户预配步骤码（SSE 进度事件 <see cref="SseEventEnvelope.Step"/> 取值）。</summary>
public static class TenantProvisioningSteps
{
    /// <summary>迁移执行中。</summary>
    public const string Migrating = "Migrating";

    /// <summary>全部模块迁移完成（租户已激活）。</summary>
    public const string Completed = "Completed";

    /// <summary>迁移失败（待重试或人工介入）。</summary>
    public const string Failed = "Failed";

    /// <summary>已达最大重试次数，停止自动重试。</summary>
    public const string MaxRetryReached = "MaxRetryReached";
}

/// <summary>租户预配状态码（SSE 进度事件 <see cref="SseEventEnvelope.Status"/> 取值）。</summary>
public static class TenantProvisioningStatuses
{
    /// <summary>预配进行中。</summary>
    public const string Provisioning = "Provisioning";

    /// <summary>预配完成、租户可用。</summary>
    public const string Active = "Active";

    /// <summary>预配失败。</summary>
    public const string Failed = "Failed";
}

/// <summary>
/// 租户预配进度推送器。经 <see cref="ISseConnectionManager"/> 向租户广播进度事件，
/// 信封 <see cref="SseEventEnvelope.BizType"/> 固定为 <c>tenant.provisioning</c>。
/// </summary>
/// <remarks>
/// 尽力而为语义：未注册 SSE 基座（<see cref="ISseConnectionManager"/> 不可解析）时静默跳过；
/// 推送异常只记 Warning 并吞掉，绝不向调用方抛出（预配主流程不因推送失败而中断）。
/// </remarks>
/// <param name="managers">SSE 连接管理器集合（取首个；未注册时为空集合）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class TenantProvisioningProgressPublisher(
    IEnumerable<ISseConnectionManager> managers,
    ILogger<TenantProvisioningProgressPublisher> logger)
{
    private readonly ISseConnectionManager? _manager = managers.FirstOrDefault();
    private readonly ILogger<TenantProvisioningProgressPublisher> _logger = logger;

    /// <summary>
    /// 推送一条租户预配进度事件。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="step">步骤码（见 <see cref="TenantProvisioningSteps"/>）。</param>
    /// <param name="progress">进度百分比 0..100。</param>
    /// <param name="status">状态码（见 <see cref="TenantProvisioningStatuses"/>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task PublishAsync(
        long tenantId, string step, int progress, string status, CancellationToken cancellationToken)
    {
        var manager = _manager;
        if (manager is null)
        {
            return;
        }

        try
        {
            var envelope = new SseEventEnvelope(
                tenantId,
                null,
                step,
                status,
                BizType: "tenant.provisioning",
                BizId: tenantId.ToString(CultureInfo.InvariantCulture),
                Step: step,
                Progress: progress,
                Status: status);
            var sseEvent = new SseEvent(
                0,
                SseEventType.TenantProvisioningProgress,
                SseEventEnvelope.Serialize(envelope),
                DateTimeOffset.UtcNow);

            await manager.PushAsync(sseEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 预配停止/取消：SSE 为尽力而为通道，静默忽略
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPushFailed(ex, tenantId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户 {TenantId} 预配进度推送失败（SSE 为尽力而为，不影响预配）")]
    private partial void LogPushFailed(Exception ex, long tenantId);
}
