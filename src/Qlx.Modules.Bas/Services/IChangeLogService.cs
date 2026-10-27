/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:40:00 +08:00 中国标准时间 星期一
 * 描述：变更日志服务契约——复用审计库变更快照的只读查询（不新建日志表）
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 变更日志服务。数据来源为审计库 <c>IAuditService.QueryChangesAsync</c>（不新建日志表、不直连审计库），
/// 敏感字段读取时递归脱敏；结果按服务层硬上限截断后分页返回。
/// </summary>
public interface IChangeLogService
{
    /// <summary>
    /// 变更日志分页查询（对象类型 / 对象 Id / 操作人 / 时间范围筛选，按变更时间倒序）。
    /// </summary>
    /// <param name="entityType">对象类型（实体名，可选）。</param>
    /// <param name="entityId">对象 Id（可选）。</param>
    /// <param name="changedBy">操作人（可选）。</param>
    /// <param name="from">起始时间（可选）。</param>
    /// <param name="toTime">截止时间（可选）。</param>
    /// <param name="page">页码（从 1 起）。</param>
    /// <param name="pageSize">每页大小（1~200）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更日志扁平分页结果。</returns>
    Task<PagedResult<ChangeLogRow>> QueryAsync(
        string? entityType,
        long? entityId,
        string? changedBy,
        DateTimeOffset? from,
        DateTimeOffset? toTime,
        int page,
        int pageSize,
        CancellationToken ct);
}
