/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警 DTO——扁平原始字段 + 数值枚举，无展示文案与格式化（B-2）
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>库存预警扁平行。</summary>
/// <param name="Id">预警 Id（出 API 经 Hashids 混淆）。</param>
/// <param name="AlertType">类型数值枚举（1 安全库存 / 2 积压 / 3 临期 / 4 在途超期）。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="BatchNo">批次号（非批次维度为空串）。</param>
/// <param name="Threshold">判定阈值（数量或天数，原始值）。</param>
/// <param name="ActualValue">实际值（数量或天数，原始值）。</param>
/// <param name="AlertDate">预警日期。</param>
/// <param name="Status">状态数值枚举（1 待处理 / 2 已处理 / 3 已忽略）。</param>
/// <param name="HandledBy">处理人标识。</param>
/// <param name="HandledNote">处理说明。</param>
/// <param name="HandledAt">处理时间。</param>
/// <param name="CreatedAt">创建时间。</param>
public sealed record InventoryAlertRow(
    long Id,
    int AlertType,
    long ItemId,
    long WarehouseId,
    string BatchNo,
    decimal Threshold,
    decimal ActualValue,
    DateOnly AlertDate,
    int Status,
    string? HandledBy,
    string? HandledNote,
    DateTimeOffset? HandledAt,
    DateTimeOffset CreatedAt);

/// <summary>处理预警入参（处理说明必填）。</summary>
/// <param name="HandledNote">处理说明（≤256，必填）。</param>
public sealed record HandleInventoryAlertPayload(string HandledNote);
