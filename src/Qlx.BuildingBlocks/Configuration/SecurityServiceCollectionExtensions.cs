/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 10:00:00 +08:00 中国标准时间 星期六
 * 描述：租户安全基线 DI 装配扩展（AddQlxTenantSecurity）：强类型 Options + 启动校验
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qlx.BuildingBlocks.Configuration;

/// <summary>租户安全基线装配扩展。</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// 注册租户安全基线：<see cref="TenantSecurityOptions"/>（<see cref="TenantSecurityOptions.SectionName"/> 节，
    /// 数据注解 + IValidatableObject 校验，启动期 ValidateOnStart 失败即拒绝启动）。
    /// </summary>
    /// <remarks>
    /// PostConfigure 回填默认扩展名白名单：.NET 配置绑定对集合属性为追加语义（默认项 + 配置项合并），
    /// 故 <see cref="TenantSecurityOptions.UploadPolicyOptions.AllowedExtensions"/> 默认留空，
    /// 此处仅在未配置时回填 <see cref="TenantSecurityOptions.UploadPolicyOptions.DefaultAllowedExtensions"/>。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddQlxTenantSecurity(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TenantSecurityOptions>()
            .Bind(configuration.GetSection(TenantSecurityOptions.SectionName))
            .PostConfigure(static options =>
            {
                if (options.Upload.AllowedExtensions.Count == 0)
                {
                    options.Upload = options.Upload with
                    {
                        AllowedExtensions = TenantSecurityOptions.UploadPolicyOptions.DefaultAllowedExtensions,
                    };
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
