/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 17:00:00 +08:00 中国标准时间 星期日
 * 描述：计量单位主数据 API（/api/bas/unit-groups、/api/bas/units）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 计量单位控制器（单位组 / 单位 / 换算关系）。业务 Id 由全局 HashidsModelBinder 自动解码；
/// 单位组与单位编码由后端生成，前端不传码（N-1）。
/// </summary>
/// <param name="service">计量单位主数据服务。</param>
[ApiController]
[Route("api/bas")]
public sealed class UnitController(IUnitService service) : ControllerBase
{
    /// <summary>计量单位组扁平列表。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单位组扁平行列表。</returns>
    [HttpGet("unit-groups")]
    public async Task<ApiResponse<IReadOnlyList<UnitGroupRow>>> GetGroups(CancellationToken ct)
        => ApiResponse<IReadOnlyList<UnitGroupRow>>.Ok(await service.ListUnitGroupsAsync(ct));

    /// <summary>新建或更新计量单位组。</summary>
    /// <param name="payload">保存入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存后的单位组扁平行。</returns>
    [HttpPost("unit-groups")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_UNIT_GROUP_SAVE", "保存了计量单位组", TargetType = "UnitGroup")]
    public async Task<ApiResponse<UnitGroupRow>> SaveGroup(
        [FromBody] SaveUnitGroupPayload payload, CancellationToken ct)
        => ApiResponse<UnitGroupRow>.Ok(await service.SaveUnitGroupAsync(payload, ct));

    /// <summary>停用/启用计量单位组。</summary>
    /// <param name="id">单位组 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的单位组扁平行。</returns>
    [HttpPost("unit-groups/{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_UNIT_GROUP_TOGGLE", "变更了计量单位组启停状态", TargetType = "UnitGroup", TargetIdParam = "id")]
    public async Task<ApiResponse<UnitGroupRow>> ToggleGroup(long id, CancellationToken ct)
        => ApiResponse<UnitGroupRow>.Ok(await service.ToggleUnitGroupAsync(id, ct));

    /// <summary>计量单位扁平列表。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单位扁平行列表。</returns>
    [HttpGet("units")]
    public async Task<ApiResponse<IReadOnlyList<UnitRow>>> GetList(CancellationToken ct)
        => ApiResponse<IReadOnlyList<UnitRow>>.Ok(await service.ListUnitsAsync(ct));

    /// <summary>新建或更新计量单位。</summary>
    /// <param name="payload">保存入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存后的单位扁平行。</returns>
    [HttpPost("units")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_UNIT_SAVE", "保存了计量单位", TargetType = "Unit")]
    public async Task<ApiResponse<UnitRow>> Save(
        [FromBody] SaveUnitPayload payload, CancellationToken ct)
        => ApiResponse<UnitRow>.Ok(await service.SaveUnitAsync(payload, ct));

    /// <summary>停用/启用计量单位。</summary>
    /// <param name="id">单位 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的单位扁平行。</returns>
    [HttpPost("units/{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_UNIT_TOGGLE", "变更了计量单位启停状态", TargetType = "Unit", TargetIdParam = "id")]
    public async Task<ApiResponse<UnitRow>> Toggle(long id, CancellationToken ct)
        => ApiResponse<UnitRow>.Ok(await service.ToggleUnitAsync(id, ct));

    /// <summary>单位换算关系列表（可按单位组过滤）。</summary>
    /// <param name="unitGroupId">单位组 Id（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>换算关系扁平行列表。</returns>
    [HttpGet("units/conversions")]
    public async Task<ApiResponse<IReadOnlyList<UnitConversionRow>>> GetConversions(
        [FromQuery] long? unitGroupId, CancellationToken ct)
        => ApiResponse<IReadOnlyList<UnitConversionRow>>.Ok(await service.ListConversionsAsync(unitGroupId, ct));

    /// <summary>新建或更新单位换算关系（换算率变更写审计，历史单据不回溯）。</summary>
    /// <param name="payload">保存入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存后的换算关系扁平行。</returns>
    [HttpPost("units/conversions")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_UNIT_CONVERSION_SAVE", "保存了单位换算关系", TargetType = "UnitConversion")]
    public async Task<ApiResponse<UnitConversionRow>> SaveConversion(
        [FromBody] SaveUnitConversionPayload payload, CancellationToken ct)
        => ApiResponse<UnitConversionRow>.Ok(await service.SaveConversionAsync(payload, ct));
}
