/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 09:50:00 +08:00 中国标准时间 星期五
 * 描述：审批中心模块门面契约（流程定义查询 / 已办列表 / 委托管理）——2A 冻结契约未覆盖的模块级能力
 */

using Qlx.Abstractions.Approvals;

namespace Qlx.Modules.Support.Services.Approvals;

/// <summary>流程定义摘要（列表展示）。</summary>
/// <param name="DefCode">定义编码。</param>
/// <param name="DefName">定义名称。</param>
/// <param name="BizType">单据类型。</param>
/// <param name="Version">版本号。</param>
/// <param name="Status">状态（Draft/Enabled/Disabled）。</param>
/// <param name="OrgScopeJson">适用组织范围 JSON（空数组=全租户）。</param>
/// <param name="NodeCount">节点数。</param>
/// <param name="BranchCount">分支数。</param>
/// <param name="UpdatedAt">最近更新时间。</param>
public sealed record SupportFlowSummary(
    string DefCode,
    string DefName,
    string BizType,
    int Version,
    string Status,
    string OrgScopeJson,
    int NodeCount,
    int BranchCount,
    DateTimeOffset UpdatedAt);

/// <summary>创建审批委托请求。</summary>
/// <param name="AgentUserId">代理人用户 Id。</param>
/// <param name="BizTypes">委托单据类型范围（空/null=全部类型，R9）。</param>
/// <param name="StartAt">生效开始时间（UTC）。</param>
/// <param name="EndAt">生效结束时间（UTC）。</param>
/// <param name="Reason">委托说明（可空）。</param>
public sealed record SupportCreateDelegateRequest(
    string AgentUserId,
    IReadOnlyList<string>? BizTypes,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason = null);

/// <summary>
/// 审批中心模块门面服务契约（流程定义查询 / 已办列表 / 委托管理）。
/// 2A 冻结契约 <see cref="IApprovalService"/> 之外的平台级能力，仅本模块前端消费。
/// </summary>
public interface ISupportApprovalAdminService
{
    /// <summary>查询当前租户全部流程定义摘要（含各版本，节点/分支数批量统计）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<SupportFlowSummary>> ListFlowsAsync(CancellationToken cancellationToken);

    /// <summary>查询指定用户已处理任务列表（最近 200 条）。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<ApprovalTaskInfo>> ListMyDoneTasksAsync(string userId, CancellationToken cancellationToken);

    /// <summary>查询指定用户作为委托人的全部委托。</summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<ApprovalDelegateInfo>> ListMyDelegatesAsync(string userId, CancellationToken cancellationToken);

    /// <summary>创建审批委托（R9：有效期内新任务自动转代理人并留痕）。</summary>
    /// <param name="delegatorId">委托人用户 Id（当前登录人）。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>委托 Id（混淆串）。</returns>
    Task<string> CreateDelegateAsync(string delegatorId, SupportCreateDelegateRequest request, CancellationToken cancellationToken);

    /// <summary>撤销委托（R9：随时可撤销，幂等）。</summary>
    /// <param name="delegatorId">委托人用户 Id（仅本人可撤销）。</param>
    /// <param name="delegateId">委托 Id（混淆串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RevokeDelegateAsync(string delegatorId, string delegateId, CancellationToken cancellationToken);
}
