/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 05:40:00 +08:00 中国标准时间 星期五
 * 描述：SSE 连接管理器（进程内单实现）：租户环形缓冲 + 连接注册表 + 信封路由 + 断线续传。
 *       2A 冻结契约 <see cref="ISseConnectionManager"/> 与端点通道 <see cref="ISseClientChannel"/> 同一单例承载；
 *       推送目标（租户/用户）封装在 DataJson 信封内，信封缺失即拒发（防跨租户串流）。
 */

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Sse;

namespace Qlx.BuildingBlocks.Sse;

/// <summary>
/// SSE 连接管理器（单例）。事件 Id 按租户单调递增（生产方传 0，由管理器分配）；
/// 每租户维护容量 <see cref="QlxSseOptions.BufferCapacity"/> 的环形缓冲支撑断线续传；
/// 每连接维护容量 <see cref="QlxSseOptions.ChannelCapacity"/> 的有界通道（满载淘汰最旧，靠续传补漏）。
/// </summary>
/// <param name="options">SSE 基座配置。</param>
/// <param name="logger">日志器。</param>
public sealed partial class SseConnectionManager(
    IOptions<QlxSseOptions> options,
    ILogger<SseConnectionManager> logger) : ISseConnectionManager, ISseClientChannel
{
    private readonly QlxSseOptions _options = options.Value;
    private readonly ILogger<SseConnectionManager> _logger = logger;

    /// <summary>连接注册表：键 = (租户 Id, 用户 Id, 连接 Id)。</summary>
    private readonly ConcurrentDictionary<(long TenantId, long UserId, string ConnectionId), SseClientChannel>
        _connections = [];

    /// <summary>租户事件环形缓冲：键 = 租户 Id。</summary>
    private readonly ConcurrentDictionary<long, TenantEventBuffer> _buffers = [];       

    /// <summary>连接总数（诊断用）。</summary>
    public int TotalConnectionCount => _connections.Count;

    // ============ 连接注册 / 注销（2A 契约） ============

    /// <inheritdoc />
    public Task RegisterConnectionAsync(long userId, string connectionId, long tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var key = (tenantId, userId, connectionId);
        var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
        if (_connections.TryRemove(key, out var stale))
        {
            // 同键重连（前端重试）：关闭旧通道避免泄漏
            stale.Channel.Writer.TryComplete();
        }

        _connections[key] = new SseClientChannel(tenantId, userId, channel);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterConnectionAsync(long userId, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        // 键含租户 Id 而注销方（端点断开回调）未携带租户：按用户 + 连接 Id 全表匹配（连接量级有限，可接受）
        foreach (var key in _connections.Keys)
        {
            if (key.UserId == userId && key.ConnectionId == connectionId
                && _connections.TryRemove(key, out var removed))
            {
                removed.Channel.Writer.TryComplete();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> GetConnectionCountAsync(long tenantId, CancellationToken cancellationToken)
        => Task.FromResult(_connections.Count(pair => pair.Key.TenantId == tenantId));

    // ============ 端点通道（ISseClientChannel） ============

    /// <inheritdoc />
    public async Task<SseClientLease> OpenStreamAsync(
        long userId, string connectionId, long tenantId, CancellationToken cancellationToken)
    {
        await RegisterConnectionAsync(userId, connectionId, tenantId, cancellationToken).ConfigureAwait(false);
        var reader = _connections[(tenantId, userId, connectionId)].Channel.Reader;
        return new SseClientLease(
            reader,
            // 【豁免点】注销为端点断开回调的清理动作，与请求令牌生命周期解耦，禁用取消令牌
            () => UnregisterConnectionAsync(userId, connectionId, CancellationToken.None));
    }

    /// <inheritdoc />
    public Task<SseReplayResult> TryReplayAsync(long tenantId, long? lastEventId, CancellationToken cancellationToken)
    {
        if (lastEventId is null or <= 0)
        {
            // 全新连接：无需续传（站内数据走收件箱 API，不依赖 SSE 补发历史）
            return Task.FromResult(new SseReplayResult(IsInRange: true, []));
        }

        if (!_buffers.TryGetValue(tenantId, out var buffer))
        {
            // 本租户从未发布过事件：携带任意 Last-Event-ID 均视为伪造
            return Task.FromResult(new SseReplayResult(IsInRange: false, []));
        }

        return buffer.TryReplayAsync(lastEventId.Value);
    }

    // ============ 推送 / 续传（2A 契约） ============

    /// <inheritdoc />
    public async Task PushAsync(SseEvent sseEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sseEvent);

        // 信封解析：缺失即拒发并告警（防跨租户串流红线）
        var envelope = SseEventEnvelope.TryParse(sseEvent.DataJson);
        if (envelope is null)
        {
            LogEnvelopeRejected(sseEvent.Type);
            return;
        }

        var capacity = _options.BufferCapacity;

        // 分配事件 Id 并写入租户环形缓冲（续传数据源）；租户 Id 与连接注册表一致使用 long
        var buffer = _buffers.GetOrAdd(
            envelope.TenantId,
            tenant => new TenantEventBuffer(capacity));
        var stored = await buffer.AppendAsync(sseEvent, cancellationToken).ConfigureAwait(false);

        // 路由投递：指定用户则单投，null 为租户内广播
        var delivered = 0;
        foreach (var pair in _connections)
        {
            var (key, channel) = (pair.Key, pair.Value);
            if (key.TenantId != envelope.TenantId
                || (envelope.RecipientUserId is not null && channel.UserId != envelope.RecipientUserId))
            {
                continue;
            }

            channel.Channel.Writer.TryWrite(stored);
            delivered++;
        }

        if (delivered == 0)
        {
            LogNoSubscriber(envelope.TenantId, stored.EventId);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SseEvent>> GetEventsSinceAsync(long tenantId, long lastEventId, CancellationToken cancellationToken)
        => _buffers.TryGetValue(tenantId, out var buffer)
            ? buffer.GetSinceAsync(lastEventId)
            : Task.FromResult<IReadOnlyList<SseEvent>>([]);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SSE 事件缺少合法路由信封，已拒发（Type={EventType}）。生产方必须以 SseEventEnvelope.Serialize 构造 DataJson")]
    private partial void LogEnvelopeRejected(SseEventType eventType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SSE 事件 {EventId} 在租户 {TenantId} 无在线订阅者（离线用户走收件箱 API）")]
    private partial void LogNoSubscriber(long tenantId, long eventId);

    /// <summary>单连接通道条目。UserId 与信封 RecipientUserId 同为 long 直接比较。</summary>
    /// <param name="TenantId">租户 Id。</param>
    /// <param name="UserId">用户 Id。</param>
    /// <param name="Channel">有界事件通道。</param>
    private sealed record SseClientChannel(long TenantId, long UserId, Channel<SseEvent> Channel);
}

/// <summary>
/// 租户事件环形缓冲。事件 Id 从 1 起按租户单调递增；容量满后覆盖最旧，
/// 续传窗口为 <c>(已分配最大 Id - 容量, 已分配最大 Id]</c>，窗外 Id 一律判为不在范围（伪造/过期）。
/// </summary>
/// <param name="capacity">环形容量（来自 <see cref="QlxSseOptions.BufferCapacity"/>）。</param>
internal sealed class TenantEventBuffer(int capacity)
{
    private readonly Lock _gate = new();
    private readonly SseEvent[] _slots = new SseEvent[capacity];
    private long _allocated;

    /// <summary>追加事件：分配递增 Id、写入环形槽位（覆盖最旧）。</summary>
    /// <param name="source">生产方事件（EventId 忽略，由本缓冲分配）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>带分配 Id 的事件。</returns>
    public Task<SseEvent> AppendAsync(SseEvent source, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var id = ++_allocated;
            var stored = source with { EventId = id };
            _slots[(int)((id - 1) % _slots.Length)] = stored;
            return Task.FromResult(stored);
        }
    }

    /// <summary>断线续传：范围判定 + 窗口内事件拉取。</summary>
    /// <param name="lastEventId">客户端最后收到的事件 Id。</param>
    /// <returns>续传结果（IsInRange=false 时 Events 为空）。</returns>
    public Task<SseReplayResult> TryReplayAsync(long lastEventId)
    {
        lock (_gate)
        {
            // 未来 Id（超出已分配最大值）= 伪造；窗外旧 Id = 已淘汰（客户端需改走收件箱 API 全量刷新）
            var oldestAvailable = Math.Max(0, _allocated - _slots.Length);
            if (lastEventId > _allocated || lastEventId < oldestAvailable)
            {
                return Task.FromResult(new SseReplayResult(IsInRange: false, []));
            }

            return Task.FromResult(new SseReplayResult(IsInRange: true, SnapshotSince(lastEventId)));
        }
    }

    /// <summary>拉取窗口内 EventId 大于 lastEventId 的事件（升序）。</summary>
    /// <param name="lastEventId">客户端最后收到的事件 Id。</param>
    /// <returns>遗漏事件列表。</returns>
    public Task<IReadOnlyList<SseEvent>> GetSinceAsync(long lastEventId)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SseEvent>>(SnapshotSince(lastEventId));
        }
    }

    /// <summary>快照窗口内事件（调用方持锁）。</summary>
    /// <param name="lastEventId">客户端最后收到的事件 Id。</param>
    /// <returns>升序事件列表。</returns>
    private List<SseEvent> SnapshotSince(long lastEventId)
    {
        List<SseEvent> results = [];
        foreach (var slot in _slots)
        {
            // 环形缓冲未写满时存在 null 槽位（容量预留），必须跳过
            if (slot is not null && slot.EventId > lastEventId)
            {
                results.Add(slot);
            }
        }

        results.Sort(static (a, b) => a.EventId.CompareTo(b.EventId));
        return results;
    }
}
