/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 08:05:00 +08:00 中国标准时间 星期五
 * 描述：信息中心 API Controller（/api/support/messages）——Controller 层仅参数提取 + Service 调用 + 响应封装
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Support.Services.Messages;

namespace Qlx.Modules.Support.Controllers;

/// <summary>
/// 信息中心控制器。所有端点需租户级 Token（OpenIddict Validation Scheme），
/// 路由经模块许可过滤（support.message.* 权限点）。
/// 收件箱项 Id / 全局项 Id 为 Hashids 混淆串（按 ID 收发生命周期约定，Service 层解码）。
/// </summary>
/// <param name="messageService">消息服务（2A 冻结契约能力）。</param>
/// <param name="supportMessageService">信息中心门面服务（合并视图 / 全局项状态 / 订阅）。</param>
/// <param name="currentUser">当前用户上下文。</param>
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[ApiController]
[Route("api/support/messages")]
public sealed class MessageController(
    IMessageService messageService,
    ISupportMessageService supportMessageService,
    ICurrentUserContext currentUser) : ControllerBase
{
    // ============ 收件箱 / 已发送 ============

    /// <summary>分页查询当前用户收件箱（本地 ∪ 全局项合并视图；unreadOnly 过滤未读）。</summary>
    /// <param name="pageIndex">页码（默认 1）。</param>
    /// <param name="pageSize">页大小（默认 20，上限取模块配置）。</param>
    /// <param name="keyword">标题/正文关键字（可空）。</param>
    /// <param name="unreadOnly">仅未读。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("inbox")]
    [ProducesResponseType(typeof(ApiResponse<SupportInboxPage>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<SupportInboxPage>> Inbox(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null, [FromQuery] bool unreadOnly = false,
        CancellationToken ct = default)
        => ApiResponse<SupportInboxPage>.Ok(await supportMessageService
            .QueryInboxAsync(pageIndex, pageSize, keyword, unreadOnly, ct).ConfigureAwait(false));

    /// <summary>分页查询当前用户已发送消息。</summary>
    /// <param name="pageIndex">页码（默认 1）。</param>
    /// <param name="pageSize">页大小（默认 20）。</param>
    /// <param name="keyword">标题/正文关键字（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("sent")]
    public async Task<ApiResponse<SupportMessagePage>> Sent(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null, CancellationToken ct = default)
        => ApiResponse<SupportMessagePage>.Ok(await supportMessageService
            .QuerySentAsync(pageIndex, pageSize, keyword, ct).ConfigureAwait(false));

    /// <summary>查询当前用户未读数。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("unread-count")]
    public async Task<ApiResponse<int>> UnreadCount(CancellationToken ct)
        => ApiResponse<int>.Ok(await messageService
            .GetUnreadCountAsync(CurrentUserId, ct).ConfigureAwait(false));

    /// <summary>查询消息渠道发送记录（只读 msg_send_logs，仅发送方可查）。</summary>
    /// <param name="messageId">消息 Id（混淆串）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("sent/{messageId}/send-logs")]
    public async Task<ApiResponse<IReadOnlyList<SupportSendLogItem>>> SendLogs(string messageId, CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupportSendLogItem>>.Ok(
            await supportMessageService.ListSendLogsAsync(messageId, ct).ConfigureAwait(false));

    /// <summary>人工重试外部渠道投递（仅发送方；绕过自动重试上限强制尝试一轮）。</summary>
    /// <param name="messageId">消息 Id（混淆串）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("sent/{messageId}/retry")]
    [AuditOperation(QlxModuleCodes.Support, "MESSAGE_RETRY_DELIVERY", "人工重试了消息渠道投递")]
    public async Task<ApiResponse> RetryFailed(string messageId, CancellationToken ct)
    {
        await supportMessageService.RetryFailedMessageAsync(messageId, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    // ============ 已读 / 删除 ============

    /// <summary>标记单条收件箱消息已读。</summary>
    /// <param name="inboxId">收件箱项 Id（混淆串）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("inbox/{inboxId}/read")]
    public async Task<ApiResponse> MarkRead(string inboxId, CancellationToken ct)
    {
        await messageService.MarkReadAsync(inboxId, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    /// <summary>标记当前用户全部消息已读。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("read-all")]
    public async Task<ApiResponse> MarkAllRead(CancellationToken ct)
    {
        await messageService.MarkAllReadAsync(CurrentUserId, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    /// <summary>个人删除收件箱项（本地项隐藏；全局项请走 global/{id}/state）。</summary>
    /// <param name="inboxId">收件箱项 Id（混淆串）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("inbox/{inboxId}/delete")]
    [AuditOperation(QlxModuleCodes.Support, "MESSAGE_INBOX_DELETE", "删除了收件箱消息")]
    public async Task<ApiResponse> DeleteInboxItem(string inboxId, CancellationToken ct)
    {
        await supportMessageService.DeleteInboxItemAsync(inboxId, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    // ============ 全局项状态 ============

    /// <summary>记录全局项用户状态（READ / ACCEPTED / DELETED；ForceAccept 公告拒绝删除）。</summary>
    /// <param name="globalId">全局项 Id（混淆串）。</param>
    /// <param name="payload">状态入参。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("global/{globalId}/state")]
    [AuditOperation(QlxModuleCodes.Support, "MESSAGE_GLOBAL_STATE", "记录了全局项用户状态")]
    public async Task<ApiResponse> MarkGlobalItem(string globalId, [FromBody] GlobalItemStatePayload payload, CancellationToken ct)
    {
        await supportMessageService.MarkGlobalItemAsync(globalId, payload.State, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    // ============ 订阅 ============

    /// <summary>查询当前用户订阅配置清单（缺失类别按默认值自动补齐）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("subscriptions")]
    public async Task<ApiResponse<IReadOnlyList<SupportSubscriptionItem>>> Subscriptions(CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupportSubscriptionItem>>.Ok(
            await supportMessageService.ListSubscriptionsAsync(ct).ConfigureAwait(false));

    /// <summary>调整订阅开关（TODO/URGE 强制订阅拒绝关闭）。</summary>
    /// <param name="msgType">消息类别。</param>
    /// <param name="payload">开关入参。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("subscriptions/{msgType}/toggle")]
    [AuditOperation(QlxModuleCodes.Support, "MESSAGE_SUBSCRIPTION_TOGGLE", "调整了消息订阅开关")]
    public async Task<ApiResponse> ToggleSubscription(string msgType, [FromBody] SubscriptionTogglePayload payload, CancellationToken ct)
    {
        await supportMessageService.ToggleSubscriptionAsync(msgType, payload.Enabled, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    /// <summary>当前用户 Id（未认证即拒绝；业务校验在 Service 层，此处仅取值）。</summary>
    private string CurrentUserId => string.IsNullOrWhiteSpace(currentUser.UserId)
        ? throw new UnauthorizedException()
        : currentUser.UserId;
}

/// <summary>全局项状态记录入参。</summary>
/// <param name="State">目标状态（READ / ACCEPTED / DELETED）。</param>
public sealed record GlobalItemStatePayload(string State);

/// <summary>订阅开关入参。</summary>
/// <param name="Enabled">目标开关。</param>
public sealed record SubscriptionTogglePayload(bool Enabled);
