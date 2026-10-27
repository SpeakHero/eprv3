/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：期初建账 DTO——新建/更新入参不含期初单号与金额（N-1/V-1），
 *       全部出参为扁平原始字段，枚举下发数值 int（中文映射在前端，B-2）
 */

namespace Qlx.Modules.Stk.Dtos;

/// <summary>期初建账单头扁平行。</summary>
/// <param name="Id">期初批次 Id。</param>
/// <param name="BatchNo">期初单号（只读，后端生成）。</param>
/// <param name="WarehouseId">仓库 Id（原始引用）。</param>
/// <param name="Status">状态数值枚举（1 草稿 / 2 待审批 / 3 已过账）。</param>
/// <param name="PostedAt">过账时间。</param>
/// <param name="Remark">备注。</param>
public sealed record OpeningBatchRow(
    long Id,
    string BatchNo,
    long WarehouseId,
    int Status,
    DateTimeOffset? PostedAt,
    string? Remark);

/// <summary>期初建账明细扁平行。</summary>
/// <param name="Id">明细 Id。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="Qty">期初数量。</param>
/// <param name="UnitCost">期初单价。</param>
/// <param name="Amount">期初金额（后端计算）。</param>
public sealed record OpeningItemRow(
    long Id,
    long ItemId,
    string? BatchNo,
    decimal Qty,
    decimal UnitCost,
    decimal Amount);

/// <summary>期初建账详情（表头 + 明细）。</summary>
/// <param name="Header">表头扁平行。</param>
/// <param name="Items">明细扁平行列表。</param>
public sealed record OpeningBatchDetailRow(
    OpeningBatchRow Header,
    IReadOnlyList<OpeningItemRow> Items);

/// <summary>期初明细入参（不含金额，金额由后端计算）。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="BatchNo">批次号（非批次商品为空）。</param>
/// <param name="Qty">期初数量。</param>
/// <param name="UnitCost">期初单价。</param>
public sealed record OpeningItemPayload(
    long ItemId,
    string? BatchNo,
    decimal Qty,
    decimal UnitCost);

/// <summary>新建期初草稿入参（不含期初单号，N-1）。</summary>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="Remark">备注。</param>
/// <param name="Lines">明细列表。</param>
public sealed record CreateOpeningPayload(
    long WarehouseId,
    string? Remark,
    IReadOnlyList<OpeningItemPayload> Lines);

/// <summary>更新期初草稿入参（仅草稿可改，整体替换明细）。</summary>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="Remark">备注。</param>
/// <param name="Lines">明细列表。</param>
public sealed record UpdateOpeningPayload(
    long WarehouseId,
    string? Remark,
    IReadOnlyList<OpeningItemPayload> Lines);

/// <summary>期初仓库汇总扁平行（仅已过账明细；不拼接仓库名称，B-2）。</summary>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="TotalQty">数量合计。</param>
/// <param name="TotalAmount">金额合计。</param>
public sealed record OpeningWarehouseSummaryRow(
    long WarehouseId,
    decimal TotalQty,
    decimal TotalAmount);
