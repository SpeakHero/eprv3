/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：期初建账服务契约（模块内）——草稿 CRUD、提交过账审批、审批通过后写 Opening 流水并幂等应用
 */

using Qlx.Abstractions.Approvals;
using Qlx.Modules.Stk.Dtos;

namespace Qlx.Modules.Stk.Services;

/// <summary>期初建账服务。</summary>
public interface IOpeningService
{
    /// <summary>期初批次扁平列表（可按状态 / 仓库过滤）。</summary>
    /// <param name="status">状态数值（1 草稿 / 2 待审批 / 3 已过账，可空）。</param>
    /// <param name="warehouseId">仓库 Id（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>期初批次扁平行列表。</returns>
    Task<IReadOnlyList<OpeningBatchRow>> ListAsync(int? status, long? warehouseId, CancellationToken ct);

    /// <summary>期初批次详情（表头 + 明细）。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>期初明细。</returns>
    Task<OpeningBatchDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建期初草稿（单号后端生成，N-1）。</summary>
    /// <param name="payload">新建入参（不含单号与金额）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的期初明细。</returns>
    Task<OpeningBatchDetailRow> CreateDraftAsync(CreateOpeningPayload payload, CancellationToken ct);

    /// <summary>修改期初草稿（仅草稿可改，明细整体替换）。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="payload">更新入参（不含单号与金额）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的期初明细。</returns>
    Task<OpeningBatchDetailRow> UpdateDraftAsync(long id, UpdateOpeningPayload payload, CancellationToken ct);

    /// <summary>删除期初草稿（仅草稿可删）。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeleteDraftAsync(long id, CancellationToken ct);

    /// <summary>提交过账审批（Draft → Pending；同仓库已过账则拒绝）。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求。</returns>
    Task<StkApprovalRequestRow> SubmitAsync(long id, CancellationToken ct);

    /// <summary>应用一条审批完成事件（事件消费者调用，按 ApprovalInstanceId 幂等）。</summary>
    /// <param name="completed">审批完成事件。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct);

    /// <summary>期初仓库汇总（仅已过账明细；扁平原值，不拼接仓库名称，B-2）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>仓库汇总扁平行列表。</returns>
    Task<IReadOnlyList<OpeningWarehouseSummaryRow>> GetWarehouseSummaryAsync(CancellationToken ct);
}
