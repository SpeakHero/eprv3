/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 18:40:00 +08:00 中国标准时间 星期四
 * 描述：审批中心领域实体（apr_ 前缀 7 张表）：
 *       流程定义 / 流程节点 / 条件分支 / 审批实例 / 节点任务 / 操作日志（只增）/ 委托
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Approvals;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>流程定义状态（apr_flow_definitions.status）。</summary>
public enum ApprovalFlowStatus
{
    /// <summary>草稿（编辑中，不可被单据提交命中）。</summary>
    Draft = 0,

    /// <summary>启用（同 biz_type + 组织范围唯一，R1）。</summary>
    Enabled = 1,

    /// <summary>停用（不再承接新提交；在途实例按版本隔离继续，R2）。</summary>
    Disabled = 2,
}

/// <summary>节点类型（apr_flow_nodes.node_type）。</summary>
public enum ApprovalNodeType
{
    /// <summary>审批节点。</summary>
    Approval = 0,

    /// <summary>条件分支节点（仅承载分支线，不生成任务）。</summary>
    Branch = 1,

    /// <summary>抄送节点（知会，不阻塞流转）。</summary>
    Cc = 2,
}

/// <summary>会签/或签模式（apr_flow_nodes.sign_mode）。</summary>
public enum ApprovalSignMode
{
    /// <summary>或签：任一审批人处理即决，其余任务自动 SKIPPED（R4）。</summary>
    OrSign = 0,

    /// <summary>会签：须全员通过，任一驳回即整体驳回（R4）。</summary>
    Countersign = 1,
}

/// <summary>节点驳回策略（apr_flow_nodes.reject_mode，R6）。</summary>
public enum ApprovalRejectMode
{
    /// <summary>逐级驳回：回上一审批节点重做。</summary>
    StepBack = 0,

    /// <summary>直达发起人：实例终态 REJECTED，单据回草稿。</summary>
    ToInitiator = 1,
}

/// <summary>超时动作（apr_flow_nodes.timeout_action，R8）。</summary>
public enum ApprovalTimeoutAction
{
    /// <summary>催办（经信息中心推送待办人）。</summary>
    Urge = 0,

    /// <summary>催办 N 次后上报上级（上级解析不到时继续催办并告警）。</summary>
    Escalate = 1,
}

/// <summary>任务来源（apr_tasks.source_type，R9/R5 留痕）。</summary>
public static class ApprovalTaskSourceTypes
{
    /// <summary>正常流转生成。</summary>
    public const string Normal = "NORMAL";

    /// <summary>委托转派（留痕「XX 委托 YY 处理」）。</summary>
    public const string Delegate = "DELEGATE";

    /// <summary>加签生成。</summary>
    public const string AddSign = "ADD_SIGN";

    /// <summary>转办生成。</summary>
    public const string Transfer = "TRANSFER";

    /// <summary>超时上报上级生成（R8）。</summary>
    public const string Escalate = "ESCALATE";
}

/// <summary>操作日志动作（apr_op_logs.action，R10 只增不改）。</summary>
public static class ApprovalLogActions
{
    /// <summary>提交发起。</summary>
    public const string Submit = "SUBMIT";
    /// <summary>审批通过。</summary>
    public const string Approve = "APPROVE";
    /// <summary>审批驳回。</summary>
    public const string Reject = "REJECT";
    /// <summary>加签。</summary>
    public const string AddSign = "ADD_SIGN";
    /// <summary>转办。</summary>
    public const string Transfer = "TRANSFER";
    /// <summary>委托转派留痕。</summary>
    public const string Delegate = "DELEGATE";
    /// <summary>催办。</summary>
    public const string Urge = "URGE";
    /// <summary>撤回。</summary>
    public const string Withdraw = "WITHDRAW";
    /// <summary>超时上报。</summary>
    public const string Escalate = "ESCALATE";
    /// <summary>终态完成（事件发布留痕）。</summary>
    public const string Complete = "COMPLETE";
    /// <summary>分支未命中告警。</summary>
    public const string BranchMissAlert = "BRANCH_MISS";
}

