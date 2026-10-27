/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 21:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms → Stk 库存命令 Outbox 实体（wms_stock_command_outbox）——作业完成与命令入队同事务，
 *       分发任务按幂等键逐条投递，失败退避重试、5 次后终态（spec §14.3 / A-17）
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Wms.Domain.Outbox;

/// <summary>
/// 库存命令 Outbox（表 wms_stock_command_outbox）。<see cref="IdempotencyKey"/> 租户内唯一，
/// 同一作业行重复完成不会产生第二条命令；<see cref="PayloadJson"/> 为 <c>PostStockTxnRequest</c> 序列化结果。
/// </summary>
[Index(nameof(TenantId), nameof(IdempotencyKey), IsUnique = true)]
[Index(nameof(TenantId), nameof(State), nameof(NextRetryAt))]
public sealed class WmsStockCommandOutbox : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>分发失败后的最大重试次数（达到即终态）。</summary>
    public const int MaxRetryCount = 5;

    /// <summary>单次失败后的退避阶梯（分钟，spec §14.3）。</summary>
    private static readonly TimeSpan[] s_backoffSteps =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8),
        TimeSpan.FromMinutes(16),
    ];

    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>幂等键（格式 <c>wms:{biz}:{docNo}:{lineNo}</c>，租户内唯一）。</summary>
    [MaxLength(160)]
    public string IdempotencyKey { get; internal set; } = string.Empty;

    /// <summary>库存变动请求载荷 JSON（<c>PostStockTxnRequest</c>）。</summary>
    public string PayloadJson { get; internal set; } = string.Empty;

    /// <summary>分发状态。</summary>
    public WmsStockCommandState State { get; private set; } = WmsStockCommandState.Pending;

    /// <summary>已失败次数。</summary>
    public int RetryCount { get; private set; }

    /// <summary>下次可分发时间；终态为 null。</summary>
    public DateTimeOffset? NextRetryAt { get; private set; }

    /// <summary>最近一次失败错误码（成功分发后清空）。</summary>
    [MaxLength(64)]
    public string? LastError { get; private set; }

    /// <summary>成功分发时间。</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public WmsStockCommandOutbox() { }

    /// <summary>创建待分发命令。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="payloadJson">库存变动请求载荷 JSON。</param>
    public WmsStockCommandOutbox(long tenantId, string idempotencyKey, string payloadJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (idempotencyKey.Length > 160)
        {
            throw new ArgumentOutOfRangeException(nameof(idempotencyKey), "幂等键长度不得超过 160");
        }

        TenantId = tenantId;
        IdempotencyKey = idempotencyKey;
        PayloadJson = payloadJson;
    }

    /// <summary>是否已分发成功。</summary>
    public bool IsDispatched => State == WmsStockCommandState.Dispatched;

    /// <summary>是否为人工重推可恢复的终态（失败且无可执行时间）。</summary>
    public bool IsTerminalFailure => State == WmsStockCommandState.Failed && NextRetryAt is null;

    /// <summary>标记分发成功（含 Stk 幂等命中）。</summary>
    /// <param name="dispatchedAt">分发时间。</param>
    public void MarkDispatched(DateTimeOffset dispatchedAt)
    {
        State = WmsStockCommandState.Dispatched;
        DispatchedAt = dispatchedAt;
        NextRetryAt = null;
        LastError = null;
    }

    /// <summary>登记一次分发失败并按退避阶梯安排下次重试；达到上限即终态。</summary>
    /// <param name="failedAt">失败时间。</param>
    /// <param name="errorCode">失败错误码。</param>
    public void RegisterFailure(DateTimeOffset failedAt, string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        RetryCount++;
        LastError = errorCode.Length > 64 ? errorCode[..64] : errorCode;
        State = WmsStockCommandState.Failed;

        if (RetryCount >= MaxRetryCount)
        {
            NextRetryAt = null;
            return;
        }

        NextRetryAt = failedAt + s_backoffSteps[RetryCount - 1];
    }

    /// <summary>人工重推：仅终态可用，重置为待分发、清零重试计数。</summary>
    /// <param name="now">重推时间。</param>
    public void ResetForRetry(DateTimeOffset now)
    {
        if (!IsTerminalFailure)
        {
            throw new InvalidOperationException("仅失败终态的命令可人工重推");
        }

        State = WmsStockCommandState.Pending;
        RetryCount = 0;
        NextRetryAt = now;
        LastError = null;
    }
}
