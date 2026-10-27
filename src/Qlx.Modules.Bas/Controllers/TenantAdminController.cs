/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：租户自管理域 API Controller（/api/tenant）——Controller 层仅参数校验 + Service 调用 + ApiResponse 包装。
 *       路由/查询参数中的业务 Id 声明为 long，由全局 HashidsLongModelBinder 自动解码（Controller 不做编解码）。
 *       业务规则（根公司不可停用、编码唯一、内置角色保护等）由 TenantAdminService 承载。
 *       全部 async/await 模式，确保 CancellationToken 传播正确（禁用 ContinueWith 包装）。
 */

using Microsoft.AspNetCore.Mvc;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 租户自管理域控制器。所有端点需租户级 Token（OpenIddict Validation Scheme + 租户上下文前置策略），
/// 动作级按权限点策略拦截（TenantAdmin 由 Handler 零计算旁路）。
/// </summary>
[ApiController]
[Route("api/tenant")]
public sealed class TenantAdminController(
    ITenantAdminService service) : ControllerBase
{
    // ============ Organization ============

    [HttpGet("organizations/tree")]
    public async Task<ApiResponse<IReadOnlyList<OrganizationFlatRow>>> GetOrgTree(CancellationToken ct)
    {
        return ApiResponse<IReadOnlyList<OrganizationFlatRow>>.Ok(await service.ListAllOrgsAsync(ct));
    }

    [HttpPost("organizations")]
    public async Task<ApiResponse<OrganizationFlatRow>> CreateOrg(
        [FromBody] SaveOrganizationPayload payload, CancellationToken ct)
    {
        return ApiResponse<OrganizationFlatRow>.Ok(await service.CreateOrgAsync(payload, ct));
    }

    [HttpPut("organizations/{id}")]
    public async Task<ApiResponse<OrganizationFlatRow>> UpdateOrg(
        long id, [FromBody] SaveOrganizationPayload payload, CancellationToken ct)
    {
        return ApiResponse<OrganizationFlatRow>.Ok(await service.UpdateOrgAsync(id, payload, ct));
    }

    [HttpPost("organizations/{id}/toggle")]
    public async Task<ApiResponse<OrganizationFlatRow>> ToggleOrgStatus(long id, CancellationToken ct)
    {
        return ApiResponse<OrganizationFlatRow>.Ok(await service.ToggleOrgStatusAsync(id, ct));
    }

    // ============ Position ============

    [HttpGet("positions")]
    public async Task<ApiResponse<PagedResult<PositionRow>>> GetPositions(
        [FromQuery] long? orgId,
        [FromQuery] string? keyword,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return ApiResponse<PagedResult<PositionRow>>.Ok(await service.QueryPositionsAsync(orgId, keyword, request, ct));
    }

    [HttpPost("positions")]
    public async Task<ApiResponse<PositionRow>> SavePosition(
        [FromBody] SavePositionPayload payload, CancellationToken ct)
    {
        return ApiResponse<PositionRow>.Ok(await service.SavePositionAsync(payload, ct));
    }

    [HttpPost("positions/{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_POSITION_TOGGLE", "变更了岗位启停状态", TargetType = "Position", TargetIdParam = "id")]
    public async Task<ApiResponse<PositionRow>> TogglePositionStatus(long id, CancellationToken ct)
    {
        return ApiResponse<PositionRow>.Ok(await service.TogglePositionStatusAsync(id, ct));
    }

    // ============ Member ============

    [HttpGet("members")]
    public async Task<ApiResponse<PagedResult<MemberRow>>> GetMembers(
        [FromQuery] long? orgId,
        [FromQuery] int? memberRole,
        [FromQuery] int? memberStatus,
        [FromQuery] string? keyword,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return ApiResponse<PagedResult<MemberRow>>.Ok(await service.QueryMembersAsync(orgId, memberRole, memberStatus, keyword, request, ct));
    }

    [HttpPost("members/invite")]
    public async Task<ApiResponse<InviteMemberResult?>> InviteMember(
        [FromBody] InviteMemberPayload payload, CancellationToken ct)
    {
        (long TenantMemberId, long PortalUserId)? result = await service.InviteMemberAsync(payload, ct);
        return ApiResponse<InviteMemberResult?>.Ok(
            result is { } r ? new InviteMemberResult(r.TenantMemberId, r.PortalUserId) : null);
    }

    [HttpGet("members/{id}")]
    public async Task<ApiResponse<MemberRow>> GetMemberDetail(long id, CancellationToken ct)
    {
        return ApiResponse<MemberRow>.Ok(await service.GetMemberDetailAsync(id, ct));
    }

    [HttpPost("members/{id}/toggle")]
    public async Task<ApiResponse> ToggleMemberStatus(long id, CancellationToken ct)
    {
        await service.ToggleMemberStatusAsync(id, ct);
        return ApiResponse.Ok();
    }

    [HttpPost("members/{id}/reset")]
    public async Task<ApiResponse> ResetMemberPassword(
        long id, [FromBody] ResetPasswordPayload payload, CancellationToken ct)
    {
        await service.ResetMemberPasswordAsync(id, payload.NewPassword, ct);
        return ApiResponse.Ok();
    }

    [HttpDelete("members/{id}")]
    public async Task<ApiResponse> RemoveMember(long id, CancellationToken ct)
    {
        await service.RemoveMemberAsync(id, ct);
        return ApiResponse.Ok();
    }

    [HttpPost("members/{id}/resend-invite")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_MEMBER_RESEND_INVITE", "重发了成员邀请", TargetType = "TenantMember", TargetIdParam = "id")]
    public async Task<ApiResponse> ResendMemberInvite(long id, CancellationToken ct)
    {
        await service.ResendMemberInviteAsync(id, ct);
        return ApiResponse.Ok();
    }

    [HttpPost("members/import")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_MEMBER_IMPORT", "批量导入了成员")]
    public async Task<ApiResponse<ImportMembersResult>> ImportMembers(
        [FromBody] IReadOnlyList<ImportMemberRow> rows, CancellationToken ct)
    {
        return ApiResponse<ImportMembersResult>.Ok(await service.ImportMembersAsync(rows, ct));
    }

    [HttpGet("members/{id}/login-logs")]
    public async Task<ApiResponse<PagedResult<MemberLoginLogRow>>> GetMemberLoginLogs(
        long id, [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return ApiResponse<PagedResult<MemberLoginLogRow>>.Ok(await service.GetMemberLoginLogsAsync(id, request, ct));
    }

    // ============ Role ============

    [HttpGet("roles")]
    public async Task<ApiResponse<IReadOnlyList<RoleRow>>> GetRoles(CancellationToken ct)
    {
        return ApiResponse<IReadOnlyList<RoleRow>>.Ok(await service.ListRolesAsync(ct));
    }

    [HttpPost("roles")]
    public async Task<ApiResponse<RoleRow>> CreateRole([FromBody] SaveRolePayload payload, CancellationToken ct)
    {
        return ApiResponse<RoleRow>.Ok(await service.CreateRoleAsync(payload, ct));
    }

    [HttpPut("roles/{id}")]
    public async Task<ApiResponse<RoleRow>> UpdateRole(
        long id, [FromBody] SaveRolePayload payload, CancellationToken ct)
    {
        return ApiResponse<RoleRow>.Ok(await service.UpdateRoleAsync(id, payload, ct));
    }

    [HttpDelete("roles/{id}")]
    public async Task<ApiResponse> DeleteRole(long id, CancellationToken ct)
    {
        await service.DeleteRoleAsync(id, ct);
        return ApiResponse.Ok();
    }

    [HttpPut("roles/{id}/permissions")]
    public async Task<ApiResponse> SaveRolePermissions(
        long id, [FromBody] IReadOnlyList<string> permissionCodes, CancellationToken ct)
    {
        await service.SaveRolePermissionsAsync(id, permissionCodes, ct);
        return ApiResponse.Ok();
    }

    [HttpGet("roles/{id}/permissions")]
    public async Task<ApiResponse<IReadOnlyList<string>>> GetRolePermissions(long id, CancellationToken ct)
    {
        return ApiResponse<IReadOnlyList<string>>.Ok(await service.GetRolePermissionCodesAsync(id, ct));
    }

    [HttpGet("roles/{id}/members")]
    public async Task<ApiResponse<IReadOnlyList<string>>> GetRoleMembers(long id, CancellationToken ct)
    {
        return ApiResponse<IReadOnlyList<string>>.Ok(await service.GetRoleMemberIdsAsync(id, ct));
    }

    [HttpPut("roles/{id}/members")]
    public async Task<ApiResponse> SaveRoleMembers(
        long id, [FromBody] IReadOnlyList<string> memberIds, CancellationToken ct)
    {
        await service.SaveRoleMembersAsync(id, memberIds, ct);
        return ApiResponse.Ok();
    }

    // ============ 只读（来自 Portal 库）============

    [HttpGet("permissions")]
    public async Task<ApiResponse<IReadOnlyList<PermissionGroupRow>>> GetPermissions(CancellationToken ct)
    {
        return ApiResponse<IReadOnlyList<PermissionGroupRow>>.Ok(await service.ListPermissionGroupsAsync(ct));
    }

    [HttpGet("modules")]
    public async Task<ApiResponse<IReadOnlyList<TenantModuleRow>>> GetTenantModules(CancellationToken ct)
    {
        return ApiResponse<IReadOnlyList<TenantModuleRow>>.Ok(await service.ListTenantModulesAsync(ct));
    }
}

/// <summary>成员重置密码入参。</summary>
public sealed record ResetPasswordPayload(string NewPassword);
