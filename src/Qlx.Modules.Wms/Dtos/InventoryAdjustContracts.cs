/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏 DTO——扁平原始字段 + 数值枚举，无展示文案与格式化金额（B-2）
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>盘盈盘亏单头扁平行。</summary>
/// <param name="Id">单据 Id（出 API 经 Hashids 混淆）。</param>
/// <param name="AdjustNo">单据号（后端生成，WL-PYK）。</param>
/// <param name="TakeId">来源盘点单 Id（手工建单为 null）。</param>
/// <param name="Type">类型数值枚举（1 盘盈 / 2 盘亏）。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="Reason">盘盈盘亏原因。</param>
/// <param name="TotalAmount">金额估算（Σ 数量 × 当前均价，原始值）。</param>
/// <param name="Remark">备注。</param>
/// <param name="Status">状态数值枚举（1 草稿 / 2 待审批 / 3 已通过）。</param>
/// <param name="CreatedAt">创建时间（原始值）。</param>
public sealed record InventoryAdjustRow(
    long Id,
    string AdjustNo,
    long? TakeId,
    int Type,
    long WarehouseId,
    string Reason,
    decimal TotalAmount,
    string? Remark,
    int Status,
    DateTimeOffset CreatedAt);

/// <summary>盘盈盘亏明细扁平行。</summary>
/// <param name="Id">明细 Id。</param>
/// <param name="LineNo">行号。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="Qty">调整数量（&gt;0，方向由单头类型决定）。</param>
/// <param name="UnitCost">成本估算单价（原始值）。</param>
/// <param name="Remark">行备注。</param>
public sealed record InventoryAdjustLineRow(
    long Id,
    int LineNo,
    long ItemId,
    string? BatchNo,
    decimal Qty,
    decimal? UnitCost,
    string? Remark);

/// <summary>盘盈盘亏单详情（头 + 扁平明细）。</summary>
/// <param name="Adjust">单据头。</param>
/// <param name="Lines">明细行。</param>
public sealed record InventoryAdjustDetailRow(
    InventoryAdjustRow Adjust,
    IReadOnlyList<InventoryAdjustLineRow> Lines);

/// <summary>新建盘盈盘亏明细入参。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="BatchNo">批次号（启用批次商品必填）。</param>
/// <param name="Qty">调整数量（&gt;0）。</param>
/// <param name="Remark">行备注。</param>
public sealed record CreateInventoryAdjustLinePayload(
    long ItemId,
    string? BatchNo,
    decimal Qty,
    string? Remark);

/// <summary>新建盘盈盘亏单入参（不含单号，N-1）。</summary>
/// <param name="Type">类型数值枚举（1 盘盈 / 2 盘亏）。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="Reason">盘盈盘亏原因（必填）。</param>
/// <param name="Remark">备注。</param>
/// <param name="Lines">明细行（≥1）。</param>
public sealed record CreateInventoryAdjustPayload(
    int Type,
    long WarehouseId,
    string Reason,
    string? Remark,
    IReadOnlyList<CreateInventoryAdjustLinePayload> Lines);
