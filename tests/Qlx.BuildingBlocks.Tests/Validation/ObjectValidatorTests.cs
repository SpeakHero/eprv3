/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 22:10:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 22:10:00 +08:00 中国标准时间 星期一
 * 描述：对象级统一验证器单元测试——null 实例 / 特性校验 / 同步与异步规则合并 / 上下文透传 / 复合结果扁平化 / 业务校验异常映射
 */

using System.ComponentModel.DataAnnotations;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Validation;

// 业务校验异常与 DataAnnotations 的 ValidationException 同名，本文件统一指业务异常
using ValidationException = Qlx.Abstractions.Exceptions.ValidationException;

namespace Qlx.BuildingBlocks.Tests.Validation;

/// <summary>特性校验桩模型：[Required] Name + [StringLength(5)] Code，用于违规 / 合规两向验证。</summary>
internal sealed class RequiredStringLengthModel
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [StringLength(5)]
    public string Code { get; init; } = string.Empty;
}

/// <summary>桩服务容器：内部字典按 Type 注册解析，未注册返回 null。</summary>
internal sealed class StubServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];

    /// <summary>注册指定服务类型的实例（支持链式调用）。</summary>
    public StubServiceProvider Register(Type serviceType, object service)
    {
        _services[serviceType] = service;
        return this;
    }

    public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
}

/// <summary>仅实现 IValidatableObject 的桩：记录收到的 ValidationContext（含服务解析与 Items），按开关返回指定成员名的失败项。</summary>
internal sealed class SyncValidatableStub : IValidatableObject
{
    /// <summary>收到的验证上下文。</summary>
    public ValidationContext? ReceivedContext { get; private set; }

    /// <summary>在 Validate 内经 context.GetService 解析到的字符串服务（未解析为 null）。</summary>
    public string? ResolvedService { get; private set; }

    /// <summary>在 Validate 内读到的 Items["item-key"] 值（不存在为 null）。</summary>
    public object? ReceivedItem { get; private set; }

    /// <summary>true 时返回一条失败项（成员名 SyncCode）。</summary>
    public bool ReturnFailure { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ReceivedContext = validationContext;
        ResolvedService = validationContext.GetService(typeof(string)) as string;
        ReceivedItem = validationContext.Items.TryGetValue("item-key", out var value) ? value : null;

        return ReturnFailure
            ? [new ValidationResult("同步规则失败", ["SyncCode"])]
            : [];
    }
}

/// <summary>仅实现 IAsyncValidatableObject 的桩：记录收到的 CancellationToken 与 ValidationContext，按开关返回失败项。</summary>
internal sealed class AsyncValidatableStub : IAsyncValidatableObject
{
    /// <summary>收到的验证上下文。</summary>
    public ValidationContext? ReceivedContext { get; private set; }

    /// <summary>收到的取消令牌（断言透传）。</summary>
    public CancellationToken ReceivedToken { get; private set; }

    /// <summary>true 时返回一条失败项（成员名 AsyncCode）。</summary>
    public bool ReturnFailure { get; init; }

    public Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)
    {
        ReceivedContext = validationContext;
        ReceivedToken = cancellationToken;
        IEnumerable<ValidationResult> results = ReturnFailure
            ? [new ValidationResult("异步规则失败", ["AsyncCode"])]
            : [];
        return Task.FromResult(results);
    }
}

/// <summary>同时实现两接口的桩：同步侧与异步侧各返回一条不同失败项。</summary>
internal sealed class DualValidatableStub : IValidatableObject, IAsyncValidatableObject
{
    public ValidationContext? ReceivedContext { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ReceivedContext = validationContext;
        return [new ValidationResult("同步失败", ["SyncField"])];
    }

    public Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)
    {
        ReceivedContext = validationContext;
        IEnumerable<ValidationResult> results = [new ValidationResult("异步失败", ["AsyncField"])];
        return Task.FromResult(results);
    }
}

/// <summary>同步 Validate 方法内抛出 InvalidOperationException 的桩。</summary>
internal sealed class ThrowingSyncValidatableStub : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => throw new InvalidOperationException("同步验证内部异常");
}

