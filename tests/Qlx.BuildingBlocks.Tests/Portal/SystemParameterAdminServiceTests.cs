/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 描述：系统参数管理服务测试：Upsert/查询/删除编排 + CancellationToken 全链路透传断言
 */

using Microsoft.Extensions.Options;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal.Domain.Configuration;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>内存桩仓储：记录每次调用收到的取消令牌，供透传断言。</summary>
public sealed class StubSystemParameterRepository : ISystemParameterRepository
{
    public sealed record CallRecord(string Method, CancellationToken Token);

    public List<CallRecord> Calls { get; } = [];

    public Dictionary<string, SystemParameter> Store { get; } = new(StringComparer.Ordinal);

    public Task<SystemParameter?> FindByKeyAsync(string paramKey, CancellationToken cancellationToken)
    {
        // 模拟 EF Core 行为：数据访问入口对取消令牌即时响应
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(new CallRecord(nameof(FindByKeyAsync), cancellationToken));
        return Task.FromResult(Store.TryGetValue(paramKey, out var p) ? p : null);
    }

    public Task<SystemParameter?> FindTrackedByKeyAsync(string paramKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(new CallRecord(nameof(FindTrackedByKeyAsync), cancellationToken));
        return Task.FromResult(Store.TryGetValue(paramKey, out var p) ? p : null);
    }

    public Task<PagedResult<SystemParameter>> QueryAsync(string? keyword, PagedRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(new CallRecord(nameof(QueryAsync), cancellationToken));
        var items = Store.Values
            .Where(p => keyword == null
                || p.ParamKey.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (p.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(p => p.ParamKey)
            .ToList();
        return Task.FromResult(new PagedResult<SystemParameter>(items, items.Count, request.Page, request.SafePageSize));
    }

    public Task AddAsync(SystemParameter parameter, CancellationToken cancellationToken)
    {
        Calls.Add(new CallRecord(nameof(AddAsync), cancellationToken));
        Store.Add(parameter.ParamKey, parameter);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SystemParameter parameter, CancellationToken cancellationToken)
    {
        Calls.Add(new CallRecord(nameof(UpdateAsync), cancellationToken));
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string paramKey, CancellationToken cancellationToken)
    {
        Calls.Add(new CallRecord(nameof(DeleteAsync), cancellationToken));
        return Task.FromResult(Store.Remove(paramKey));
    }
}

/// <summary>内存桩当前用户上下文。</summary>
public sealed class StubCurrentUserContext(string? userName) : ICurrentUserContext
{
    public string? UserId { get; } = userName;
    public string? UserName { get; } = userName;
    public string? UserDisplayName { get; } = userName;
    public string? CurrentUserIp => null;
    public bool IsAuthenticated { get; } = userName is not null;
}

/// <summary>
/// 系统参数管理服务测试。
/// 令牌透传断言方式：桩仓储记录每个方法收到的 <see cref="CancellationToken"/>，
/// 断言与测试方法创建的源令牌（cts.Token）为同一实例——证明 Controller→Service→Repository
/// 全链路原样透传、无任何一层吞掉或替换令牌。
/// </summary>
public sealed class SystemParameterAdminServiceTests
{
    private static (SystemParameterAdminService Service, StubSystemParameterRepository Repo) CreateService()
    {
        var repo = new StubSystemParameterRepository();
        var service = new SystemParameterAdminService(
            repo,
            new MemoryCacheService(Options.Create(new CacheOptions())),
            new StubCurrentUserContext("qlxadmin"));
        return (service, repo);
    }

    [Fact]
    public async Task UpsertNewCreatesParameterWithAuditFields()
    {
        var (service, repo) = CreateService();
        using var cts = new CancellationTokenSource();

        var item = await service.UpsertAsync(
            "auth.max_password_fail_attempts",
            new SaveSystemParameterRequest { ParamValue = "5", Description = "锁定阈值" },
            cts.Token);

        Assert.Equal("5", item.ParamValue);
        Assert.Equal("qlxadmin", item.UpdatedBy);
        Assert.True(repo.Store.ContainsKey("auth.max_password_fail_attempts"));
    }

    [Fact]
    public async Task UpsertExistingUpdatesInPlace()
    {
        var (service, repo) = CreateService();
        using var cts = new CancellationTokenSource();
        repo.Store["auth.password_fail_window_minutes"] = new SystemParameter(
            "auth.password_fail_window_minutes", "10");

        await service.UpsertAsync(
            "auth.password_fail_window_minutes",
            new SaveSystemParameterRequest { ParamValue = "15" },
            cts.Token);

        Assert.Equal("15", repo.Store["auth.password_fail_window_minutes"].ParamValue);
    }

    [Fact]
    public async Task UpsertRejectsInvalidParamKeyFormat()
    {
        var (service, _) = CreateService();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<ValidationException>(() => service.UpsertAsync(
            "Invalid-Key", new SaveSystemParameterRequest { ParamValue = "1" }, cts.Token));
    }

    [Fact]
    public async Task GetThrowsNotFoundWhenMissing()
    {
        var (service, _) = CreateService();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetAsync("no.such_key", cts.Token));
    }

    [Fact]
    public async Task DeleteRemovesExistingAndThrowsForMissing()
    {
        var (service, repo) = CreateService();
        using var cts = new CancellationTokenSource();
        repo.Store["auth.lockout_key"] = new SystemParameter("auth.lockout_key", "1");

        await service.DeleteAsync("auth.lockout_key", cts.Token);
        Assert.False(repo.Store.ContainsKey("auth.lockout_key"));

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.DeleteAsync("auth.lockout_key", cts.Token));
    }

