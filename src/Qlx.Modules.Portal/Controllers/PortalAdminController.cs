/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 20:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：门户管理端端点（/api/portal/admin）：租户 / 用户 / 模块 / 工作台 / 租户升级，仅门户管理员可访问
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
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Sse;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 门户管理端端点。契约与前端 <c>src/Qlx.Web/src/mock/portalAdmin.ts</c> 文件头声明一一对应，
/// 仅门户管理员（身份级 Token 携带 is_portal_admin=1）可访问。
/// </summary>
/// <param name="tenantService">租户管理服务。</param>
/// <param name="userService">门户用户管理服务。</param>
/// <param name="moduleService">模块目录服务。</param>
/// <param name="overviewService">工作台概览服务。</param>
/// <param name="upgradeService">租户升级服务。</param>
/// <param name="sseStream">SSE 端点通道（门户侧预配进度流，与连接管理器同单例）。</param>
/// <param name="currentUser">当前用户上下文（token sub claim 为 Hashids 混淆串）。</param>
/// <param name="idObfuscator">ID 混淆器（边界解码租户/用户标识）。</param>
/// <param name="sseOptions">SSE 基座配置（心跳周期）。</param>
[ApiController]
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Policy = QlxAuthConstants.Policies.PortalAdmin)]
[Route("api/portal/admin")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class PortalAdminController(
    IPortalTenantAdminService tenantService,
    IPortalUserAdminService userService,
    IPortalModuleAdminService moduleService,
    IPortalOverviewService overviewService,
    IPortalTenantUpgradeService upgradeService,
    ISseClientChannel sseStream,
    ICurrentUserContext currentUser,
    IIdObfuscator idObfuscator,
    IOptions<QlxSseOptions> sseOptions) : ControllerBase
{
    private readonly QlxSseOptions _sseOptions = sseOptions.Value;

    // =============== 租户 ===============

    /// <summary>分页查询租户。</summary>
    [HttpGet("tenants")]
    public async Task<ApiResponse<PortalPagedResult<PortalTenantItem>>> QueryTenants(
        [FromQuery] PortalTenantQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await tenantService.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalPagedResult<PortalTenantItem>>.Ok(page);
    }

    /// <summary>新建租户（自动建库 + 默认许可 + 初始管理员）。</summary>
    [HttpPost("tenants")]
    public async Task<ApiResponse<PortalTenantItem>> CreateTenant(
        [FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await tenantService.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalTenantItem>.Ok(item, "租户创建成功，数据库初始化中");
    }

    /// <summary>启用 / 停用租户。</summary>
    [HttpPost("tenants/{id}/toggle-status")]
    public async Task<ApiResponse<PortalTenantItem>> ToggleTenantStatus(
        string id, CancellationToken cancellationToken)
    {
        var item = await tenantService.ToggleStatusAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalTenantItem>.Ok(item, "状态已更新");
    }

    /// <summary>租户详情（含许可变更记录）。</summary>
    [HttpGet("tenants/{id}")]
    public async Task<ApiResponse<PortalTenantDetailResponse>> GetTenant(
        string id, CancellationToken cancellationToken)
    {
        var detail = await tenantService.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalTenantDetailResponse>.Ok(detail);
    }

    /// <summary>保存租户模块授权（整单提交）。</summary>
    [HttpPut("tenants/{id}/modules")]
    public async Task<ApiResponse> SaveTenantModules(
        string id, [FromBody] SaveTenantLicenseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await tenantService.SaveLicenseAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ApiResponse.Ok("授权已保存");
    }

    /// <summary>租户成员列表（详情页只读）。</summary>
    [HttpGet("tenants/{id}/members")]
    public async Task<ApiResponse<IReadOnlyList<TenantMembershipItem>>> ListTenantMembers(
        string id, CancellationToken cancellationToken)
    {
        var items = await tenantService.ListMembersAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<TenantMembershipItem>>.Ok(items);
    }

    // =============== 租户升级（共享库 → 独立库） ===============

    /// <summary>发起升级：登记任务由后台流水线执行（异步完成，不阻塞请求）。</summary>
    [HttpPost("tenants/{id}/promote-to-isolated")]
    public async Task<ApiResponse<TenantUpgradeJobItem>> PromoteToIsolated(
        string id, CancellationToken cancellationToken)
    {
        var item = await upgradeService.PromoteToIsolatedAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<TenantUpgradeJobItem>.Ok(item, "升级任务已登记，后台执行中");
    }

    /// <summary>查询租户最近一次升级任务进度。</summary>
    [HttpGet("tenants/{id}/upgrade-job")]
    public async Task<ApiResponse<TenantUpgradeJobItem?>> GetUpgradeJob(
        string id, CancellationToken cancellationToken)
    {
        var item = await upgradeService.GetUpgradeJobAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<TenantUpgradeJobItem?>.Ok(item);
    }

    /// <summary>重试失败的升级任务（断点续跑）。</summary>
    [HttpPost("tenants/{id}/upgrade-job/retry")]
    public async Task<ApiResponse<TenantUpgradeJobItem>> RetryUpgradeJob(
        string id, CancellationToken cancellationToken)
    {
        var item = await upgradeService.RetryUpgradeJobAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<TenantUpgradeJobItem>.Ok(item, "升级任务已重新进入待处理队列");
    }

    // =============== 门户用户 ===============

    /// <summary>分页查询门户用户。</summary>
    [HttpGet("users")]
    public async Task<ApiResponse<PortalPagedResult<PortalUserItem>>> QueryUsers(
        [FromQuery] PortalUserQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await userService.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalPagedResult<PortalUserItem>>.Ok(page);
    }

    /// <summary>启用 / 停用门户用户。</summary>
    [HttpPost("users/{id}/toggle-status")]
    public async Task<ApiResponse<PortalUserItem>> ToggleUserStatus(
        string id, CancellationToken cancellationToken)
    {
        var item = await userService.ToggleStatusAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalUserItem>.Ok(item, "状态已更新");
    }

    /// <summary>重置门户用户密码。</summary>
    [HttpPost("users/{id}/reset-password")]
    public async Task<ApiResponse> ResetUserPassword(
        string id, [FromBody] ResetPortalUserPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await userService.ResetPasswordAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ApiResponse.Ok("密码已重置，用户需重新登录");
    }

    /// <summary>用户详情（成员关系 + 登录日志）。</summary>
    [HttpGet("users/{id}")]
    public async Task<ApiResponse<PortalUserDetailResponse>> GetUser(
        string id, CancellationToken cancellationToken)
    {
        var detail = await userService.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalUserDetailResponse>.Ok(detail);
    }

    // =============== 模块目录 / 工作台 ===============

    /// <summary>模块目录（含授权租户数与功能点）。</summary>
    [HttpGet("modules")]
    public async Task<ApiResponse<IReadOnlyList<PortalModuleItem>>> ListModules(CancellationToken cancellationToken)
    {
        var items = await moduleService.ListModulesAsync(cancellationToken).ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<PortalModuleItem>>.Ok(items);
    }

    /// <summary>全平台许可变更记录。</summary>
    [HttpGet("modules/license-records")]
    public async Task<ApiResponse<IReadOnlyList<LicenseRecordItem>>> ListLicenseRecords(
        CancellationToken cancellationToken)
    {
        var items = await moduleService.ListLicenseRecordsAsync(cancellationToken).ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<LicenseRecordItem>>.Ok(items);
    }

    /// <summary>门户管理工作台概览。</summary>
    [HttpGet("workbench/overview")]
    public async Task<ApiResponse<PortalWorkbenchOverviewResponse>> GetOverview(CancellationToken cancellationToken)
    {
        var overview = await overviewService.GetOverviewAsync(cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalWorkbenchOverviewResponse>.Ok(overview);
    }

    // =============== 租户预配进度流（SSE） ===============

    /// <summary>
    /// 租户预配进度流。门户管理员 token 无租户上下文，无法复用 <c>/api/support/messages/stream</c>，
    /// 故在门户侧建立专用通道：连接注册到被订阅租户 Id 上，仅转发 <see cref="SseEventType.TenantProvisioningProgress"/>
    /// 类型事件（租户业务事件一律丢弃，避免消息/审批串流到门户端）。
    /// </summary>
    /// <param name="id">租户 Id（Hashids 混淆串）。</param>
    /// <param name="connectionId">连接 Id（前端生成，缺省服务端生成）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("tenants/{id}/provisioning-stream")]
    [Produces(MediaTypeNames.Text.EventStream)]
    public async Task StreamTenantProvisioning(
        string id, [FromQuery] string? connectionId, CancellationToken cancellationToken)
    {
        if (!idObfuscator.TryDecode(id, out var tenantId) || tenantId <= 0)
        {
            throw new UnauthorizedException("租户标识无法建立预配进度通道。");
        }

        var userIdText = string.IsNullOrWhiteSpace(currentUser.UserId)
            ? throw new UnauthorizedException("当前用户标识无法建立预配进度通道。")
            : currentUser.UserId;

        // sub claim 为 Hashids 混淆串：连接注册表以真实 long 用户 Id 为键，此处边界解码
        if (!idObfuscator.TryDecode(userIdText, out var userId))
        {
            throw new UnauthorizedException("当前用户标识无法建立预配进度通道。");
        }

        var connection = string.IsNullOrWhiteSpace(connectionId)
            ? Guid.NewGuid().ToString("N")
            : connectionId;

        Response.ContentType = MediaTypeNames.Text.EventStream;
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // 断线续传：仅补发预配进度类事件（窗外/伪造 Last-Event-ID 按全新连接处理）
        var lastEventId = ParseLastEventId();
        var replay = await sseStream.TryReplayAsync(tenantId, lastEventId, cancellationToken).ConfigureAwait(false);

        await using var lease = await sseStream
            .OpenStreamAsync(userId, connection, tenantId, cancellationToken)
            .ConfigureAwait(false);

        var heartbeatInterval = TimeSpan.FromSeconds(_sseOptions.HeartbeatSeconds);
        foreach (var missed in replay.Events)
        {
            if (missed.Type == SseEventType.TenantProvisioningProgress)
            {
                await WriteEventAsync(missed, cancellationToken).ConfigureAwait(false);
            }
        }

        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            // 排空当前可读事件（同一批事件一次 flush，减少 syscall；非预配进度类事件直接丢弃）
            var drained = false;
            while (lease.Reader.TryRead(out var ready))
            {
                if (ready.Type != SseEventType.TenantProvisioningProgress)
                {
                    continue;
                }

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
    /// <param name="sseEvent">事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WriteEventAsync(SseEvent sseEvent, CancellationToken cancellationToken)
    {
        var frame = $"id: {sseEvent.EventId}\nevent: {sseEvent.Type}\ndata: {sseEvent.DataJson}\n\n";
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写心跳注释帧（SSE 注释以冒号开头，客户端忽略）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WriteHeartbeatAsync(CancellationToken cancellationToken)
    {
        await Response.Body.WriteAsync(": ping\n\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