/// <summary>委托状态（apr_delegates.status，R9）。</summary>
public enum ApprovalDelegationStatus
{
    /// <summary>生效中（有效期内自动转派）。</summary>
    Active = 0,

    /// <summary>已撤销（随时可撤销）。</summary>
    Revoked = 1,

    /// <summary>已过期（时间窗自然结束）。</summary>
    Expired = 2,
}

/// <summary>
/// 流程定义实体（表 <c>apr_flow_definitions</c>）。同 biz_type + 组织范围仅一个 ENABLED 版本（R1）；
/// 单据提交时锁定 def_version，在途实例不受新版本影响（R2）。
/// </summary>
[Index(nameof(TenantId), nameof(BizType), nameof(DefCode), nameof(Version), IsUnique = true)]
[Index(nameof(TenantId), nameof(BizType), nameof(Status))]
[TenantTablePrefix("apr_")]
public sealed class ApprovalFlowDefinition : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>定义编码（全局唯一，如 <c>flow.purchase.request</c>）。</summary>
    [MaxLength(100)]
    public string DefCode { get; internal set; } = string.Empty;

    /// <summary>定义名称。</summary>
    [MaxLength(200)]
    public string DefName { get; internal set; } = string.Empty;

    /// <summary>单据类型（采购申请/报销单等，与业务模块对齐）。</summary>
    [MaxLength(64)]
    public string BizType { get; internal set; } = string.Empty;

    /// <summary>适用组织范围 JSON（全租户为空数组；否则组织 Id 数组）。</summary>
    [MaxLength(2000)]
    public string OrgScopeJson { get; internal set; } = "[]";

    /// <summary>版本号（同码递增；提交锁定，R2 版本隔离）。</summary>
    public int Version { get; internal set; } = 1;

    /// <summary>设计器图数据 JSON（节点/连线/属性全量，供流程设计器与版本对比）。</summary>
    [MaxLength(200_000)]
    public string DesignJson { get; internal set; } = string.Empty;

    /// <summary>定义状态。</summary>
    public ApprovalFlowStatus Status { get; internal set; } = ApprovalFlowStatus.Draft;

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ApprovalFlowDefinition() { }

    /// <summary>创建流程定义（初始 Draft，由注册服务启用）。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="defCode">定义编码。</param>
    /// <param name="defName">定义名称。</param>
    /// <param name="bizType">单据类型。</param>
    /// <param name="orgScopeJson">组织范围 JSON。</param>
    /// <param name="version">版本号。</param>
    /// <param name="designJson">设计器 JSON。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public ApprovalFlowDefinition(
        long tenantId, string defCode, string defName, string bizType,
        string orgScopeJson, int version, string designJson, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(defCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(defName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        TenantId = tenantId;
        DefCode = defCode;
        DefName = defName;
        BizType = bizType;
        OrgScopeJson = orgScopeJson;
        Version = version;
        DesignJson = designJson;
        Status = ApprovalFlowStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>启用 / 停用定义。</summary>
    /// <param name="status">目标状态。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void SetStatus(ApprovalFlowStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }
}

/// <summary>
/// 流程节点实体（表 <c>apr_flow_nodes</c>，定义态）。审批人策略 / 超时 / 加签与驳回策略全量承载。
/// </summary>
[Index(nameof(TenantId), nameof(DefId), nameof(NodeKey), IsUnique = true)]
[TenantTablePrefix("apr_")]
public sealed class ApprovalFlowNode : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>流程定义 Id。</summary>
    public long DefId { get; internal set; }

    /// <summary>节点键（定义内唯一，如 <c>dept_leader</c>）。</summary>
    [MaxLength(64)]
    public string NodeKey { get; internal set; } = string.Empty;

    /// <summary>节点类型。</summary>
    public ApprovalNodeType NodeType { get; internal set; } = ApprovalNodeType.Approval;

    /// <summary>节点名称。</summary>
    [MaxLength(100)]
    public string NodeName { get; internal set; } = string.Empty;

    /// <summary>审批人类型（ApprovalApproverTypes 字典值）。</summary>
    [MaxLength(32)]
    public string ApproverType { get; internal set; } = ApprovalApproverTypes.User;

    /// <summary>审批人引用（用户 Id 列表 / 角色编码 / 组织 Id / 快照字段名，按类型解释；可空）。</summary>
    [MaxLength(256)]
    public string? ApproverRef { get; internal set; }

    /// <summary>会签/或签模式。</summary>
    public ApprovalSignMode SignMode { get; internal set; } = ApprovalSignMode.OrSign;

    /// <summary>超时小时数（0=不限；到期动作见 TimeoutAction）。</summary>
    public int TimeoutHours { get; internal set; }

    /// <summary>超时动作。</summary>
    public ApprovalTimeoutAction TimeoutAction { get; internal set; } = ApprovalTimeoutAction.Urge;

    /// <summary>是否允许加签。</summary>
    public bool AllowAddSign { get; internal set; } = true;

    /// <summary>驳回策略（R6）。</summary>
    public ApprovalRejectMode RejectMode { get; internal set; } = ApprovalRejectMode.StepBack;

    /// <summary>排序号（链路顺序）。</summary>
    public int SortNo { get; internal set; }

    /// <summary>发起人节点自动避让（R13 可选：审批人=发起人时自动通过）。</summary>
    public bool SkipIfInitiator { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ApprovalFlowNode() { }

    /// <summary>创建流程节点。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="defId">流程定义 Id。</param>
    /// <param name="nodeKey">节点键。</param>
    /// <param name="nodeType">节点类型。</param>
    /// <param name="nodeName">节点名称。</param>
    /// <param name="approverType">审批人类型。</param>
    /// <param name="approverRef">审批人引用（可空）。</param>
    /// <param name="signMode">会签/或签模式。</param>
    /// <param name="timeoutHours">超时小时数。</param>
    /// <param name="timeoutAction">超时动作。</param>
    /// <param name="allowAddSign">是否允许加签。</param>
    /// <param name="rejectMode">驳回策略。</param>
    /// <param name="sortNo">排序号。</param>
    /// <param name="skipIfInitiator">发起人避让开关（R13）。</param>
    public ApprovalFlowNode(
        long tenantId, long defId, string nodeKey, ApprovalNodeType nodeType, string nodeName,
        string approverType, string? approverRef, ApprovalSignMode signMode,
        int timeoutHours, ApprovalTimeoutAction timeoutAction, bool allowAddSign,
        ApprovalRejectMode rejectMode, int sortNo, bool skipIfInitiator = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverType);

        TenantId = tenantId;
        DefId = defId;
        NodeKey = nodeKey;
        NodeType = nodeType;
        NodeName = nodeName;
        ApproverType = approverType;
        ApproverRef = approverRef;
        SignMode = signMode;
        TimeoutHours = timeoutHours;
        TimeoutAction = timeoutAction;
        AllowAddSign = allowAddSign;
        RejectMode = rejectMode;
        SortNo = sortNo;
        SkipIfInitiator = skipIfInitiator;
    }
}

