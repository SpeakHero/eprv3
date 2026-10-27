/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 23:10:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 23:10:00 +08:00 中国标准时间 星期一
 * 描述：服务接口方法入口自动校验代理——基于 BCL DispatchProxy（零第三方依赖），对 [Validate] 标记参数与实现 IValidatableObject / IAsyncValidatableObject 的 DTO 自动执行 ObjectValidator 校验
 */

using Qlx.Abstractions.Exceptions;

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Qlx.BuildingBlocks.Validation;

/// <summary>
/// 服务接口方法入口自动校验代理：基于 BCL <see cref="DispatchProxy"/>（零第三方依赖）拦截接口方法调用，
/// 在进入实现方法前自动执行 <see cref="ObjectValidator"/> 校验，失败抛出
/// <see cref="Qlx.Abstractions.Exceptions.ValidationException"/>（FieldErrors 驼峰字段名，与 MVC 422 行为一致）。
/// </summary>
/// <remarks>
/// <para>
/// 校验触发规则（满足其一即校验，按参数运行时类型判断）：
/// ① 接口方法参数标注 <see cref="ValidateAttribute"/>（适用于仅含 DataAnnotations 特性的 DTO）；
/// ② 参数运行时实例实现 <see cref="IValidatableObject"/>（接口自动识别，无需标记）；
/// ③ 参数运行时实例实现 <see cref="IAsyncValidatableObject"/>（接口自动识别，走异步校验，无需标记）。
/// </para>
/// <para>
/// 异步校验包装：存在 <see cref="IAsyncValidatableObject"/> 参数时，被拦截方法必须返回 <see cref="Task"/> 或
/// <see cref="Task{TResult}"/>（代理先完成异步校验再调用实现）；同步返回类型遇异步可验证参数抛 <see cref="NotSupportedException"/>。
/// 方法参数中的 <see cref="CancellationToken"/> 原样透传给异步校验。
/// </para>
/// <para>
/// 校验上下文的服务容器为创建代理时传入的 <see cref="IServiceProvider"/>（DTO 内经
/// <see cref="ValidationContext.GetService(Type)"/> 可解析 DI 服务，如 IOptions 配置）。
/// 实现方法内部抛出的异常经 <see cref="ExceptionDispatchInfo"/> 原样透传，不包装。
/// </para>
/// </remarks>
/// <typeparam name="T">被代理的服务接口类型（DispatchProxy 仅支持接口）。</typeparam>
public class ValidationDispatchProxy<T> : DispatchProxy where T : class
{
    /// <summary>各接口方法的 [Validate] 标记参数下标缓存（运行时接口识别不参与缓存，逐次判断成本可忽略）。</summary>
    private static readonly ConcurrentDictionary<MethodInfo, int[]> s_markedIndicesCache = new();

    /// <summary>泛型异步包装方法句柄（Task&lt;TResult&gt; 返回类型经反射调用）。</summary>
    private static readonly MethodInfo s_invokeValidatedGeneric =
        typeof(ValidationDispatchProxy<T>).GetMethod(nameof(InvokeValidatedGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("未找到异步包装方法 InvokeValidatedGenericAsync");

    private T _target = default!;
    private IServiceProvider _serviceProvider = default!;

    /// <summary>创建包裹 <paramref name="target"/> 的校验代理实例。</summary>
    /// <param name="target">真实服务实现实例。</param>
    /// <param name="serviceProvider">校验上下文服务容器（供 DTO 在 IValidatableObject.Validate 内解析 DI 服务）。</param>
    /// <returns>实现 <typeparamref name="T"/> 接口的代理对象。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 或 <paramref name="serviceProvider"/> 为 null。</exception>
    public static T Create(T target, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        T proxy = DispatchProxy.Create<T, ValidationDispatchProxy<T>>();
        var self = (ValidationDispatchProxy<T>)(object)proxy;
        self._target = target;
        self._serviceProvider = serviceProvider;
        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];

        int[] markedIndices = s_markedIndicesCache.GetOrAdd(targetMethod, static method =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            List<int>? indices = null;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].IsDefined(typeof(ValidateAttribute), inherit: false))
                {
                    (indices ??= []).Add(i);
                }
            }

