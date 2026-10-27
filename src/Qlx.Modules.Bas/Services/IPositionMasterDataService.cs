/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 13:15:00 +08:00 中国标准时间 星期日
 * 描述：岗位主数据服务接口——分页查询/保存/停用启用/编制调整/在岗统计/超编审批申请
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 岗位主数据服务。编码沿用既有 <c>IEncodingGenerator</c>（<c>pos-</c> 前缀），
/// 写路径仅失效缓存不写缓存，每次写操作落审计快照。
/// </summary>
public interface IPositionMasterDataService
{
    /// <summary>岗位分页查询（可按组织子树 + 关键字过滤）。</summary>
    Task<PagedResult<PositionMasterRow>> ListAsync(long? orgId, string? keyword, PagedRequest request, CancellationToken ct);

    /// <summary>新建或更新岗位。</summary>
    Task<PositionMasterRow> SaveAsync(SavePositionCommand payload, CancellationToken ct);

    /// <summary>停用/启用翻转；停用前置为在岗人数为 0。</summary>
    Task<PositionMasterRow> ToggleAsync(long id, CancellationToken ct);

    /// <summary>调整编制人数；不得低于当前在岗人数。</summary>
    Task<PositionMasterRow> AdjustHeadcountAsync(long id, int headcount, CancellationToken ct);

    /// <summary>在岗统计（当前返回既有汇总值，不回源人员任职记录）。</summary>
    Task<PositionStatsRow> GetStatsAsync(long id, CancellationToken ct);

    /// <summary>提交超编审批申请（申请编制高于当前编制时挂起）。</summary>
    Task<ApprovalRequestRow> SubmitOverstaffRequestAsync(long id, SubmitOverstaffPayload payload, CancellationToken ct);

    /// <summary>应用审批通过的编制调整（幂等由调用方按请求状态保证）。</summary>
    Task ApplyHeadcountAsync(long positionId, int headcount, CancellationToken ct);
}
