/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存命令 Outbox 服务实现——失败终态人工重推（R-8），重复重推 409
 */

using System.Globalization;

using Qlx.Abstractions.Exceptions;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>库存命令 Outbox 服务实现。</summary>
/// <param name="repo">Wms 租户仓储。</param>
internal sealed class StockCommandService(IWmsTenantRepository repo) : IStockCommandService
{
    /// <summary>状态机非法流转错误码。</summary>
    internal const string ErrorInvalidState = "WMS_INVALID_STATE";

    private readonly IWmsTenantRepository _repo = repo;

    /// <inheritdoc />
    public async Task<StockCommandRow> RetryAsync(long id, CancellationToken ct)
    {
        if (id <= 0)
        {
            throw new ValidationException("id", "库存命令 Id 非法");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await _repo.ExecuteInTransactionAsync(async token =>
        {
            WmsStockCommandOutbox row = await _repo
                .FindStockCommandOutboxAsync(id, token).ConfigureAwait(false)
                ?? throw new NotFoundException("库存命令", id.ToString(CultureInfo.InvariantCulture));

            if (!row.IsTerminalFailure)
            {
                throw new ConflictException("仅分发失败终态的命令可重推", ErrorInvalidState);
            }

            row.ResetForRetry(now);
            _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(row);
        }, ct).ConfigureAwait(false);
    }

    private static StockCommandRow Map(WmsStockCommandOutbox row)
        => new(
            row.Id,
            row.IdempotencyKey,
            (int)row.State,
            row.RetryCount,
            row.NextRetryAt,
            row.LastError,
            row.DispatchedAt);
}
