/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 04:40:00 +08:00 中国标准时间 星期五
 * 描述：SupportJobExecutionLogQuery 测试——InMemory 提供程序 + 真实 SupportTenantDbContext，
 *       验证投影 / 过滤 / 排序语义（SQLite 提供程序不支持 DateTimeOffset 比较翻译，生产库为 SQL Server 不受影响）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Jobs;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Services.Jobs;
using Qlx.BuildingBlocks.Tests.Jobs;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// <see cref="SupportJobExecutionLogQuery"/> 单元测试（job_logs 只增不改，租户上下文固定）。
/// </summary>
public sealed class SupportJobExecutionLogQueryTests
{
    private const long TenantId = 42L;

    /// <summary>构建 InMemory 上下文（独立库名避免用例间串数据；全局租户过滤保持生效）。</summary>
    private static SupportTenantDbContext CreateDb()
        => new(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"job-logs-{Guid.NewGuid():N}")
                .Options);

    /// <summary>写入一条 job_logs（参数与 SupportJobExecutionStore.AppendLogsAsync 落库形态一致）。</summary>
    private static JobLogEntity BuildLog(
        Guid executionId, JobLogLevel level, string message, DateTimeOffset logAt, string taskCode = "test.log.job")
        => new(TenantId, executionId, taskCode, level, message, detail: null, stackTrace: null, logAt);

    [Fact]
    public async Task GetLogsByExecutionIdAsyncReturnsOrderedEntriesForTargetExecution()
    {
        await using var db = CreateDb();
        var executionId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var baseTime = new DateTimeOffset(2026, 10, 23, 4, 0, 0, TimeSpan.Zero);
        db.JobLogs.AddRange(
            BuildLog(executionId, JobLogLevel.Info, "任务开始执行", baseTime.AddSeconds(1)),
            BuildLog(executionId, JobLogLevel.Error, "任务执行失败", baseTime.AddSeconds(3)),
            BuildLog(executionId, JobLogLevel.Warn, "重试排程", baseTime.AddSeconds(2)),
            BuildLog(otherId, JobLogLevel.Info, "其他执行", baseTime.AddSeconds(4)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new SupportJobExecutionLogQuery(db);
        var logs = await query.GetLogsByExecutionIdAsync(executionId, TestContext.Current.CancellationToken);

        Assert.Equal(3, logs.Count);
        Assert.All(logs, l => Assert.Equal(executionId, l.ExecutionId));
        var expectedTimes = new[]
        {
            baseTime.AddSeconds(1), baseTime.AddSeconds(2), baseTime.AddSeconds(3),
        };
        Assert.Equal(expectedTimes, logs.Select(static l => l.LogAt).ToArray());
        Assert.Equal(JobLogLevel.Error, logs[2].Level);
    }

    [Fact]
    public async Task QueryAsyncFiltersByTimeRangeAndTaskCode()
    {
        await using var db = CreateDb();
        var baseTime = new DateTimeOffset(2026, 10, 23, 4, 0, 0, TimeSpan.Zero);
        db.JobLogs.AddRange(
            BuildLog(Guid.NewGuid(), JobLogLevel.Info, "窗口内", baseTime.AddMinutes(5)),
            BuildLog(Guid.NewGuid(), JobLogLevel.Info, "窗口外（早于起点）", baseTime.AddMinutes(-5)),
            BuildLog(Guid.NewGuid(), JobLogLevel.Info, "其他任务", baseTime.AddMinutes(5), taskCode: "other.job"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new SupportJobExecutionLogQuery(db);
        var logs = await query.QueryAsync(
            new JobLogQuery(baseTime, baseTime.AddMinutes(10), TaskCode: "test.log.job"),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs);
        Assert.Equal("窗口内", entry.Message);
    }

    [Fact]
    public async Task QueryAsyncMinLevelExcludesLowerLevels()
    {
        await using var db = CreateDb();
        var baseTime = new DateTimeOffset(2026, 10, 23, 4, 0, 0, TimeSpan.Zero);
        var executionId = Guid.NewGuid();
        db.JobLogs.AddRange(
            BuildLog(executionId, JobLogLevel.Debug, "调试信息", baseTime),
            BuildLog(executionId, JobLogLevel.Info, "常规信息", baseTime.AddSeconds(1)),
            BuildLog(executionId, JobLogLevel.Error, "错误信息", baseTime.AddSeconds(2)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new SupportJobExecutionLogQuery(db);
        var logs = await query.QueryAsync(
            new JobLogQuery(baseTime.AddMinutes(-1), baseTime.AddMinutes(1), MinLevel: JobLogLevel.Error),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs);
        Assert.Equal(JobLogLevel.Error, entry.Level);
    }

    [Fact]
    public async Task QueryAsyncFromGreaterThanToThrows()
    {
        await using var db = CreateDb();
        var query = new SupportJobExecutionLogQuery(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.QueryAsync(
            new JobLogQuery(
                new DateTimeOffset(2026, 10, 23, 5, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 23, 4, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken));
    }
}
