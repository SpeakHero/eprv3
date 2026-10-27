/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 21:00:00 +08:00 中国标准时间 星期一
 * 描述：库存命令分发任务（wms.stock.command.dispatch）——按幂等键逐条投递 Stk，
 *       成功/幂等命中置已分发，失败按 1/2/4/8/16 分钟退避重试，5 次后终态（spec §14.3）
 */

using System.Diagnostics;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.Modules.Wms.Domain.Outbox;

namespace Qlx.Modules.Wms.Jobs;

/// <summary>
/// 库存命令分发任务处理器。单批至多 <see cref="BatchSize"/> 行，按 Id 升序逐条投递；
/// 每行投递走 Stk 自带事务，本任务仅更新 Outbox 行状态（Wms 侧一次 SaveChanges）。
/// </summary>
/// <param name="db">当前租户的 Wms DbContext。</param>
/// <param name="stockTxnCommandService">Stk 库存写入命令契约（唯一台账写入口）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class WmsStockCommandDispatchJobHandler(
    WmsTenantDbContext db,
    IStockTxnCommandService stockTxnCommandService,
    ILogger<WmsStockCommandDispatchJobHandler> logger) : IJobHandler
{
    /// <summary>单轮最大分发行数。</summary>
    internal const int BatchSize = 100;

    /// <summary>载荷反序列化失败错误码。</summary>
    internal const string ErrorPayloadInvalid = "WMS_PAYLOAD_INVALID";

    /// <summary>投递抛出异常时的错误码。</summary>
    internal const string ErrorDispatchException = "WMS_DISPATCH_EXCEPTION";

    private readonly WmsTenantDbContext _db = db;
    private readonly IStockTxnCommandService _stockTxnCommandService = stockTxnCommandService;
    private readonly ILogger<WmsStockCommandDispatchJobHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => WmsJobCodes.StockCommandDispatch;

    /// <inheritdoc />
    public Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return DispatchOnceAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>
    /// 执行一轮分发（内部可见，供单测注入时间点验证退避阶梯）。
    /// </summary>
    /// <param name="now">本轮基准时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮统计结果。</returns>
    internal async Task<CompletedResult> DispatchOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        List<WmsStockCommandOutbox> rows = await _db.StockCommandOutbox
            .Where(r =>
                (r.State == WmsStockCommandState.Pending
                    && (r.NextRetryAt == null || r.NextRetryAt <= now))
                || (r.State == WmsStockCommandState.Failed
                    && r.NextRetryAt != null && r.NextRetryAt <= now))
            .OrderBy(static r => r.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return BuildResult(stopwatch, 0, 0, 0, 0);
        }

        var dispatched = 0;
        var failed = 0;
        var terminal = 0;

        foreach (WmsStockCommandOutbox row in rows)
        {
            PostStockTxnRequest? request = JsonSerializer.Deserialize<PostStockTxnRequest>(
                row.PayloadJson, QlxJsonDefaults.PreserveNull);
            if (request is null)
            {
                row.RegisterFailure(now, ErrorPayloadInvalid);
                failed++;
                terminal += row.IsTerminalFailure ? 1 : 0;
                LogRowFailed(row.Id, row.IdempotencyKey, ErrorPayloadInvalid);
                continue;
            }

            try
            {
                StockPostResult result = await _stockTxnCommandService
                    .PostAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                // Success 含 STK_DUPLICATE 幂等命中（Stk 侧按成功返回），均视为已分发
                if (result.Success)
                {
                    row.MarkDispatched(now);
                    dispatched++;
                    continue;
                }

                string errorCode = result.ErrorCode ?? ErrorDispatchException;
                row.RegisterFailure(now, errorCode);
                failed++;
                terminal += row.IsTerminalFailure ? 1 : 0;
                LogRowFailed(row.Id, row.IdempotencyKey, errorCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单行异常不阻断整批：登记错误码并按退避阶梯重试（记录明细便于人工排查）
                row.RegisterFailure(now, ErrorDispatchException);
                failed++;
                terminal += row.IsTerminalFailure ? 1 : 0;
                LogRowThrew(row.Id, row.IdempotencyKey, ex);
            }
        }

        _ = await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogBatchCompleted(rows.Count, dispatched, failed, terminal);
        return BuildResult(stopwatch, rows.Count, dispatched, failed, terminal);
    }

    /// <summary>组装任务完成结果。</summary>
    private static CompletedResult BuildResult(
        Stopwatch stopwatch, int scanned, int dispatched, int failed, int terminal)
        => new(
            new { Scanned = scanned, Dispatched = dispatched, Failed = failed, Terminal = terminal },
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);

    [LoggerMessage(Level = LogLevel.Warning, Message = "库存命令分发失败：outboxId={OutboxId}，key={IdempotencyKey}，error={ErrorCode}")]
    private partial void LogRowFailed(long outboxId, string idempotencyKey, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "库存命令分发异常：outboxId={OutboxId}，key={IdempotencyKey}")]
    private partial void LogRowThrew(long outboxId, string idempotencyKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "库存命令分发完成：扫描 {Scanned}，成功 {Dispatched}，失败 {Failed}（终态 {Terminal}）")]
    private partial void LogBatchCompleted(int scanned, int dispatched, int failed, int terminal);
}
