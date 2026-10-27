/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 21:00:00 +08:00 中国标准时间 星期一
 * 描述：库存命令 Outbox 入队实现——同事务追加（A-17）、幂等键租户内去重、载荷为 PostStockTxnRequest
 */

using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Wms.Domain.Outbox;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 库存命令 Outbox 入队实现。只追加跟踪态实体，不提交事务；
/// 同一幂等键在「本批 → 跟踪态 → 数据库」三级逐层去重，数据库唯一索引兜底并发。
/// </summary>
/// <param name="db">当前请求的 Wms 租户 DbContext。</param>
/// <param name="tenantCtx">当前租户上下文。</param>
internal sealed class StockCommandQueue(WmsTenantDbContext db, ITenantContext tenantCtx) : IStockCommandQueue
{
    /// <summary>幂等键最大长度（与实体列长一致）。</summary>
    internal const int IdempotencyKeyMaxLength = 160;

    /// <summary>来源单据号最大长度（与来源单号列长一致）。</summary>
    internal const int SourceDocNoMaxLength = 64;

    private readonly WmsTenantDbContext _db = db;
    private readonly ITenantContext _tenantCtx = tenantCtx;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<int> EnqueueAsync(
        IReadOnlyList<PostStockTxnRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return 0;
        }

        long tenantId = TenantId;
        Validate(requests);

        // 本批去重：同一作业单据内行号不允许重复，重复视为调用错误而非静默丢弃
        var batchKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (PostStockTxnRequest request in requests)
        {
            if (!batchKeys.Add(request.IdempotencyKey))
            {
                throw new ValidationException("idempotencyKey", $"同一批次内幂等键重复：{request.IdempotencyKey}");
            }
        }

        // 跟踪态去重：同事务内已入队但尚未提交的行
        var trackedKeys = new HashSet<string>(
            _db.StockCommandOutbox.Local.Select(static x => x.IdempotencyKey),
            StringComparer.Ordinal);

        // 数据库去重：历史已入队（可能已分发）的行
        string[] candidates = [.. batchKeys.Where(k => !trackedKeys.Contains(k))];
        var existingKeys = candidates.Length == 0
            ? []
            : await _db.StockCommandOutbox
                .AsNoTracking()
                .Where(x => candidates.Contains(x.IdempotencyKey))
                .Select(static x => x.IdempotencyKey)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var known = new HashSet<string>(existingKeys, StringComparer.Ordinal);

        var added = 0;
        foreach (PostStockTxnRequest request in requests)
        {
            if (trackedKeys.Contains(request.IdempotencyKey) || known.Contains(request.IdempotencyKey))
            {
                continue;
            }

            string payloadJson = JsonSerializer.Serialize(request, QlxJsonDefaults.PreserveNull);
            _db.StockCommandOutbox.Add(
                new WmsStockCommandOutbox(tenantId, request.IdempotencyKey, payloadJson));

            trackedKeys.Add(request.IdempotencyKey);
            added++;
        }

        return added;
    }

    /// <summary>入队前置校验（V-1：跨模块命令边界独立完整校验）。</summary>
    private static void Validate(IReadOnlyList<PostStockTxnRequest> requests)
    {
        foreach (PostStockTxnRequest request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                throw new ValidationException("idempotencyKey", "幂等键不能为空");
            }

            if (request.IdempotencyKey.Length > IdempotencyKeyMaxLength)
            {
                throw new ValidationException(
                    "idempotencyKey", $"幂等键长度不得超过 {IdempotencyKeyMaxLength}");
            }

            if (string.IsNullOrWhiteSpace(request.SourceDocNo))
            {
                throw new ValidationException("sourceDocNo", "来源单据号不能为空");
            }

            if (request.SourceDocNo.Length > SourceDocNoMaxLength)
            {
                throw new ValidationException(
                    "sourceDocNo", $"来源单据号长度不得超过 {SourceDocNoMaxLength}");
            }

            if (request.SourceLineNo <= 0)
            {
                throw new ValidationException("sourceLineNo", "来源单据行号必须大于 0");
            }

            if (request.Qty <= 0m)
            {
                throw new ValidationException("qty", "数量必须大于 0");
            }
        }
    }
}
