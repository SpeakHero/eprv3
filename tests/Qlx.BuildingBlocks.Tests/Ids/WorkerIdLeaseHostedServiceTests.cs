/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 11:40:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 11:40:00 +08:00 中国标准时间 星期五
 * 描述：workerId 租约启动服务单元测试（未初始化时正常获取、已初始化时幂等跳过）
 */

using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Tests.Ids;

public sealed class WorkerIdLeaseHostedServiceTests
{
    /// <summary>可计数获取次数的假租约，替代 Redis / 静态实现。</summary>
    private sealed class FakeWorkerIdLease : IWorkerIdLease
    {
        public int AcquireCallCount { get; private set; }

        public long? WorkerId { get; private set; }

        public Task<long> AcquireAsync(CancellationToken cancellationToken)
        {
            AcquireCallCount++;
            WorkerId = 42;
            return Task.FromResult(42L);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task StartAsyncAcquiresLeaseWhenHolderNotInitialized()
    {
        var lease = new FakeWorkerIdLease();
        var holder = new WorkerIdLeaseHolder();
        var service = new WorkerIdLeaseHostedService(lease, holder);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(42, holder.WorkerId);
        Assert.Equal(1, lease.AcquireCallCount);
    }

    [Fact]
    public async Task StartAsyncSkipsAcquireWhenHolderAlreadyInitialized()
    {
        var lease = new FakeWorkerIdLease();
        var holder = new WorkerIdLeaseHolder { WorkerId = 7 };
        var service = new WorkerIdLeaseHostedService(lease, holder);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(7, holder.WorkerId);
        Assert.Equal(0, lease.AcquireCallCount);
    }
}