/// <summary>
/// 条件分支实体（表 <c>apr_flow_branches</c>，定义态）。expr_json 为条件引擎 Parse 产出的 AST JSON
/// （<c>ConditionExpression.ToJson()</c> 格式），节点完成时反序列化求值（R3）。
/// </summary>
[Index(nameof(TenantId), nameof(DefId), nameof(FromNodeKey))]
[TenantTablePrefix("apr_")]
public sealed class ApprovalFlowBranch : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>流程定义 Id。</summary>
    public long DefId { get; internal set; }

    /// <summary>来源节点键。</summary>
    [MaxLength(64)]
    public string FromNodeKey { get; internal set; } = string.Empty;

    /// <summary>目标节点键（终态分支为空串）。</summary>
    [MaxLength(64)]
    public string ToNodeKey { get; internal set; } = string.Empty;

    /// <summary>条件表达式 AST JSON（$type 判别符；兜底分支为空串）。</summary>
    [MaxLength(4000)]
    public string ExprJson { get; internal set; } = string.Empty;

    /// <summary>优先级（数值大者先求值）。</summary>
    public int Priority { get; internal set; }

    /// <summary>兜底分支（每条来源线仅一条，全部未命中时走兜底，R3）。</summary>
    public bool IsDefault { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ApprovalFlowBranch() { }

    /// <summary>创建条件分支。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="defId">流程定义 Id。</param>
    /// <param name="fromNodeKey">来源节点键。</param>
    /// <param name="toNodeKey">目标节点键。</param>
    /// <param name="exprJson">条件表达式 AST JSON（兜底分支传空串）。</param>
    /// <param name="priority">优先级。</param>
    /// <param name="isDefault">是否兜底分支。</param>
    public ApprovalFlowBranch(
        long tenantId, long defId, string fromNodeKey, string toNodeKey,
        string exprJson, int priority, bool isDefault)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNodeKey);

        TenantId = tenantId;
        DefId = defId;
        FromNodeKey = fromNodeKey;
        ToNodeKey = toNodeKey;
        ExprJson = exprJson;
        Priority = priority;
        IsDefault = isDefault;
    }
}

