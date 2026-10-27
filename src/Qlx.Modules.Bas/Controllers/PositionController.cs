/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 14:15:00 +08:00 中国标准时间 星期日
 * 描述：岗位主数据 API（/api/bas/positions）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 岗位主数据控制器。业务 Id 由全局 HashidsModelBinder 自动解码；岗位编码由后端生成，前端不传码（N-1）。
/// </summary>
/// <param name="service">岗位主数据服务。</param>
[ApiController]
[Route("api/bas/positions")]
public sealed class PositionController(IPositionMasterDataService service) : ControllerBase
{
    /// <summary>岗位分页列表。</summary>
    /// <param name="orgId">组织 Id（含全部下级）。</param>
    /// <param name="keyword">名称/编码/说明/职责关键字。</param>
    /// <param name="request">分页参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>岗位分页结果。</returns>
    [HttpGet]
    public async Task<ApiResponse<PagedResult<PositionMasterRow>>> GetList(
        [FromQuery] long? orgId,
        [FromQuery] string? keyword,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
        => ApiResponse<PagedResult<PositionMasterRow>>.Ok(await service.ListAsync(orgId, keyword, request, ct));

    /// <summary>新建或更新岗位。</summary>
    /// <param name="payload">保存入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存后的岗位扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_POSITION_SAVE", "保存了岗位信息", TargetType = "Position")]
    public async Task<ApiResponse<PositionMasterRow>> Save(
        [FromBody] SavePositionCommand payload, CancellationToken ct)
        => ApiResponse<PositionMasterRow>.Ok(await service.SaveAsync(payload, ct));

    /// <summary>停用/启用岗位。</summary>
    /// <param name="id">岗位 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的岗位扁平行。</returns>
    [HttpPost("{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_POSITION_TOGGLE", "变更了岗位启停状态", TargetType = "Position", TargetIdParam = "id")]
    public async Task<ApiResponse<PositionMasterRow>> Toggle(long id, CancellationToken ct)
        => ApiResponse<PositionMasterRow>.Ok(await service.ToggleAsync(id, ct));

    /// <summary>提交超编审批申请（挂起，审批通过后调整编制）。</summary>
    /// <param name="id">岗位 Id。</param>
    /// <param name="payload">申请入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    [HttpPost("{id}/overstaff-requests")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_POSITION_OVERSTAFF_REQUEST", "提交了岗位超编审批申请", TargetType = "Position", TargetIdParam = "id")]
    public async Task<ApiResponse<ApprovalRequestRow>> CreateOverstaffRequest(
        long id, [FromBody] SubmitOverstaffPayload payload, CancellationToken ct)
        => ApiResponse<ApprovalRequestRow>.Ok(await service.SubmitOverstaffRequestAsync(id, payload, ct));
}
