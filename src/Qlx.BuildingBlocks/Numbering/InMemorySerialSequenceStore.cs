/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：内存单据序号存储实现，ConcurrentDictionary 原子递增，仅用于开发与单实例部署
 */

using System.Collections.Concurrent;

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 内存序号存储。以 <see cref="ConcurrentDictionary{TKey, TValue}"/> 的
/// <c>AddOrUpdate</c> 原子递增实现，进程重启后序号丢失，
/// 生产环境必须替换为数据库序列或 Redis INCR 持久化实现。
/// </summary>
public sealed class InMemorySerialSequenceStore : ISerialSequenceStore
{
    private readonly ConcurrentDictionary<string, long> _sequences = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<long> GetNextAsync(string sequenceKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceKey);

        var next = _sequences.AddOrUpdate(sequenceKey, 1L, static (_, current) => current + 1L);
        return Task.FromResult(next);
    }
}
