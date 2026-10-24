/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：业务 Id 模型绑定器——路由/查询参数中的 long / long? 自动按 Hashids 解码，
 *       Controller 直接声明 long，无需手工接收字符串再在 Service 解码。
 */

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Web;

/// <summary>
/// 业务 Id 模型绑定器：把对外混淆字符串解码为真实 <see cref="long"/>（兼容纯数字入参）。
/// 非法输入写入 <see cref="ModelBindingContext.ModelState"/>，统一由全局异常/校验管线返回 400。
/// </summary>
public sealed class HashidsLongModelBinder : IModelBinder
{
    private readonly bool _isNullable;

    /// <summary>创建绑定器实例。</summary>
    /// <param name="isNullable">目标类型是否为 <see cref="long"/>?。</param>
    public HashidsLongModelBinder(bool isNullable)
    {
        _isNullable = isNullable;
    }

    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            BindMissing(bindingContext);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

        var raw = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            BindMissing(bindingContext);
            return Task.CompletedTask;
        }

        if (ObfuscatedIdParser.TryParse(raw, out var id))
        {
            bindingContext.Result = ModelBindingResult.Success(id);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.AddModelError(bindingContext.ModelName, "无效的资源 Id");
        return Task.CompletedTask;
    }

    /// <summary>缺省值处理：可空类型绑定 null，非可空类型记录模型错误。</summary>
    private void BindMissing(ModelBindingContext bindingContext)
    {
        if (_isNullable)
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return;
        }

        bindingContext.ModelState.AddModelError(bindingContext.ModelName, "缺少必填参数");
    }
}

/// <summary>
/// 业务 Id 模型绑定器提供器：为 <see cref="long"/> / <see cref="long"/>? 参数自动选择
/// <see cref="HashidsLongModelBinder"/>，其余类型返回 <see langword="null"/> 交回默认绑定器。
/// </summary>
public sealed class HashidsLongModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var modelType = context.Metadata.ModelType;
        if (modelType == typeof(long))
        {
            return new HashidsLongModelBinder(isNullable: false);
        }

        if (modelType == typeof(long?))
        {
            return new HashidsLongModelBinder(isNullable: true);
        }

        return null;
    }
}
