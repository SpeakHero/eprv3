/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 17:00:00 +08:00 中国标准时间 星期日
 * 描述：商品分类主数据 API（/api/bas/categories）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 商品分类控制器。业务 Id 由全局 HashidsModelBinder 自动解码；分类编码由后端统一发号器生成，
/// 前端不传码（N-1）。
/// </summary>
/// <param name="service">商品分类主数据服务。</param>
[ApiController]
[Route("api/bas/categories")]
public sealed class CategoryController(IItemCategoryService service) : ControllerBase
{
    /// <summary>分类扁平列表（分类树由前端按 ParentId 组装，B-2-1）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分类扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<ItemCategoryRow>>> GetList(CancellationToken ct)
        => ApiResponse<IReadOnlyList<ItemCategoryRow>>.Ok(await service.ListAsync(ct));

    /// <summary>分类详情。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分类扁平行。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<ItemCategoryRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<ItemCategoryRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建分类。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的分类扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CATEGORY_CREATE", "创建了商品分类", TargetType = "ItemCategory")]
    public async Task<ApiResponse<ItemCategoryRow>> Create(
        [FromBody] CreateItemCategoryPayload payload, CancellationToken ct)
        => ApiResponse<ItemCategoryRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>更新分类。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的分类扁平行。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CATEGORY_UPDATE", "修改了商品分类", TargetType = "ItemCategory", TargetIdParam = "id")]
    public async Task<ApiResponse<ItemCategoryRow>> Update(
        long id, [FromBody] UpdateItemCategoryPayload payload, CancellationToken ct)
        => ApiResponse<ItemCategoryRow>.Ok(await service.UpdateAsync(id, payload, ct));

    /// <summary>停用/启用分类。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的分类扁平行。</returns>
    [HttpPost("{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CATEGORY_TOGGLE", "变更了商品分类启停状态", TargetType = "ItemCategory", TargetIdParam = "id")]
    public async Task<ApiResponse<ItemCategoryRow>> Toggle(long id, CancellationToken ct)
        => ApiResponse<ItemCategoryRow>.Ok(await service.ToggleAsync(id, ct));

    /// <summary>提交分类停用审批申请（挂起，审批通过后停用）。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    [HttpPost("{id}/disable-requests")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CATEGORY_DISABLE_REQUEST", "提交了商品分类停用审批申请", TargetType = "ItemCategory", TargetIdParam = "id")]
    public async Task<ApiResponse<ApprovalRequestRow>> CreateDisableRequest(long id, CancellationToken ct)
        => ApiResponse<ApprovalRequestRow>.Ok(await service.SubmitDisableRequestAsync(id, ct));
}
