/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.1 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-23 11:40:00 +08:00 中国标准时间 星期五
 * 描述：workerId 租约启动服务，宿主启动时异步获取租约并填充持有器（对预初始化场景幂等）
 */

using Microsoft.Extensions.Hosting;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// workerId 租约启动服务。在宿主启动阶段异步获取租约（Redis 场景含网络 IO），
/// 避免在 DI 工厂中同步阻塞异步调用；停止时释放租约。
/// 若持有器已初始化（如 Program.Main 在启动期迁移前预初始化），本服务幂等跳过，不重复获取租约。
/// </summary>
public sealed class WorkerIdLeaseHostedService(IWorkerIdLease lease, IWorkerIdLeaseHolder holder) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 幂等短路：启动期迁移链路（早于 HostedService）可能已预初始化租约，
        // 重复获取会在 Redis 场景浪费 workerId 名额
        if (holder.WorkerId is not null)
        {
            return;
        }

        holder.WorkerId = await lease.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lease.DisposeAsync().ConfigureAwait(false);
        holder.WorkerId = null;
    }
}