/// <summary>
/// 审批实例实体（表 <c>apr_instances</c>）。状态机由 <c>ApprovalInstanceStateMachine</c> 守卫；
/// snapshot_json 为提交时单据关键字段快照，是分支条件求值的唯一依据（防中途改单影响判定）。
/// </summary>
[Index(nameof(TenantId), nameof(InstNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(BizType), nameof(BizId))]
[Index(nameof(TenantId), nameof(InitiatorId), nameof(Status), nameof(StartedAt))]
[TenantTablePrefix("apr_")]
public sealed class ApprovalInstance : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>实例编号（APR + yyyyMMdd + 雪花尾 8 位）。</summary>
    [MaxLength(64)]
    public string InstNo { get; internal set; } = string.Empty;

    /// <summary>流程定义 Id。</summary>
    public long DefId { get; internal set; }

    /// <summary>锁定的定义版本（R2 版本隔离）。</summary>
    public int DefVersion { get; internal set; }

    /// <summary>定义编码（冗余，事件回执用）。</summary>
    [MaxLength(100)]
    public string DefCode { get; internal set; } = string.Empty;

    /// <summary>单据类型。</summary>
    [MaxLength(64)]
    public string BizType { get; internal set; } = string.Empty;

    /// <summary>单据 Id（业务模块主键）。</summary>
    [MaxLength(64)]
    public string BizId { get; internal set; } = string.Empty;

    /// <summary>单据号（冗余，通知展示用）。</summary>
    [MaxLength(64)]
    public string BizNo { get; internal set; } = string.Empty;

    /// <summary>标题。</summary>
    [MaxLength(200)]
    public string Title { get; internal set; } = string.Empty;

    /// <summary>发起人用户 Id。</summary>
    [MaxLength(64)]
    public string InitiatorId { get; internal set; } = string.Empty;

    /// <summary>发起组织 Id（审批人解析上下文）。</summary>
    public long OrgId { get; internal set; }

    /// <summary>当前节点键（终态后为空串）。</summary>
    [MaxLength(64)]
    public string CurrentNodeKey { get; internal set; } = string.Empty;

    /// <summary>实例状态（状态机守卫）。</summary>
    public Qlx.Abstractions.Approvals.ApprovalStatus Status { get; internal set; }
        = Qlx.Abstractions.Approvals.ApprovalStatus.Pending;

    /// <summary>发起时间（UTC）。</summary>
    public DateTimeOffset StartedAt { get; internal set; }

    /// <summary>终态时间（UTC；可空）。</summary>
    public DateTimeOffset? EndedAt { get; internal set; }

    /// <summary>单据关键字段快照 JSON（分支求值依据）。</summary>
    [MaxLength(100_000)]
    public string SnapshotJson { get; internal set; } = "{}";

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ApprovalInstance() { }

    /// <summary>创建审批实例（初始 Pending，提交时锁定定义版本）。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="instNo">实例编号。</param>
    /// <param name="defId">流程定义 Id。</param>
    /// <param name="defVersion">锁定的定义版本。</param>
    /// <param name="defCode">定义编码。</param>
    /// <param name="bizType">单据类型。</param>
    /// <param name="bizId">单据 Id。</param>
    /// <param name="bizNo">单据号。</param>
    /// <param name="title">标题。</param>
    /// <param name="initiatorId">发起人用户 Id。</param>
    /// <param name="orgId">发起组织 Id。</param>
    /// <param name="snapshotJson">快照 JSON。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public ApprovalInstance(
        long tenantId, string instNo, long defId, int defVersion, string defCode,
        string bizType, string bizId, string bizNo, string title, string initiatorId,
        long orgId, string snapshotJson, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizId);
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);

        TenantId = tenantId;
        InstNo = instNo;
        DefId = defId;
        DefVersion = defVersion;
        DefCode = defCode;
        BizType = bizType;
        BizId = bizId;
        BizNo = bizNo;
        Title = title;
        InitiatorId = initiatorId;
        OrgId = orgId;
        SnapshotJson = snapshotJson;
        Status = Qlx.Abstractions.Approvals.ApprovalStatus.Pending;
        StartedAt = now;
    }

    /// <summary>回写实例编号（实例 Id 由拦截器填充后按 Id 派生，保证全局唯一）。</summary>
    /// <param name="instNo">实例编号。</param>
    public void AssignInstNo(string instNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instNo);
        InstNo = instNo;
    }

    /// <summary>推进实例状态（调用方负责经 <c>ApprovalInstanceStateMachine.EnsureTransition</c> 校验）。</summary>
    /// <param name="status">目标状态。</param>
    /// <param name="currentNodeKey">当前节点键（终态传空串）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void SetStatus(Qlx.Abstractions.Approvals.ApprovalStatus status, string currentNodeKey, DateTimeOffset now)
    {
        Status = status;
        CurrentNodeKey = currentNodeKey;
        EndedAt = Qlx.Abstractions.Approvals.ApprovalStatus.Pending == status ? null : now;
    }
}

