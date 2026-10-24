/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 12:45:00 +08:00 中国标准时间 星期四
 * 描述：B4-02 审计路由分流测试——门户登录→plog_operation，租户登录→log_login
 */

using Microsoft.Extensions.Logging.Abstractions;
using Qlx.Abstractions.Audit;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Audit;

/// <summary>B4-02 审计路由分流验证：两条路径不串写。</summary>
public sealed class PortalLoginAuditLoggerRoutingTests
{
    [Fact]
    public async Task PortalLoginSuccessWritesToPlatformLog()
    {
        var audit = new StubAuditService();
        var tenantLog = new StubTenantLogWriter();
        var logger = CreateLogger(audit, tenantLog);

        await logger.RecordSuccessAsync("admin", "门户管理", 0, "127.0.0.1", TestContext.Current.CancellationToken);

        Assert.Single(audit.Operations);
        Assert.Equal("LOGIN_SUCCESS", audit.Operations[0].Operation);
        Assert.Equal(0L, audit.Operations[0].TenantId);
        Assert.Empty(tenantLog.Logins);
    }

    [Fact]
    public async Task TenantLoginSuccessWritesToTenantLog()
    {
        var audit = new StubAuditService();
        var tenantLog = new StubTenantLogWriter();
        var logger = CreateLogger(audit, tenantLog);

        await logger.RecordSuccessAsync("zhangsan", "测试租户", 1001, "192.168.1.1", TestContext.Current.CancellationToken);

        Assert.Empty(audit.Operations);
        var login = Assert.Single(tenantLog.Logins);
        Assert.Equal(1001L, login.TenantId);
        Assert.Equal("zhangsan", login.LoginAccount);
        Assert.True(login.IsSuccess);
    }

    [Fact]
    public async Task LoginFailureAlwaysWritesToPlatformLog()
    {
        var audit = new StubAuditService();
        var tenantLog = new StubTenantLogWriter();
        var logger = CreateLogger(audit, tenantLog);

        await logger.RecordFailureAsync("unknown", "10.0.0.1", TestContext.Current.CancellationToken);

        Assert.Single(audit.Operations);
        Assert.Equal("LOGIN_FAILED", audit.Operations[0].Operation);
        Assert.Empty(tenantLog.Logins);
    }

    [Fact]
    public async Task PlatformLogWriteFailureDoesNotThrow()
    {
        var audit = new StubAuditService { ThrowOnWrite = true };
        var tenantLog = new StubTenantLogWriter();
        var logger = CreateLogger(audit, tenantLog);

        // 不应抛异常（审计降级仅告警）
        await logger.RecordSuccessAsync("admin", "门户管理", 0, null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TenantLogWriteFailureDoesNotThrow()
    {
        var audit = new StubAuditService();
        var tenantLog = new StubTenantLogWriter { ThrowOnWrite = true };
        var logger = CreateLogger(audit, tenantLog);

        // 不应抛异常（审计降级仅告警）
        await logger.RecordSuccessAsync("zhangsan", "测试租户", 1001, null, TestContext.Current.CancellationToken);
    }

    private static PortalLoginAuditLogger CreateLogger(StubAuditService audit, StubTenantLogWriter tenantLog)
        => new(
            audit,
            tenantLog,
            new SnowflakeIdGenerator(workerId: 22),
            NullLogger<PortalLoginAuditLogger>.Instance);

    /// <summary>审计服务桩。</summary>
    private sealed class StubAuditService : IAuditService
    {
        public List<OperationLogEntry> Operations { get; } = [];
        public bool ThrowOnWrite { get; init; }

        public Task CaptureChangeAsync(AuditLogEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CaptureOperationAsync(OperationLogEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("模拟写入失败");
            Operations.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLogEntry>> QueryChangesAsync(
            long tenantId, CancellationToken cancellationToken, string? entityType = null,
            long? entityId = null, string? changedBy = null, DateTimeOffset? from = null, DateTimeOffset? toTime = null)
            => Task.FromResult<IReadOnlyList<AuditLogEntry>>([]);

        public Task<IReadOnlyList<OperationLogEntry>> QueryOperationsAsync(
            long tenantId, CancellationToken cancellationToken, string? moduleCode = null,
            string? operation = null, string? operatedBy = null, DateTimeOffset? from = null, DateTimeOffset? toTime = null)
            => Task.FromResult<IReadOnlyList<OperationLogEntry>>([]);

        public Task<IReadOnlyList<OperationLogEntry>> QueryOperationsByOperatorAsync(
            string operatedBy, CancellationToken cancellationToken, string? moduleCode = null, int take = 100)
            => Task.FromResult<IReadOnlyList<OperationLogEntry>>([]);
    }

    /// <summary>租户日志写入器桩。</summary>
    private sealed class StubTenantLogWriter : ITenantLogWriter
    {
        public List<TenantLogLoginEntry> Logins { get; } = [];
        public List<TenantLogOperationEntry> Operations { get; } = [];
        public bool ThrowOnWrite { get; init; }

        public Task WriteOperationAsync(TenantLogOperationEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("模拟写入失败");
            Operations.Add(entry);
            return Task.CompletedTask;
        }

        public Task WriteLoginAsync(TenantLogLoginEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("模拟写入失败");
            Logins.Add(entry);
            return Task.CompletedTask;
        }
    }
}
