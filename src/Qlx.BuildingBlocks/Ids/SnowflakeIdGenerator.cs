/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：自研雪花 ID 生成器，41 位毫秒时间 + 10 位 workerId + 12 位序列，线程安全
 */

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 自研雪花 ID 生成器。位分配：1 位符号（恒 0）+ 41 位毫秒时间戳（相对自定义纪元）
/// + 10 位工作节点（0-1023）+ 12 位毫秒内序列（0-4095）。
/// 线程安全；同一毫秒内序列耗尽时自旋等待下一毫秒。
/// </summary>
public sealed class SnowflakeIdGenerator : ISnowflakeIdGenerator
{
    /// <summary>
    /// 工作节点位数。
    /// </summary>
    public const int WorkerIdBits = 10;

    /// <summary>
    /// 毫秒内序列位数。
    /// </summary>
    public const int SequenceBits = 12;

    /// <summary>
    /// 允许的最大工作节点编号。
    /// </summary>
    public const long MaxWorkerId = (1L << WorkerIdBits) - 1;

    private const long MaxSequence = (1L << SequenceBits) - 1;
    private const int MaxClockBackwardsMs = 5;

    private readonly long _epochMs;
    private readonly long _workerId;
    private readonly Lock _sync = new();

    private long _lastTimestampMs = -1L;
    private long _sequence;

    /// <summary>
    /// 使用指定工作节点与纪元创建生成器。
    /// </summary>
    /// <param name="workerId">工作节点编号（0-1023），多实例部署必须经租约分配避免重复。</param>
    /// <param name="epoch">自定义纪元；为 <see langword="null"/> 时使用 2026-01-01 UTC。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="workerId"/> 越界时抛出。</exception>
    public SnowflakeIdGenerator(long workerId, DateTimeOffset? epoch = null)
    {
        if (workerId < 0 || workerId > MaxWorkerId)
        {
            throw new ArgumentOutOfRangeException(nameof(workerId), workerId, $"workerId 必须在 0 到 {MaxWorkerId} 之间");
        }

        _workerId = workerId;
        _epochMs = (epoch ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 当前工作节点编号。
    /// </summary>
    public long WorkerId => _workerId;

    /// <inheritdoc />
    public long NextId()
    {
        lock (_sync)
        {
            var timestamp = CurrentTimestampMs();

            if (timestamp < _lastTimestampMs)
            {
                var backwardsMs = _lastTimestampMs - timestamp;
                if (backwardsMs > MaxClockBackwardsMs)
                {
                    throw new InvalidOperationException($"检测到时钟回拨 {backwardsMs}ms，超过容忍上限 {MaxClockBackwardsMs}ms，拒绝生成 ID");
                }

                // 容忍范围内的小幅回拨：等待时钟追平后继续，避免重复 ID
                timestamp = WaitNextMillis(_lastTimestampMs);
            }

            if (timestamp == _lastTimestampMs)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    timestamp = WaitNextMillis(_lastTimestampMs);
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestampMs = timestamp;
            return ((timestamp - _epochMs) << (WorkerIdBits + SequenceBits)) | (_workerId << SequenceBits) | _sequence;
        }
    }

    private static long CurrentTimestampMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long WaitNextMillis(long lastTimestampMs)
    {
        var timestamp = CurrentTimestampMs();
        while (timestamp <= lastTimestampMs)
        {
            Thread.Yield();
            timestamp = CurrentTimestampMs();
        }

        return timestamp;
    }
}