/// <summary>
/// 节点任务实体（表 <c>apr_tasks</c>）。source_type 留痕 NORMAL/DELEGATE/ADD_SIGN/TRANSFER/ESCALATE（R9/R5/R8）；
/// UrgeCount/UrgeLastAt 支撑超时催办链路（R8，次数与间隔参数化）。
/// </summary>
[Index(nameof(TenantId), nameof(InstId), nameof(NodeKey))]
[Index(nameof(TenantId), nameof(ApproverId), nameof(Status))]
[TenantTablePrefix("apr_")]
public sealed class ApprovalTask : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>审批实例 Id。</summary>
    public long InstId { get; internal set; }

    /// <summary>节点键。</summary>
    [MaxLength(64)]
    public string NodeKey { get; internal set; } = string.Empty;

    /// <summary>节点名称（冗余，列表展示免联查）。</summary>
    [MaxLength(100)]
    public string NodeName { get; internal set; } = string.Empty;

    /// <summary>审批人用户 Id（实际处理人；委托场景为代理人）。</summary>
    [MaxLength(64)]
    public string ApproverId { get; internal set; } = string.Empty;

    /// <summary>任务来源（ApprovalTaskSourceTypes）。</summary>
    [MaxLength(32)]
    public string SourceType { get; internal set; } = ApprovalTaskSourceTypes.Normal;

    /// <summary>来源人（委托人 / 转办人 / 加签人 / 被上报人；正常流转为空）。</summary>
    [MaxLength(64)]
    public string? SourceFrom { get; internal set; }

    /// <summary>任务状态。</summary>
    public Qlx.Abstractions.Approvals.ApprovalTaskStatus Status { get; internal set; }
        = Qlx.Abstractions.Approvals.ApprovalTaskStatus.Pending;

    /// <summary>审批意见（可空）。</summary>
    [MaxLength(1000)]
    public string? Opinion { get; internal set; }

    /// <summary>操作 IP（可空）。</summary>
    [MaxLength(64)]
    public string? Ip { get; internal set; }

    /// <summary>超时时点（UTC；0 小时节点的为 null）。</summary>
    public DateTimeOffset? DueAt { get; internal set; }

    /// <summary>处理时间（UTC；可空）。</summary>
    public DateTimeOffset? HandledAt { get; internal set; }

    /// <summary>已催办次数（R8：达到上限后上报上级）。</summary>
    public int UrgeCount { get; internal set; }

    /// <summary>最近催办时间（UTC；可空，催办节流依据）。</summary>
    public DateTimeOffset? UrgeLastAt { get; internal set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ApprovalTask() { }

    /// <summary>创建节点任务（初始 Pending）。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="instId">实例 Id。</param>
    /// <param name="nodeKey">节点键。</param>
    /// <param name="nodeName">节点名称。</param>
    /// <param name="approverId">审批人用户 Id。</param>
    /// <param name="sourceType">任务来源。</param>
    /// <param name="sourceFrom">来源人（可空）。</param>
    /// <param name="dueAt">超时时点（可空）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public ApprovalTask(
        long tenantId, long instId, string nodeKey, string nodeName, string approverId,
        string sourceType, string? sourceFrom, DateTimeOffset? dueAt, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);

        TenantId = tenantId;
        InstId = instId;
        NodeKey = nodeKey;
        NodeName = nodeName;
        ApproverId = approverId;
        SourceType = sourceType;
        SourceFrom = sourceFrom;
        DueAt = dueAt;
        Status = Qlx.Abstractions.Approvals.ApprovalTaskStatus.Pending;
        UrgeCount = 0;
        CreatedAt = now;
    }

    /// <summary>推进任务状态（调用方负责经 <c>ApprovalInstanceStateMachine.EnsureTaskTransition</c> 校验）。</summary>
    /// <param name="status">目标状态。</param>
    /// <param name="opinion">审批意见（可空）。</param>
    /// <param name="ip">操作 IP（可空）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void SetStatus(Qlx.Abstractions.Approvals.ApprovalTaskStatus status, string? opinion, string? ip, DateTimeOffset now)
    {
        Status = status;
        Opinion = opinion;
        Ip = ip;
        HandledAt = now;
    }

    /// <summary>登记一次催办（节流判断由调用方完成）。</summary>
    /// <param name="now">当前时间（UTC）。</param>
    public void RecordUrge(DateTimeOffset now)
    {
        UrgeCount++;
        UrgeLastAt = now;
    }
}

