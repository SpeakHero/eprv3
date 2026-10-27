/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：成本调整服务契约（模块内）——草稿 CRUD、阈值选审批定义码、审批通过后红蓝字成对写流水并幂等应用
 */

using Qlx.Abstractions.Approvals;
using Qlx.Modules.Stk.Dtos;

namespace Qlx.Modules.Stk.Services;

/// <summary>成本调整服务。</summary>
public interface ICostAdjustService
{
    /// <summary>成本调整单扁平列表（可按状态过滤）。</summary>
    /// <param name="status">状态数值（1 草稿 / 2 待审批 / 3 已通过 / 4 已驳回，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成本调整单扁平行列表。</returns>
    Task<IReadOnlyList<CostAdjustRow>> ListAsync(int? status, CancellationToken ct);

    /// <summary>成本调整单详情（表头 + 明细）。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成本调整明细。</returns>
    Task<CostAdjustDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建成本调整草稿（单号后端生成，旧均价与差额后端取值，N-1/V-1）。</summary>
    /// <param name="payload">新建入参（不含单号、旧均价与差额）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的成本调整明细。</returns>
    Task<CostAdjustDetailRow> CreateDraftAsync(CreateCostAdjustPayload payload, CancellationToken ct);

    /// <summary>修改成本调整草稿（仅草稿可改，明细整体替换）。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的成本调整明细。</returns>
    Task<CostAdjustDetailRow> UpdateDraftAsync(long id, UpdateCostAdjustPayload payload, CancellationToken ct);

    /// <summary>删除成本调整草稿（仅草稿可删）。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeleteDraftAsync(long id, CancellationToken ct);

    /// <summary>提交审批（按差额绝对值与阈值选择审批定义码）。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求。</returns>
    Task<StkApprovalRequestRow> SubmitAsync(long id, CancellationToken ct);

    /// <summary>应用一条审批完成事件（事件消费者调用，按 ApprovalInstanceId 幂等）。</summary>
    /// <param name="completed">审批完成事件。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct);
}
