/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 20:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：门户管理端端点（/api/portal/admin）：租户 / 用户 / 模块 / 工作台 / 租户升级，仅门户管理员可访问
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 门户管理端端点。契约与前端 <c>src/Qlx.Web/src/mock/portalAdmin.ts</c> 文件头声明一一对应，
/// 仅门户管理员（身份级 Token 携带 is_portal_admin=1）可访问。
/// </summary>
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
    IPortalTenantUpgradeService upgradeService) : ControllerBase
{
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
}
