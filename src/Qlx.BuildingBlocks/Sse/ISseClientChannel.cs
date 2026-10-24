/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 05:35:00 +08:00 中国标准时间 星期五
 * 描述：SSE 客户端流契约——端点经本接口建立连接租约（通道读取器）与断线续传范围判定，
 *       与 2A 冻结契约 <see cref="Qlx.Abstractions.Sse.ISseConnectionManager"/> 由同一单例同时实现
 */

using System.Threading.Channels;

namespace Qlx.BuildingBlocks.Sse;

/// <summary>
/// SSE 客户端流接口。SSE 端点（租户侧控制器）使用本接口建立连接并消费事件，
/// 路由/缓冲/推送仍经 <see cref="Qlx.Abstractions.Sse.ISseConnectionManager"/>，实现为同一单例。
/// </summary>
public interface ISseClientChannel
{
    /// <summary>
    /// 建立连接租约：注册连接并返回事件通道读取器（端点 await foreach 消费；断开时释放租约）。
    /// </summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="connectionId">连接 Id（前端生成）。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接租约（IAsyncDisposable，释放即注销连接）。</returns>
    Task<SseClientLease> OpenStreamAsync(
        long userId, string connectionId, long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 断线续传范围判定与事件拉取：校验客户端 <c>Last-Event-ID</c> 是否仍落在租户环形缓冲窗口内，
    /// 防伪造（超出窗口 / 未来 Id 返回 IsInRange=false，端点回 400 或 event:error）。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="lastEventId">客户端最后收到的事件 Id（null=全新连接，无需续传）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>续传结果（IsInRange=false 时 Events 为空）。</returns>
    Task<SseReplayResult> TryReplayAsync(long tenantId, long? lastEventId, CancellationToken cancellationToken);
}

/// <summary>断线续传结果。</summary>
/// <param name="IsInRange">Last-Event-ID 是否仍在缓冲窗口内（true=可续传；false=伪造或已淘汰）。</param>
/// <param name="Events">需补发的事件列表（按 EventId 升序）。</param>
public sealed record SseReplayResult(bool IsInRange, IReadOnlyList<Qlx.Abstractions.Sse.SseEvent> Events);

/// <summary>
/// SSE 连接租约。端点持有期间从 <see cref="Reader"/> 消费事件；断开时释放，
/// 内部完成连接注销与通道关闭。
/// </summary>
public sealed class SseClientLease : IAsyncDisposable
{
    private readonly Func<Task> _dispose;

    /// <summary>初始化租约。</summary>
    /// <param name="reader">事件通道读取器。</param>
    /// <param name="dispose">释放委托（注销连接）。</param>
    internal SseClientLease(ChannelReader<Qlx.Abstractions.Sse.SseEvent> reader, Func<Task> dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    /// <summary>事件通道读取器（<c>await foreach (var evt in lease.Reader.ReadAllAsync(ct))</c>）。</summary>
    public ChannelReader<Qlx.Abstractions.Sse.SseEvent> Reader { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _dispose().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
