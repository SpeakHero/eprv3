/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 23:40:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 23:40:00 +08:00 中国标准时间 星期一
 * 描述：ValidationDispatchProxy 单元测试——[Validate] 标记校验 / IValidatableObject 与 IAsyncValidatableObject 接口自动识别 /
 *       DI 服务透传 / CancellationToken 透传 / 异常原样透传 / 异步包装失败前置拒绝 / AddValidatedScoped DI 集成
 */

using Microsoft.Extensions.DependencyInjection;

using Qlx.BuildingBlocks.Validation;

using System.ComponentModel.DataAnnotations;

// 业务校验异常与 DataAnnotations 的 ValidationException 同名，本文件统一指业务异常
using ValidationException = Qlx.Abstractions.Exceptions.ValidationException;

namespace Qlx.BuildingBlocks.Tests.Validation;

/// <summary>仅含 DataAnnotations 特性的 DTO 桩：Name 必填（需 [Validate] 标记才会被代理校验）。</summary>
public sealed class ProxyAnnotatedRequest
{
    /// <summary>必填名称。</summary>
    [Required(ErrorMessage = "名称必填")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>同步可验证 DTO 桩：ReturnFailure 时返回一条成员名 SyncCode 的失败项；记录解析到的字符串服务。</summary>
public sealed class ProxySyncValidatable : IValidatableObject
{
    /// <summary>true 时返回失败项。</summary>
    public bool ReturnFailure { get; init; }

    /// <summary>Validate 内经 context.GetService(typeof(string)) 解析到的服务实例。</summary>
    public string? ResolvedService { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ResolvedService = validationContext.GetService(typeof(string)) as string;
        return ReturnFailure ? [new ValidationResult("同步规则失败", ["SyncCode"])] : [];
    }
}

/// <summary>异步可验证 DTO 桩：ReturnFailure 时返回一条成员名 AsyncCode 的失败项；记录收到的取消令牌。</summary>
public sealed class ProxyAsyncValidatable : IAsyncValidatableObject
{
    /// <summary>true 时返回失败项。</summary>
    public bool ReturnFailure { get; init; }

    /// <summary>收到的取消令牌（断言代理透传方法参数中的 CancellationToken）。</summary>
    public CancellationToken ReceivedToken { get; private set; }

    public Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)
    {
        ReceivedToken = cancellationToken;
        IEnumerable<ValidationResult> results = ReturnFailure ? [new ValidationResult("异步规则失败", ["AsyncCode"])] : [];
        return Task.FromResult(results);
    }
}

/// <summary>被代理的服务接口桩（DispatchProxy 仅支持接口；运行时动态程序集要求公开可见性）。</summary>
public interface IProxyValidatedService
{
    /// <summary>[Validate] 标记的 DTO 参数。</summary>
    Task<string> CreateAsync([Validate] ProxyAnnotatedRequest request, CancellationToken cancellationToken);

    /// <summary>未标记的纯特性 DTO 参数（不应被代理校验）。</summary>
    Task<string> CreateUnmarkedAsync(ProxyAnnotatedRequest request, CancellationToken cancellationToken);

    /// <summary>IValidatableObject 参数（接口自动识别，无标记）。</summary>
    Task<string> HandleSyncAsync(ProxySyncValidatable request, CancellationToken cancellationToken);

    /// <summary>IAsyncValidatableObject 参数（接口自动识别 + 异步包装，Task&lt;string&gt; 返回）。</summary>
    Task<string> HandleAsyncAsync(ProxyAsyncValidatable request, CancellationToken cancellationToken);

    /// <summary>同步返回方法 + IAsyncValidatableObject 参数（代理应拒绝）。</summary>
    string Echo(ProxyAsyncValidatable request);

    /// <summary>实现内直接抛异常（验证透传不包装）。</summary>
    Task ThrowAsync(CancellationToken cancellationToken);
}

/// <summary>服务实现桩：记录各方法被调用次数（校验失败时不得触达实现）。</summary>
public sealed class ProxyValidatedServiceCore : IProxyValidatedService
{
    /// <summary>CreateAsync 调用次数。</summary>
    public int CreateCalls { get; private set; }

    /// <summary>CreateUnmarkedAsync 调用次数。</summary>
    public int CreateUnmarkedCalls { get; private set; }

    /// <summary>HandleSyncAsync 调用次数。</summary>
    public int HandleSyncCalls { get; private set; }

    /// <summary>HandleAsyncAsync 调用次数。</summary>
    public int HandleAsyncCalls { get; private set; }

    public Task<string> CreateAsync(ProxyAnnotatedRequest request, CancellationToken cancellationToken)
    {
        CreateCalls++;
        return Task.FromResult("created");
    }

    public Task<string> CreateUnmarkedAsync(ProxyAnnotatedRequest request, CancellationToken cancellationToken)
    {
        CreateUnmarkedCalls++;
        return Task.FromResult("unmarked");
    }

    public Task<string> HandleSyncAsync(ProxySyncValidatable request, CancellationToken cancellationToken)
    {
        HandleSyncCalls++;
        return Task.FromResult("sync");
    }

    public Task<string> HandleAsyncAsync(ProxyAsyncValidatable request, CancellationToken cancellationToken)
    {
        HandleAsyncCalls++;
        return Task.FromResult("async");
    }

    public string Echo(ProxyAsyncValidatable request) => "echo";

    public Task ThrowAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("实现内部异常");
}

/// <summary>方法入口自动校验代理测试。</summary>
public sealed class ValidationDispatchProxyTests
{
    private static readonly string s_diService = "di-service-instance";

