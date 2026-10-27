/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 22:10:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 22:10:00 +08:00 中国标准时间 星期一
 * 描述：对象级统一验证器——合并 DataAnnotations 特性、IValidatableObject 与 IAsyncValidatableObject 校验结果并支持一键抛出业务校验异常
 */

using Qlx.Abstractions.Exceptions;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
// 业务校验异常与 DataAnnotations 的 ValidationException 同名，本文件统一指业务异常
using ValidationException = Qlx.Abstractions.Exceptions.ValidationException;

namespace Qlx.BuildingBlocks.Validation;

/// <summary>
/// 对象级统一验证器：在 BCL <see cref="Validator"/> 原生行为（特性校验 + <see cref="IValidatableObject.Validate"/>）之上，
/// 追加 <see cref="IAsyncValidatableObject"/> 异步规则，并支持把全部失败项一次性映射为 <see cref="FieldError"/> 后抛出业务校验异常。
/// </summary>
/// <remarks>
/// 合并语义：所有公开方法底层先执行
/// <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}, bool)"/>
/// （覆盖特性校验与 <see cref="IValidatableObject.Validate"/>，保持框架原生行为），
/// 再追加异步规则结果；若结果为 <see cref="CompositeValidationResult"/> 则递归展开其 Results，最终输出扁平化的物化列表。
/// 验证方法内部抛出的异常不捕获、不包装，原样向上传播。
/// </remarks>
public static class ObjectValidator
{
    /// <summary>同步验证入口拒绝异步可验证对象时的异常消息（禁止静默漏验）。</summary>
    private const string AsyncOnlyMessage = "该对象实现了 IAsyncValidatableObject，请改用 ValidateAsync 进行验证。";

    /// <summary>同步验证对象：返回全部失败项（空集合表示验证通过）。</summary>
    /// <param name="instance">待验证实例；null 直接返回空集合，不执行任何验证。</param>
    /// <param name="serviceProvider">服务容器（经验证上下文 <see cref="ValidationContext.GetService(Type)"/> 供验证逻辑解析 DI 服务）；可为 null。</param>
    /// <param name="items">附加键值对（经 <see cref="ValidationContext.Items"/> 透传）；可为 null。</param>
    /// <param name="validateAllProperties">是否校验全部属性特性；false 时仅校验对象级规则。</param>
    /// <returns>扁平化后的失败结果集合（物化列表）。</returns>
    /// <exception cref="NotSupportedException">实例实现了 <see cref="IAsyncValidatableObject"/>（同步入口无法执行异步规则，请改用 <see cref="ValidateAsync(object?, IServiceProvider?, IDictionary{object, object}?, CancellationToken, bool)"/>）。</exception>
    /// <remarks>验证方法内部抛出的异常原样传播。</remarks>
    public static IEnumerable<ValidationResult> Validate(object? instance, IServiceProvider? serviceProvider = null, IDictionary<object, object>? items = null, bool validateAllProperties = true)
    {
        if (instance is null)
        {
            return [];
        }

        RejectAsyncOnly(instance);
        ValidationContext context = CreateContext(instance, serviceProvider, items);
        return CollectSyncResults(instance, context, validateAllProperties);
    }