            return indices is null ? [] : [.. indices];
        });

        // 收集需异步校验的参数（IAsyncValidatableObject 的校验须 await，无法在本同步拦截点完成）
        List<object>? asyncTargets = null;
        for (int i = 0; i < args.Length; i++)
        {
            object? arg = args[i];
            if (arg is null)
            {
                continue;
            }

            if (arg is IAsyncValidatableObject)
            {
                (asyncTargets ??= []).Add(arg);
                continue;
            }

            // [Validate] 标记 或 IValidatableObject 接口自动识别
            if (Array.IndexOf(markedIndices, i) >= 0 || arg is IValidatableObject)
            {
                ObjectValidator.ValidateOrThrow(arg, _serviceProvider);
            }
        }

        if (asyncTargets is null)
        {
            return InvokeTarget(targetMethod, args);
        }

        // 异步校验须包装返回值：先 await 校验再调用实现，方法必须返回 Task / Task<TResult>
        CancellationToken cancellationToken = ExtractCancellationToken(args);
        if (targetMethod.ReturnType == typeof(Task))
        {
            return InvokeValidatedAsync(targetMethod, args, asyncTargets, cancellationToken);
        }

        if (targetMethod.ReturnType.IsGenericType
            && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return s_invokeValidatedGeneric
                .MakeGenericMethod(targetMethod.ReturnType.GetGenericArguments()[0])
                .Invoke(this, [targetMethod, args, asyncTargets, cancellationToken]);
        }

        throw new NotSupportedException(
            $"方法 {targetMethod.DeclaringType?.Name}.{targetMethod.Name} 的参数实现了 IAsyncValidatableObject，"
            + "需经异步校验后方可调用，故方法返回类型必须为 Task 或 Task<TResult>");
    }

    /// <summary>调用真实实现并原样透传内部异常（剥除 TargetInvocationException 包装）。</summary>
    private object? InvokeTarget(MethodInfo targetMethod, object?[]? args)
    {
        try
        {
            return targetMethod.Invoke(_target, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(tie.InnerException);
            throw; // 不可达：上方 Throw 不返回
        }
    }

    /// <summary>异步校验包装（Task 返回）：先校验全部异步可验证参数，再调用并等待实现方法。</summary>
    private async Task InvokeValidatedAsync(MethodInfo targetMethod, object?[]? args, List<object> asyncTargets, CancellationToken cancellationToken)
    {
        foreach (object target in asyncTargets)
        {
            await ObjectValidator.ValidateOrThrowAsync(target, _serviceProvider, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await ((Task)InvokeTarget(targetMethod, args)!).ConfigureAwait(false);
    }

    /// <summary>异步校验包装（Task&lt;TResult&gt; 返回）：经 <see cref="s_invokeValidatedGeneric"/> 反射调用。</summary>
    private async Task<TResult> InvokeValidatedGenericAsync<TResult>(MethodInfo targetMethod, object?[]? args, List<object> asyncTargets, CancellationToken cancellationToken)
    {
        foreach (object target in asyncTargets)
        {
            await ObjectValidator.ValidateOrThrowAsync(target, _serviceProvider, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return await ((Task<TResult>)InvokeTarget(targetMethod, args)!).ConfigureAwait(false);
    }

    /// <summary>取方法参数中的首个取消令牌（无则 default），透传给异步校验。</summary>
    private static CancellationToken ExtractCancellationToken(object?[]? args)
    {
        if (args is not null)
        {
            foreach (object? arg in args)
            {
                if (arg is CancellationToken token)
                {
                    return token;
                }
            }
        }

        return default;
    }
}
