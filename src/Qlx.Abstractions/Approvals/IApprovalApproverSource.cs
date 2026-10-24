/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 18:30:00 +08:00 中国标准时间 星期四
 * 描述：审批人来源契约（按审批人类型细粒度解析），Support 兜底实现 + Bas 组织/角色实现，宿主 DI 聚合注册
 */

namespace Qlx.Abstractions.Approvals;

/// <summary>
/// 审批人类型（流程节点 <c>approver_type</c> 字典值）。
/// </summary>
public static class ApprovalApproverTypes
{
    /// <summary>发起人本人（节点自动通过或退回发起人场景）。</summary>
    public const string Initiator = "INITIATOR";

    /// <summary>指定人（approver_ref 为用户 Id 列表，逗号分隔）。</summary>
    public const string User = "USER";

    /// <summary>单据字段人员（approver_ref 为快照字段名，字段值为用户 Id 列表）。</summary>
    public const string FieldUser = "FIELD_USER";

    /// <summary>角色（approver_ref 为角色编码；Bas 实现按成员主要角色承担）。</summary>
    public const string Role = "ROLE";

    /// <summary>岗位（approver_ref 为岗位编码；Bas 岗位体系落地前不支持）。</summary>
    public const string Position = "POSITION";

    /// <summary>部门负责人（approver_ref 为组织 Id，空=实例发起组织；Bas 实现按组织管理员承担）。</summary>
    public const string DeptLeader = "DEPT_LEADER";
}

/// <summary>
/// 审批人解析请求。
/// </summary>
public sealed record ApprovalApproverResolveRequest(
    string ApproverType,
    string? ApproverRef,
    long TenantId,
    string InitiatorUserId,
    long OrgId,
    IReadOnlyDictionary<string, object?> Snapshot);

/// <summary>
/// 审批人来源契约：按 <see cref="ApprovalApproverTypes"/> 细粒度解析节点审批人。
/// 引擎（审批中心）按来源注册顺序轮询：<see cref="CanResolve"/> 命中即消费，
/// 各模块分别实现（Support 兜底 INITIATOR/USER/FIELD_USER，Bas 组织侧 ROLE/POSITION/DEPT_LEADER），
/// 由宿主 DI 聚合为组合解析器，模块之间零引用。
/// </summary>
public interface IApprovalApproverSource
{
    /// <summary>
    /// 判定本来源能否解析指定审批人类型。
    /// </summary>
    /// <param name="approverType">审批人类型（字典值）。</param>
    /// <returns>能解析返回 true。</returns>
    bool CanResolve(string approverType);

    /// <summary>
    /// 解析审批人用户 Id 列表（去重、保序；解析不到任何人时返回空列表，由引擎按策略处置）。
    /// </summary>
    /// <param name="request">解析请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批人用户 Id 列表。</returns>
    Task<IReadOnlyList<string>> ResolveAsync(
        ApprovalApproverResolveRequest request, CancellationToken cancellationToken);
}
