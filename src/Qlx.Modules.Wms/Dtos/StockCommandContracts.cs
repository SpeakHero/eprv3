/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存命令 Outbox 契约（人工重推响应）
 */

namespace Qlx.Modules.Wms.Dtos;

/// <summary>库存命令 Outbox 扁平行（枚举以数值下发，文案由前端映射）。</summary>
/// <param name="Id">Outbox 行 Id。</param>
/// <param name="IdempotencyKey">幂等键。</param>
/// <param name="State">分发状态（1 待分发 / 2 已分发 / 3 失败）。</param>
/// <param name="RetryCount">已失败次数。</param>
/// <param name="NextRetryAt">下次可分发时间（终态为空）。</param>
/// <param name="LastError">最近一次失败错误码。</param>
/// <param name="DispatchedAt">成功分发时间。</param>
public sealed record StockCommandRow(
    long Id,
    string IdempotencyKey,
    int State,
    int RetryCount,
    DateTimeOffset? NextRetryAt,
    string? LastError,
    DateTimeOffset? DispatchedAt);
