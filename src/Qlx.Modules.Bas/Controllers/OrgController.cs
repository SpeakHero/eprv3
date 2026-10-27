/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 14:10:00 +08:00 中国标准时间 星期日
 * 描述：组织架构主数据 API（/api/bas/orgs）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 组织架构主数据控制器。业务 Id 声明为 <see cref="long"/>，由全局 HashidsModelBinder 自动解码；
/// 编码由后端统一发号器生成，前端不传码（N-1）。
/// </summary>
/// <param name="service">组织主数据服务。</param>
[ApiController]
[Route("api/bas/orgs")]
public sealed class OrgController(IOrgMasterDataService service) : ControllerBase
{
    /// <summary>组织扁平列表；传 <paramref name="asOfDate"/> 时返回该日期生效的历史版本。</summary>
    /// <param name="asOfDate">回看日期（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>组织扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<OrgRow>>> GetList(
        [FromQuery] DateOnly? asOfDate, CancellationToken ct)
        => ApiResponse<IReadOnlyList<OrgRow>>.Ok(await service.ListAsync(asOfDate, ct));

    /// <summary>组织详情。</summary>
    /// <param name="id">组织 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>组织扁平行。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<OrgRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<OrgRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>组织版本历史。</summary>
    /// <param name="id">组织 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>版本历史行列表。</returns>
    [HttpGet("{id}/history")]
    public async Task<ApiResponse<IReadOnlyList<OrgHistoryRow>>> History(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<OrgHistoryRow>>.Ok(await service.GetHistoryAsync(id, ct));

    /// <summary>创建组织。</summary>
    /// <param name="payload">创建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的组织扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_ORG_CREATE", "创建了组织机构", TargetType = "Organization")]
    public async Task<ApiResponse<OrgRow>> Create(
        [FromBody] SaveOrgPayload payload, CancellationToken ct)
        => ApiResponse<OrgRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>更新组织。</summary>
    /// <param name="id">组织 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的组织扁平行。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_ORG_UPDATE", "修改了组织机构", TargetType = "Organization", TargetIdParam = "id")]
    public async Task<ApiResponse<OrgRow>> Update(
        long id, [FromBody] SaveOrgPayload payload, CancellationToken ct)
        => ApiResponse<OrgRow>.Ok(await service.UpdateAsync(id, payload, ct));

    /// <summary>停用/启用组织。</summary>
    /// <param name="id">组织 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的组织扁平行。</returns>
    [HttpPost("{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_ORG_TOGGLE", "变更了组织机构启停状态", TargetType = "Organization", TargetIdParam = "id")]
    public async Task<ApiResponse<OrgRow>> Toggle(long id, CancellationToken ct)
        => ApiResponse<OrgRow>.Ok(await service.ToggleAsync(id, ct));

    /// <summary>合并组织（源组织归档）。</summary>
    /// <param name="id">源组织 Id。</param>
    /// <param name="payload">合并入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>归档后的源组织扁平行。</returns>
    [HttpPost("{id}/merge")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_ORG_MERGE", "合并了组织机构", TargetType = "Organization", TargetIdParam = "id")]
    public async Task<ApiResponse<OrgRow>> Merge(
        long id, [FromBody] MergeOrgPayload payload, CancellationToken ct)
        => ApiResponse<OrgRow>.Ok(await service.MergeAsync(id, payload.TargetOrgId, ct));

    /// <summary>提交组织调整审批申请（挂起，审批通过后生效）。</summary>
    /// <param name="payload">调整申请入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    [HttpPost("requests")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_ORG_ADJUST_REQUEST", "提交了组织调整审批申请", TargetType = "Organization")]
    public async Task<ApiResponse<ApprovalRequestRow>> CreateRequest(
        [FromBody] SubmitOrgAdjustPayload payload, CancellationToken ct)
        => ApiResponse<ApprovalRequestRow>.Ok(await service.SubmitAdjustRequestAsync(payload, ct));
}
