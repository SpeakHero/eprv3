/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：成本调整 DTO——新建/更新入参不含单号、旧均价与差额（N-1/V-1），
 *       全部出参为扁平原始字段，枚举下发数值 int（中文映射在前端，B-2）
 */

namespace Qlx.Modules.Stk.Dtos;

/// <summary>成本调整单头扁平行。</summary>
/// <param name="Id">成本调整单 Id。</param>
/// <param name="AdjNo">成本调整单号（只读，后端生成）。</param>
/// <param name="ReasonCode">调整原因（1 价格差错 / 2 盘盈亏联动 / 3 其他）。</param>
/// <param name="WarehouseId">仓库 Id（空表示全部仓库）。</param>
/// <param name="TotalDiffAmount">差额合计。</param>
/// <param name="Status">状态数值枚举（1 草稿 / 2 待审批 / 3 已通过 / 4 已驳回）。</param>
/// <param name="AppliedAt">生效时间。</param>
/// <param name="Remark">备注。</param>
public sealed record CostAdjustRow(
    long Id,
    string AdjNo,
    int ReasonCode,
    long? WarehouseId,
    decimal TotalDiffAmount,
    int Status,
    DateTimeOffset? AppliedAt,
    string? Remark);

/// <summary>成本调整明细扁平行。</summary>
/// <param name="Id">明细 Id。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="OldAvgCost">旧均价（台账当前值，后端取值）。</param>
/// <param name="NewAvgCost">新均价。</param>
/// <param name="DiffAmount">差额（后端计算）。</param>
public sealed record CostAdjustLineRow(
    long Id,
    long ItemId,
    long WarehouseId,
    decimal OldAvgCost,
    decimal NewAvgCost,
    decimal DiffAmount);

/// <summary>成本调整详情（表头 + 明细）。</summary>
/// <param name="Header">表头扁平行。</param>
/// <param name="Lines">明细扁平行列表。</param>
public sealed record CostAdjustDetailRow(
    CostAdjustRow Header,
    IReadOnlyList<CostAdjustLineRow> Lines);

/// <summary>成本调整明细入参（不含旧均价与差额，均由后端取值计算）。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="NewAvgCost">新移动加权平均单价。</param>
public sealed record CostAdjustLinePayload(
    long ItemId,
    long WarehouseId,
    decimal NewAvgCost);

/// <summary>新建成本调整草稿入参（不含单号，N-1）。</summary>
/// <param name="ReasonCode">调整原因（1/2/3）。</param>
/// <param name="WarehouseId">仓库 Id（空表示全部仓库）。</param>
/// <param name="Lines">明细列表。</param>
/// <param name="Remark">备注。</param>
public sealed record CreateCostAdjustPayload(
    int ReasonCode,
    long? WarehouseId,
    IReadOnlyList<CostAdjustLinePayload> Lines,
    string? Remark);

/// <summary>更新成本调整草稿入参（仅草稿可改，整体替换明细）。</summary>
/// <param name="ReasonCode">调整原因（1/2/3）。</param>
/// <param name="WarehouseId">仓库 Id（空表示全部仓库）。</param>
/// <param name="Lines">明细列表。</param>
/// <param name="Remark">备注。</param>
public sealed record UpdateCostAdjustPayload(
    int ReasonCode,
    long? WarehouseId,
    IReadOnlyList<CostAdjustLinePayload> Lines,
    string? Remark);
