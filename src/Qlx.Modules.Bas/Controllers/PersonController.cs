/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 22:35:00 +08:00 中国标准时间 星期日
 * 描述：人员主数据 API（/api/bas/persons）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装；
 *       明文端点独立字段级权限点并写审计，常规响应一律脱敏
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 人员主数据控制器。业务 Id 由全局 HashidsModelBinder 自动解码；人员编码由后端统一发号器生成，
/// 前端不传码（N-1）。证件号 / 手机号 / 银行卡号仅经 <see cref="GetPlaintext"/> 下发明文，
/// 该端点受 <c>bas.person.field.plaintext</c> 字段级权限保护并写入敏感字段访问审计。
/// </summary>
/// <param name="service">人员主数据服务。</param>
[ApiController]
[Route("api/bas/persons")]
public sealed class PersonController(IPersonService service) : ControllerBase
{
    /// <summary>人员分页列表（类型 / 在职状态 / 组织 / 岗位 / 关键字筛选）。</summary>
    /// <param name="personType">人员类型数值（可选）。</param>
    /// <param name="employStatus">在职状态数值（可选）。</param>
    /// <param name="orgId">所属组织 Id（可选）。</param>
    /// <param name="positionId">所属岗位 Id（可选）。</param>
    /// <param name="keyword">关键字（姓名 / 编码）。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页大小。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分页扁平行结果（敏感字段已脱敏）。</returns>
    [HttpGet]
    public async Task<ApiResponse<PagedResult<PersonRow>>> GetList(
        int? personType,
        int? employStatus,
        long? orgId,
        long? positionId,
        string? keyword,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => ApiResponse<PagedResult<PersonRow>>.Ok(
            await service.ListAsync(personType, employStatus, orgId, positionId, keyword, page, pageSize, ct));

    /// <summary>人员详情（主档 + 当前任职 + 任职历史 + 证件列表）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>人员详情（敏感字段已脱敏）。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<PersonDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<PersonDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建人员。</summary>
    /// <param name="payload">新建入参（无编码字段）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的人员扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_CREATE", "创建了人员主数据", TargetType = "Person")]
    public async Task<ApiResponse<PersonRow>> Create(
        [FromBody] SavePersonPayload payload, CancellationToken ct)
        => ApiResponse<PersonRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>更新人员基础信息与任职（调岗/换组织关闭旧任职并追加新记录）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的人员扁平行。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_UPDATE", "修改了人员主数据", TargetType = "Person", TargetIdParam = "id")]
    public async Task<ApiResponse<PersonRow>> Update(
        long id, [FromBody] UpdatePersonPayload payload, CancellationToken ct)
        => ApiResponse<PersonRow>.Ok(await service.UpdateAsync(id, payload, ct));

    /// <summary>办理离职（终态，关闭当前任职；提交后持久发布离职事件）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">离职入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的人员扁平行。</returns>
    [HttpPost("{id}/leave")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_LEAVE", "办理了人员离职", TargetType = "Person", TargetIdParam = "id")]
    public async Task<ApiResponse<PersonRow>> Leave(
        long id, [FromBody] ResignPersonPayload payload, CancellationToken ct)
        => ApiResponse<PersonRow>.Ok(await service.ResignAsync(id, payload, ct));

    /// <summary>再入职（仅离职状态允许，复用同一人员记录）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">再入职入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的人员扁平行。</returns>
    [HttpPost("{id}/rehire")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_REHIRE", "办理了人员再入职", TargetType = "Person", TargetIdParam = "id")]
    public async Task<ApiResponse<PersonRow>> Rehire(
        long id, [FromBody] RehirePersonPayload payload, CancellationToken ct)
        => ApiResponse<PersonRow>.Ok(await service.RehireAsync(id, payload, ct));

    /// <summary>读取敏感字段明文（证件号 / 手机号 / 银行卡号；字段级权限 + 敏感字段访问审计）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>明文行。</returns>
    [HttpGet("{id}/plaintext")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_PLAINTEXT_VIEW", "查看了人员敏感字段明文", TargetType = "Person", TargetIdParam = "id")]
    public async Task<ApiResponse<PersonPlaintextRow>> GetPlaintext(long id, CancellationToken ct)
        => ApiResponse<PersonPlaintextRow>.Ok(await service.GetPlaintextAsync(id, ct));

    /// <summary>人员证件档案扁平集合。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>证件扁平行列表。</returns>
    [HttpGet("{id}/certs")]
    public async Task<ApiResponse<IReadOnlyList<PersonCertRow>>> GetCerts(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<PersonCertRow>>.Ok(await service.ListCertsAsync(id, ct));

    /// <summary>登记人员证件档案。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">证件入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的证件扁平行。</returns>
    [HttpPost("{id}/certs")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_CERT_SAVE", "登记了人员证件档案", TargetType = "Person", TargetIdParam = "id")]
    public async Task<ApiResponse<PersonCertRow>> SaveCert(
        long id, [FromBody] SavePersonCertPayload payload, CancellationToken ct)
        => ApiResponse<PersonCertRow>.Ok(await service.SaveCertAsync(id, payload, ct));

    /// <summary>更新人员证件档案。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="certId">证件记录 Id。</param>
    /// <param name="payload">证件入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的证件扁平行。</returns>
    [HttpPut("{id}/certs/{certId}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_CERT_UPDATE", "修改了人员证件档案", TargetType = "Person", TargetIdParam = "id")]
    public async Task<ApiResponse<PersonCertRow>> UpdateCert(
        long id, long certId, [FromBody] SavePersonCertPayload payload, CancellationToken ct)
        => ApiResponse<PersonCertRow>.Ok(await service.UpdateCertAsync(id, certId, payload, ct));

    /// <summary>删除人员证件档案。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="certId">证件记录 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>无内容。</returns>
    [HttpDelete("{id}/certs/{certId}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_PERSON_CERT_DELETE", "删除了人员证件档案", TargetType = "Person", TargetIdParam = "id")]
    public async Task<ApiResponse<bool>> DeleteCert(long id, long certId, CancellationToken ct)
    {
        await service.DeleteCertAsync(id, certId, ct);
        return ApiResponse<bool>.Ok(true);
    }
}