/// <summary>
/// 操作日志实体（表 <c>apr_op_logs</c>，只增不改，R10）。全部动作留痕，禁止提供任何修改 / 删除入口。
/// </summary>
[Index(nameof(TenantId), nameof(InstId), nameof(CreatedAt))]
[TenantTablePrefix("apr_")]
public sealed class ApprovalLog : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>审批实例 Id。</summary>
    public long InstId { get; internal set; }

    /// <summary>动作（ApprovalLogActions）。</summary>
    [MaxLength(32)]
    public string Action { get; internal set; } = string.Empty;

    /// <summary>操作人用户 Id（系统动作为 system）。</summary>
    [MaxLength(64)]
    public string OperatorId { get; internal set; } = string.Empty;

    /// <summary>明细（意见 / 委托关系 / 分支命中说明等）。</summary>
    [MaxLength(2000)]
    public string Detail { get; internal set; } = string.Empty;

    /// <summary>操作 IP（可空）。</summary>
    [MaxLength(64)]
    public string? Ip { get; internal set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ApprovalLog() { }

    /// <summary>创建操作日志（只增）。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="instId">实例 Id。</param>
    /// <param name="action">动作。</param>
    /// <param name="operatorId">操作人用户 Id。</param>
    /// <param name="detail">明细。</param>
    /// <param name="ip">操作 IP（可空）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public ApprovalLog(
        long tenantId, long instId, string action, string operatorId, string detail, string? ip, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        TenantId = tenantId;
        InstId = instId;
        Action = action;
        OperatorId = operatorId;
        Detail = detail;
        Ip = ip;
        CreatedAt = now;
    }
}