    /// <summary>创建代理：服务容器注册一个 string 实例供 DTO 校验解析。</summary>
    private static (IProxyValidatedService Proxy, ProxyValidatedServiceCore Impl) CreateProxy()
    {
        var impl = new ProxyValidatedServiceCore();
        var provider = new StubServiceProvider().Register(typeof(string), s_diService);
        return (ValidationDispatchProxy<IProxyValidatedService>.Create(impl, provider), impl);
    }

    [Fact]
    public async Task MarkedParameterInvalidThrowsBeforeInvoke()
    {
        // 4.1：[Validate] 标记参数非法——抛 ValidationException（驼峰字段名），实现方法未被调用
        var (proxy, impl) = CreateProxy();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => proxy.CreateAsync(new ProxyAnnotatedRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("name", Assert.Single(exception.FieldErrors).FieldName);
        Assert.Equal(0, impl.CreateCalls);
    }

    [Fact]
    public async Task MarkedParameterValidPassesThrough()
    {
        // 4.2：标记参数合法——调用透传并返回实现结果
        var (proxy, impl) = CreateProxy();

        var result = await proxy.CreateAsync(
            new ProxyAnnotatedRequest { Name = "合规" }, TestContext.Current.CancellationToken);

        Assert.Equal("created", result);
        Assert.Equal(1, impl.CreateCalls);
    }

    [Fact]
    public async Task UnmarkedPlainParameterIsNotValidated()
    {
        // 4.3：未标记且未实现验证接口的纯特性 DTO——代理不校验，直接透传
        var (proxy, impl) = CreateProxy();

        var result = await proxy.CreateUnmarkedAsync(new ProxyAnnotatedRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("unmarked", result);
        Assert.Equal(1, impl.CreateUnmarkedCalls);
    }

    [Fact]
    public async Task SyncValidatableAutoDetectedWithoutMarker()
    {
        // 4.4：IValidatableObject 参数无标记——接口自动识别并校验，失败前置拒绝（实现未触达）
        var (proxy, impl) = CreateProxy();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => proxy.HandleSyncAsync(new ProxySyncValidatable { ReturnFailure = true }, TestContext.Current.CancellationToken));

        Assert.Equal("syncCode", Assert.Single(exception.FieldErrors).FieldName);
        Assert.Equal(0, impl.HandleSyncCalls);
    }

    [Fact]
    public async Task SyncValidatableReceivesProxyServiceProvider()
    {
        // 4.5：DTO 的 Validate 内经 ValidationContext.GetService 解析到代理创建时传入的 DI 服务
        var (proxy, impl) = CreateProxy();
        var dto = new ProxySyncValidatable();

        var result = await proxy.HandleSyncAsync(dto, TestContext.Current.CancellationToken);

        Assert.Equal("sync", result);
        Assert.Equal(s_diService, dto.ResolvedService);
        Assert.Equal(1, impl.HandleSyncCalls);
    }

    [Fact]
    public async Task AsyncValidatableFailsBeforeInvokeOnTaskOfT()
    {
        // 4.6：IAsyncValidatableObject 参数 + Task<string> 返回——异步校验失败前置拒绝，实现未触达
        var (proxy, impl) = CreateProxy();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => proxy.HandleAsyncAsync(new ProxyAsyncValidatable { ReturnFailure = true }, TestContext.Current.CancellationToken));

        Assert.Equal("asyncCode", Assert.Single(exception.FieldErrors).FieldName);
        Assert.Equal(0, impl.HandleAsyncCalls);
    }

    [Fact]
    public async Task AsyncValidatableReceivesForwardedCancellationToken()
    {
        // 4.7：方法参数中的 CancellationToken 原样透传给异步校验
        var (proxy, impl) = CreateProxy();
        var dto = new ProxyAsyncValidatable();
        using var cts = new CancellationTokenSource();

        var result = await proxy.HandleAsyncAsync(dto, cts.Token);

        Assert.Equal("async", result);
        Assert.Equal(cts.Token, dto.ReceivedToken);
        Assert.Equal(1, impl.HandleAsyncCalls);
    }

    [Fact]
    public async Task TargetExceptionPassesThroughUnwrapped()
    {
        // 4.8：实现方法抛出的异常原样透传（不被 TargetInvocationException 包装）
        var (proxy, _) = CreateProxy();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => proxy.ThrowAsync(TestContext.Current.CancellationToken));

        Assert.Equal("实现内部异常", exception.Message);
    }

    [Fact]
    public void SyncReturnMethodWithAsyncValidatableThrowsNotSupported()
    {
        // 4.9：同步返回方法遇 IAsyncValidatableObject 参数——抛 NotSupportedException 并指明返回类型约束
        var (proxy, _) = CreateProxy();

        var exception = Assert.Throws<NotSupportedException>(() => proxy.Echo(new ProxyAsyncValidatable()));

        Assert.Contains("Task", exception.Message);
    }

    [Fact]
    public async Task AddValidatedScopedResolvesProxiedService()
    {
        // 4.10：DI 集成——AddValidatedScoped 注册后解析接口获得校验代理，非法参数被拦截
        var services = new ServiceCollection();
        services.AddValidatedScoped<IProxyValidatedService, ProxyValidatedServiceCore>();
        await using ServiceProvider provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<IProxyValidatedService>();

        _ = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(new ProxyAnnotatedRequest(), TestContext.Current.CancellationToken));
        var result = await service.CreateAsync(
            new ProxyAnnotatedRequest { Name = "合规" }, TestContext.Current.CancellationToken);
        Assert.Equal("created", result);
    }
}
