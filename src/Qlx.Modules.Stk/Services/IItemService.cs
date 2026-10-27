/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品档案服务契约（模块内）——商品新建/关键属性变更/停用均走审批挂起，事件到达后幂等应用
 */

using Qlx.Abstractions.Approvals;
using Qlx.Modules.Stk.Dtos;

namespace Qlx.Modules.Stk.Services;

/// <summary>商品档案服务。</summary>
public interface IItemService
{
    /// <summary>商品扁平列表（成本字段按权限裁剪）。</summary>
    Task<IReadOnlyList<ItemRow>> ListAsync(CancellationToken ct);

    /// <summary>商品详情（成本字段按权限裁剪）。</summary>
    Task<ItemRow> GetAsync(long id, CancellationToken ct);

    /// <summary>提交商品新建申请（审批通过后落库，N-1 编码后端生成）。</summary>
    Task<StkApprovalRequestRow> CreateAsync(CreateItemPayload payload, CancellationToken ct);

    /// <summary>更新商品：关键属性变化转审批挂起，否则直接生效。</summary>
    Task<UpdateItemResult> UpdateAsync(long id, UpdateItemPayload payload, CancellationToken ct);

    /// <summary>启用/停用商品（存在库存时拒绝直接停用）。</summary>
    Task<ItemRow> ToggleAsync(long id, CancellationToken ct);

    /// <summary>提交商品停用申请。</summary>
    Task<StkApprovalRequestRow> SubmitDisableRequestAsync(long id, CancellationToken ct);

    /// <summary>商品价格类变更日志。</summary>
    Task<IReadOnlyList<ItemPriceLogRow>> ListPriceLogsAsync(long id, CancellationToken ct);

    /// <summary>应用一条审批完成事件（事件消费者调用，按 ApprovalInstanceId 幂等）。</summary>
    Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct);
}
