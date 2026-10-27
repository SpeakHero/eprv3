/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点 DTO——扁平原始字段 + 数值枚举；盲盘由后端裁剪账面量/差异（字段级安全过滤 B-3）
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>盘点计划扁平行。</summary>
/// <param name="Id">计划 Id（出 API 经 Hashids 混淆）。</param>
/// <param name="PlanNo">计划号（后端生成）。</param>
/// <param name="PlanDate">盘点日期。</param>
/// <param name="ScopeType">范围类型数值枚举（1 全仓 / 2 品类 / 3 库位）。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="CategoryId">品类 Id。</param>
/// <param name="LocationId">库位 Id。</param>
/// <param name="IsBlind">是否盲盘。</param>
/// <param name="Remark">备注。</param>
/// <param name="TakeId">已生成的盘点单 Id（未生成为 null）。</param>
/// <param name="CreatedAt">创建时间（原始值）。</param>
public sealed record StockTakePlanRow(
    long Id,
    string PlanNo,
    DateOnly PlanDate,
    int ScopeType,
    long WarehouseId,
    long? CategoryId,
    long? LocationId,
    bool IsBlind,
    string? Remark,
    long? TakeId,
    DateTimeOffset CreatedAt);

/// <summary>新建盘点计划入参（不含计划号，N-1）。</summary>
/// <param name="PlanDate">盘点日期（必填）。</param>
/// <param name="ScopeType">范围类型数值枚举（1 全仓 / 2 品类 / 3 库位）。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="CategoryId">品类 Id（范围 = 品类时必填）。</param>
/// <param name="LocationId">库位 Id（范围 = 库位时必填）。</param>
/// <param name="IsBlind">是否盲盘。</param>
/// <param name="Remark">备注。</param>
public sealed record CreateStockTakePlanPayload(
    DateOnly PlanDate,
    int ScopeType,
    long WarehouseId,
    long? CategoryId,
    long? LocationId,
    bool IsBlind,
    string? Remark);

/// <summary>盘点单头扁平行。</summary>
/// <param name="Id">盘点单 Id。</param>
/// <param name="TakeNo">盘点单号（后端生成）。</param>
/// <param name="PlanId">盘点计划 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="Round">轮次（1 初盘 / 2 复盘）。</param>
/// <param name="IsBlind">是否盲盘。</param>
/// <param name="SourceTakeId">来源初盘单 Id（复盘单）。</param>
/// <param name="Status">状态数值枚举（1 待盘点 / 2 初盘中 / 3 初盘完成 / 4 复盘中 / 5 已完成 / 6 已取消）。</param>
/// <param name="Remark">备注。</param>
/// <param name="CreatedAt">创建时间（原始值）。</param>
public sealed record StockTakeRow(
    long Id,
    string TakeNo,
    long PlanId,
    long WarehouseId,
    int Round,
    bool IsBlind,
    long? SourceTakeId,
    int Status,
    string? Remark,
    DateTimeOffset CreatedAt);

/// <summary>盘点明细扁平行（盲盘时 <c>BookQty</c>/<c>DiffQty</c> 为 null）。</summary>
/// <param name="Id">明细 Id。</param>
/// <param name="LineNo">行号。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="BookQty">账面数量快照（盲盘不下发）。</param>
/// <param name="ActualQty">实盘数量（未录入为 null）。</param>
/// <param name="DiffQty">差异数量（盲盘不下发）。</param>
/// <param name="DiffReason">差异原因。</param>
/// <param name="Frozen">是否已冻结。</param>
public sealed record StockTakeLineRow(
    long Id,
    int LineNo,
    long ItemId,
    string? BatchNo,
    decimal? BookQty,
    decimal? ActualQty,
    decimal? DiffQty,
    string? DiffReason,
    bool Frozen);

/// <summary>盘点单详情（头 + 扁平明细）。</summary>
/// <param name="Take">盘点单头。</param>
/// <param name="Lines">明细行。</param>
public sealed record StockTakeDetailRow(
    StockTakeRow Take,
    IReadOnlyList<StockTakeLineRow> Lines);

/// <summary>保存实盘明细入参。</summary>
/// <param name="LineId">明细 Id。</param>
/// <param name="ActualQty">实盘数量（&gt;=0）。</param>
/// <param name="DiffReason">差异原因（差异非零时必填）。</param>
public sealed record SaveStockTakeCountLinePayload(
    long LineId,
    decimal ActualQty,
    string? DiffReason);

/// <summary>保存实盘入参。</summary>
/// <param name="Lines">实盘结果（可部分录入）。</param>
public sealed record SaveStockTakeCountsPayload(
    IReadOnlyList<SaveStockTakeCountLinePayload> Lines);

/// <summary>盘点完成结果（初盘有差异时回传自动生成的复盘单 Id）。</summary>
/// <param name="Detail">完成后的盘点单详情。</param>
/// <param name="RecheckTakeId">自动生成的复盘单 Id（无差异为 null）。</param>
public sealed record StockTakeFinishResultRow(
    StockTakeDetailRow Detail,
    long? RecheckTakeId);
