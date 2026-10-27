/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：仓库/库区/库位 DTO——新建与更新均不含编码（N-1：编码由后端生成，前端只读回显）
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>仓库扁平行。</summary>
/// <param name="Id">仓库 Id（出 API 时经 Hashids 混淆）。</param>
/// <param name="Code">仓库编码（只读）。</param>
/// <param name="Name">仓库名称。</param>
/// <param name="WhType">仓库类型数值枚举（1 自有 / 2 租赁 / 3 临时）。</param>
/// <param name="OrgId">归属组织 Id。</param>
/// <param name="Address">地址。</param>
/// <param name="KeeperId">仓管员人员 Id。</param>
/// <param name="Remark">备注。</param>
/// <param name="Status">启停状态数值枚举。</param>
/// <param name="ApprovalRequired">本次停用是否已转审批挂起（仅 Toggle 响应为 true，其余读路径恒为 false）。</param>
public sealed record WarehouseRow(
    long Id,
    string Code,
    string Name,
    int WhType,
    long? OrgId,
    string? Address,
    long? KeeperId,
    string? Remark,
    int Status,
    bool ApprovalRequired = false);

/// <summary>新建仓库入参（不含编码）。</summary>
/// <param name="Name">名称。</param>
/// <param name="WhType">仓库类型。</param>
/// <param name="OrgId">归属组织 Id。</param>
/// <param name="Address">地址。</param>
/// <param name="KeeperId">仓管员人员 Id。</param>
/// <param name="Remark">备注。</param>
public sealed record CreateWarehousePayload(
    string Name,
    int WhType,
    long? OrgId,
    string? Address,
    long? KeeperId,
    string? Remark);

/// <summary>更新仓库入参（编码不可修改）。</summary>
/// <param name="Name">名称。</param>
/// <param name="WhType">仓库类型。</param>
/// <param name="OrgId">归属组织 Id。</param>
/// <param name="Address">地址。</param>
/// <param name="KeeperId">仓管员人员 Id。</param>
/// <param name="Remark">备注。</param>
public sealed record UpdateWarehousePayload(
    string Name,
    int WhType,
    long? OrgId,
    string? Address,
    long? KeeperId,
    string? Remark);

/// <summary>库区扁平行。</summary>
/// <param name="Id">库区 Id。</param>
/// <param name="WarehouseId">所属仓库 Id。</param>
/// <param name="Code">库区编码。</param>
/// <param name="Name">库区名称。</param>
/// <param name="SortOrder">排序号原始值。</param>
/// <param name="Status">启停状态数值枚举。</param>
public sealed record ZoneRow(
    long Id,
    long WarehouseId,
    string Code,
    string Name,
    int SortOrder,
    int Status);

/// <summary>新建库区入参。</summary>
/// <param name="Code">库区编码（同仓库内唯一）。</param>
/// <param name="Name">名称。</param>
/// <param name="SortOrder">排序号。</param>
public sealed record CreateZonePayload(string Code, string Name, int SortOrder);

/// <summary>更新库区入参（编码不可修改）。</summary>
/// <param name="Name">名称。</param>
/// <param name="SortOrder">排序号。</param>
public sealed record UpdateZonePayload(string Name, int SortOrder);

/// <summary>库位扁平行。</summary>
/// <param name="Id">库位 Id。</param>
/// <param name="WarehouseId">所属仓库 Id。</param>
/// <param name="ZoneId">所属库区 Id。</param>
/// <param name="Code">库位编码。</param>
/// <param name="Barcode">库位条码。</param>
/// <param name="SortOrder">排序号原始值。</param>
/// <param name="Status">启停状态数值枚举。</param>
public sealed record LocationRow(
    long Id,
    long WarehouseId,
    long ZoneId,
    string Code,
    string? Barcode,
    int SortOrder,
    int Status);

/// <summary>新建库位入参。</summary>
/// <param name="Code">库位编码（同库区内唯一）。</param>
/// <param name="Barcode">条码。</param>
/// <param name="SortOrder">排序号。</param>
public sealed record CreateLocationPayload(string Code, string? Barcode, int SortOrder);

/// <summary>更新库位入参（编码不可修改）。</summary>
/// <param name="Barcode">条码。</param>
/// <param name="SortOrder">排序号。</param>
public sealed record UpdateLocationPayload(string? Barcode, int SortOrder);
