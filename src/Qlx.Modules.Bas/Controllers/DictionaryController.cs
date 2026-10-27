/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:30:00 +08:00 中国标准时间 星期一
 * 描述：数据字典 API（/api/bas/dictionaries）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 数据字典控制器。两级可见性（集团级 + 本子公司级）、集团级写保护、系统内置保护与引用保护
/// 全部由服务层判定；业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">数据字典服务。</param>
[ApiController]
[Route("api/bas/dictionaries")]
public sealed class DictionaryController(IDictionaryService service) : ControllerBase
{
    /// <summary>字典类型扁平列表（归属层级 / 状态筛选，按当前成员可见范围过滤）。</summary>
    /// <param name="ownerLevel">归属层级数值（可选）。</param>
    /// <param name="status">启停状态数值（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>字典类型扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<DictTypeRow>>> GetList(
        int? ownerLevel, int? status, CancellationToken ct)
        => ApiResponse<IReadOnlyList<DictTypeRow>>.Ok(
            await service.ListTypesAsync(ownerLevel, status, ct));

    /// <summary>字典类型详情。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>字典类型扁平行。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<DictTypeRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<DictTypeRow>.Ok(await service.GetTypeAsync(id, ct));

    /// <summary>新建字典类型（类型编码创建后不可修改）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的字典类型扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_DICT_TYPE_CREATE", "创建了字典类型", TargetType = "DictType")]
    public async Task<ApiResponse<DictTypeRow>> Create(
        [FromBody] SaveDictTypePayload payload, CancellationToken ct)
        => ApiResponse<DictTypeRow>.Ok(await service.CreateTypeAsync(payload, ct));

    /// <summary>重命名字典类型（编码与归属层级不可修改）。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的字典类型扁平行。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_DICT_TYPE_UPDATE", "修改了字典类型", TargetType = "DictType", TargetIdParam = "id")]
    public async Task<ApiResponse<DictTypeRow>> Update(
        long id, [FromBody] UpdateDictTypePayload payload, CancellationToken ct)
        => ApiResponse<DictTypeRow>.Ok(await service.UpdateTypeAsync(id, payload, ct));

    /// <summary>启停字典类型。</summary>
    /// <param name="id">字典类型 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>启停后的字典类型扁平行。</returns>
    [HttpPost("{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_DICT_TYPE_TOGGLE", "启停了字典类型", TargetType = "DictType", TargetIdParam = "id")]
    public async Task<ApiResponse<DictTypeRow>> Toggle(long id, CancellationToken ct)
        => ApiResponse<DictTypeRow>.Ok(await service.ToggleTypeAsync(id, ct));

    /// <summary>指定类型下的字典项扁平列表（按排序号升序）。</summary>
    /// <param name="typeCode">字典类型编码。</param>
    /// <param name="status">启停状态数值（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>字典项扁平行列表。</returns>
    [HttpGet("{typeCode}/items")]
    public async Task<ApiResponse<IReadOnlyList<DictItemRow>>> GetItems(
        string typeCode, int? status, CancellationToken ct)
        => ApiResponse<IReadOnlyList<DictItemRow>>.Ok(
            await service.ListItemsAsync(typeCode, status, ct));

    /// <summary>新增字典项（项编码创建后不可修改）。</summary>
    /// <param name="typeCode">字典类型编码。</param>
    /// <param name="payload">新增入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的字典项扁平行。</returns>
    [HttpPost("{typeCode}/items")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_DICT_ITEM_CREATE", "新增了字典项", TargetType = "DictItem")]
    public async Task<ApiResponse<DictItemRow>> CreateItem(
        string typeCode, [FromBody] SaveDictItemPayload payload, CancellationToken ct)
        => ApiResponse<DictItemRow>.Ok(await service.CreateItemAsync(typeCode, payload, ct));

    /// <summary>更新字典项名称与排序号（项编码不可修改）。</summary>
    /// <param name="id">字典项 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的字典项扁平行。</returns>
    [HttpPut("items/{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_DICT_ITEM_UPDATE", "修改了字典项", TargetType = "DictItem", TargetIdParam = "id")]
    public async Task<ApiResponse<DictItemRow>> UpdateItem(
        long id, [FromBody] UpdateDictItemPayload payload, CancellationToken ct)
        => ApiResponse<DictItemRow>.Ok(await service.UpdateItemAsync(id, payload, ct));

    /// <summary>启停字典项。</summary>
    /// <param name="id">字典项 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>启停后的字典项扁平行。</returns>
    [HttpPost("items/{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_DICT_ITEM_TOGGLE", "启停了字典项", TargetType = "DictItem", TargetIdParam = "id")]
    public async Task<ApiResponse<DictItemRow>> ToggleItem(long id, CancellationToken ct)
        => ApiResponse<DictItemRow>.Ok(await service.ToggleItemAsync(id, ct));

    /// <summary>删除字典项（系统内置 / 被业务引用时拒绝，仅可停用）。</summary>
    /// <param name="id">字典项 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>空响应。</returns>
    [HttpDelete("items/{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_DICT_ITEM_DELETE", "删除了字典项", TargetType = "DictItem", TargetIdParam = "id")]
    public async Task<ApiResponse<object>> DeleteItem(long id, CancellationToken ct)
    {
        await service.DeleteItemAsync(id, ct);
        return ApiResponse<object>.Ok(new { });
    }
}
