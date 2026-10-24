/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 08:15:00 +08:00 中国标准时间 星期五
 * 描述：SSE 实时推送流端点（GET /api/support/messages/stream，text/event-stream）：
 *       Last-Event-ID 断线续传 + 心跳保活 + 断开注销（文档 §2.3：SSE 单向通讯禁止轮询）
 */

using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Sse;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Sse;

namespace Qlx.Modules.Support.Controllers;

/// <summary>
/// SSE 消息流端点。帧格式 <c>id: {EventId}\nevent: {Type}\ndata: {DataJson}\n\n</c>；
/// 每 <see cref="QlxSseOptions.HeartbeatSeconds"/> 秒下发 <c>: ping</c> 注释帧防代理断链；
/// 客户端携带 Last-Event-ID 头续传窗口内遗漏事件（窗外/伪造 Id 直接以全新连接开始，靠收件箱 API 补齐）。
/// </summary>
/// <param name="sseStream">SSE 端点通道（连接注册 / 续传，与连接管理器同单例）。</param>
/// <param name="tenantContext">租户上下文。</param>
/// <param name="currentUser">当前用户上下文。</param>
/// <param name="idObfuscator">ID 混淆器（用户标识 sub claim 为 Hashids 混淆串，边界解码为真实 long）。</param>
/// <param name="options">SSE 基座配置。</param>
[Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[ApiController]
public sealed class MessageStreamController(
    ISseClientChannel sseStream,
    ITenantContext tenantContext,
    ICurrentUserContext currentUser,
    Qlx.BuildingBlocks.Ids.IIdObfuscator idObfuscator,
    IOptions<QlxSseOptions> options) : ControllerBase
{
    private readonly QlxSseOptions _options = options.Value;

    /// <summary>建立 SSE 事件流（长连接；客户端断开或请求中止时服务端自动注销连接）。</summary>
    /// <param name="connectionId">连接 Id（前端生成，缺省服务端生成）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("api/support/messages/stream")]
    [Produces(MediaTypeNames.Text.EventStream)]
    public async Task Stream([FromQuery] string? connectionId, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not > 0)
        {
            throw new ForbiddenException("缺少租户上下文，无法建立消息推送通道。");
        }

        var userIdText = string.IsNullOrWhiteSpace(currentUser.UserId)
            ? throw new UnauthorizedException()
            : currentUser.UserId;

        // 2A 冻结契约的连接注册表以 long 用户 Id 为键：sub claim 为 Hashids 混淆串，此处唯一一次边界解码
        if (!idObfuscator.TryDecode(userIdText, out var userId))
        {
            throw new UnauthorizedException("当前用户标识无法建立消息推送通道。");
        }

        var tenantId = tenantContext.TenantId.Value;
        var connection = string.IsNullOrWhiteSpace(connectionId)
            ? Guid.NewGuid().ToString("N")
            : connectionId;

        Response.ContentType = MediaTypeNames.Text.EventStream;
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // 断线续传：窗口内遗漏事件先补发（窗外/伪造 Last-Event-ID 按全新连接处理，历史走收件箱 API）
        var lastEventId = ParseLastEventId();
        var replay = await sseStream.TryReplayAsync(tenantId, lastEventId, cancellationToken).ConfigureAwait(false);

        await using var lease = await sseStream
            .OpenStreamAsync(userId, connection, tenantId, cancellationToken)
            .ConfigureAwait(false);

        var heartbeatInterval = TimeSpan.FromSeconds(_options.HeartbeatSeconds);
        foreach (var missed in replay.Events)
        {
            await WriteEventAsync(missed, cancellationToken).ConfigureAwait(false);
        }

        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            // 排空当前可读事件（同一批事件一次 flush，减少 syscall）
            var drained = false;
            while (lease.Reader.TryRead(out var ready))
            {
                await WriteEventAsync(ready, cancellationToken).ConfigureAwait(false);
                drained = true;
            }

            if (drained)
            {
                await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // 等待下一事件或心跳到期（二者先到先执行）
            var waitTask = lease.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(waitTask, Task.Delay(heartbeatInterval, cancellationToken))
                .ConfigureAwait(false);
            if (completed == waitTask)
            {
                if (!await waitTask.ConfigureAwait(false))
                {
                    break; // 通道已完成（连接被注销）
                }

                continue;
            }

            await WriteHeartbeatAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>解析 Last-Event-ID 请求头（非法/缺失按全新连接处理）。</summary>
    private long? ParseLastEventId()
        => Request.Headers.TryGetValue("Last-Event-ID", out var values)
            && long.TryParse(values.ToString(), out var parsed)
                ? parsed
                : null;

    /// <summary>写单条事件帧。</summary>
    private async Task WriteEventAsync(SseEvent sseEvent, CancellationToken cancellationToken)
    {
        var frame = $"id: {sseEvent.EventId}\nevent: {sseEvent.Type}\ndata: {sseEvent.DataJson}\n\n";
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写心跳注释帧（SSE 注释以冒号开头，客户端忽略）。</summary>
    private async Task WriteHeartbeatAsync(CancellationToken cancellationToken)
    {
        await Response.Body.WriteAsync(": ping\n\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
