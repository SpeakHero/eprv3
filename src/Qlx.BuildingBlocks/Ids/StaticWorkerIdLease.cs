/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：静态配置 workerId 租约实现，供开发环境与单实例部署使用
 */

using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 静态 workerId 租约：直接从配置读取 workerId，不做互斥。
/// 仅限开发环境或单实例部署；生产多实例必须使用 Redis 租约。
/// </summary>
public sealed class StaticWorkerIdLease(IOptions<SnowflakeOptions> options) : IWorkerIdLease
{
    private readonly long _workerId = options.Value.StaticWorkerId;

    /// <inheritdoc />
    public long? WorkerId { get; private set; }

    /// <inheritdoc />
    public Task<long> AcquireAsync(CancellationToken cancellationToken)
    {
        WorkerId = _workerId;
        return Task.FromResult(_workerId);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        WorkerId = null;
        return ValueTask.CompletedTask;
    }
}
