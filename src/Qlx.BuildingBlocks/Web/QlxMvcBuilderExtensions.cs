/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：MVC 收发生命周期统一装配扩展——全局 JSON 业务 Id 约定 + 全局 long Id 模型绑定器。
 *       所有业务模块共用一次注册，禁止各模块自行实现 Id 编解码。
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.JsonConverters;

namespace Qlx.BuildingBlocks.Web;

/// <summary>
/// MVC 统一序列化 / 参数绑定装配扩展。
/// </summary>
public static class QlxMvcBuilderExtensions
{
    /// <summary>
    /// 应用全平台统一 JSON 策略，并按 <see cref="QlxIdJsonMetadata"/> 约定自动混淆业务 Id。
    /// <para>替代 Host 内联 <c>AddJsonOptions</c>，保证所有模块一致。</para>
    /// </summary>
    /// <param name="builder">MVC 构建器。</param>
    /// <returns>同一 <paramref name="builder"/>，便于链式调用。</returns>
    public static IMvcBuilder AddQlxJsonSerialization(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddJsonOptions(static options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = QlxJsonDefaults.Default.PropertyNamingPolicy;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = QlxJsonDefaults.Default.PropertyNameCaseInsensitive;
            options.JsonSerializerOptions.WriteIndented = QlxJsonDefaults.Default.WriteIndented;
            options.JsonSerializerOptions.DefaultIgnoreCondition = QlxJsonDefaults.Default.DefaultIgnoreCondition;
            options.JsonSerializerOptions.MaxDepth = QlxJsonDefaults.Default.MaxDepth;
            options.JsonSerializerOptions.TypeInfoResolver = QlxIdJsonMetadata.CreateResolver();
        });

        return builder;
    }

    /// <summary>
    /// 注册全局业务 Id 模型绑定器（路由 / 查询参数中的 <see cref="long"/> / <see cref="long"/>? 自动解码）。
    /// </summary>
    /// <param name="builder">MVC 构建器。</param>
    /// <returns>同一 <paramref name="builder"/>，便于链式调用。</returns>
    public static IMvcBuilder AddQlxIdModelBinding(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<MvcOptions>(static options =>
            options.ModelBinderProviders.Insert(0, new HashidsLongModelBinderProvider()));

        return builder;
    }

    /// <summary>
    /// 注册全局方法级操作审计过滤器：标注 <c>[AuditOperation]</c> 的 Action 执行后写
    /// <c>IAuditService.CaptureOperationAsync</c>（成功 / 异常双留痕，详见 <see cref="Audit.OperationAuditFilter"/>）。
    /// </summary>
    /// <param name="builder">MVC 构建器。</param>
    /// <returns>同一 <paramref name="builder"/>，便于链式调用。</returns>
    public static IMvcBuilder AddQlxOperationAudit(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<Audit.OperationAuditFilter>();
        builder.Services.Configure<MvcOptions>(static options =>
            options.Filters.Add<Audit.OperationAuditFilter>());

        return builder;
    }
}
