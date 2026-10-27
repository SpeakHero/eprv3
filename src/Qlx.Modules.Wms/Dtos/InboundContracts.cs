/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:20:00 +08:00 中国标准时间 星期一
 * 描述：入库作业 DTO——扁平原始字段 + 数值枚举，无展示文案与树形结构（B-2）
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>入库作业头扁平行。</summary>
/// <param name="Id">作业 Id（出 API 经 Hashids 混淆）。</param>
/// <param name="TaskNo">作业单号（后端生成）。</param>
/// <param name="SourceType">来源类型数值枚举（1 采购 / 2 销售退货 / 3 调拨 / 4 盘盈）。</param>
/// <param name="SourceDocNo">来源单据号。</param>
/// <param name="WarehouseId">入库仓库 Id。</param>
/// <param name="Status">状态数值枚举（1 待作业 / 2 作业中 / 3 已完成 / 4 已取消）。</param>
/// <param name="Remark">备注。</param>
/// <param name="CreatedAt">创建时间（原始值）。</param>
public sealed record StockInTaskRow(
    long Id,
    string TaskNo,
    int SourceType,
    string SourceDocNo,
    long WarehouseId,
    int Status,
    string? Remark,
    DateTimeOffset CreatedAt);

/// <summary>入库作业明细扁平行。</summary>
/// <param name="Id">明细 Id。</param>
/// <param name="LineNo">行号。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="ExpectQty">预期数量。</param>
/// <param name="CountQty">清点数量。</param>
/// <param name="ConfirmQty">确认数量。</param>
/// <param name="LocationId">库位 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="UnitCost">入库单价（原始值，未回退填充）。</param>
public sealed record StockInTaskLineRow(
    long Id,
    int LineNo,
    long ItemId,
    decimal ExpectQty,
    decimal CountQty,
    decimal ConfirmQty,
    long? LocationId,
    string? BatchNo,
    decimal? UnitCost);

/// <summary>入库作业详情（头 + 扁平明细）。</summary>
/// <param name="Task">作业头。</param>
/// <param name="Lines">明细行。</param>
public sealed record StockInTaskDetailRow(
    StockInTaskRow Task,
    IReadOnlyList<StockInTaskLineRow> Lines);

/// <summary>新建入库作业明细入参。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="ExpectQty">预期数量（&gt;0）。</param>
/// <param name="LocationId">库位 Id（可选）。</param>
/// <param name="BatchNo">批次号（启用批次商品完成时必填）。</param>
/// <param name="UnitCost">入库单价（可选，&gt;=0）。</param>
public sealed record CreateStockInLinePayload(
    long ItemId,
    decimal ExpectQty,
    long? LocationId,
    string? BatchNo,
    decimal? UnitCost);

/// <summary>新建入库作业入参（不含单号，N-1）。</summary>
/// <param name="SourceType">来源类型数值枚举（1..4）。</param>
/// <param name="SourceDocNo">来源单据号（非空 ≤32）。</param>
/// <param name="WarehouseId">入库仓库 Id。</param>
/// <param name="Remark">备注。</param>
/// <param name="Lines">明细行（≥1）。</param>
public sealed record CreateStockInTaskPayload(
    int SourceType,
    string SourceDocNo,
    long WarehouseId,
    string? Remark,
    IReadOnlyList<CreateStockInLinePayload> Lines);

/// <summary>完成作业明细入参（清点/确认结果）。</summary>
/// <param name="LineId">明细 Id。</param>
/// <param name="CountQty">清点数量（&gt;=0）。</param>
/// <param name="ConfirmQty">确认数量（&gt;=0 且 ≤清点数量）。</param>
/// <param name="LocationId">库位 Id（可选，须属同仓库且启用）。</param>
/// <param name="BatchNo">批次号（启用批次商品必填）。</param>
/// <param name="UnitCost">入库单价（可选，&gt;=0）。</param>
public sealed record CompleteStockInLinePayload(
    long LineId,
    decimal CountQty,
    decimal ConfirmQty,
    long? LocationId,
    string? BatchNo,
    decimal? UnitCost);

/// <summary>完成作业入参。</summary>
/// <param name="Lines">明细结果（须覆盖该单全部明细）。</param>
public sealed record CompleteStockInTaskPayload(IReadOnlyList<CompleteStockInLinePayload> Lines);
