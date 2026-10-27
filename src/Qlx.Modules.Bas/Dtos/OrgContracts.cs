/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 13:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas 组织架构与岗位主数据 DTO——仅扁平原始字段与数值枚举（B-2），
 *       树组装、中文映射、展示排序一律由前端完成；业务 Id 由全局 Hashids 元数据自动混淆。
 */

namespace Qlx.Modules.Bas.Dtos;

// ================ 组织 ================

/// <summary>
/// 组织扁平行（原始字段）。<c>Type</c>/<c>Status</c> 为数值枚举，映射字典在前端公共层维护。
/// </summary>
/// <param name="Id">组织 Id。</param>
/// <param name="ParentId">父级组织 Id（null 为根）。</param>
/// <param name="Code">组织编码（统一发号器生成，不可修改）。</param>
/// <param name="Name">组织名称。</param>
/// <param name="Type">节点类型（1=集团 / 2=子公司 / 3=部门 / 4=项目部）。</param>
/// <param name="LeaderPersonId">负责人自然人 Id。</param>
/// <param name="EffectDate">生效日期。</param>
/// <param name="ExpiryDate">失效日期（null 表示长期有效）。</param>
/// <param name="Version">数据版本号。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="SortOrder">同级排序号。</param>
/// <param name="IsBuiltin">是否系统内置根节点。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record OrgRow(
    long Id,
    long? ParentId,
    string Code,
    string Name,
    int Type,
    long? LeaderPersonId,
    DateOnly EffectDate,
    DateOnly? ExpiryDate,
    int Version,
    int Status,
    int SortOrder,
    bool IsBuiltin,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 组织新建 / 更新入参（无编码字段：编码由后端统一发号器生成，N-1）。
/// </summary>
/// <param name="ParentId">父级组织 Id。</param>
/// <param name="Name">组织名称。</param>
/// <param name="Type">节点类型数值。</param>
/// <param name="LeaderPersonId">负责人自然人 Id。</param>
/// <param name="SortOrder">同级排序号。</param>
/// <param name="EffectDate">生效日期（null 取当天）。</param>
/// <param name="ExpiryDate">失效日期（null 表示长期有效）。</param>
public sealed record SaveOrgPayload(
    long? ParentId,
    string Name,
    int Type,
    long? LeaderPersonId,
    int SortOrder,
    DateOnly? EffectDate = null,
    DateOnly? ExpiryDate = null);

/// <summary>组织版本历史行（快照 JSON 为当次写操作后的扁平原始字段）。</summary>
/// <param name="Id">历史行 Id。</param>
/// <param name="OrgId">组织 Id。</param>
/// <param name="Version">数据版本号。</param>
/// <param name="SnapshotJson">版本快照 JSON。</param>
/// <param name="EffectiveFrom">生效时间。</param>
/// <param name="ChangedBy">变更人。</param>
public sealed record OrgHistoryRow(
    long Id,
    long OrgId,
    int Version,
    string SnapshotJson,
    DateTimeOffset EffectiveFrom,
    string ChangedBy);

/// <summary>组织合并入参。</summary>
/// <param name="TargetOrgId">并入的目标组织 Id。</param>
public sealed record MergeOrgPayload(long TargetOrgId);

/// <summary>
/// 组织调整审批申请入参（审批通过后按 <see cref="Action"/> 应用变更）。
/// </summary>
/// <param name="Action">动作：create / update / disable / merge。</param>
/// <param name="OrgId">目标组织 Id（create 时为空）。</param>
/// <param name="Name">组织名称。</param>
/// <param name="ParentId">父级组织 Id。</param>
/// <param name="Type">节点类型数值。</param>
/// <param name="LeaderPersonId">负责人自然人 Id。</param>
/// <param name="SortOrder">同级排序号。</param>
/// <param name="TargetOrgId">合并目标组织 Id（merge 时必填）。</param>
/// <param name="Reason">申请理由。</param>
public sealed record SubmitOrgAdjustPayload(
    string Action,
    long? OrgId,
    string? Name,
    long? ParentId,
    int? Type,
    long? LeaderPersonId,
    int? SortOrder,
    long? TargetOrgId,
    string? Reason);

// ================ 岗位 ================

/// <summary>岗位扁平行（数值枚举，无展示拼接）。</summary>
/// <param name="Id">岗位 Id。</param>
/// <param name="OrgId">所属组织 Id。</param>
/// <param name="Name">岗位名称。</param>
/// <param name="Code">岗位编码（后端生成，不可修改）。</param>
/// <param name="Headcount">编制人数。</param>
/// <param name="CurrentCount">当前在岗人数（既有汇总值，暂不回源人员任职记录）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="Description">岗位说明。</param>
/// <param name="Duty">岗位职责。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record PositionMasterRow(
    long Id,
    long OrgId,
    string Name,
    string Code,
    int Headcount,
    int CurrentCount,
    int Status,
    string? Description,
    string? Duty,
    DateTimeOffset UpdatedAt);

/// <summary>岗位新建 / 更新入参（无编码字段）。</summary>
/// <param name="Id">岗位 Id（null 表示新建）。</param>
/// <param name="Name">岗位名称。</param>
/// <param name="OrgId">所属组织 Id。</param>
/// <param name="Headcount">编制人数。</param>
/// <param name="Description">岗位说明。</param>
/// <param name="Duty">岗位职责。</param>
public sealed record SavePositionCommand(
    long? Id,
    string Name,
    long OrgId,
    int Headcount,
    string? Description,
    string? Duty);

/// <summary>岗位在岗统计行（编制与在岗原始数值）。</summary>
/// <param name="PositionId">岗位 Id。</param>
/// <param name="Headcount">编制人数。</param>
/// <param name="CurrentCount">当前在岗人数。</param>
public sealed record PositionStatsRow(
    long PositionId,
    int Headcount,
    int CurrentCount);

/// <summary>超编审批申请入参。</summary>
/// <param name="Headcount">申请调整后的编制人数。</param>
/// <param name="Reason">申请理由。</param>
public sealed record SubmitOverstaffPayload(
    int Headcount,
    string? Reason);

/// <summary>超编审批挂起载荷（审批通过后按此调整编制）。</summary>
/// <param name="PositionId">岗位 Id。</param>
/// <param name="Headcount">申请调整后的编制人数。</param>
/// <param name="Reason">申请理由。</param>
public sealed record OverstaffPayload(
    long PositionId,
    int Headcount,
    string? Reason);

// ================ 审批挂起请求 ================

/// <summary>审批挂起请求行（PayloadJson 为期望变更的原始载荷）。</summary>
/// <param name="Id">请求行 Id。</param>
/// <param name="ApprovalInstanceId">审批实例 Id（未发起时为空）。</param>
/// <param name="BizType">业务类型。</param>
/// <param name="BizId">业务 Id。</param>
/// <param name="PayloadJson">期望变更载荷 JSON。</param>
/// <param name="Status">请求状态（1=待审批 / 2=通过 / 3=驳回 / 4=撤回）。</param>
/// <param name="AppliedAt">生效时间。</param>
/// <param name="CreatedAt">创建时间。</param>
public sealed record ApprovalRequestRow(
    long Id,
    string? ApprovalInstanceId,
    string BizType,
    long BizId,
    string? PayloadJson,
    int Status,
    DateTimeOffset? AppliedAt,
    DateTimeOffset CreatedAt);
