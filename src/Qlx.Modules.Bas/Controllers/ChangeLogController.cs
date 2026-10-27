/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:45:00 +08:00 中国标准时间 星期一
 * 描述：变更日志 API（/api/bas/changelogs）——只读查询审计库变更快照，敏感字段已脱敏
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 变更日志控制器。数据来源于审计库变更快照（不新建日志表），查询结果经服务层硬上限截断后分页。
/// </summary>
/// <param name="service">变更日志服务。</param>
[ApiController]
[Route("api/bas/changelogs")]
public sealed class ChangeLogController(IChangeLogService service) : ControllerBase
{
    /// <summary>变更日志分页列表（对象类型 / 对象 Id / 操作人 / 时间范围筛选）。</summary>
    /// <param name="entityType">对象类型（可选）。</param>
    /// <param name="entityId">对象 Id（可选）。</param>
    /// <param name="changedBy">操作人（可选）。</param>
    /// <param name="from">起始时间（可选）。</param>
    /// <param name="to">截止时间（可选）。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页大小。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更日志扁平分页结果。</returns>
    [HttpGet]
    public async Task<ApiResponse<PagedResult<ChangeLogRow>>> GetList(
        string? entityType,
        long? entityId,
        string? changedBy,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => ApiResponse<PagedResult<ChangeLogRow>>.Ok(
            await service.QueryAsync(entityType, entityId, changedBy, from, to, page, pageSize, ct));
}
