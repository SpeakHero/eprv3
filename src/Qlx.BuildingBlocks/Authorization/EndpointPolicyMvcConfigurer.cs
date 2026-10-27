/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：MvcOptions 配置器——将 EndpointPolicyConvention 挂入 MvcOptions.Conventions
 *       首位（IConfigureOptions 模式，与 PermissionPolicyConfigurer 同构，随 AddControllers 生效）。
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Authorization;

/// <summary>
/// 授权约定注册器（<see cref="IConfigureOptions{TOptions}"/> 模式）。
/// 由 <c>AddQlxEndpointPolicies</c> 注册为 Singleton；<c>AddControllers</c>
/// 构建 ApplicationModel 时自动应用。
/// </summary>
/// <param name="catalog">启动期定稿的端点授权规则目录。</param>
public sealed class EndpointPolicyMvcConfigurer(EndpointPolicyCatalog catalog)
    : IConfigureOptions<MvcOptions>
{
    /// <inheritdoc />
    public void Configure(MvcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Conventions.Insert(0, new EndpointPolicyConvention(catalog));
    }
}
