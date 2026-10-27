/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：调拨与在途 DTO——扁平原始字段 + 数值枚举，无展示文案与超期标记（B-2）
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>调拨单头扁平行。</summary>
/// <param name="Id">调拨单 Id（出 API 经 Hashids 混淆）。</param>
/// <param name="TransferNo">调拨单号（后端生成）。</param>
/// <param name="FromWarehouseId">调出仓库 Id。</param>
/// <param name="ToWarehouseId">调入仓库 Id。</param>
/// <param name="FromOrgId">调出仓归属组织 Id。</param>
/// <param name="ToOrgId">调入仓归属组织 Id。</param>
/// <param name="InTransitDays">预计在途天数。</param>
/// <param name="Status">状态数值枚举（1 待审批 / 2 已生效 / 3 调出已发货 / 4 调入已收货 / 5 已作废）。</param>
/// <param name="Remark">备注。</param>
/// <param name="CreatedAt">创建时间（原始值）。</param>
public sealed record StockTransferRow(
    long Id,
    string TransferNo,
    long FromWarehouseId,
    long ToWarehouseId,
    long? FromOrgId,
    long? ToOrgId,
    int InTransitDays,
    int Status,
    string? Remark,
    DateTimeOffset CreatedAt);

/// <summary>调拨明细扁平行。</summary>
/// <param name="Id">明细 Id。</param>
/// <param name="LineNo">行号。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="Qty">调拨数量。</param>
/// <param name="ShipQty">发货数量。</param>
/// <param name="ReceiveQty">收货数量。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="UnitCost">成本快照。</param>
/// <param name="DiffNote">收货差异说明。</param>
public sealed record StockTransferLineRow(
    long Id,
    int LineNo,
    long ItemId,
    decimal Qty,
    decimal ShipQty,
    decimal ReceiveQty,
    string? BatchNo,
    decimal? UnitCost,
    string? DiffNote);

/// <summary>调拨单详情（头 + 扁平明细）。</summary>
/// <param name="Transfer">调拨单头。</param>
/// <param name="Lines">明细行。</param>
public sealed record StockTransferDetailRow(
    StockTransferRow Transfer,
    IReadOnlyList<StockTransferLineRow> Lines);

/// <summary>新建调拨明细入参。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="Qty">调拨数量（&gt;0）。</param>
/// <param name="BatchNo">批次号（启用批次商品必填）。</param>
public sealed record CreateStockTransferLinePayload(long ItemId, decimal Qty, string? BatchNo);

/// <summary>新建调拨单入参（不含单号，N-1）。</summary>
/// <param name="FromWarehouseId">调出仓库 Id。</param>
/// <param name="ToWarehouseId">调入仓库 Id（须不同于调出仓）。</param>
/// <param name="InTransitDays">预计在途天数（可空，缺省取系统参数）。</param>
/// <param name="Remark">备注。</param>
/// <param name="Lines">明细行（≥1）。</param>
public sealed record CreateStockTransferPayload(
    long FromWarehouseId,
    long ToWarehouseId,
    int? InTransitDays,
    string? Remark,
    IReadOnlyList<CreateStockTransferLinePayload> Lines);

/// <summary>发货明细入参。</summary>
/// <param name="LineId">明细 Id。</param>
/// <param name="ShipQty">发货数量（&gt;=0 且 ≤ 调拨数量）。</param>
public sealed record ShipStockTransferLinePayload(long LineId, decimal ShipQty);

/// <summary>发货入参。</summary>
/// <param name="Lines">发货明细结果。</param>
public sealed record ShipStockTransferPayload(IReadOnlyList<ShipStockTransferLinePayload> Lines);

/// <summary>收货明细入参。</summary>
/// <param name="LineId">明细 Id。</param>
/// <param name="ReceiveQty">收货数量（&gt;=0 且 ≤ 发货数量）。</param>
/// <param name="DiffNote">差异说明（收货数量少于发货数量时必填）。</param>
public sealed record ReceiveStockTransferLinePayload(
    long LineId,
    decimal ReceiveQty,
    string? DiffNote);

/// <summary>收货入参。</summary>
/// <param name="Lines">收货明细结果（须覆盖全部已发货明细）。</param>
public sealed record ReceiveStockTransferPayload(
    IReadOnlyList<ReceiveStockTransferLinePayload> Lines);

/// <summary>在途库存扁平行（超期标记由前端按 预计到达 计算）。</summary>
/// <param name="Id">在途行 Id。</param>
/// <param name="TransferId">调拨单 Id。</param>
/// <param name="TransferNo">调拨单号。</param>
/// <param name="TransferLineId">调拨明细 Id。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="FromWarehouseId">调出仓库 Id。</param>
/// <param name="ToWarehouseId">调入仓库 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="Qty">在途数量。</param>
/// <param name="UnitCost">成本快照。</param>
/// <param name="ShippedAt">发货时间（原始值）。</param>
/// <param name="ExpectArrive">预计到达时间（原始值）。</param>
/// <param name="Status">在途状态数值枚举（1 在途 / 2 已关闭）。</param>
public sealed record InTransitStockRow(
    long Id,
    long TransferId,
    string TransferNo,
    long TransferLineId,
    long ItemId,
    long FromWarehouseId,
    long ToWarehouseId,
    string? BatchNo,
    decimal Qty,
    decimal UnitCost,
    DateTimeOffset ShippedAt,
    DateTimeOffset ExpectArrive,
    int Status);
