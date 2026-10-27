/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品档案 API（/api/stk/items）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装；
 *       新建/关键属性变更/停用均返回审批挂起请求（编码由后端生成，前端不传码，N-1）
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Services;

namespace Qlx.Modules.Stk.Controllers;

/// <summary>
/// 商品档案控制器。业务 Id 由全局 HashidsModelBinder 自动解码；商品编码由后端生成（N-1）。
/// </summary>
/// <param name="service">商品档案服务。</param>
[ApiController]
[Route("api/stk/items")]
public sealed class ItemController(IItemService service) : ControllerBase
{
    /// <summary>商品扁平列表（成本字段按权限裁剪）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>商品扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<ItemRow>>> GetList(CancellationToken ct)
        => ApiResponse<IReadOnlyList<ItemRow>>.Ok(await service.ListAsync(ct));

    /// <summary>商品详情。</summary>
    /// <param name="id">商品 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>商品扁平行。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<ItemRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<ItemRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>商品价格类变更日志。</summary>
    /// <param name="id">商品 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更日志扁平行列表。</returns>
    [HttpGet("{id}/price-logs")]
    public async Task<ApiResponse<IReadOnlyList<ItemPriceLogRow>>> GetPriceLogs(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<ItemPriceLogRow>>.Ok(await service.ListPriceLogsAsync(id, ct));

    /// <summary>提交商品新建申请（审批通过后落库）。</summary>
    /// <param name="payload">新建入参（不含商品编码）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Stk, "STK_ITEM_CREATE", "提交了商品新建申请", TargetType = "Item")]
    public async Task<ApiResponse<StkApprovalRequestRow>> Create(
        [FromBody] CreateItemPayload payload, CancellationToken ct)
        => ApiResponse<StkApprovalRequestRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>更新商品：关键属性变化转审批挂起，否则直接生效。</summary>
    /// <param name="id">商品 Id。</param>
    /// <param name="payload">更新入参（不含商品编码）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新结果（直接生效或审批挂起）。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_ITEM_UPDATE", "修改了商品", TargetType = "Item", TargetIdParam = "id")]
    public async Task<ApiResponse<UpdateItemResult>> Update(
        long id, [FromBody] UpdateItemPayload payload, CancellationToken ct)
        => ApiResponse<UpdateItemResult>.Ok(await service.UpdateAsync(id, payload, ct));

    /// <summary>启用/停用商品（存在库存时拒绝直接停用）。</summary>
    /// <param name="id">商品 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的商品扁平行。</returns>
    [HttpPost("{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_ITEM_TOGGLE", "变更了商品启停状态", TargetType = "Item", TargetIdParam = "id")]
    public async Task<ApiResponse<ItemRow>> Toggle(long id, CancellationToken ct)
        => ApiResponse<ItemRow>.Ok(await service.ToggleAsync(id, ct));

    /// <summary>提交商品停用申请。</summary>
    /// <param name="id">商品 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求。</returns>
    [HttpPost("{id}/disable-request")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_ITEM_DISABLE_REQUEST", "提交了商品停用申请", TargetType = "Item", TargetIdParam = "id")]
    public async Task<ApiResponse<StkApprovalRequestRow>> CreateDisableRequest(long id, CancellationToken ct)
        => ApiResponse<StkApprovalRequestRow>.Ok(await service.SubmitDisableRequestAsync(id, ct));
}
