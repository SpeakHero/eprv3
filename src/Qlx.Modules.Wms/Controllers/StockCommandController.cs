/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存命令 Outbox API（/api/wms/stock-commands）——失败终态人工重推（R-8）
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 库存命令控制器。仅提供失败终态人工重推；业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">库存命令服务。</param>
[ApiController]
[Route("api/wms/stock-commands")]
public sealed class StockCommandController(IStockCommandService service) : ControllerBase
{
    /// <summary>人工重推（仅 state=3 可重推，重复 409 WMS_INVALID_STATE）。</summary>
    /// <param name="id">Outbox 行 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>重推后的命令行。</returns>
    [HttpPost("{id}/retry")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_COMMAND_RETRY", "重推了库存命令", TargetType = "StockCommand", TargetIdParam = "id")]
    public async Task<ApiResponse<StockCommandRow>> Retry(long id, CancellationToken ct)
        => ApiResponse<StockCommandRow>.Ok(await service.RetryAsync(id, ct));
}
