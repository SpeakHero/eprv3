/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业 DTO——扁平原始字段 + 数值枚举，无展示文案与树形结构（B-2）；FEFO 建议为业务规则扁平行
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>出库作业头扁平行。</summary>
/// <param name="Id">作业 Id（出 API 经 Hashids 混淆）。</param>
/// <param name="TaskNo">作业单号（后端生成）。</param>
/// <param name="SourceType">来源类型数值枚举（1 销售 / 2 采购退货 / 3 调拨 / 4 盘亏 / 5 领用）。</param>
/// <param name="SourceDocNo">来源单据号。</param>
/// <param name="WarehouseId">出库仓库 Id。</param>
/// <param name="Status">状态数值枚举（1 待作业 / 2 拣货中 / 3 待复核 / 4 已完成 / 5 已取消）。</param>
/// <param name="PickerId">拣货人用户 Id。</param>
/// <param name="CheckerId">复核人用户 Id。</param>
/// <param name="HandoverTo">交接人。</param>
/// <param name="TotalAmount">出库总金额（完成时估算）。</param>
/// <param name="Remark">备注。</param>
/// <param name="CreatedAt">创建时间（原始值）。</param>
public sealed record StockOutTaskRow(
    long Id,
    string TaskNo,
    int SourceType,
    string SourceDocNo,
    long WarehouseId,
    int Status,
    string? PickerId,
    string? CheckerId,
    string? HandoverTo,
    decimal TotalAmount,
    string? Remark,
    DateTimeOffset CreatedAt);

/// <summary>出库作业明细扁平行。</summary>
/// <param name="Id">明细 Id。</param>
/// <param name="LineNo">行号。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="ApplyQty">申请数量。</param>
/// <param name="PickQty">实拣数量。</param>
/// <param name="CheckQty">复核数量。</param>
/// <param name="LocationId">库位 Id。</param>
/// <param name="BatchNo">批次号。</param>
public sealed record StockOutTaskLineRow(
    long Id,
    int LineNo,
    long ItemId,
    decimal ApplyQty,
    decimal PickQty,
    decimal CheckQty,
    long? LocationId,
    string? BatchNo);

/// <summary>FEFO 拣货建议扁平行（批次效期升序，业务规则在后端求值）。</summary>
/// <param name="LineNo">对应明细行号。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="ExpiryDate">失效日期（原始值）。</param>
/// <param name="Qty">批次可用数量。</param>
public sealed record StockOutFefoSuggestionRow(
    int LineNo,
    long ItemId,
    string BatchNo,
    DateTimeOffset? ExpiryDate,
    decimal Qty);

/// <summary>出库作业详情（头 + 扁平明细 + FEFO 建议）。</summary>
/// <param name="Task">作业头。</param>
/// <param name="Lines">明细行。</param>
/// <param name="FefoSuggestions">FEFO 拣货建议。</param>
public sealed record StockOutTaskDetailRow(
    StockOutTaskRow Task,
    IReadOnlyList<StockOutTaskLineRow> Lines,
    IReadOnlyList<StockOutFefoSuggestionRow> FefoSuggestions);

/// <summary>新建出库作业明细入参。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="ApplyQty">申请出库数量（&gt;0）。</param>
/// <param name="LocationId">库位 Id（可选）。</param>
/// <param name="BatchNo">批次号（可选，拣货时必填）。</param>
public sealed record CreateStockOutLinePayload(
    long ItemId,
    decimal ApplyQty,
    long? LocationId,
    string? BatchNo);

/// <summary>新建出库作业入参（不含单号，N-1）。</summary>
/// <param name="SourceType">来源类型数值枚举（1..5）。</param>
/// <param name="SourceDocNo">来源单据号（非空 ≤32）。</param>
/// <param name="WarehouseId">出库仓库 Id。</param>
/// <param name="Remark">备注。</param>
/// <param name="Lines">明细行（≥1）。</param>
public sealed record CreateStockOutTaskPayload(
    int SourceType,
    string SourceDocNo,
    long WarehouseId,
    string? Remark,
    IReadOnlyList<CreateStockOutLinePayload> Lines);

/// <summary>拣货明细入参。</summary>
/// <param name="LineId">明细 Id。</param>
/// <param name="PickQty">实拣数量（&gt;=0 且 ≤ 申请数量）。</param>
/// <param name="LocationId">库位 Id（可选，须属同仓库且启用）。</param>
/// <param name="BatchNo">批次号（启用批次商品实拣 &gt; 0 时必填）。</param>
public sealed record PickStockOutLinePayload(
    long LineId,
    decimal PickQty,
    long? LocationId,
    string? BatchNo);

/// <summary>拣货入参。</summary>
/// <param name="Lines">拣货明细结果。</param>
public sealed record PickStockOutTaskPayload(IReadOnlyList<PickStockOutLinePayload> Lines);

/// <summary>复核明细入参。</summary>
/// <param name="LineId">明细 Id。</param>
/// <param name="CheckQty">复核数量（须等于实拣数量）。</param>
public sealed record CheckStockOutLinePayload(long LineId, decimal CheckQty);

/// <summary>复核入参。</summary>
/// <param name="HandoverTo">交接人（必填）。</param>
/// <param name="Lines">复核明细结果（须覆盖该单全部明细）。</param>
public sealed record CheckStockOutTaskPayload(
    string HandoverTo,
    IReadOnlyList<CheckStockOutLinePayload> Lines);

/// <summary>完成作业结果。</summary>
/// <param name="Task">作业头（审批挂起时状态仍为待复核）。</param>
/// <param name="TotalAmount">出库总金额。</param>
/// <param name="ApprovalRequired">是否转大额审批挂起（true 时未写 Outbox）。</param>
public sealed record StockOutCompleteResultRow(
    StockOutTaskRow Task,
    decimal TotalAmount,
    bool ApprovalRequired);