    /// <summary>同步验证对象（预构建上下文重载）：<see cref="ValidationContext"/> 原样透传（含自定义 DisplayName / Items / ServiceProvider）。</summary>
    /// <param name="validationContext">验证上下文（其 <see cref="ValidationContext.ObjectInstance"/> 为待验证实例）。</param>
    /// <returns>扁平化后的失败结果集合（物化列表）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validationContext"/> 为 null。</exception>
    /// <exception cref="NotSupportedException">实例实现了 <see cref="IAsyncValidatableObject"/>（请改用 <see cref="ValidateAsync(ValidationContext, CancellationToken)"/>）。</exception>
    /// <remarks>验证方法内部抛出的异常原样传播。</remarks>
    public static IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);
        var instance = validationContext.ObjectInstance;
        RejectAsyncOnly(instance);
        return CollectSyncResults(instance, validationContext, validateAllProperties: true);
    }

    /// <summary>
    /// 异步验证对象：合并特性校验、<see cref="IValidatableObject.Validate"/> 与 <see cref="IAsyncValidatableObject.ValidateAsync"/> 的全部结果。
    /// </summary>
    /// <param name="instance">待验证实例；null 时返回已完成任务包裹的空集合。</param>
    /// <param name="serviceProvider">服务容器；可为 null。</param>
    /// <param name="items">附加键值对；可为 null。</param>
    /// <param name="cancellationToken">取消令牌（原样透传给 <see cref="IAsyncValidatableObject.ValidateAsync"/>）。</param>
    /// <param name="validateAllProperties">是否校验全部属性特性；false 时仅校验对象级规则。</param>
    /// <returns>扁平化后的失败结果集合（物化列表），空集合表示验证通过。</returns>
    /// <remarks>验证方法内部抛出的异常原样传播。</remarks>
    [SuppressMessage("Usage", "CA1068:CancellationToken parameters must come last", Justification = "公开 API 签名按规格固定，validateAllProperties 以默认参数收尾；cancellationToken 为可选参数，调用方经命名参数传递")]
    public static async Task<IEnumerable<ValidationResult>> ValidateAsync(object? instance, IServiceProvider? serviceProvider = null, IDictionary<object, object>? items = null, CancellationToken cancellationToken = default, bool validateAllProperties = true)
    {
        if (instance is null)
        {
            return [];
        }

        ValidationContext context = CreateContext(instance, serviceProvider, items);
        return await ValidateCoreAsync(instance, context, validateAllProperties, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>异步验证对象（预构建上下文重载）：上下文原样透传（含自定义 DisplayName / Items / ServiceProvider）。</summary>
    /// <param name="validationContext">验证上下文（其 <see cref="ValidationContext.ObjectInstance"/> 为待验证实例）。</param>
    /// <param name="cancellationToken">取消令牌（原样透传给 <see cref="IAsyncValidatableObject.ValidateAsync"/>）。</param>
    /// <returns>扁平化后的失败结果集合（物化列表），空集合表示验证通过。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validationContext"/> 为 null。</exception>
    /// <remarks>验证方法内部抛出的异常原样传播。</remarks>
    public static async Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validationContext);
        return await ValidateCoreAsync(validationContext.ObjectInstance, validationContext, validateAllProperties: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>同步验证并在失败时抛出业务校验异常：全部失败项一次性映射为 <see cref="FieldError"/> 后抛出 <see cref="ValidationException"/>。</summary>
    /// <param name="instance">待验证实例；null 视为验证通过。</param>
    /// <param name="serviceProvider">服务容器；可为 null。</param>
    /// <param name="items">附加键值对；可为 null。</param>
    /// <param name="validateAllProperties">是否校验全部属性特性。</param>
    /// <exception cref="ValidationException">存在失败项（一次性携带全部失败字段）。</exception>
    /// <exception cref="NotSupportedException">实例实现了 <see cref="IAsyncValidatableObject"/>（请改用 <see cref="ValidateOrThrowAsync(object?, IServiceProvider?, IDictionary{object, object}?, CancellationToken, bool)"/>）。</exception>
    /// <remarks>
    /// 验证通过时静默返回。字段名取失败结果的首个非空成员名并经 <see cref="JsonNamingPolicy.CamelCase"/> 转驼峰
    /// （如 NewPassword → newPassword）；对象级错误（无成员名）字段名为空字符串，与 MVC ValidationProblemDetails 空键行为一致。
    /// </remarks>
    public static void ValidateOrThrow(object? instance, IServiceProvider? serviceProvider = null, IDictionary<object, object>? items = null, bool validateAllProperties = true)
    {
        var failures = Validate(instance, serviceProvider, items, validateAllProperties).ToList();
        ThrowIfAnyFailure(failures);
    }

    /// <summary>异步验证并在失败时抛出业务校验异常：全部失败项一次性映射为 <see cref="FieldError"/> 后抛出 <see cref="ValidationException"/>。</summary>
    /// <param name="instance">待验证实例；null 视为验证通过。</param>
    /// <param name="serviceProvider">服务容器；可为 null。</param>
    /// <param name="items">附加键值对；可为 null。</param>
    /// <param name="cancellationToken">取消令牌（原样透传给 <see cref="IAsyncValidatableObject.ValidateAsync"/>）。</param>
    /// <param name="validateAllProperties">是否校验全部属性特性。</param>
    /// <exception cref="ValidationException">存在失败项（一次性携带全部失败字段）。</exception>
    /// <remarks>
    /// 验证通过时静默返回；映射规则同 <see cref="ValidateOrThrow(object?, IServiceProvider?, IDictionary{object, object}?, bool)"/>
    /// （字段名驼峰化，对象级错误字段名为空字符串）。验证方法内部抛出的异常原样传播。
    /// </remarks>
    [SuppressMessage("Usage", "CA1068:CancellationToken parameters must come last", Justification = "公开 API 签名按规格固定，validateAllProperties 以默认参数收尾；cancellationToken 为可选参数，调用方经命名参数传递")]
    public static async Task ValidateOrThrowAsync(object? instance, IServiceProvider? serviceProvider = null, IDictionary<object, object>? items = null, CancellationToken cancellationToken = default, bool validateAllProperties = true)
    {
        var failures = (await ValidateAsync(instance, serviceProvider, items, cancellationToken, validateAllProperties).ConfigureAwait(false)).ToList();
        ThrowIfAnyFailure(failures);
    }

    /// <summary>
    /// 服务层便捷扩展：以当前服务容器为验证上下文的服务来源，一行调用即可复用 DTO 全部验证
    /// （如 <c>this.ValidateOrThrow(request)</c>）。
    /// </summary>
    /// <param name="services">当前服务容器（作为验证上下文的服务容器传入，经 <see cref="ValidationContext.GetService(Type)"/> 解析）。</param>
    /// <param name="instance">待验证实例；null 视为验证通过。</param>
    /// <param name="items">附加键值对；可为 null。</param>
    /// <param name="validateAllProperties">是否校验全部属性特性。</param>
    /// <exception cref="ValidationException">存在失败项。</exception>
    /// <exception cref="NotSupportedException">实例实现了 <see cref="IAsyncValidatableObject"/>（请改用 <see cref="ValidateOrThrowAsync(IServiceProvider, object?, IDictionary{object, object}?, CancellationToken, bool)"/>）。</exception>
    public static void ValidateOrThrow(this IServiceProvider services, object? instance, IDictionary<object, object>? items = null, bool validateAllProperties = true)
    {
        ValidateOrThrow(instance, services, items, validateAllProperties);
    }

    /// <summary>
    /// 服务层便捷扩展（异步）：以当前服务容器为验证上下文的服务来源，一行调用即可复用 DTO 全部验证
    /// （如 <c>await this.ValidateOrThrowAsync(request)</c>）。
    /// </summary>
    /// <param name="services">当前服务容器（作为验证上下文的服务容器传入，经 <see cref="ValidationContext.GetService(Type)"/> 解析）。</param>
    /// <param name="instance">待验证实例；null 视为验证通过。</param>
    /// <param name="items">附加键值对；可为 null。</param>
    /// <param name="cancellationToken">取消令牌（原样透传给 <see cref="IAsyncValidatableObject.ValidateAsync"/>）。</param>
    /// <param name="validateAllProperties">是否校验全部属性特性。</param>
    /// <exception cref="ValidationException">存在失败项。</exception>
    [SuppressMessage("Usage", "CA1068:CancellationToken parameters must come last", Justification = "公开 API 签名按规格固定，validateAllProperties 以默认参数收尾；cancellationToken 为可选参数，调用方经命名参数传递")]
    public static Task ValidateOrThrowAsync(this IServiceProvider services, object? instance, IDictionary<object, object>? items = null, CancellationToken cancellationToken = default, bool validateAllProperties = true)
    {
        return ValidateOrThrowAsync(instance, services, items, cancellationToken, validateAllProperties);
    }

    /// <summary>构建验证上下文：单个 <see cref="ValidationContext"/> 复用于特性校验与异步规则。</summary>
    /// <remarks>
    /// BCL 构造签名为 <c>IDictionary&lt;object, object?&gt;?</c>，而公开契约的键值对值约定为非空；
    /// 两者在运行时同构（可空性仅为编译期注解），此处显式转换以零拷贝原样透传调用方字典。
    /// </remarks>
    private static ValidationContext CreateContext(object instance, IServiceProvider? serviceProvider, IDictionary<object, object>? items)
    {
        return new(instance, serviceProvider, (IDictionary<object, object?>?)items);
    }

    /// <summary>实例实现 IAsyncValidatableObject 时同步入口直接拒绝，避免异步规则被静默漏验。</summary>
    private static void RejectAsyncOnly(object instance)
    {
        if (instance is IAsyncValidatableObject)
        {
            throw new NotSupportedException(AsyncOnlyMessage);
        }
    }

    /// <summary>执行 BCL 同步验证（特性校验 + IValidatableObject.Validate）并扁平化结果。</summary>
    private static List<ValidationResult> CollectSyncResults(object instance, ValidationContext context, bool validateAllProperties)
    {
        var rawResults = new List<ValidationResult>();
        _ = Validator.TryValidateObject(instance, context, rawResults, validateAllProperties);

        var flattened = new List<ValidationResult>(rawResults.Count);
        foreach (ValidationResult result in rawResults)
        {
            FlattenResult(result, flattened);
        }

        return flattened;
    }

    /// <summary>异步核心：同步结果 ∪ 异步规则结果，全部扁平化为物化列表。</summary>
    private static async Task<List<ValidationResult>> ValidateCoreAsync(object instance, ValidationContext context, bool validateAllProperties, CancellationToken cancellationToken)
    {
        List<ValidationResult> results = CollectSyncResults(instance, context, validateAllProperties);

        if (instance is IAsyncValidatableObject asyncValidatable)
        {
            IEnumerable<ValidationResult>? asyncResults = await asyncValidatable.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            if (asyncResults is not null)
            {
                foreach (ValidationResult result in asyncResults)
                {
                    FlattenResult(result, results);
                }
            }
        }

        return results;
    }

    /// <summary>递归展开 CompositeValidationResult 的 Results，普通结果直接加入目标列表。</summary>
    private static void FlattenResult(ValidationResult result, List<ValidationResult> target)
    {
        if (result is CompositeValidationResult composite)
        {
            foreach (ValidationResult inner in composite.Results)
            {
                FlattenResult(inner, target);
            }

            return;
        }

        target.Add(result);
    }

    /// <summary>存在失败项时一次性映射全部 FieldError 并抛出业务校验异常；否则静默返回。</summary>
    private static void ThrowIfAnyFailure(List<ValidationResult> failures)
    {
        if (failures.Count > 0)
        {
            throw new ValidationException(failures.Select(ToFieldError));
        }
    }

    /// <summary>把单条失败结果映射为 FieldError：首个非空成员名转驼峰（无成员名则空字符串），消息为 null 时取空字符串。</summary>
    private static FieldError ToFieldError(ValidationResult result)
    {
        var memberName = result.MemberNames.FirstOrDefault(static name => !string.IsNullOrEmpty(name));
        var fieldName = memberName is null ? string.Empty : JsonNamingPolicy.CamelCase.ConvertName(memberName);
        return new FieldError(fieldName, result.ErrorMessage ?? string.Empty);
    }
}

/// <summary>
/// .NET Framework 时代 <c>System.ComponentModel.DataAnnotations.CompositeValidationResult</c> 的兼容实现
/// （.NET 10 BCL 未内置该类型）：把多条子验证结果聚合为一条复合结果，<see cref="ObjectValidator"/> 会递归展开其 Results。
/// </summary>
public class CompositeValidationResult : ValidationResult
{
    private readonly List<ValidationResult> _results = [];

    /// <summary>以默认错误消息初始化（无成员名，即对象级复合结果）。</summary>
    /// <param name="errorMessage">复合结果的错误消息。</param>
    public CompositeValidationResult(string errorMessage)
        : base(errorMessage, [])
    {
    }

    /// <summary>以错误消息与成员名初始化。</summary>
    /// <param name="errorMessage">复合结果的错误消息。</param>
    /// <param name="memberNames">复合结果关联的成员名集合。</param>
    public CompositeValidationResult(string errorMessage, IEnumerable<string> memberNames)
        : base(errorMessage, memberNames)
    {
    }

    /// <summary>聚合的子验证结果集合。</summary>
    public IEnumerable<ValidationResult> Results => _results;

    /// <summary>追加一条子验证结果（子项自身仍可为复合结果，展开时递归处理）。</summary>
    /// <param name="result">要追加的子验证结果。</param>
    public void AddResult(ValidationResult result)
    {
        _results.Add(result);
    }
}