/// <summary>
/// 委托实体（表 <c>apr_delegates</c>）。有效期内新任务自动转代理人并留痕 source_type=DELEGATE（R9）；
/// 随时可撤销；BizTypesJson 为空数组表示全部单据类型。
/// </summary>
[Index(nameof(TenantId), nameof(DelegatorId), nameof(Status), nameof(StartTime))]
[Index(nameof(TenantId), nameof(AgentId))]
[TenantTablePrefix("apr_")]
public sealed class ApprovalDelegation : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>委托人用户 Id。</summary>
    [MaxLength(64)]
    public string DelegatorId { get; internal set; } = string.Empty;

    /// <summary>代理人用户 Id。</summary>
    [MaxLength(64)]
    public string AgentId { get; internal set; } = string.Empty;

    /// <summary>委托单据类型范围 JSON（空数组=全部）。</summary>
    [MaxLength(2000)]
    public string BizTypesJson { get; internal set; } = "[]";

    /// <summary>生效开始时间（UTC）。</summary>
    public DateTimeOffset StartTime { get; internal set; }

    /// <summary>生效结束时间（UTC）。</summary>
    public DateTimeOffset EndTime { get; internal set; }

    /// <summary>委托状态。</summary>
    public ApprovalDelegationStatus Status { get; internal set; } = ApprovalDelegationStatus.Active;

    /// <summary>委托说明（可空）。</summary>
    [MaxLength(500)]
    public string? Reason { get; internal set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public ApprovalDelegation() { }

    /// <summary>创建委托（初始 Active；结束时间必须晚于开始时间）。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="delegatorId">委托人用户 Id。</param>
    /// <param name="agentId">代理人用户 Id。</param>
    /// <param name="bizTypesJson">单据类型范围 JSON（空数组=全部）。</param>
    /// <param name="startTime">生效开始时间（UTC）。</param>
    /// <param name="endTime">生效结束时间（UTC）。</param>
    /// <param name="reason">委托说明（可空）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public ApprovalDelegation(
        long tenantId, string delegatorId, string agentId, string bizTypesJson,
        DateTimeOffset startTime, DateTimeOffset endTime, string? reason, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(delegatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (endTime <= startTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), "委托结束时间必须晚于开始时间。");
        }

        TenantId = tenantId;
        DelegatorId = delegatorId;
        AgentId = agentId;
        BizTypesJson = bizTypesJson;
        StartTime = startTime;
        EndTime = endTime;
        Status = ApprovalDelegationStatus.Active;
        Reason = reason;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>撤销委托（幂等：非 Active 状态忽略）。</summary>
    /// <param name="now">当前时间（UTC）。</param>
    public void Revoke(DateTimeOffset now)
    {
        if (Status != ApprovalDelegationStatus.Active)
        {
            return;
        }

        Status = ApprovalDelegationStatus.Revoked;
        UpdatedAt = now;
    }

    /// <summary>判定指定时点是否在有效期内且生效。</summary>
    /// <param name="at">判定时点（UTC）。</param>
    /// <returns>有效返回 true。</returns>
    public bool IsValidAt(DateTimeOffset at)
        => Status == ApprovalDelegationStatus.Active && StartTime <= at && at <= EndTime;
}