/// <summary>异步 ValidateAsync 方法内抛出 InvalidOperationException 的桩。</summary>
internal sealed class ThrowingAsyncValidatableStub : IAsyncValidatableObject
{
    public Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("异步验证内部异常");
}

/// <summary>含对象级错误（ValidationResult 无 MemberNames）的桩。</summary>
internal sealed class ObjectLevelErrorStub : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => [new ValidationResult("对象整体不满足业务约束")];
}

/// <summary>返回 CompositeValidationResult 两层嵌套结果的桩：验证复合结果被递归扁平化。</summary>
internal sealed class CompositeErrorStub : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 嵌套组合：根组合含一条普通失败与一个内层组合（内层组合再含一条失败），验证递归展开
        var innerComposite = new CompositeValidationResult("内层组合");
        innerComposite.AddResult(new ValidationResult("内层失败二", ["InnerTwo"]));

        var rootComposite = new CompositeValidationResult("组合失败", ["CompositeRoot"]);
        rootComposite.AddResult(new ValidationResult("内层失败一", ["InnerOne"]));
        rootComposite.AddResult(innerComposite);

        return [rootComposite, new ValidationResult("外层失败", ["Outer"])];
    }
}

/// <summary>对象级统一验证器测试。</summary>
public sealed class ObjectValidatorTests
{
    [Fact]
    public async Task NullInstanceReturnsEmptyWithoutThrowing()
    {
        // 3.1：null 实例——同步与异步入口均返回空集合且不抛异常（显式 object? 转换避免重载决议选中 ValidationContext 重载）
        Assert.Empty(ObjectValidator.Validate((object?)null));
        Assert.Empty(await ObjectValidator.ValidateAsync((object?)null, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AttributeModelViolationsAreCollected()
    {
        // 3.2：普通对象违规——失败项含对应成员名且消息非空
        var model = new RequiredStringLengthModel { Name = string.Empty, Code = "TOOLONG" };

        var results = ObjectValidator.Validate(model).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RequiredStringLengthModel.Name)) && !string.IsNullOrEmpty(r.ErrorMessage));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RequiredStringLengthModel.Code)) && !string.IsNullOrEmpty(r.ErrorMessage));
    }

    [Fact]
    public void AttributeModelPassesSilently()
    {
        // 3.2：普通对象合规——空集合
        var model = new RequiredStringLengthModel { Name = "淇利馨", Code = "QLX01" };
        Assert.Empty(ObjectValidator.Validate(model));
    }

    [Fact]
    public void SyncValidatableFailuresAndContextDataAreCollected()
    {
        // 3.3：IValidatableObject 失败项被收集；serviceProvider / items 在 Validate 内可用
        var services = new StubServiceProvider().Register(typeof(string), "桩服务");
        var items = new Dictionary<object, object> { ["item-key"] = "附加数据" };
        var stub = new SyncValidatableStub { ReturnFailure = true };

        var results = ObjectValidator.Validate(stub, services, items).ToList();

        var failure = Assert.Single(results);
        Assert.Equal("同步规则失败", failure.ErrorMessage);
        Assert.Contains("SyncCode", failure.MemberNames);

        Assert.Same(stub, stub.ReceivedContext!.ObjectInstance);
        Assert.Equal("桩服务", stub.ResolvedService);
        Assert.Equal("附加数据", stub.ReceivedItem);
    }

    [Fact]
    public void SyncValidatablePassesSilently()
    {
        // 3.3：IValidatableObject 通过——空集合
        var stub = new SyncValidatableStub { ReturnFailure = false };
        Assert.Empty(ObjectValidator.Validate(stub));
    }

    [Fact]
    public async Task AsyncValidatableResultsCollectedWithTokenPassedThrough()
    {
        // 3.4：异步重载收集结果且取消令牌原样透传
        using var cts = new CancellationTokenSource();
        var stub = new AsyncValidatableStub { ReturnFailure = true };

        var results = await ObjectValidator.ValidateAsync(stub, cancellationToken: cts.Token);

        var failure = Assert.Single(results);
        Assert.Equal("异步规则失败", failure.ErrorMessage);
        Assert.Contains("AsyncCode", failure.MemberNames);
        Assert.Equal(cts.Token, stub.ReceivedToken);
    }

    [Fact]
    public void SyncValidateThrowsForAsyncValidatable()
    {
        // 3.4：同步入口拒绝 IAsyncValidatableObject，禁止静默漏验
        var exception = Assert.Throws<NotSupportedException>(() => ObjectValidator.Validate(new AsyncValidatableStub()));

        Assert.Contains("IAsyncValidatableObject", exception.Message);
        Assert.Contains("ValidateAsync", exception.Message);
    }

    [Fact]
    public async Task DualInterfaceResultsAreMerged()
    {
        // 3.5：双接口两侧结果合并，成员名与消息完整、数量正确
        var results = (await ObjectValidator.ValidateAsync(new DualValidatableStub(), cancellationToken: TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(2, results.Count);
        var syncFailure = results.Single(r => r.MemberNames.Contains("SyncField"));
        Assert.Equal("同步失败", syncFailure.ErrorMessage);
        var asyncFailure = results.Single(r => r.MemberNames.Contains("AsyncField"));
        Assert.Equal("异步失败", asyncFailure.ErrorMessage);
    }

    [Fact]
    public void PrebuiltContextPassesDisplayNameAndItemsThrough()
    {
        // 3.6：预构建 ValidationContext 的 DisplayName / Items 原样透传
        // 注意：BCL 构造形参为 IDictionary<object, object?>，此处字典值类型按 BCL 约定声明
        var stub = new SyncValidatableStub { ReturnFailure = false };
        var context = new ValidationContext(stub, null, new Dictionary<object, object?> { ["item-key"] = "上下文数据" })
        {
            DisplayName = "重置密码请求"
        };

        ObjectValidator.Validate(context);

        Assert.Equal("重置密码请求", stub.ReceivedContext!.DisplayName);
        Assert.Equal("上下文数据", stub.ReceivedItem);
    }

    [Fact]
    public void PrebuiltContextSyncValidateThrowsForAsyncValidatable()
    {
        // 3.6：预构建上下文重载同样拒绝 IAsyncValidatableObject
        var context = new ValidationContext(new AsyncValidatableStub());
        Assert.Throws<NotSupportedException>(() => ObjectValidator.Validate(context));
    }

    [Fact]
    public void ValidateRethrowsInnerException()
    {
        // 3.6：验证方法内部异常原样传播（不包装）
        Assert.Throws<InvalidOperationException>(() => ObjectValidator.Validate(new ThrowingSyncValidatableStub()));
    }

    [Fact]
    public async Task ValidateAsyncRethrowsInnerException()
    {
        // 3.6：异步验证方法内部异常原样传播（不包装）
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => ObjectValidator.ValidateAsync(new ThrowingAsyncValidatableStub(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PrebuiltContextRethrowsInnerException()
    {
        // 3.6：预构建上下文重载的同步异常原样传播
        var context = new ValidationContext(new ThrowingSyncValidatableStub());
        Assert.Throws<InvalidOperationException>(() => ObjectValidator.Validate(context));
    }

    [Fact]
    public async Task PrebuiltContextAsyncRethrowsInnerException()
    {
        // 3.6：预构建上下文重载的异步异常原样传播
        var context = new ValidationContext(new ThrowingAsyncValidatableStub());
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => ObjectValidator.ValidateAsync(context, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CompositeResultsAreFlattenedRecursively()
    {
        // 核心合并语义：CompositeValidationResult 递归展开，组合本身不进入结果
        var results = ObjectValidator.Validate(new CompositeErrorStub()).ToList();

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, r => r.ErrorMessage == "组合失败");
        Assert.DoesNotContain(results, r => r.ErrorMessage == "内层组合");
        Assert.Contains(results, r => r.MemberNames.Contains("InnerOne") && r.ErrorMessage == "内层失败一");
        Assert.Contains(results, r => r.MemberNames.Contains("InnerTwo") && r.ErrorMessage == "内层失败二");
        Assert.Contains(results, r => r.MemberNames.Contains("Outer") && r.ErrorMessage == "外层失败");
    }

    [Fact]
    public void ValidateOrThrowThrowsWithCamelCaseFieldErrors()
    {
        // 3.7：失败一次性抛出全部 FieldErrors，字段名转驼峰（Name→name、Code→code）
        var model = new RequiredStringLengthModel { Name = string.Empty, Code = "TOOLONG" };

        var exception = Assert.Throws<ValidationException>(() => ObjectValidator.ValidateOrThrow(model));

        Assert.Equal(2, exception.FieldErrors.Count);
        var nameError = exception.FieldErrors.Single(e => e.FieldName == "name");
        Assert.False(string.IsNullOrEmpty(nameError.ErrorMessage));
        Assert.Equal("code", exception.FieldErrors.Single(e => e.FieldName == "code").FieldName);
    }

    [Fact]
    public void ValidateOrThrowMapsObjectLevelErrorToEmptyFieldName()
    {
        // 3.7：对象级错误字段名为空字符串（与 MVC ValidationProblemDetails 空键行为一致）
        var exception = Assert.Throws<ValidationException>(() => ObjectValidator.ValidateOrThrow(new ObjectLevelErrorStub()));

        var error = Assert.Single(exception.FieldErrors);
        Assert.Equal(string.Empty, error.FieldName);
        Assert.Equal("对象整体不满足业务约束", error.ErrorMessage);
    }

    [Fact]
    public void ValidateOrThrowDoesNotThrowWhenValid()
    {
        // 3.7：通过时静默返回
        var model = new RequiredStringLengthModel { Name = "淇利馨", Code = "QLX01" };
        ObjectValidator.ValidateOrThrow(model);
    }

    [Fact]
    public async Task ValidateOrThrowAsyncThrowsWithTokenPassedThrough()
    {
        // 3.7：异步失败抛出映射后的异常，且取消令牌经 IAsyncValidatableObject 桩透传
        using var cts = new CancellationTokenSource();
        var stub = new AsyncValidatableStub { ReturnFailure = true };

        var exception = await Assert.ThrowsAsync<ValidationException>(() => ObjectValidator.ValidateOrThrowAsync(stub, cancellationToken: cts.Token));

        var error = Assert.Single(exception.FieldErrors);
        Assert.Equal("asyncCode", error.FieldName);
        Assert.Equal("异步规则失败", error.ErrorMessage);
        Assert.Equal(cts.Token, stub.ReceivedToken);
    }

    [Fact]
    public void ServiceProviderExtensionValidateOrThrowForwardsServices()
    {
        // 3.7：IServiceProvider 扩方法把服务容器作为 serviceProvider 传入（桩内 GetService 可解析）
        var services = new StubServiceProvider().Register(typeof(string), "扩展桩服务");
        var stub = new SyncValidatableStub { ReturnFailure = true };

        Assert.Throws<ValidationException>(() => services.ValidateOrThrow(stub));

        Assert.Equal("扩展桩服务", stub.ResolvedService);
    }

    [Fact]
    public void ServiceProviderExtensionValidateOrThrowPassesWhenValid()
    {
        // 3.7：IServiceProvider 扩方法通过时不抛
        var services = new StubServiceProvider();
        services.ValidateOrThrow(new RequiredStringLengthModel { Name = "淇利馨", Code = "QLX01" });
    }

    [Fact]
    public async Task ServiceProviderExtensionValidateOrThrowAsyncThrows()
    {
        // 3.7：IServiceProvider 扩方法异步失败抛出（对象级错误字段名为空字符串）
        var services = new StubServiceProvider();

        var exception = await Assert.ThrowsAsync<ValidationException>(() => services.ValidateOrThrowAsync(new ObjectLevelErrorStub(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(string.Empty, Assert.Single(exception.FieldErrors).FieldName);
    }
}