    [Fact]
    public async Task QueryAppliesKeywordFilter()
    {
        var (service, repo) = CreateService();
        using var cts = new CancellationTokenSource();
        repo.Store["auth.max_password_fail_attempts"] = new SystemParameter("auth.max_password_fail_attempts", "5");
        repo.Store["mail.smtp_host"] = new SystemParameter("mail.smtp_host", "smtp.example.com", "邮件服务器");

        var page = await service.QueryAsync(
            new SystemParameterQueryRequest { Keyword = "auth", Page = 1, PageSize = 10 }, cts.Token);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("auth.max_password_fail_attempts", page.Items[0].ParamKey);
    }

    [Fact]
    public async Task CancellationTokenPropagatesToRepositoryUnchanged()
    {
        // 核心断言：Service 层必须把收到的令牌原样传给仓储的每个方法（同一实例）
        var (service, repo) = CreateService();
        using var cts = new CancellationTokenSource();
        repo.Store["auth.lockout_key"] = new SystemParameter("auth.lockout_key", "1");

        await service.GetAsync("auth.lockout_key", cts.Token);
        await service.UpsertAsync("auth.lockout_key", new SaveSystemParameterRequest { ParamValue = "2" }, cts.Token);
        await service.DeleteAsync("auth.lockout_key", cts.Token);
        await service.QueryAsync(new SystemParameterQueryRequest(), cts.Token);

        Assert.NotEmpty(repo.Calls);
        Assert.All(repo.Calls, call => Assert.Equal(cts.Token, call.Token));
        Assert.All(repo.Calls, call => Assert.False(call.Token.IsCancellationRequested));
    }

    [Fact]
    public async Task RepositoryOperationThrowsOperationCanceledWhenTokenCancelled()
    {
        // 已取消令牌 → 仓储侧（真 EF 场景为数据库 IO）立即取消，链路不得吞掉异常
        var (service, repo) = CreateService();
        using var cts = new CancellationTokenSource();
        repo.Store["auth.lockout_key"] = new SystemParameter("auth.lockout_key", "1");
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAsync("auth.lockout_key", cts.Token));
    }
}
