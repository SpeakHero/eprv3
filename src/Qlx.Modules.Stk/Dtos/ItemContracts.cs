/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品档案 DTO——新建/更新入参均不含商品编码（N-1：编码由后端生成，前端只读回显）；
 *       成本字段在无 stk.cost.field 权限时不下发（B-3-1）
 */

namespace Qlx.Modules.Stk.Dtos;

/// <summary>商品档案扁平行。</summary>
/// <param name="Id">商品 Id（出 API 时经 Hashids 混淆）。</param>
/// <param name="ItemCode">商品编码（只读）。</param>
/// <param name="Name">商品名称。</param>
/// <param name="Spec">规格型号。</param>
/// <param name="Barcode">条码。</param>
/// <param name="CategoryId">商品分类 Id。</param>
/// <param name="UnitId">计量单位 Id。</param>
/// <param name="BatchEnabled">是否启用批次管理。</param>
/// <param name="ExpiryEnabled">是否启用有效期管理。</param>
/// <param name="TaxRate">税率。</param>
/// <param name="RefCostPrice">参考成本价（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="GuidePrice">指导价（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="Status">启停状态数值枚举。</param>
public sealed record ItemRow(
    long Id,
    string ItemCode,
    string Name,
    string? Spec,
    string? Barcode,
    long CategoryId,
    long UnitId,
    bool BatchEnabled,
    bool ExpiryEnabled,
    decimal TaxRate,
    decimal? RefCostPrice,
    decimal? GuidePrice,
    int Status);

/// <summary>新建商品入参（不含商品编码，N-1）。</summary>
/// <param name="Name">商品名称。</param>
/// <param name="Spec">规格型号。</param>
/// <param name="Barcode">条码。</param>
/// <param name="CategoryId">商品分类 Id。</param>
/// <param name="UnitId">计量单位 Id。</param>
/// <param name="BatchEnabled">是否启用批次管理。</param>
/// <param name="ExpiryEnabled">是否启用有效期管理。</param>
/// <param name="TaxRate">税率。</param>
/// <param name="RefCostPrice">参考成本价。</param>
/// <param name="GuidePrice">指导价。</param>
public sealed record CreateItemPayload(
    string Name,
    string? Spec,
    string? Barcode,
    long CategoryId,
    long UnitId,
    bool BatchEnabled,
    bool ExpiryEnabled,
    decimal TaxRate,
    decimal? RefCostPrice,
    decimal? GuidePrice);

/// <summary>更新商品入参（不含商品编码，编码不可修改）。</summary>
/// <param name="Name">商品名称。</param>
/// <param name="Spec">规格型号。</param>
/// <param name="Barcode">条码。</param>
/// <param name="CategoryId">商品分类 Id。</param>
/// <param name="UnitId">计量单位 Id。</param>
/// <param name="BatchEnabled">是否启用批次管理。</param>
/// <param name="ExpiryEnabled">是否启用有效期管理。</param>
/// <param name="TaxRate">税率。</param>
/// <param name="RefCostPrice">参考成本价。</param>
/// <param name="GuidePrice">指导价。</param>
public sealed record UpdateItemPayload(
    string Name,
    string? Spec,
    string? Barcode,
    long CategoryId,
    long UnitId,
    bool BatchEnabled,
    bool ExpiryEnabled,
    decimal TaxRate,
    decimal? RefCostPrice,
    decimal? GuidePrice);

/// <summary>商品关键属性变更审批挂起载荷（目标商品 + 完整更新入参）。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="Payload">完整更新入参。</param>
public sealed record ItemKeyFieldChangePayload(long ItemId, UpdateItemPayload Payload);

/// <summary>商品停用审批挂起载荷。</summary>
/// <param name="ItemId">商品 Id。</param>
public sealed record ItemDisablePayload(long ItemId);

/// <summary>商品价格类变更日志扁平行。</summary>
/// <param name="Id">日志 Id。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="Field">变更字段（1 参考成本 / 2 指导价 / 3 税率 / 4 单位）。</param>
/// <param name="OldValue">变更前值。</param>
/// <param name="NewValue">变更后值。</param>
/// <param name="ApprovedBy">变更生效人。</param>
/// <param name="CreatedAt">记录时间。</param>
public sealed record ItemPriceLogRow(
    long Id,
    long ItemId,
    int Field,
    string? OldValue,
    string? NewValue,
    string? ApprovedBy,
    DateTimeOffset CreatedAt);

/// <summary>审批挂起请求扁平行（出参）。</summary>
/// <param name="Id">请求 Id。</param>
/// <param name="ApprovalInstanceId">审批实例 Id（发起审批前为 null）。</param>
/// <param name="BizType">业务类型。</param>
/// <param name="BizId">业务 Id。</param>
/// <param name="Status">请求状态数值枚举。</param>
public sealed record StkApprovalRequestRow(
    long Id,
    string? ApprovalInstanceId,
    string BizType,
    long BizId,
    int Status);

/// <summary>商品更新结果：直接生效返回扁平行，需审批返回挂起请求。</summary>
/// <param name="PendingApproval">是否已转为审批挂起。</param>
/// <param name="Row">直接生效后的商品扁平行（挂起时为 null）。</param>
/// <param name="Request">审批挂起请求（直接生效时为 null）。</param>
public sealed record UpdateItemResult(
    bool PendingApproval,
    ItemRow? Row,
    StkApprovalRequestRow? Request);
