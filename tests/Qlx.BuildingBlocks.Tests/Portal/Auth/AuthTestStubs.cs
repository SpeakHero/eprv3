/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 描述：登录认证体系白盒测试共享桩件——缓存/系统参数/登录审计/租户上下文/服务提供者/OpenIddict 应用管理器
 */

using System.Collections.Concurrent;
using System.Reflection;
using OpenIddict.Abstractions;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

/// <summary>内存字典缓存桩（忽略 TTL 过期；测试进程内状态可断言，键可枚举）。</summary>
internal sealed class StubCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, object?> _store = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> Snapshot => _store;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        => Task.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken, CacheEntryOptions? options = null)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
        => Task.FromResult(_store.TryRemove(key, out _));

    public Task<long> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var removed = 0L;
        foreach (var key in _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (_store.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task<T> GetOrCreateAsync<T>(
        string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken,
        CacheEntryOptions? options = null)
        => factory(cancellationToken);
}

/// <summary>系统参数桩：按 key 覆盖整型参数，未覆盖回落 fallback（模拟参数表未初始化）。</summary>
internal sealed class StubSystemParameterProvider : ISystemParameterProvider
{
    public Dictionary<string, int> Ints { get; } = new(StringComparer.Ordinal);

    public Task<string> GetStringAsync(string paramKey, string fallback, CancellationToken cancellationToken)
        => Task.FromResult(fallback);

    public Task<int> GetIntAsync(string paramKey, int fallback, CancellationToken cancellationToken)
        => Task.FromResult(Ints.TryGetValue(paramKey, out var value) ? value : fallback);
}

/// <summary>登录审计桩：记录成功/失败调用入参供断言。</summary>
internal sealed class StubPortalLoginAuditLogger : IPortalLoginAuditLogger
{
    public List<(string Account, string TenantName, long TenantId, string? Ip)> Successes { get; } = [];

    public List<(string Account, string? Ip)> Failures { get; } = [];

    public Task RecordSuccessAsync(
        string loginAccount, string tenantName, long tenantId, string? ipAddress, CancellationToken cancellationToken)
    {
        Successes.Add((loginAccount, tenantName, tenantId, ipAddress));
        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(string loginAccount, string? ipAddress, CancellationToken cancellationToken)
    {
        Failures.Add((loginAccount, ipAddress));
        return Task.CompletedTask;
    }
}

/// <summary>待激活激活器桩（激活器本体由 TenantMemberAdminTests 专项覆盖，此处隔离认证主流程）。</summary>
internal sealed class StubPendingInviteActivator : IPendingInviteActivator
{
    public int CallCount { get; private set; }

    public Task<int> ActivateAsync(PortalUser user, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(0);
    }
}

/// <summary>租户上下文桩：记录 SetTenantId 写入序列，供断言进入/恢复语义。</summary>
internal sealed class StubTenantContext : ITenantContext
{
    public List<long?> Writes { get; } = [];

    public string? TenantCode { get; private set; }

    public long? TenantId { get; private set; }

    public bool IsResolved => TenantId is > 0;

    public void SetTenantId(long tenantId)
    {
        TenantId = tenantId;
        Writes.Add(tenantId);
    }

    public void SetTenantCode(string? tenantCode) => TenantCode = tenantCode;
}

/// <summary>服务提供者桩：按类型注册可选查询契约（未注册即返回 null，模拟模块未接入）。</summary>
internal sealed class StubServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public StubServiceProvider Register<T>(T instance)
        where T : notnull
    {
        // 以声明类型与全部已实现的接口为键：服务方按接口解析（GetService<TInterface>()）必须命中
        _services[typeof(T)] = instance;
        foreach (var iface in typeof(T).GetInterfaces())
        {
            _services.TryAdd(iface, instance);
        }

        return this;
    }

    public object? GetService(Type serviceType)
        => _services.TryGetValue(serviceType, out var service) ? service : null;
}

/// <summary>租户成员角色查询桩。</summary>
internal sealed class StubTenantMemberRoleQuery : ITenantMemberRoleQuery
{
    public List<TenantMemberRoleItem> Roles { get; } = [];

    public Task<IReadOnlyList<TenantMemberRoleItem>> ListMemberRolesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TenantMemberRoleItem>>(Roles);
}

/// <summary>租户角色权限查询桩。</summary>
internal sealed class StubTenantRolePermissionQuery : ITenantRolePermissionQuery
{
    public List<string> MemberPermissions { get; } = [];

    public List<string> RolePermissions { get; } = [];

    public Task<IReadOnlyList<string>> ListMemberPermissionCodesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>(MemberPermissions);

    public Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(
        long roleId, long tenantId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>(RolePermissions);
}

/// <summary>门户角色权限查询桩。</summary>
internal sealed class StubPortalRolePermissionQuery : IPortalRolePermissionQuery
{
    public List<string> Permissions { get; } = [];

    public Task<IReadOnlyList<string>> ListUserPermissionCodesAsync(
        long portalUserId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>(Permissions);
}

/// <summary>
/// OpenIddict 应用管理器桩（DispatchProxy 免全量接口实现；未覆盖成员调用即抛）。
/// 认证服务仅消费 FindByClientIdAsync / HasPermissionAsync 两个成员。
/// </summary>
public class StubOpenIddictApplicationManagerProxy : DispatchProxy
{
    /// <summary>按 client_id 查到的应用对象；null 表示客户端不存在。</summary>
    public object? Application { get; set; }

    /// <summary>应用持有的权限集合（endpoint/token、grant types 等）。</summary>
    public HashSet<string> GrantedPermissions { get; } = new(StringComparer.Ordinal);

    /// <summary>创建实现 <see cref="IOpenIddictApplicationManager"/> 的代理实例。</summary>
    public static IOpenIddictApplicationManager Create(object? application, params string[] grantedPermissions)
    {
        var proxy = DispatchProxy.Create<IOpenIddictApplicationManager, StubOpenIddictApplicationManagerProxy>();
        var stub = (StubOpenIddictApplicationManagerProxy)(object)proxy;
        stub.Application = application;
        stub.GrantedPermissions.UnionWith(grantedPermissions);
        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => targetMethod?.Name switch
        {
            nameof(IOpenIddictApplicationManager.FindByClientIdAsync)
                => new ValueTask<object?>(Application),
            nameof(IOpenIddictApplicationManager.HasPermissionAsync)
                => new ValueTask<bool>(GrantedPermissions.Contains((string)args![1]!)),
            _ => throw new NotImplementedException(targetMethod?.Name),
        };
}

/// <summary>OpenIddict 客户端权限辅助：构造「全许可」应用管理器桩。</summary>
internal static class OpenIddictClientPermissions
{
    /// <summary>认证服务要求的五项许可（Token 端点 + refresh_token + 三个自定义 grant）。</summary>
    public static string[] All =>
    [
        OpenIddictConstants.Permissions.Endpoints.Token,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
        OpenIddictConstants.Permissions.Prefixes.GrantType + Qlx.Modules.Portal.Auth.QlxAuthConstants.GrantTypes.PortalIdentity,
        OpenIddictConstants.Permissions.Prefixes.GrantType + Qlx.Modules.Portal.Auth.QlxAuthConstants.GrantTypes.TenantContext,
        OpenIddictConstants.Permissions.Prefixes.GrantType + Qlx.Modules.Portal.Auth.QlxAuthConstants.GrantTypes.TenantSilent,
    ];
}
